using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace MonoMod.BaseLoader {
    public abstract class ModAsset {

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

        /// <summary>
        /// Open a stream to read the asset data from.
        /// </summary>
        /// <param name="stream">The resulting stream.</param>
        /// <param name="isSection">Is the stream already a section (SectionOffset and SectionLength)?</param>
        protected abstract void Open(out Stream stream, out bool isSection);

    }

    public sealed class ModAssetBranch : ModAsset {
        protected override void Open(out Stream stream, out bool isSection) {
            throw new InvalidOperationException();
        }
    }

    public class FileSystemModAsset : ModAsset {
        /// <summary>
        /// The path to the source file.
        /// </summary>
        public string PathFile;

        public FileSystemModAsset(string file) {
            PathFile = file;
        }

        protected override void Open(out Stream stream, out bool isSection) {
            if (!File.Exists(PathFile)) {
                stream = null;
                isSection = false;
                return;
            }

            stream = File.OpenRead(PathFile);
            isSection = false;
        }
    }

    public class AssemblyModAsset : ModAsset {
        /// <summary>
        /// The containing assembly.
        /// </summary>
        public Assembly Assembly;
        /// <summary>
        /// The name of the resource in the assembly.
        /// </summary>
        public string ResourceName;

        public AssemblyModAsset(Assembly assembly, string resourceName) {
            Assembly = assembly;
            ResourceName = resourceName;
        }

        protected override void Open(out Stream stream, out bool isSection) {
            stream = Assembly.GetManifestResourceStream(ResourceName);
            isSection = false;
        }
    }
}
