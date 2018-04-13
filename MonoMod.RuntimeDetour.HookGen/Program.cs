using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MonoMod.RuntimeDetour.HookGen {
    class Program {
        static void Main(string[] args) {
            Console.WriteLine("MonoMod.RuntimeDetour.HookGen " + typeof(Program).Assembly.GetName().Version);
            Console.WriteLine("using MonoMod " + typeof(MonoModder).Assembly.GetName().Version);
            Console.WriteLine("using MonoMod.RuntimeDetour " + typeof(Detour).Assembly.GetName().Version);

            if (args.Length == 0) {
                Console.WriteLine("No valid arguments (assembly path) passed.");
                if (System.Diagnostics.Debugger.IsAttached) // Keep window open when running in IDE
                    Console.ReadKey();
                return;
            }

            string pathIn;
            string pathOut;

            int pathInI = 0;

            for (int i = 0; i < args.Length; i++) {
                // TODO: Parse args.
            }

            if (pathInI >= args.Length) {
                Console.WriteLine("No assembly path passed.");
                if (System.Diagnostics.Debugger.IsAttached) // Keep window open when running in IDE
                    Console.ReadKey();
                return;
            }

            pathIn = args[pathInI];
            pathOut = args.Length != 1 && pathInI != args.Length - 1 ? args[args.Length - 1] : null;

            pathOut = pathOut ?? Path.Combine(Path.GetDirectoryName(pathIn), "MMHOOK_" + Path.ChangeExtension(Path.GetFileName(pathIn), "dll"));

            using (MonoModder mm = new MonoModder() {
                InputPath = pathIn,
                OutputPath = pathOut
            }) {
                mm.Read();

                mm.MapDependencies();

                if (File.Exists(pathOut)) {
                    mm.Log($"Clearing {pathOut}");
                    File.Delete(pathOut);
                }

                mm.Log("[HookGen] Starting HookGenerator");
                HookGenerator gen = new HookGenerator(mm);
                using (ModuleDefinition mOut = gen.OutputModule) {
                    gen.Generate();
                    mOut.Write(pathOut);
                }

                mm.Log("[HookGen] Done.");
            }

            if (System.Diagnostics.Debugger.IsAttached) // Keep window open when running in IDE
                Console.ReadKey();
            return;
        }
    }
}
