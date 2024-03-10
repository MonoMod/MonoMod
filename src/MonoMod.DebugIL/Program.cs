using System;
using System.IO;

namespace MonoMod.DebugIL
{
    public sealed class Program
    {

        public static void Main(string[] args)
        {
            Console.WriteLine("MonoMod.DebugIL " + typeof(Program).Assembly.GetName().Version);

            if (args.Length == 0)
            {
                Console.WriteLine("No valid arguments (assembly path) passed.");
                if (System.Diagnostics.Debugger.IsAttached) // Keep window open when running in IDE
                    Console.ReadKey();
                return;
            }

            string pathIn;
            string pathOut;

            var pathInI = 0;

            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--relative")
                {
                    Environment.SetEnvironmentVariable("MONOMOD_DEBUGIL_RELATIVE", "1");
                    pathInI = i + 1;

                }
                else if (args[i] == "--skip-maxstack")
                {
                    Environment.SetEnvironmentVariable("MONOMOD_DEBUGIL_SKIP_MAXSTACK", "1");
                    pathInI = i + 1;

                }
                else if (args[i] == "--diff")
                {
                    Environment.SetEnvironmentVariable("MONOMOD_DEBUGIL_RELATIVE", "1");
                    Environment.SetEnvironmentVariable("MONOMOD_DEBUGIL_SKIP_MAXSTACK", "1");
                    pathInI = i + 1;
                }
            }

            if (pathInI >= args.Length)
            {
                Console.WriteLine("No assembly path passed.");
                if (System.Diagnostics.Debugger.IsAttached) // Keep window open when running in IDE
                    Console.ReadKey();
                return;
            }

            pathIn = args[pathInI];
            pathOut = args.Length != 1 && pathInI != args.Length - 1 ? args[args.Length - 1] : null;

            pathOut = pathOut ?? Path.Combine(Path.GetDirectoryName(pathIn), "MMDBGIL_" + Path.GetFileName(pathIn));

            using (var mm = new MonoModder()
            {
                InputPath = pathIn,
                OutputPath = pathOut
            })
            {
                mm.Read();

                DebugILGenerator.Generate(mm);

                mm.Write();

                mm.Log("[DbgILGen] Done.");
            }

            if (System.Diagnostics.Debugger.IsAttached) // Keep window open when running in IDE
                Console.ReadKey();
        }

    }
}
