using MonoMod.Utils;
using MonoMod.InlineRT;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace MonoMod.BaseLoader {
    public static class ModManager {

        public static string Name;

        private static string _VersionString = "0.0.0-dev";
        /// <summary>
        /// The currently installed version in string form.
        /// </summary>
        public static string VersionString {
            get {
                return _VersionString;
            }
            set {
                _VersionString = value;

                int versionSplitIndex = _VersionString.IndexOf('-');
                if (versionSplitIndex == -1) {
                    Version = new Version(_VersionString);
                    VersionSuffix = "";
                    VersionTag = "";
                    VersionCommit = "";

                } else {
                    Version = new Version(_VersionString.Substring(0, versionSplitIndex));
                    VersionSuffix = _VersionString.Substring(versionSplitIndex + 1);
                    versionSplitIndex = VersionSuffix.IndexOf('-');
                    if (versionSplitIndex == -1) {
                        VersionTag = VersionSuffix;
                        VersionCommit = "";
                    } else {
                        VersionTag = VersionSuffix.Substring(0, versionSplitIndex);
                        VersionCommit = VersionSuffix.Substring(versionSplitIndex + 1);
                    }
                }
            }
        }

        /// <summary>
        /// The currently installed version.
        /// </summary>
        public static Version Version { get; private set; }
        /// <summary>
        /// The currently installed version suffix. For "1.2.3-a-b", this is "a-b"
        /// </summary>
        public static string VersionSuffix { get; private set; }
        /// <summary>
        /// The currently installed version tag. For "1.2.3-a-b", this is "a"
        /// </summary>
        public static string VersionTag { get; private set; }
        /// <summary>
        /// The currently installed version tag. For "1.2.3-a-b", this is "b"
        /// </summary>
        public static string VersionCommit { get; private set; }

        /// <summary>
        /// The command line arguments passed when launching the game.
        /// </summary>
        public static ReadOnlyCollection<string> Args = new ReadOnlyCollection<string>(new List<string>());

        /// <summary>
        /// A collection of all currently loaded ModBases (mods).
        /// </summary>
        public static ReadOnlyCollection<ModBase> Mods => _Mods.AsReadOnly();
        internal static List<ModBase> _Mods = new List<ModBase>();
        private static List<Type> _ModuleTypes = new List<Type>();
        private static List<IDictionary<string, MethodInfo>> _ModuleMethods = new List<IDictionary<string, MethodInfo>>();
        private static List<IDictionary<string, FastReflectionDelegate>> _ModuleMethodDelegates = new List<IDictionary<string, FastReflectionDelegate>>();

        /// <summary>
        /// The path to the game's directory. Defaults to the entry assembly's directory.
        /// </summary>
        public static string PathGame;
        /// <summary>
        /// The path to the /ModSettings directory.
        /// </summary>
        public static string PathSettings;

        private static bool _Booted;
        public static void Boot(string name, string version, ModBase coreModule) {
            if (_Booted)
                return;
            _Booted = true;

            Name = name;
            VersionString = version;

            Logger.Log(LogLevel.Info, "core", $"Booting {name}");
            Logger.Log(LogLevel.Info, "core", $"Version: {VersionString}");

            if (string.IsNullOrEmpty(PathGame)) {
                PathGame = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            }
            if (string.IsNullOrEmpty(PathSettings)) {
                PathSettings = Path.Combine(PathGame, "ModSettings");
            }
            Directory.CreateDirectory(PathSettings);

            // Automatically load all modules.
            if (coreModule != null) {
                coreModule.Metadata = coreModule.Metadata ?? new ModMetadata {
                    Name = Name,
                    Version = Version
                };
                Register(coreModule);
            }
            ModLoader.LoadAuto();

            // Also let all mods parse the arguments.
            Queue<string> args = new Queue<string>(Args);
            while (args.Count > 0) {
                string arg = args.Dequeue();
                foreach (ModBase mod in Mods) {
                    if (mod.ParseArg(arg, args))
                        break;
                }
            }
        }

        private static bool _Initialized;
        public static void Initialize() {
            if (_Initialized)
                return;
            _Initialized = true;
            Invoke("Initialize");
        }

        /// <summary>
        /// Register a new ModBase (mod) dynamically. Invokes LoadSettings and Load.
        /// </summary>
        /// <param name="module">Mod to register.</param>
        public static void Register(this ModBase module) {
            lock (_Mods) {
                _Mods.Add(module);
                _ModuleTypes.Add(module.GetType());
                _ModuleMethods.Add(new Dictionary<string, MethodInfo>());
                _ModuleMethodDelegates.Add(new Dictionary<string, FastReflectionDelegate>());
            }

            module.LoadSettings();
            module.Load();

            Logger.Log(LogLevel.Info, "core", $"Module {module.Metadata} registered.");

            // Attempt to load mods after their dependencies have been loaded.
            // Only load and lock the delayed list if we're not already loading delayed mods.
            if (Interlocked.CompareExchange(ref ModLoader.DelayedLock, 1, 0) == 0) {
                lock (ModLoader.Delayed) {
                    for (int i = ModLoader.Delayed.Count - 1; i > -1; i--) {
                        ModLoader.DelayedEntry entry = ModLoader.Delayed[i];
                        if (!ModLoader.DependenciesLoaded(entry.Meta))
                            continue;

                        ModLoader.LoadMod(entry.Meta);
                        ModLoader.Delayed.RemoveAt(i);

                        entry.Callback?.Invoke();
                    }
                }
                Interlocked.Decrement(ref ModLoader.DelayedLock);
            }
        }

        /// <summary>
        /// Unregisters an already registered ModBase (mod) dynamically. Invokes Unload.
        /// </summary>
        /// <param name="module"></param>
        public static void Unregister(this ModBase module) {
            module.Unload();

            lock (_Mods) {
                int index = _Mods.IndexOf(module);
                _Mods.RemoveAt(index);
                _ModuleTypes.RemoveAt(index);
                _ModuleMethods.RemoveAt(index);
            }

            Logger.Log(LogLevel.Info, "core", $"Module {module.Metadata} unregistered.");
        }

        // A shared object a day keeps the GC away!
        public static readonly Type[] _EmptyTypeArray = new Type[0];
        public static readonly object[] _EmptyObjectArray = new object[0];

        /// <summary>
        /// Invoke a method in all loaded ModBases.
        /// </summary>
        /// <param name="methodName">The name of the method.</param>
        /// <param name="args">Any arguments to be passed to the methods.</param>
        public static void Invoke(string methodName, params object[] args)
            => InvokeTyped(methodName, null, args);
        /// <summary>
        /// Invoke a method in all loaded ModBases.
        /// </summary>
        /// <param name="methodName">The name of the method.</param>
        /// <param name="argsTypes">The types of the arguments passed to the methods.</param>
        /// <param name="args">Any arguments to be passed to the methods.</param>
        public static void InvokeTyped(string methodName, Type[] argsTypes, params object[] args) {
            if (args == null) {
                args = _EmptyObjectArray;
                if (argsTypes == null)
                    argsTypes = _EmptyTypeArray;
            } else if (argsTypes == null) {
                argsTypes = Type.GetTypeArray(args);
            }

            if (!Debugger.IsAttached) {
                // Fast codepath: DynamicMethodDelegate
                // Unfortunately prevents us from stepping into invoked methods.
                for (int i = 0; i < _Mods.Count; i++) {
                    ModBase module = _Mods[i];
                    IDictionary<string, FastReflectionDelegate> moduleMethods = _ModuleMethodDelegates[i];
                    FastReflectionDelegate method;

                    if (moduleMethods.TryGetValue(methodName, out method)) {
                        if (method == null)
                            continue;
                        method(module, args);
                        continue;
                    }

                    MethodInfo methodInfo = _ModuleTypes[i].GetMethod(methodName, argsTypes);
                    if (methodInfo != null)
                        method = methodInfo.GetFastDelegate();
                    moduleMethods[methodName] = method;
                    if (method == null)
                        continue;

                    method(module, args);
                }

            } else {
                // Slow codepath: MethodInfo.Invoke
                // Doesn't hinder us from stepping into the invoked methods.
                for (int i = 0; i < _Mods.Count; i++) {
                    ModBase module = _Mods[i];
                    IDictionary<string, MethodInfo> moduleMethods = _ModuleMethods[i];
                    MethodInfo methodInfo;

                    if (moduleMethods.TryGetValue(methodName, out methodInfo)) {
                        if (methodInfo == null)
                            continue;
                        methodInfo.Invoke(module, args);
                        continue;
                    }

                    methodInfo = _ModuleTypes[i].GetMethod(methodName, argsTypes);
                    moduleMethods[methodName] = methodInfo;
                    if (methodInfo == null)
                        continue;

                    methodInfo.Invoke(module, args);
                }
            }
        }

    }
}
