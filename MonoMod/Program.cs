using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MonoMod.MonoMod {
    class Program {

#if !MONOMOD_NO_ENTRY
        public static void Main(string[] args) {
            Console.WriteLine("MonoMod " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);

            if (args.Length == 0) {
                Console.WriteLine("No valid arguments (assembly path) passed.");
                if (System.Diagnostics.Debugger.IsAttached) // Keep window open when running in IDE
                    Console.ReadKey();
                return;
            }

            string pathIn = args[0];
            string pathOut = args.Length != 1 ? args[args.Length - 1] : ("MONOMODDED_" + pathIn);

            if (File.Exists(pathOut)) File.Delete(pathOut);

            using (MonoModder mm = new MonoModder() {
                Input = File.OpenRead(args[0]),
                Output = File.OpenWrite(pathOut)
            }) {
                if (args.Length <= 2) {
                    mm.Log("[Main] Scanning for mods in directory.");
                    mm.ReadMod(Directory.GetParent(pathIn).FullName);
                } else {
                    mm.Log("[Main] Reading mods list from arguments.");
                    for (int i = 1; i < args.Length - 1; i++)
                        mm.ReadMod(args[i]);
                }

                mm.Log("[Main] mm.AutoPatch();");
                mm.AutoPatch();
            }
        }
#endif

    }
}
