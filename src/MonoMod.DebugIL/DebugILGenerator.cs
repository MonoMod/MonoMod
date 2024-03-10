using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MonoMod.DebugIL
{
    public class DebugILGenerator
    {

#pragma warning disable CA1707 // Identifiers should not contain underscores
        public static readonly System.Reflection.ConstructorInfo m_DebuggableAttribute_ctor =
            typeof(DebuggableAttribute).GetConstructor(new Type[] { typeof(DebuggableAttribute.DebuggingModes) });
#pragma warning restore CA1707 // Identifiers should not contain underscores

        public MonoModder Modder { get; }
        public bool Relative { get; }
        public bool SkipMaxStack { get; }
        public string OutputPath { get; set; }

        public DebugILGenerator(MonoModder modder)
        {
            Helpers.ThrowIfArgumentNull(modder);
            Modder = modder;

            OutputPath = Path.GetFullPath(modder.OutputPath);
            modder.OutputPath = Path.Combine(OutputPath, Path.GetFileName(modder.InputPath));

            Relative = Switches.TryGetSwitchEnabled("DebugIL_Relative", out var enabled) && enabled;
            SkipMaxStack = Switches.TryGetSwitchEnabled("DebugIL_SkipMaxStack", out enabled) && enabled;
        }

        public static void Generate(MonoModder modder)
            => new DebugILGenerator(modder).Generate();

        public void Generate()
        {
            if (Directory.Exists(OutputPath))
            {
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

            var tasksL = new List<Task>();

            tasksL.Add(Task.Factory.StartNew(() =>
            {
                DumpMetadata(OutputPath);
            }, default, TaskCreationOptions.None, TaskScheduler.Default));

            var indices = new Dictionary<string, int>();

            // GetTypes includes nested types.
            foreach (var type in Modder.Module.GetTypes())
            {
                // Modder.Log($"[DbgIlGen] Enqueueing {type.FullName}");

                var path = Path.Combine(
                    OutputPath,
                    string.Join(
                        Path.DirectorySeparatorChar.ToString(),
                        type.FullName
                            .Split('.', '/').Select(
                                part => DebugILWriter.PathVerifyRegex.Replace(part, "_")
                            ).ToArray()
                    )
                );

                tasksL.Add(Task.Factory.StartNew(() =>
                {
                    DumpType(path, type);
                }, default, TaskCreationOptions.None, TaskScheduler.Default));

                foreach (MethodDefinition method in type.Methods)
                {
                    if (indices.TryGetValue(method.Name, out var index))
                    {
                        indices[method.Name] = ++index;
                    }
                    else
                    {
                        index = 0;
                        indices[method.Name] = 0;
                    }
                    tasksL.Add(Task.Factory.StartNew(() =>
                    {
                        DumpMethod(path, index, method);
                    }, default, TaskCreationOptions.None, TaskScheduler.Default));
                }
                indices.Clear();
            }

            var tasksA = tasksL.ToArray();
            int completed;
            var all = tasksA.Length;
            while ((completed = tasksL.Count(t => t.IsCompleted)) < all)
            {
                Console.Write($"[MonoMod] [DbgIlGen] {completed} / {all} task{(all != 1 ? "s" : "")} finished        \r");
                Task.WaitAny(tasksA, -1);
            }
            Console.Write($"[MonoMod] [DbgIlGen] {all} / {all} task{(all != 1 ? "s" : "")} finished        \r");
            Console.WriteLine();
        }

        public void DumpMetadata(string path)
        {
            using (var writer = new DebugILWriter(path, "__AssemblyInfo__"))
            {
                writer.WriteLine("// MonoMod DebugILGenerator");
                writer.WriteLine($"// MonoMod Version: {MonoModder.Version}");
                writer.WriteLine();

                writer.WriteLine("// Input assembly:");
                writer.WriteLine($"// {Modder.Module.Assembly.Name.FullName}");
                writer.WriteLine($"// {Modder.InputPath}");
                writer.WriteLine();

                writer.WriteLine("// Assembly references:");
                foreach (AssemblyNameReference dep in Modder.Module.AssemblyReferences)
                {
                    writer.WriteLine($"// {dep.FullName}");
                }
                writer.WriteLine();

                // TODO: [DbgILGen] Other assembly metadata?
            }
        }

        public static void DumpType(string path, TypeDefinition type)
        {
            Helpers.ThrowIfArgumentNull(type);
            using (var writer = new DebugILWriter(path, "__TypeInfo__"))
            {
                writer.WriteLine("// MonoMod DebugILGenerator");
                writer.WriteLine($"// Type: ({type.Attributes}) {type.FullName}");
                writer.WriteLine();

                writer.WriteLine("// Fields:");
                foreach (FieldDefinition member in type.Fields)
                {
                    writer.WriteLine($"// ({member.Attributes}) {member.FieldType.FullName} {member.Name}");
                }
                writer.WriteLine();

                writer.WriteLine("// Properties:");
                foreach (PropertyDefinition member in type.Properties)
                {
                    writer.WriteLine($"// ({member.Attributes}) {member.PropertyType.FullName} {member.Name}");
                }
                writer.WriteLine();

                writer.WriteLine("// Events:");
                foreach (EventDefinition member in type.Events)
                {
                    writer.WriteLine($"// ({member.Attributes}) {member.EventType.FullName} {member.Name}");
                }
                writer.WriteLine();

                // TODO: [DbgILGen] Other type metadata?
            }
        }

        public void DumpMethod(string parent, int index, MethodDefinition method)
        {
            Helpers.ThrowIfArgumentNull(method);
            method.NoInlining = true;
            method.NoOptimization = true;

            using (var writer = new DebugILWriter(parent, method.Name, index))
            {
                writer.WriteLine("// MonoMod DebugILGenerator");
                writer.WriteLine($"// Method: ({method.Attributes}) {method.GetID()}");
                writer.WriteLine();

                // TODO: [DbgILGen] Other method metadata?

                writer.WriteLine("// Body:");
                if (!method.HasBody)
                {
                    writer.WriteLine("// No body found.");
                    writer.WriteLine();
                    return;
                }

                // TODO: [DbgILGen] Method body metadata?

                if (!SkipMaxStack)
                    writer.WriteLine($".maxstack {method.Body.MaxStackSize}");

                // Always assure a debug scope exists!
                method.DebugInformation.GetOrAddScope().Variables.Clear();
                if (method.Body.HasVariables)
                {
                    writer.WriteLine(method.Body.InitLocals ? ".locals init (" : ".locals (");

                    for (var i = 0; i < method.Body.Variables.Count; i++)
                    {
                        var vd = method.Body.Variables[i];
                        var name = vd.GenerateVariableName();
                        method.DebugInformation.GetOrAddScope().Variables.Add(new VariableDebugInformation(vd, name));
                        writer.WriteLine($"    [{i}] {(!vd.VariableType.IsPrimitive && !vd.VariableType.IsValueType ? "class " : "")}{vd.VariableType.FullName} {name}{(i < method.Body.Variables.Count - 1 ? "," : "")}");
                    }
                    writer.WriteLine(")");
                }

                writer.WriteLine("// Code:");
                method.DebugInformation.SequencePoints.Clear();
                var symbolDoc = new Document(writer.FullPath)
                {
                    LanguageVendor = DocumentLanguageVendor.Microsoft,
                    Language = DocumentLanguage.CSharp, // Even Visual Studio can't deal with Cil!
                    HashAlgorithm = DocumentHashAlgorithm.None,
                    Type = DocumentType.Text
                };

                // The exception block pretty printing is based off of
                // https://github.com/BepInEx/HarmonyX/blob/a570001c568629d745c88fbc46e70cc7d0c9becf/Harmony/Internal/Util/MethodBodyLogExtensions.cs#L44
                // Thanks to ghorsington (denikson) for allowing MonoMod to use it!

                // Cache exception blocks for pretty printing
                var handlerMap = new Dictionary<Instruction, List<ExceptionBlock>>();

                ExceptionBlock AddBlock(Instruction instr, ExceptionBlockType t)
                {
                    if (instr == null)
                        return new ExceptionBlock();

                    if (!handlerMap.TryGetValue(instr, out List<ExceptionBlock> list))
                        handlerMap[instr] = list = new List<ExceptionBlock>();

                    var block = new ExceptionBlock()
                    {
                        BlockType = t
                    };
                    list.Add(block);
                    return block;
                }

                foreach (var handler in method.Body.ExceptionHandlers)
                {
                    AddBlock(handler.TryStart, ExceptionBlockType.BeginExceptionBlock);
                    AddBlock(handler.TryEnd, ExceptionBlockType.EndExceptionBlock);
                    AddBlock(handler.HandlerEnd, ExceptionBlockType.EndExceptionBlock);
                    switch (handler.HandlerType)
                    {
                        case ExceptionHandlerType.Catch:
                            AddBlock(handler.HandlerStart, ExceptionBlockType.BeginCatchBlock).CatchType =
                                handler.CatchType ?? Modder.Module.TypeSystem.Object;
                            break;

                        case ExceptionHandlerType.Filter:
                            AddBlock(handler.FilterStart, ExceptionBlockType.BeginExceptFilterBlock);
                            break;

                        case ExceptionHandlerType.Finally:
                            AddBlock(handler.HandlerStart, ExceptionBlockType.BeginFinallyBlock);
                            break;

                        case ExceptionHandlerType.Fault:
                            AddBlock(handler.HandlerStart, ExceptionBlockType.BeginFaultBlock);
                            break;

                        default:
                            throw new NotSupportedException($"Unsupported exception handler type ${handler.HandlerType}");
                    }
                }

                var handlerStack = new Stack<string>();

                for (var instri = 0; instri < method.Body.Instructions.Count; instri++)
                {
                    var instr = method.Body.Instructions[instri];

                    if (handlerMap.TryGetValue(instr, out var blocks))
                    {
                        // Force exception close to the start for correct output
                        blocks.Sort((a, b) => a.BlockType == ExceptionBlockType.EndExceptionBlock ? -1 : 0);

                        foreach (ExceptionBlock block in blocks)
                        {
                            switch (block.BlockType)
                            {
                                case ExceptionBlockType.BeginExceptionBlock:
                                    writer.WriteLine(".try {");
                                    handlerStack.Push(".try");
                                    break;

                                case ExceptionBlockType.BeginCatchBlock:
                                    writer.WriteLine($"catch {block.CatchType.FullName} {{");
                                    handlerStack.Push("handler (catch)");
                                    break;

                                case ExceptionBlockType.BeginExceptFilterBlock:
                                    writer.WriteLine("filter {");
                                    handlerStack.Push("handler (filter)");
                                    break;

                                case ExceptionBlockType.BeginFaultBlock:
                                    writer.WriteLine("fault {");
                                    handlerStack.Push("handler (fault)");
                                    break;

                                case ExceptionBlockType.BeginFinallyBlock:
                                    writer.WriteLine("finally {");
                                    handlerStack.Push("handler (finally)");
                                    break;

                                case ExceptionBlockType.EndExceptionBlock:
                                    writer.WriteLine($"}} // end {handlerStack.Pop()}");
                                    break;

                                default:
                                    throw new NotSupportedException($"Unsupported exception handler type ${block.BlockType}");
                            }
                        }
                    }

                    var instrStr = Relative ? instr.ToRelativeString() : instr.ToString();

                    method.DebugInformation.SequencePoints.Add(
                        new SequencePoint(instr, symbolDoc)
                        {
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
