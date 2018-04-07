using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using MonoMod.Helpers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using MonoMod.InlineRT;

namespace MonoMod.RuntimeDetour {
    public static unsafe class DetourHelper {

        public static void Write(this IntPtr to, ref int offs, byte value) {
            *((byte*) ((long) to + offs)) = value;
            offs += 1;
        }
        public static void Write(this IntPtr to, ref int offs, ushort value) {
            *((ushort*) ((long) to + offs)) = value;
            offs += 2;
        }
        public static void Write(this IntPtr to, ref int offs, uint value) {
            *((uint*) ((long) to + offs)) = value;
            offs += 4;
        }
        public static void Write(this IntPtr to, ref int offs, ulong value) {
            *((ulong*) ((long) to + offs)) = value;
            offs += 8;
        }

        public static DynamicMethod CreateILCopy(this MethodBase method) {
            MethodBody body = method.GetMethodBody();
            if (body == null) {
                throw new InvalidOperationException("P/Invoke methods cannot be copied!");
            }

            ParameterInfo[] args = method.GetParameters();
            Type[] argTypes;
            if (!method.IsStatic) {
                argTypes = new Type[args.Length + 1];
                argTypes[0] = method.DeclaringType;
                for (int i = 0; i < args.Length; i++)
                    argTypes[i + 1] = args[i].ParameterType;
            } else {
                argTypes = new Type[args.Length];
                for (int i = 0; i < args.Length; i++)
                    argTypes[i] = args[i].ParameterType;
            }

            DynamicMethod dm = new DynamicMethod(
                $"orig_{method.Name}",
                // method.Attributes, method.CallingConvention, // DynamicMethod only supports public, static and standard
                (method as MethodInfo)?.ReturnType ?? typeof(void), argTypes,
                method.DeclaringType,
                false
            );

            ILGenerator il = dm.GetILGenerator();

            // TODO: Move away from using Harmony's ILCopying code in MonoMod...
            List<Label> endLabels = new List<Label>();
            List<Harmony.ILCopying.ExceptionBlock> endBlocks = new List<Harmony.ILCopying.ExceptionBlock>();
            Harmony.ILCopying.MethodCopier copier = new Harmony.ILCopying.MethodCopier(method, il);
            copier.Finalize(endLabels, endBlocks);
            foreach (Label label in endLabels)
                il.MarkLabel(label);
            foreach (Harmony.ILCopying.ExceptionBlock block in endBlocks)
                Harmony.ILCopying.Emitter.MarkBlockAfter(il, block);

            return dm;
        }

        static OpCodeContainer[] GetOpCodes(byte[] data) {
            List<OpCodeContainer> opCodes = new List<OpCodeContainer>();
            foreach (byte opCodeByte in data)
                opCodes.Add(new OpCodeContainer(opCodeByte));
            return opCodes.ToArray();
        }

        class OpCodeContainer {
            public OpCode? code;
            byte data;

            public OpCodeContainer(byte opCode) {
                data = opCode;
                try {
                    code = (OpCode) typeof(OpCodes).GetFields().First(t => ((OpCode) (t.GetValue(null))).Value == opCode).GetValue(null);
                } catch { }
            }
        }

    }
}
