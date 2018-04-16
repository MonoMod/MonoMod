using Mono.Cecil;
using MonoMod;
using MonoMod.Utils;
using MonoMod.InlineRT;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace MonoMod.BaseLoader {
    // Special meta types.
    public sealed class AssetTypeDirectory { private AssetTypeDirectory() { } }
    public sealed class AssetTypeAssembly { private AssetTypeAssembly() { } }

    // Delegate types.
    public delegate string TypeGuesser(string file, out Type type, out string format);

    // Source types.
    public abstract class ModContentSource {
        protected abstract void Crawl();
        internal void _Crawl() => Crawl();
    }

    public class DirectoryModContent : ModContentSource {
        /// <summary>
        /// The path to the mod directory.
        /// </summary>
        public string Path;

        public DirectoryModContent(string path) {
            Path = path;
        }

        protected override void Crawl() => Crawl(null);

        protected virtual void Crawl(string dir, string root = null) {
            if (root == null)
                root = dir;
            string[] files = Directory.GetFiles(dir);
            for (int i = 0; i < files.Length; i++) {
                string file = files[i];
                ModContentManager.Add(file.Substring(root.Length + 1), new FileSystemModAsset(file));
            }
            files = Directory.GetDirectories(dir);
            for (int i = 0; i < files.Length; i++) {
                string file = files[i];
                Crawl(file, root);
            }
        }
    }

    public class AssemblyModContent : ModContentSource {
        /// <summary>
        /// The assembly containing the mod content as resources.
        /// </summary>
        public Assembly Assembly;

        public AssemblyModContent(Assembly asm) {
            Assembly = asm;
        }

        protected override void Crawl() {
            string[] resourceNames = Assembly.GetManifestResourceNames();
            for (int i = 0; i < resourceNames.Length; i++) {
                string name = resourceNames[i];
                int indexOfContent = name.IndexOf("Content");
                if (indexOfContent < 0)
                    continue;
                name = name.Substring(indexOfContent + 8);
                ModContentManager.Add(name, new AssemblyModAsset(Assembly, resourceNames[i]));
            }
        }
    }

    // Main helper type.
    public static class ModContentManager {

        /// <summary>
        /// The path to the original /Content directory. Used to shorten any "full" asset paths.
        /// </summary>
        public static string PathContentOrig { get; internal set; }

        /// <summary>
        /// List of all currently loaded content mods.
        /// </summary>
        public readonly static List<ModContentSource> Mods = new List<ModContentSource>();

        /// <summary>
        /// Mod content mapping. Use Content.Add, Get, and TryGet where applicable instead.
        /// </summary>
        public readonly static Dictionary<string, ModAsset> Map = new Dictionary<string, ModAsset>();
        /// <summary>
        /// Mod content mapping, directories only. Use Content.Add, Get, and TryGet where applicable instead.
        /// </summary>
        public readonly static Dictionary<string, ModAsset> MapDirs = new Dictionary<string, ModAsset>();

        internal readonly static List<string> LoadedAssetPaths = new List<string>();
        internal readonly static List<string> LoadedAssetFullPaths = new List<string>();
        internal readonly static List<WeakReference> LoadedAssets = new List<WeakReference>();

        /// <summary>
        /// Gets the ModAsset mapped to the given relative path.
        /// </summary>
        /// <param name="path">The relative asset path.</param>
        /// <param name="metadata">The resulting mod asset meta object.</param>
        /// <param name="includeDirs">Whether to include directories or not.</param>
        /// <returns>True if a mapping for the given path is present, false otherwise.</returns>
        public static bool TryGet(string path, out ModAsset metadata, bool includeDirs = false) {
            path = path.Replace('\\', '/');

            if (includeDirs) {
                if (MapDirs.TryGetValue(path, out metadata)) return true;
            }
            if (Map.TryGetValue(path, out metadata)) return true;
            return false;
        }
        /// <summary>
        /// Gets the ModAsset mapped to the given relative path.
        /// </summary>
        /// <param name="path">The relative asset path.</param>
        /// <param name="includeDirs">Whether to include directories or not.</param>
        /// <returns>The resulting mod asset meta object, or null.</returns>
        public static ModAsset Get(string path, bool includeDirs = false) {
            ModAsset metadata;
            TryGet(path, out metadata, includeDirs);
            return metadata;
        }

        /// <summary>
        /// Adds a new mapping for the given relative content path.
        /// </summary>
        /// <param name="path">The relative asset path.</param>
        /// <param name="metadata">The matching mod asset meta object.</param>
        /// <returns>The passed mod asset meta object.</returns>
        public static ModAsset Add(string path, ModAsset metadata) {
            path = path.Replace('\\', '/');
                
            if (metadata.Type == null)
                path = GuessType(path, out metadata.Type, out metadata.Format);

            metadata.PathVirtual = path;

            // We want our new mapping to replace the previous one, but need to replace the previous one in the shadow structure.
            ModAsset metadataPrev;
            if (!Map.TryGetValue(path, out metadataPrev))
                metadataPrev = null;

            // Hardcoded case: Handle directories separately.
            else if (metadata.Type == typeof(AssetTypeDirectory))
                MapDirs[path] = metadata;
            else
                Map[path] = metadata;

            // If we're not already the highest level shadow "node"...
            if (path != "") {
                // Add directories automatically.
                string pathDir = Path.GetDirectoryName(path).Replace('\\', '/');
                ModAsset metadataDir;
                if (!MapDirs.TryGetValue(pathDir, out metadataDir)) {
                    metadataDir = new ModAssetBranch {
                        PathVirtual = pathDir,
                        Type = typeof(AssetTypeDirectory)
                    };
                    Add(pathDir, metadataDir);
                }
                // If a previous mapping exists, replace it in the shadow structure.
                int metadataPrevIndex = metadataDir.Children.IndexOf(metadataPrev);
                if (metadataPrevIndex != -1)
                    metadataDir.Children[metadataPrevIndex] = metadata;
                else
                    metadataDir.Children.Add(metadata);
            }

            return metadata;
        }

        /// <summary>
        /// Invoked when GuessType can't guess the asset format / type.
        /// </summary>
        public static event TypeGuesser OnGuessType;
        /// <summary>
        /// Guess the file type and format based on its path. 
        /// </summary>
        /// <param name="file">The relative asset path.</param>
        /// <param name="type">The file type.</param>
        /// <param name="format">The file format (file ending).</param>
        /// <returns>The passed asset path, trimmed if required.</returns>
        public static string GuessType(string file, out Type type, out string format) {
            type = typeof(object);
            format = file.Length < 4 ? null : file.Substring(file.Length - 3);

            if (file.EndsWith(".dll")) {
                type = typeof(AssetTypeAssembly);

            } else if (OnGuessType != null) {
                // Allow mods to parse custom types.
                Delegate[] ds = OnGuessType.GetInvocationList();
                for (int i = 0; i < ds.Length; i++) {
                    Type typeMod;
                    string formatMod;
                    string fileMod = ((TypeGuesser) ds[i])(file, out typeMod, out formatMod);
                    if (fileMod == null || typeMod == null || formatMod == null)
                        continue;
                    file = fileMod;
                    type = typeMod;
                    format = formatMod;
                    break;
                }
            }

            return file;
        }

        /// <summary>
        /// Recrawl all currently loaded mods and recreate the content mappings. If you want to apply the new mapping, call Reprocess afterwards.
        /// </summary>
        public static void Recrawl() {
            Map.Clear();
            MapDirs.Clear();

            for (int i = 0; i < Mods.Count; i++)
                Mods[i]._Crawl();
        }

        /// <summary>
        /// Crawl through the content mod and automatically fill the mod asset map.
        /// </summary>
        /// <param name="meta">The content mod to crawl through.</param>
        public static void Crawl(ModContentSource meta) {
            if (!Mods.Contains(meta))
                Mods.Add(meta);
            meta._Crawl();
        }

        /// <summary>
        /// Reprocess all loaded / previously processed assets, re-applying any changes after a recrawl.
        /// </summary>
        public static void Reprocess() {
            for (int i = 0; i < LoadedAssets.Count; i++) {
                WeakReference weak = LoadedAssets[i];
                if (!weak.IsAlive)
                    continue;
                Process(weak.Target, LoadedAssetFullPaths[i]);
            }
        }

        /// <summary>
        /// Invoked when content is being processed (most likely on load), allowing you to manipulate it.
        /// </summary>
        public static event Func<object, string, object> OnProcess;
        /// <summary>
        /// Process an asset and register it for further reprocessing in the future.
        /// Apply any mod-related changes to the asset based on the existing mod asset meta map.
        /// </summary>
        /// <param name="asset">The asset to process.</param>
        /// <param name="assetNameFull">The "full name" of the asset, preferable the relative asset path.</param>
        /// <returns>The processed asset.</returns>
        public static object Process(object asset, string assetNameFull) {
            string assetName = assetNameFull;
            if (!string.IsNullOrEmpty(PathContentOrig) && assetName.StartsWith(PathContentOrig)) {
                assetName = assetName.Substring(PathContentOrig.Length + 1);
            }

            int loadedIndex = LoadedAssetPaths.IndexOf(assetName);
            if (loadedIndex == -1) {
                LoadedAssetPaths.Add(assetName);
                LoadedAssetFullPaths.Add(assetNameFull);
                LoadedAssets.Add(new WeakReference(asset));
            } else {
                LoadedAssets[loadedIndex] = new WeakReference(asset);
            }

            return OnProcess?.InvokePassing(asset, assetNameFull) ?? asset;
        }

    }
}
