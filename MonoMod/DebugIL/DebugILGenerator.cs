using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MonoMod.DebugIL {
    public class DebugILGenerator {

        public static Regex PathVerifyRegex = new Regex("[" + Regex.Escape(new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars())) + "]", RegexOptions.Compiled);

        public MonoModder Modder;

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

        public DebugILGenerator(MonoModder modder) {
            Modder = modder;

            OutputPath = modder.OutputPath;
            modder.OutputPath = Path.Combine(OutputPath, Path.GetFileName(modder.InputPath));
        }

        public static void Generate(MonoModder modder)
            => new DebugILGenerator(modder).Generate();

        public void Generate() {
            Directory.CreateDirectory(FullPath);

            GenerateMetadata();

            foreach (TypeDefinition type in Modder.Module.Types)
                GenerateFor(type);
        }

        public void GenerateMetadata() {
            CurrentPath.Push("AssemblyInfo.il");
            using (StreamWriter writer = new StreamWriter(FullPath)) {
                writer.WriteLine("MonoMod DebugILGenerator");
                writer.Write("MonoMod Version: ");
                writer.WriteLine(MonoModder.Version);
                writer.WriteLine();

                writer.WriteLine("Input assembly:");
                writer.WriteLine(Modder.Module.Assembly.Name.FullName);
                writer.WriteLine(Modder.InputPath);
                writer.WriteLine();

                writer.WriteLine("Assembly references:");
                foreach (AssemblyNameReference dep in Modder.Module.AssemblyReferences)
                    writer.WriteLine(dep.FullName);
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

            CurrentPath.Push("TypeInfo.il");
            using (StreamWriter writer = new StreamWriter(FullPath)) {
                writer.WriteLine("Type name:");
                writer.WriteLine(type.FullName);
                writer.WriteLine();

                writer.WriteLine("Fields:");
                foreach (FieldReference field in type.Fields)
                    writer.WriteLine(field.FullName);
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

            using (StreamWriter writer = new StreamWriter(FullPath)) {
                writer.WriteLine("Method signature:");
                writer.WriteLine(method.FullName);
                writer.WriteLine();

                writer.WriteLine("Method findable ID:");
                writer.WriteLine(method.GetFindableID());
                writer.WriteLine();

                // TODO: [DbgILGen] Other method metadata?

                writer.WriteLine("Body:");
                if (!method.HasBody)
                    writer.WriteLine("NO METHOD BODY!");
                else {
                    int line = 8;

                    // TODO: [DbgILGen] Method body metadata?
                    writer.WriteLine("IL:"); line++;
                    method.DebugInformation.SequencePoints.Clear();
                    Document symbolDoc = new Document(FullPath) {
                        LanguageVendor = DocumentLanguageVendor.Microsoft,
                        Language = DocumentLanguage.Cil,
                        HashAlgorithm = DocumentHashAlgorithm.None,
                        Type = DocumentType.Text
                    };
                    foreach (Instruction instr in method.Body.Instructions) {
                        writer.Write(instr.ToString());
                        method.DebugInformation.SequencePoints.Add(
                            new SequencePoint(instr, symbolDoc) {
                                StartLine = line,
                                StartColumn = 0,
                                EndLine = line,
                                EndColumn = 0
                            }
                        );
                        line++;
                    }
                }

                writer.WriteLine();
            }

            CurrentPath.Pop();
        }

    }
}
