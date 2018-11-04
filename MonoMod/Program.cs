using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MonoMod.Utils;
using System.Reflection;

namespace MonoMod {
    class Program {

#if !MONOMOD_NO_ENTRY
        public static int Main(string[] args) {
            Console.WriteLine("MonoMod " + typeof(Program).GetTypeInfo().Assembly.GetName().Version);

            if (args.Length == 0) {
                Console.WriteLine("No valid arguments (assembly path) passed.");
                if (System.Diagnostics.Debugger.IsAttached) // Keep window open when running in IDE
                    Console.ReadKey();
                return 0;
            }

            string pathIn;
            string pathOut;

            int pathInI = 0;

            for (int i = 0; i < args.Length; i++)
                if (args[i] == "--dependency-missing-throw=0" || args[i] == "--lean-dependencies") {
                    Environment.SetEnvironmentVariable("MONOMOD_DEPENDENCY_MISSING_THROW", "0");
                    pathInI = i + 1;
                } else if (args[i] == "--cleanup=0" || args[i] == "--skip-cleanup") {
                    Environment.SetEnvironmentVariable("MONOMOD_CLEANUP", "0");
                    pathInI = i + 1;
                } else if (args[i] == "--cleanup-all=1" || args[i] == "--cleanup-all") {
                    Environment.SetEnvironmentVariable("MONOMOD_CLEANUP_ALL", "1");
                    pathInI = i + 1;
                } else if (args[i] == "--verbose=1" || args[i] == "--verbose" || args[i] == "-v") {
                    Environment.SetEnvironmentVariable("MONOMOD_LOG_VERBOSE", "1");
                    pathInI = i + 1;
                } else if (args[i] == "--cache=0" || args[i] == "--uncached") {
                    Environment.SetEnvironmentVariable("MONOMOD_RELINKER_CACHED", "0");
                    pathInI = i + 1;
                }

            if (pathInI >= args.Length) {
                Console.WriteLine("No assembly path passed.");
                if (System.Diagnostics.Debugger.IsAttached) // Keep window open when running in IDE
                    Console.ReadKey();
                return 0;
            }

            pathIn = args[pathInI];
            pathOut = args.Length != 1 && pathInI != args.Length - 1 ? args[args.Length - 1] : null;
            pathOut = pathOut ?? Path.Combine(Path.GetDirectoryName(pathIn), "MONOMODDED_" + Path.GetFileName(pathIn));

            if (File.Exists(pathOut)) File.Delete(pathOut);

#if !DEBUG
            try {
#endif
                using (MonoModder mm = new MonoModder() {
                    InputPath = pathIn,
                    OutputPath = pathOut
                }) {
                    mm.Read();

                    if (args.Length <= 2) {
                        mm.Log("[Main] Scanning for mods in directory.");
                        mm.ReadMod(Directory.GetParent(pathIn).FullName);
                    } else {
                        mm.Log("[Main] Reading mods list from arguments.");
                        for (int i = pathInI + 1; i < args.Length - 1; i++)
                            mm.ReadMod(args[i]);
                    }

                    mm.MapDependencies();

                    mm.Log("[Main] mm.AutoPatch();");
                    mm.AutoPatch();

                    mm.Write();

                    mm.Log("[Main] Done.");
                }
#if !DEBUG
            } catch (Exception e) {
                Console.WriteLine(e);
                if (System.Diagnostics.Debugger.IsAttached) // Keep window open when running in IDE
                    Console.ReadKey();
                return -1;
            }
#endif

            if (System.Diagnostics.Debugger.IsAttached) // Keep window open when running in IDE
                Console.ReadKey();
            return 0;
        }
#endif

    }
}
