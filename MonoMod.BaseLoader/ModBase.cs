using MonoMod.Utils;
using MonoMod.InlineRT;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using YamlDotNet.Serialization;

namespace MonoMod.BaseLoader {
    /// <summary>
    /// Your main mod class inherits from this class.
    /// </summary>
    public abstract class ModBase {

        /// <summary>
        /// Used by the loader itself to store any module metadata.
        /// 
        /// The metadata is usually parsed from meta.yaml in the archive.
        /// 
        /// You can override this property to provide dynamic metadata at runtime.
        /// Note that this doesn't affect mod loading.
        /// </summary>
        public virtual ModMetadata Metadata { get; set; }

        /// <summary>
        /// The type used for the settings object. Used for serialization, among other things.
        /// </summary>
        public abstract Type SettingsType { get; }
        /// <summary>
        /// Any settings stored across runs. The mod loader loads this before Load gets invoked.
        /// Define your custom property returning _Settings typecasted as your custom settings type.
        /// </summary>
        public virtual ModSettings _Settings { get; set; }

        /// <summary>
        /// Load the mod settings. Loads the settings from {ModManager.PathSettings}/{Metadata.Name}.yaml by default.
        /// </summary>
        public virtual void LoadSettings() {
            if (SettingsType == null)
                return;

            _Settings = (ModSettings) SettingsType.GetConstructor(ModManager._EmptyTypeArray).Invoke(ModManager._EmptyObjectArray);

            string extension = ".yaml";

            string path = Path.Combine(ModManager.PathSettings, Metadata.Name + extension);
            if (!File.Exists(path))
                return;

            try {
                using (Stream stream = File.OpenRead(path))
                using (StreamReader reader = new StreamReader(path))
                    _Settings = (ModSettings) YamlHelper.Deserializer.Deserialize(reader, SettingsType);
            } catch {
            }
        }

        /// <summary>
        /// Save the mod settings. Saves the settings to {ModManager.PathSettings}/{Metadata.Name}.yaml by default.
        /// </summary>
        public virtual void SaveSettings() {
            if (SettingsType == null || _Settings == null)
                return;

            string extension = ".yaml";

            string path = Path.Combine(ModManager.PathSettings, Metadata.Name + extension);
            if (File.Exists(path))
                File.Delete(path);

            using (Stream stream = File.OpenWrite(path))
            using (StreamWriter writer = new StreamWriter(stream))
                YamlHelper.Serializer.Serialize(writer, _Settings, SettingsType);
        }

        /// <summary>
        /// Perform any initializing actions after all mods have been loaded.
        /// Do not depend on any specific order in which the mods get initialized.
        /// </summary>
        public abstract void Load();

        /// <summary>
        /// Perform any initializing actions after the game has been initialized.
        /// Do not depend on any specific order in which the mods get initialized.
        /// </summary>
        public virtual void Initialize() {
        }

        /// <summary>
        /// Perform any content loading actions after the game's content has been loaded.
        /// </summary>
        /// <param name="firstLoad">Is this the first load?</param>
        public virtual void LoadContent(bool firstLoad) {
        }

        /// <summary>
        /// Unload any unmanaged resources allocated by the mod (f.e. textures) and
        /// undo any changes performed by the mod.
        /// </summary>
        public abstract void Unload();

        /// <summary>
        /// Parse the current command-line argument and any follow-ups.
        /// </summary>
        /// <param name="arg">The current command line argument.</param>
        /// <param name="args">Any further arguments the mod may want to dequeue and parse.</param>
        /// <returns>True if the argument "belongs" to the mod, false otherwise.</returns>
        public virtual bool ParseArg(string arg, Queue<string> args) {
            return false;
        }

    }
}
