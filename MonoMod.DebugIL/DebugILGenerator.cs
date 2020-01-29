#if !CECIL0_9
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MonoMod.DebugIL {
    public class DebugILGenerator {

        public static readonly System.Reflection.ConstructorInfo m_DebuggableAttribute_ctor =
            typeof(DebuggableAttribute).GetConstructor(new Type[] { typeof(DebuggableAttribute.DebuggingModes) });

        public MonoModder Modder;

        public bool Relative = false;
        public bool SkipMaxStack = false;

        public string OutputPath;

        public DebugILGenerator(MonoModder modder) {
            Modder = modder;

            OutputPath = Path.GetFullPath(modder.OutputPath);
            modder.OutputPath = Path.Combine(OutputPath, Path.GetFileName(modder.InputPath));

            Relative = Environment.GetEnvironmentVariable("MONOMOD_DEBUGIL_RELATIVE") == "1";
            SkipMaxStack = Environment.GetEnvironmentVariable("MONOMOD_DEBUGIL_SKIP_MAXSTACK") == "1";
        }

        public static void Generate(MonoModder modder)
            => new DebugILGenerator(modder).Generate();

        public void Generate() {
            if (Directory.Exists(OutputPath)) {
                Console.WriteLine($"[MonoMod] [DbgIlGen] Clearing {OutputPath}");
                Directory.Delete(OutputPath, true);
            }

            CustomAttribute debuggable = Modder.Module.Assembly.GetCustomAttribute("System.Diagnostics.DebuggableAttribute");
            if (debuggable != null)
                Modder.Module.Assembly.CustomAttributes.Remove(debuggable);
            debuggable = new CustomAttribute(Modder.Module.ImportReference(m_DebuggableAttribute_ctor));
            debuggable.ConstructorArguments.Add(new CustomAttributeArgument(
                Modder.Module.ImportReference(typeof(DebuggableAttribute.DebuggingModes)),
                DebuggableAttribute.DebuggingModes.Default |
                DebuggableAttribute.DebuggingModes.DisableOptimizations |
                DebuggableAttribute.DebuggingModes.EnableEditAndContinue
            ));
            Modder.Module.Assembly.CustomAttributes.Add(debuggable);

            Console.WriteLine($"[MonoMod] [DbgIlGen] Enqueueing dumping tasks...");

            List<Task> tasksL = new List<Task>();

            tasksL.Add(Task.Factory.StartNew(() => {
                DumpMetadata(OutputPath);
            }));

            Dictionary<string, int> indices = new Dictionary<string, int>();

            // GetTypes includes nested types.
            foreach (TypeDefinition type in Modder.Module.GetTypes()) {
                // Modder.Log($"[DbgIlGen] Enqueueing {type.FullName}");

                string path = Path.Combine(
                    OutputPath,
                    string.Join(
                        Path.DirectorySeparatorChar.ToString(),
                        type.FullName
                            .Split('.', '/').Select(
                                part => DebugILWriter.PathVerifyRegex.Replace(part, "_")
                            ).ToArray()
                    )
                );
                
                tasksL.Add(Task.Factory.StartNew(() => {
                    DumpType(path, type);
                }));

                foreach (MethodDefinition method in type.Methods) {
                    if (indices.TryGetValue(method.Name, out int index)) {
                        indices[method.Name] = ++index;
                    } else {
                        index = 0;
                        indices[method.Name] = 0;
                    }
                    tasksL.Add(Task.Factory.StartNew(() => {
                        DumpMethod(path, index, method);
                    }));
                }
                indices.Clear();
            }

            Task[] tasksA = tasksL.ToArray();
            int completed;
            int all = tasksA.Length;
            while ((completed = tasksL.Count(t => t.IsCompleted)) < all) {
                Console.Write($"[MonoMod] [DbgIlGen] {completed} / {all} task{(all != 1 ? "s" : "")} finished        \r");
                Task.WaitAny(tasksA, -1);
            }
            Console.Write($"[MonoMod] [DbgIlGen] {all} / {all} task{(all != 1 ? "s" : "")} finished        \r");
            Console.WriteLine();
        }

        public void DumpMetadata(string path) {
            using (DebugILWriter writer = new DebugILWriter(path, "__AssemblyInfo__")) {
                writer.WriteLine("// MonoMod DebugILGenerator");
                writer.WriteLine($"// MonoMod Version: {MonoModder.Version}");
                writer.WriteLine();

                writer.WriteLine("// Input assembly:");
                writer.WriteLine($"// {Modder.Module.Assembly.Name.FullName}");
                writer.WriteLine($"// {Modder.InputPath}");
                writer.WriteLine();

                writer.WriteLine("// Assembly references:");
                foreach (AssemblyNameReference dep in Modder.Module.AssemblyReferences) {
                    writer.WriteLine($"// {dep.FullName}");
                }
                writer.WriteLine();

                // TODO: [DbgILGen] Other assembly metadata?
            }
        }

        public void DumpType(string path, TypeDefinition type) {
            using (DebugILWriter writer = new DebugILWriter(path, "__TypeInfo__")) {
                writer.WriteLine("// MonoMod DebugILGenerator");
                writer.WriteLine($"// Type: ({type.Attributes.ToString()}) {type.FullName}");
                writer.WriteLine();

                writer.WriteLine("// Fields:");
                foreach (FieldDefinition member in type.Fields) {
                    writer.WriteLine($"// ({member.Attributes}) {member.FieldType.FullName} {member.Name}");
                }
                writer.WriteLine();

                writer.WriteLine("// Properties:");
                foreach (PropertyDefinition member in type.Properties) {
                    writer.WriteLine($"// ({member.Attributes}) {member.PropertyType.FullName} {member.Name}");
                }
                writer.WriteLine();

                writer.WriteLine("// Events:");
                foreach (EventDefinition member in type.Events) {
                    writer.WriteLine($"// ({member.Attributes}) {member.EventType.FullName} {member.Name}");
                }
                writer.WriteLine();

                // TODO: [DbgILGen] Other type metadata?
            }
        }

        public void DumpMethod(string parent, int index, MethodDefinition method) {
            method.NoInlining = true;
            method.NoOptimization = true;

            using (DebugILWriter writer = new DebugILWriter(parent, method.Name, index)) {
                writer.WriteLine("// MonoMod DebugILGenerator");
                writer.WriteLine($"// Method: ({method.Attributes}) {method.GetID()}");
                writer.WriteLine();

                // TODO: [DbgILGen] Other method metadata?

                writer.WriteLine("// Body:");
                if (!method.HasBody) {
                    writer.WriteLine("// No body found.");
                    writer.WriteLine();
                    return;
                }

                // TODO: [DbgILGen] Method body metadata?

                if (!SkipMaxStack)
                    writer.WriteLine($".maxstack {method.Body.MaxStackSize}");

                // Always assure a debug scope exists!
                method.DebugInformation.GetOrAddScope().Variables.Clear();
                if (method.Body.HasVariables) {
                    writer.WriteLine(method.Body.InitLocals ? ".locals init (" : ".locals (");

                    for (int i = 0; i < method.Body.Variables.Count; i++) {
                        VariableDefinition vd = method.Body.Variables[i];
                        string name = vd.GenerateVariableName(method, i);
                        method.DebugInformation.GetOrAddScope().Variables.Add(new VariableDebugInformation(vd, name));
                        writer.WriteLine($"    [{i}] {(!vd.VariableType.IsPrimitive && !vd.VariableType.IsValueType ? "class " : "")} {vd.VariableType.FullName} {name} {(i < method.Body.Variables.Count - 1 ? "," : "")}");
                    }
                    writer.WriteLine(")");
                }

                writer.WriteLine("// Code:");
                method.DebugInformation.SequencePoints.Clear();
                Document symbolDoc = new Document(writer.FullPath) {
                    LanguageVendor = DocumentLanguageVendor.Microsoft,
                    Language = DocumentLanguage.CSharp, // Even Visual Studio can't deal with Cil!
                    HashAlgorithm = DocumentHashAlgorithm.None,
                    Type = DocumentType.Text
                };

                ILProcessor il = method.Body.GetILProcessor();
                for (int instri = 0; instri < method.Body.Instructions.Count; instri++) {
                    Instruction instr = method.Body.Instructions[instri];
                    string instrStr = Relative ? instr.ToRelativeString() : instr.ToString();

                    method.DebugInformation.SequencePoints.Add(
                        new SequencePoint(instr, symbolDoc) {
                            StartLine = writer.Line,
                            StartColumn = 1,
                            EndLine = writer.Line,
                            EndColumn = instrStr.Length + 1
                        }
                    );

                    writer.WriteLine(instrStr);
                }

                writer.WriteLine();
            }
        }

    }
}
#endif
