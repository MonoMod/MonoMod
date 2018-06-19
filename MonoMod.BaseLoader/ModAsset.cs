using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace MonoMod.BaseLoader {
    public abstract class ModAsset {

        /// <summary>
        /// The mod asset's source.
        /// </summary>
        public ModContent Source;

        /// <summary>
        /// The type matching the mod asset.
        /// </summary>
        public Type Type = null;
        /// <summary>
        /// The original file extension.
        /// </summary>
        public string Format = null;

        /// <summary>
        /// The virtual / mapped asset path.
        /// </summary>
        public string PathVirtual;

        /// <summary>
        /// The "children" assets in f.e. directory type "assets."
        /// </summary>
        public List<ModAsset> Children = new List<ModAsset>();

        /// <summary>
        /// If the asset is a section of a larger file, the asset starting offset.
        /// </summary>
        public long SectionOffset;
        /// <summary>
        /// If the asset is a section of a larger file, the asset length.
        /// </summary>
        public int SectionLength;

        /// <summary>
        /// A stream to read the asset data from.
        /// </summary>
        public virtual Stream Stream {
            get {
                Stream stream;
                bool isSection;
                Open(out stream, out isSection);

                if (stream == null || SectionLength == 0 || isSection)
                    return stream;
                return new LimitedStream(stream, SectionOffset, SectionLength);
            }
        }

        /// <summary>
        /// The contents of the asset.
        /// </summary>
        public virtual byte[] Data {
            get {
                using (Stream stream = Stream) {
                    if (stream is MemoryStream) {
                        return ((MemoryStream) stream).GetBuffer();
                    }

                    using (MemoryStream ms = new MemoryStream()) {
                        byte[] buffer = new byte[2048];
                        int read;
                        while (0 < (read = stream.Read(buffer, 0, buffer.Length))) {
                            ms.Write(buffer, 0, read);
                        }
                        return ms.ToArray();
                    }
                }
            }
        }

        protected ModAsset(ModContent source) {
            Source = source;
        }

        /// <summary>
        /// Open a stream to read the asset data from.
        /// </summary>
        /// <param name="stream">The resulting stream.</param>
        /// <param name="isSection">Is the stream already a section (SectionOffset and SectionLength)?</param>
        protected abstract void Open(out Stream stream, out bool isSection);

        /// <summary>
        /// Deserialize the asset using a deserializer based on the AssetType (f.e. AssetTypeYaml -> YamlDotNet).
        /// </summary>
        /// <typeparam name="T">The target type.</typeparam>
        /// <param name="result">The asset in its deserialized (object) form.</param>
        /// <returns>True if deserializing the asset succeeded, false otherwise.</returns>
        public bool TryDeserialize<T>(out T result) {
            if (Type == typeof(AssetTypeYaml)) {
                using (StreamReader reader = new StreamReader(Stream))
                    result = YamlHelper.Deserializer.Deserialize<T>(reader);
                return true;
            }

            // TODO: Deserialize AssetTypeXml

            result = default(T);
            return false;
        }

        /// <summary>
        /// Deserialize the asset using a deserializer based on the AssetType (f.e. AssetTypeYaml -> YamlDotNet).
        /// </summary>
        /// <typeparam name="T">The target type.</typeparam>
        /// <returns>The asset in its deserialized (object) form or default(T).</returns>
        public T Deserialize<T>() {
            T result;
            TryDeserialize(out result);
            return result;
        }

        /// <summary>
        /// Deserialize this asset's matching .meta asset. Uses TryDeserialize internally.
        /// </summary>
        /// <typeparam name="T">The target meta type.</typeparam>
        /// <param name="meta">The requested meta object.</param>
        /// <returns>True if deserializing the meta asset succeeded, false otherwise.</returns>
        public bool TryGetMeta<T>(out T meta) {
            ModAsset metaAsset;
            if (ModContentManager.TryGet(PathVirtual + ".meta", out metaAsset) &&
                metaAsset.TryDeserialize(out meta)
            )
                return true;
            meta = default(T);
            return false;
        }

        /// <summary>
        /// Deserialize this asset's matching .meta asset. Uses TryDeserialize internally.
        /// </summary>
        /// <typeparam name="T">The target meta type.</typeparam>
        /// <returns>The requested meta object or default(T).</returns>
        public T GetMeta<T>() {
            T meta;
            TryGetMeta(out meta);
            return meta;
        }

    }

    public abstract class ModAsset<T> : ModAsset where T : ModContent {
        public new T Source => base.Source as T;
        protected ModAsset(T source)
            : base(source) {
        }
    }

    public sealed class ModAssetBranch : ModAsset {
        public ModAssetBranch()
            : base(null) {
        }

        protected override void Open(out Stream stream, out bool isSection) {
            throw new InvalidOperationException();
        }
    }

    public class FileSystemModAsset : ModAsset<FileSystemModContent> {
        /// <summary>
        /// The path to the source file.
        /// </summary>
        public string Path;

        public FileSystemModAsset(FileSystemModContent source, string path)
            : base(source) {
            Path = path;
        }

        protected override void Open(out Stream stream, out bool isSection) {
            if (!File.Exists(Path)) {
                stream = null;
                isSection = false;
                return;
            }

            stream = File.OpenRead(Path);
            isSection = false;
        }
    }

    public class AssemblyModAsset : ModAsset<AssemblyModContent> {
        /// <summary>
        /// The name of the resource in the assembly.
        /// </summary>
        public string ResourceName;

        public AssemblyModAsset(AssemblyModContent source, string resourceName)
            : base(source) {
            ResourceName = resourceName;
        }

        protected override void Open(out Stream stream, out bool isSection) {
            stream = Source.Assembly.GetManifestResourceStream(ResourceName);
            isSection = false;
        }
    }

#if MONOMOD_BASELOADER_ZIP
    public class ZipModAsset : ModAsset<ZipModContent> {
        /// <summary>
        /// The path to the source file inside the archive.
        /// </summary>
        public string Path;

        public ZipModAsset(ZipModContent source, string path)
            : base(source) {
            Path = path;
        }

        protected override void Open(out Stream stream, out bool isSection) {
            string file = Path.Replace('\\', '/');
            using (ZipFile zip = new ZipFile(Source.Path)) {
                foreach (ZipEntry entry in zip.Entries) {
                    if (entry.FileName.Replace('\\', '/') == file) {
                        stream = entry.ExtractStream();
                        isSection = false;
                        return;
                    }
                }
            }

            throw new KeyNotFoundException($"{GetType().Name} {Path} not found in archive {Source.Path}");
        }
    }
#endif
}
