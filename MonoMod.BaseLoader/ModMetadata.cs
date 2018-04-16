using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.BaseLoader {
    /// <summary>
    /// Any module metadata, usually mirroring the data in your metadata.yaml
    /// </summary>
    public class ModMetadata {

        /// <summary>
        /// The path to the mod container, be it a directory, .zip file or something else. Set at runtime.
        /// </summary>
        public virtual string Container { get; set; }

        /// <summary>
        /// The name of the mod.
        /// </summary>
        public virtual string Name { get; set; }

        /// <summary>
        /// The mod version.
        /// </summary>
        public virtual Version Version { get; set; } = new Version(1, 0);

        /// <summary>
        /// The path of the mod .dll inside the container or the absolute file path.
        /// </summary>
        public virtual string DLL { get; set; }

        /// <summary>
        /// Whether the mod has been prelinked or not.
        /// If you don't know what prelinked mods are, don't touch this field.
        /// </summary>
        public virtual bool Prelinked { get; set; } = false;

        /// <summary>
        /// The dependencies of the mod.
        /// </summary>
        public virtual List<ModMetadata> Dependencies { get; set; } = new List<ModMetadata>();

        public virtual Stream OpenStream(out string result, params string[] names) {
            result = null;
            return null;
        }

        public virtual Assembly OpenAssembly(string name) {
            return null;
        }

        public override string ToString() {
            return Name + " " + Version;
        }

        /// <summary>
        /// Perform a few basic post-parsing operations. For example, make the DLL path absolute if the mod is in a directory.
        /// </summary>
        public virtual void PostParse() {
            // Add dependency to API 1.0 if missing.
            bool dependsOnAPI = false;
            foreach (ModMetadata dep in Dependencies) {
                if (dep.Name == "API" ||
                    dep.Name == ModManager.Name) {
                    dependsOnAPI = true;
                    break;
                }
            }
            if (!dependsOnAPI) {
                Logger.Log(LogLevel.Warn, "loader", $"No dependency to API found in {ToString()}! Adding dependency to API 1.0...");
                Dependencies.Insert(0, new ModMetadata() {
                    Name = "API",
                    Version = new Version(1, 0)
                });
            }
        }
    }

    public class DirectoryModMetadata : ModMetadata {
        public override Stream OpenStream(out string result, params string[] names) {
            foreach (string name in names) {
                string path = name;
                if (!File.Exists(path))
                    path = Path.Combine(Container, name);
                if (!File.Exists(path))
                    continue;
                result = path;
                return File.OpenRead(path);
            }

            result = null;
            return null;
        }

        public override Assembly OpenAssembly(string name) {
            string asmPath = Path.Combine(Container, name + ".dll");
            if (!File.Exists(asmPath))
                return null;
            return Assembly.LoadFrom(asmPath);
        }

        public override void PostParse() {
            if (!string.IsNullOrEmpty(Container) && !File.Exists(DLL))
                DLL = Path.Combine(Container, DLL.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));

            base.PostParse();
        }
    }
}
