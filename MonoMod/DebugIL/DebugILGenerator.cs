using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MonoMod.DebugIL {
    public class DebugILGenerator {

        public readonly static Regex PathVerifyRegex =
            new Regex("[" + Regex.Escape(new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars())) + "]", RegexOptions.Compiled);
        public readonly static System.Reflection.ConstructorInfo m_DebuggableAttribute_ctor =
            typeof(DebuggableAttribute).GetConstructor(new Type[] { typeof(DebuggableAttribute.DebuggingModes) });

        public MonoModder Modder;

        public bool Relative = false;

        public string OutputPath;
        public Stack<string> CurrentPath = new Stack<string>();
        public string FullPath {
            get {
                string path = OutputPath;
                // Incorrect order
                /*
                foreach (string part in CurrentPath)
                    path = Path.Combine(path, part);
                */
                string[] pathSplit = CurrentPath.ToArray();
                for (int i = pathSplit.Length - 1; i >= 0; i--)
                    path = Path.Combine(path, pathSplit[i]);
                return path;
            }
        }

        // Could be easily used to keep track of line # when printing all methods in one .il
        public int Line;

        public DebugILGenerator(MonoModder modder) {
            Modder = modder;

            OutputPath = Path.GetFullPath(modder.OutputPath);
            modder.OutputPath = Path.Combine(OutputPath, Path.GetFileName(modder.InputPath));

            Relative = Environment.GetEnvironmentVariable("MONOMOD_DEBUGIL_RELATIVE") == "1";
        }

        public static void DeleteRecursive(string path) {
            foreach (string dir in Directory.GetDirectories(path))
                DeleteRecursive(dir);

            foreach (string file in Directory.GetFiles(path))
                File.Delete(file);

            Directory.Delete(path);
            Thread.Sleep(0); // Required to stay in sync with filesystem... thanks, .NET Framework!
        }

        public static void Generate(MonoModder modder)
            => new DebugILGenerator(modder).Generate();

        public void Log(object obj) {
            Log(obj.ToString());
        }

        public void Log(string txt) {
            if (Modder.Logger != null) {
                Modder.Logger(txt);
                return;
            }
            Console.Write("[MonoMod] [DbgILGen] ");
            Console.WriteLine(txt);
        }

        public void Generate() {
            if (Directory.Exists(FullPath)) {
                Log($"Clearing {FullPath}");
                DeleteRecursive(FullPath);
            }

            Directory.CreateDirectory(FullPath);
            Thread.Sleep(0); // Required to stay in sync with filesystem... thanks, .NET Framework!

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
            Modder.Module.Assembly.AddAttribute(debuggable);

            GenerateMetadata();

            foreach (TypeDefinition type in Modder.Module.Types)
                GenerateFor(type);
        }

        public void GenerateMetadata() {
            CurrentPath.Push("AssemblyInfo.il");
            using (StreamWriter writer = new StreamWriter(FullPath)) {
                writer.WriteLine("// MonoMod DebugILGenerator");
                writer.Write("// MonoMod Version: ");
                writer.WriteLine(MonoModder.Version);
                writer.WriteLine();

                writer.WriteLine("// Input assembly:");
                writer.Write("// ");
                writer.WriteLine(Modder.Module.Assembly.Name.FullName);
                writer.Write("// ");
                writer.WriteLine(Modder.InputPath);
                writer.WriteLine();

                writer.WriteLine("// Assembly references:");
                foreach (AssemblyNameReference dep in Modder.Module.AssemblyReferences) {
                    writer.Write("// ");
                    writer.WriteLine(dep.FullName);
                }
                writer.WriteLine();

                // TODO: [DbgILGen] Other assembly metadata?

                writer.WriteLine();
            }
            CurrentPath.Pop();
        }

        public void GenerateFor(TypeDefinition type) {
            int namespaceDepth = 0;
            if (type.DeclaringType == null && !string.IsNullOrEmpty(type.Namespace)) {
                string[] namespacePath = type.Namespace.Split('.');
                namespaceDepth = namespacePath.Length;
                CurrentPath.PushRange(namespacePath);
            }
            CurrentPath.Push(PathVerifyRegex.Replace(type.Name, ""));
            Directory.CreateDirectory(FullPath);

            Log($"Generating for type {type.FullName}");

            CurrentPath.Push("TypeInfo.il");
            using (StreamWriter writer = new StreamWriter(FullPath)) {
                writer.WriteLine("// MonoMod DebugILGenerator");
                writer.Write("// Type: (");
                writer.Write(type.Attributes);
                writer.Write(") ");
                writer.WriteLine(type.FullName);
                writer.WriteLine();

                writer.WriteLine("// Fields:");
                foreach (FieldDefinition field in type.Fields) {
                    writer.Write("// (");
                    writer.Write(field.Attributes);
                    writer.Write(") ");
                    writer.Write(field.FieldType.FullName);
                    writer.Write(" ");
                    writer.WriteLine(field.Name);
                }
                writer.WriteLine();

                // TODO: [DbgILGen] Other type metadata?

                writer.WriteLine();
            }
            CurrentPath.Pop();

            foreach (MethodDefinition method in type.Methods)
                GenerateFor(method);

            foreach (TypeDefinition nested in type.NestedTypes)
                GenerateFor(nested);

            CurrentPath.Pop();
            CurrentPath.PopRange(namespaceDepth);
        }

        public void GenerateFor(MethodDefinition method) {
            string nameEscaped = PathVerifyRegex.Replace(method.Name, "");
            CurrentPath.Push(nameEscaped + ".il");
            int pathCollision = 0;
            while (File.Exists(FullPath)) {
                pathCollision++;
                CurrentPath.Pop();
                CurrentPath.Push(nameEscaped + "." + pathCollision + ".il");
            }

            method.NoInlining = true;
            method.NoOptimization = true;

            using (StreamWriter writer = new StreamWriter(FullPath)) {
                Line = 1;
                writer.WriteLine("// MonoMod DebugILGenerator"); Line++;
                writer.Write("// Method: (");
                writer.Write(method.Attributes);
                writer.Write(") ");
                writer.WriteLine(method.GetFindableID()); Line++;
                writer.WriteLine(); Line++;

                // TODO: [DbgILGen] Other method metadata?

                writer.WriteLine("// Body:"); Line++;
                if (!method.HasBody) {
                    writer.WriteLine("// No body found."); Line++;
                } else {
                    // TODO: [DbgILGen] Method body metadata?

                    writer.Write(".maxstack ");
                    writer.WriteLine(method.Body.MaxStackSize); Line++;

                    // Always assure a debug scope exists!
                    method.DebugInformation.GetOrAddScope().Variables.Clear();
                    if (method.Body.HasVariables) {
                        if (method.Body.InitLocals)
                            writer.WriteLine(".locals init (");
                        else
                            writer.WriteLine(".locals (");
                        Line++;
                        for (int i = 0; i < method.Body.Variables.Count; i++) {
                            VariableDefinition @var = method.Body.Variables[i];
                            writer.Write("    [");
                            writer.Write(i);
                            writer.Write("] ");
                            if (!@var.VariableType.IsPrimitive && !@var.VariableType.IsValueType)
                                writer.Write("class ");
                            writer.Write(@var.VariableType.FullName);
                            string name = @var.GenerateVariableName(method, i);
                            method.DebugInformation.GetOrAddScope().Variables.Add(new VariableDebugInformation(@var, name));
                            writer.Write(" ");
                            writer.Write(name);
                            if (i < method.Body.Variables.Count - 1)
                                writer.WriteLine(",");
                            else
                                writer.WriteLine();
                            Line++;
                        }
                        writer.WriteLine(")"); Line++;
                    }

                    writer.WriteLine("// Code:"); Line++;
                    method.DebugInformation.SequencePoints.Clear();
                    Document symbolDoc = new Document(FullPath) {
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
                                StartLine = Line,
                                StartColumn = 1,
                                EndLine = Line,
                                EndColumn = instrStr.Length + 1
                            }
                        );

                        writer.WriteLine(instrStr); Line++;
                    }
                }

                writer.WriteLine();
            }

            CurrentPath.Pop();
        }

    }
}
