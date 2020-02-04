using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MonoMod.Utils;
using System.Reflection;

namespace MonoMod.DebugIL {
    class Program {

        public static void Main(string[] args) {
#if CECIL0_9
            throw new NotSupportedException();
#else
            Console.WriteLine("MonoMod.DebugIL " + typeof(Program).Assembly.GetName().Version);

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
                if (args[i] == "--relative") {
                    Environment.SetEnvironmentVariable("MONOMOD_DEBUGIL_RELATIVE", "1");
                    pathInI = i + 1;

                } else if (args[i] == "--skip-maxstack") {
                    Environment.SetEnvironmentVariable("MONOMOD_DEBUGIL_SKIP_MAXSTACK", "1");
                    pathInI = i + 1;

                } else if (args[i] == "--diff") {
                    Environment.SetEnvironmentVariable("MONOMOD_DEBUGIL_RELATIVE", "1");
                    Environment.SetEnvironmentVariable("MONOMOD_DEBUGIL_SKIP_MAXSTACK", "1");
                    pathInI = i + 1;
                } else if (args[i] == "--pdb") {
                    Environment.SetEnvironmentVariable("MONOMOD_DEBUGIL_FORMAT", "PDB");
                    pathInI = i + 1;
                } else if (args[i] == "--mdb") {
                    Environment.SetEnvironmentVariable("MONOMOD_DEBUGIL_FORMAT", "MDB");
                    pathInI = i + 1;
                }
            }

            var debugFormat = DebugSymbolFormat.Auto;

            var envDebugFormat = Environment.GetEnvironmentVariable("MONOMOD_DEBUGIL_FORMAT");
            if (envDebugFormat != null) {
                envDebugFormat = envDebugFormat.ToLowerInvariant();
                if (envDebugFormat == "pdb")
                    debugFormat = DebugSymbolFormat.PDB;
                else if (envDebugFormat == "mdb")
                    debugFormat = DebugSymbolFormat.MDB;
            }

            if (pathInI >= args.Length) {
                Console.WriteLine("No assembly path passed.");
                if (System.Diagnostics.Debugger.IsAttached) // Keep window open when running in IDE
                    Console.ReadKey();
                return;
            }

            pathIn = args[pathInI];
            pathOut = args.Length != 1 && pathInI != args.Length - 1 ? args[args.Length - 1] : null;

            pathOut = pathOut ?? Path.Combine(Path.GetDirectoryName(pathIn), "MMDBGIL_" + Path.GetFileName(pathIn));

            using (MonoModder mm = new MonoModder() {
                DebugSymbolOutputFormat = debugFormat,
                InputPath = pathIn,
                OutputPath = pathOut
            }) {
                mm.Read();

                DebugILGenerator.Generate(mm);

                mm.Write();

                mm.Log("[DbgILGen] Done.");
            }

            if (System.Diagnostics.Debugger.IsAttached) // Keep window open when running in IDE
                Console.ReadKey();
#endif
        }

    }
}
