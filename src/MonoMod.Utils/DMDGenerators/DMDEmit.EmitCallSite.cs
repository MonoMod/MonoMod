using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using CallSite = Mono.Cecil.CallSite;

namespace MonoMod.Utils
{
    // The following mostly qualifies as r/badcode material.
    internal static partial class _DMDEmit
    {

        // Mono
        private static readonly MethodInfo? _ILGen_make_room =
            typeof(ILGenerator).GetMethod("make_room", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo? _ILGen_emit_int =
            typeof(ILGenerator).GetMethod("emit_int", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo? _ILGen_ll_emit =
            typeof(ILGenerator).GetMethod("ll_emit", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo? mDynamicMethod_AddRef
            = typeof(DynamicMethod).GetMethod("AddRef", BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(object)], null);
        private static readonly Func<DynamicMethod, object?, int>? DynamicMethod_AddRef =
            mDynamicMethod_AddRef?.CreateDelegate<Func<DynamicMethod, object?, int>>();

        // .NET 8+
        private static readonly Type? TRuntimeILGenerator = Type.GetType("System.Reflection.Emit.RuntimeILGenerator");

        // .NET
        private static readonly MethodInfo? _ILGen_EnsureCapacity =
            typeof(ILGenerator).GetMethod("EnsureCapacity", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? TRuntimeILGenerator?.GetMethod("EnsureCapacity", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo? _ILGen_PutInteger4 =
            typeof(ILGenerator).GetMethod("PutInteger4", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? TRuntimeILGenerator?.GetMethod("PutInteger4", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo? _ILGen_InternalEmit =
            typeof(ILGenerator).GetMethod("InternalEmit", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? TRuntimeILGenerator?.GetMethod("InternalEmit", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo? _ILGen_UpdateStackSize =
            typeof(ILGenerator).GetMethod("UpdateStackSize", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? TRuntimeILGenerator?.GetMethod("UpdateStackSize", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo? f_DynILGen_m_scope =
            typeof(ILGenerator).Assembly
            .GetType("System.Reflection.Emit.DynamicILGenerator")?.GetField("m_scope", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? f_DynScope_m_tokens =
            typeof(ILGenerator).Assembly
            .GetType("System.Reflection.Emit.DynamicScope")?.GetField("m_tokens", BindingFlags.NonPublic | BindingFlags.Instance);

        // Based on https://referencesource.microsoft.com/#mscorlib/system/reflection/mdimport.cs,74bfbae3c61889bc
        private static readonly Type?[] CorElementTypes = [
            null,               // END
            typeof(void),       // VOID
            typeof(bool),       // BOOL
            typeof(char),       // CHAR
            typeof(sbyte),      // I1
            typeof(byte),       // U1
            typeof(short),      // I2
            typeof(ushort),     // U2
            typeof(int),        // I4
            typeof(uint),       // U4
            typeof(long),       // I8
            typeof(ulong),      // U8
            typeof(float),      // R4
            typeof(double),     // R8
            typeof(string),     // STRING
            null,               // PTR
            null,               // BYREF
            null,               // VALUETYPE
            null,               // CLASS
            null,               // VAR
            null,               // ARRAY
            null,               // GENERICINST
            null,               // TYPEDBYREF
            null,               // (unused)
            typeof(IntPtr),     // I
            typeof(UIntPtr),    // U
            null,               // (unused)
            null,               // FNPTR
            typeof(object),     // OBJECT
            // all others don't have specific types associated
        ];

        private abstract class TokenCreator
        {
            public abstract int GetTokenForType(Type type);
            public abstract int GetTokenForSig(byte[] sig);
        }

        private sealed class NetTokenCreator : TokenCreator
        {
            private readonly List<object> tokens;

            public NetTokenCreator(ILGenerator il)
            {
                Helpers.Assert(f_DynScope_m_tokens is not null);
                Helpers.Assert(f_DynILGen_m_scope is not null);

                var list = (List<object>?)f_DynScope_m_tokens.GetValue(f_DynILGen_m_scope.GetValue(il));
                Helpers.Assert(list is not null, "DynamicMethod object list is null!");
                tokens = list;
            }

            public override int GetTokenForType(Type type)
            {
                tokens.Add(type.TypeHandle);
                return (tokens.Count - 1) | 0x02000000; /* (int) MetadataTokenType.TypeDef */
            }

            public override int GetTokenForSig(byte[] sig)
            {
                tokens.Add(sig);
                return (tokens.Count - 1) | 0x11000000; /* (int) MetadataTokenType.Signature */
            }
        }

        private sealed class MonoTokenCreator : TokenCreator
        {
            private readonly DynamicMethod dm;
            private readonly Func<DynamicMethod, object?, int> addRef;
            public MonoTokenCreator(DynamicMethod dm)
            {
                Helpers.Assert(DynamicMethod_AddRef is not null);
                addRef = DynamicMethod_AddRef;
                this.dm = dm;
            }

            public override int GetTokenForType(Type type)
                => addRef(dm, type);
            public override int GetTokenForSig(byte[] sig)
                => addRef(dm, sig); // I don't think this can actually be implemented for mono? It seems to always expect a SignatureHelper
            // I assume, however, that we can't use SignatureHelper here because it is horribly broken on some (probably older) mono builds.
        }

        private abstract class CallSiteEmitter
        {
            public abstract void EmitCallSite(DynamicMethod dm, ILGenerator il, OpCode opcode, CallSite csite);
        }

        private sealed class NetCallSiteEmitter : CallSiteEmitter
        {
            public override void EmitCallSite(DynamicMethod dm, ILGenerator il, OpCode opcode, CallSite csite)
            {
                /* The mess in this method is heavily based off of the code available at the following links:
                 * https://github.com/Microsoft/referencesource/blob/3b1eaf5203992df69de44c783a3eda37d3d4cd10/mscorlib/system/reflection/emit/dynamicmethod.cs#L791
                 * https://github.com/Microsoft/referencesource/blob/3b1eaf5203992df69de44c783a3eda37d3d4cd10/mscorlib/system/reflection/emit/dynamicilgenerator.cs#L353
                 * https://github.com/mono/mono/blob/82e573122a55482bf6592f36f819597238628385/mcs/class/corlib/System.Reflection.Emit/DynamicMethod.cs#L411
                 * https://github.com/mono/mono/blob/82e573122a55482bf6592f36f819597238628385/mcs/class/corlib/System.Reflection.Emit/ILGenerator.cs#L800
                 * https://github.com/dotnet/coreclr/blob/0fbd855e38bc3ec269479b5f6bf561dcfd67cbb6/src/System.Private.CoreLib/src/System/Reflection/Emit/SignatureHelper.cs#L57
                 */

                TokenCreator tokenCreator = DynamicMethod_AddRef is not null
                    ? new MonoTokenCreator(dm) : new NetTokenCreator(il);

                var signature = new byte[32];
                var currSig = 0;
                var sizeLoc = -1;

                // We're emitting a StandAloneMethodSig

                AddData(((byte)csite.CallingConvention) | (csite.HasThis ? 0x20 : 0) | (csite.ExplicitThis ? 0x40 : 0));
                sizeLoc = currSig++;

                var modReq = new List<Type>();
                var modOpt = new List<Type>();

                ResolveWithModifiers(csite.ReturnType, out var returnType, out var returnTypeModReq, out var returnTypeModOpt, modReq, modOpt);
                AddArgument(returnType, returnTypeModReq, returnTypeModOpt);

                foreach (var param in csite.Parameters)
                {
                    if (param.ParameterType.IsSentinel)
                        AddElementType(0x41 /* CorElementType.Sentinel */);

                    if (param.ParameterType.IsPinned)
                    {
                        AddElementType(0x45 /* CorElementType.Pinned */);
                        // AddArgument(param.ParameterType.ResolveReflection());
                        // continue;
                    }

                    ResolveWithModifiers(param.ParameterType, out var paramType, out var paramTypeModReq, out var paramTypeModOpt, modReq, modOpt);
                    AddArgument(paramType, paramTypeModReq, paramTypeModOpt);
                }

                AddElementType(0x00 /* CorElementType.End */);

                // For most signatures, this will set the number of elements in a byte which we have reserved for it.
                // However, if we have a field signature, we don't set the length and return.
                // If we have a signature with more than 128 arguments, we can't just set the number of elements,
                // we actually have to allocate more space (e.g. shift everything in the array one or more spaces to the
                // right.  We do this by making a copy of the array and leaving the correct number of blanks.  This new
                // array is now set to be m_signature and we use the AddData method to set the number of elements properly.
                // The forceCopy argument can be used to force SetNumberOfSignatureElements to make a copy of
                // the array.  This is useful for GetSignature which promises to trim the array to be the correct size anyway.

                byte[] temp;
                int newSigSize;
                var currSigHolder = currSig;

                // We need to have more bytes for the size.  Figure out how many bytes here.
                // Since we need to copy anyway, we're just going to take the cost of doing a
                // new allocation.
                if (csite.Parameters.Count < 0x80)
                {
                    newSigSize = 1;
                }
                else if (csite.Parameters.Count < 0x4000)
                {
                    newSigSize = 2;
                }
                else
                {
                    newSigSize = 4;
                }

                // Allocate the new array.
                temp = new byte[currSig + newSigSize - 1];

                // Copy the calling convention.  The calling convention is always just one byte
                // so we just copy that byte.  Then copy the rest of the array, shifting everything
                // to make room for the new number of elements.
                temp[0] = signature[0];
                Buffer.BlockCopy(signature, sizeLoc + 1, temp, sizeLoc + newSigSize, currSigHolder - (sizeLoc + 1));
                signature = temp;

                //Use the AddData method to add the number of elements appropriately compressed.
                currSig = sizeLoc;
                AddData(csite.Parameters.Count);
                currSig = currSigHolder + (newSigSize - 1);

                // This case will only happen if the user got the signature through 
                // InternalGetSignature first and then called GetSignature.
                if (signature.Length > currSig)
                {
                    temp = new byte[currSig];
                    Array.Copy(signature, temp, currSig);
                    signature = temp;
                }

                // Emit.

                if (_ILGen_emit_int != null)
                {
                    // Mono
                    _ILGen_make_room!.Invoke(il, new object[] { 6 });
                    _ILGen_ll_emit!.Invoke(il, new object[] { opcode });
                    _ILGen_emit_int!.Invoke(il, new object[] { tokenCreator.GetTokenForSig(signature) });
                }
                else
                {
                    // .NET
                    _ILGen_EnsureCapacity!.Invoke(il, new object[] { 7 });
                    _ILGen_InternalEmit!.Invoke(il, new object[] { opcode });

                    // The only IL instruction that has VarPop behaviour, that takes a
                    // Signature token as a parameter is calli.  Pop the parameters and
                    // the native function pointer.  To be conservative, do not pop the
                    // this pointer since this information is not easily derived from
                    // SignatureHelper.
                    if (opcode.StackBehaviourPop == System.Reflection.Emit.StackBehaviour.Varpop)
                    {
                        // Pop the arguments and native function pointer off the stack.
                        _ILGen_UpdateStackSize!.Invoke(il, new object[] { opcode, -csite.Parameters.Count - 1 });
                    }

                    _ILGen_PutInteger4!.Invoke(il, new object[] { tokenCreator.GetTokenForSig(signature) });
                }

                void AddArgument(Type clsArgument, Type[] requiredCustomModifiers, Type[] optionalCustomModifiers)
                {
                    if (optionalCustomModifiers != null)
                        foreach (var t in optionalCustomModifiers)
                            InternalAddTypeToken(tokenCreator.GetTokenForType(t), 0x20 /* CorElementType.CModOpt */);

                    if (requiredCustomModifiers != null)
                        foreach (var t in requiredCustomModifiers)
                            InternalAddTypeToken(tokenCreator.GetTokenForType(t), 0x1F /* CorElementType.CModReqd */);

                    AddOneArgTypeHelper(clsArgument);
                }

                void AddData(int data)
                {
                    // A managed representation of CorSigCompressData; 

                    if (currSig + 4 > signature!.Length)
                    {
                        signature = ExpandArray(signature);
                    }

                    if (data <= 0x7F)
                    {
                        signature[currSig++] = (byte)(data & 0xFF);
                    }
                    else if (data <= 0x3FFF)
                    {
                        signature[currSig++] = (byte)((data >> 8) | 0x80);
                        signature[currSig++] = (byte)(data & 0xFF);
                    }
                    else if (data <= 0x1FFFFFFF)
                    {
                        signature[currSig++] = (byte)((data >> 24) | 0xC0);
                        signature[currSig++] = (byte)((data >> 16) & 0xFF);
                        signature[currSig++] = (byte)((data >> 8) & 0xFF);
                        signature[currSig++] = (byte)((data) & 0xFF);
                    }
                    else
                    {
                        throw new ArgumentException("Integer or token was too large to be encoded.");
                    }
                }

                byte[] ExpandArray(byte[] inArray, int requiredLength = -1)
                {
                    if (requiredLength < inArray.Length)
                        requiredLength = inArray.Length * 2;

                    var outArray = new byte[requiredLength];
                    Buffer.BlockCopy(inArray, 0, outArray, 0, inArray.Length);
                    return outArray;
                }

                void AddElementType(byte cvt)
                {
                    // Adds an element to the signature.  A managed represenation of CorSigCompressElement
                    if (currSig + 1 > signature.Length)
                        signature = ExpandArray(signature);

                    signature[currSig++] = cvt;
                }

                void AddToken(int token)
                {
                    // A managed represenation of CompressToken
                    // Pulls the token appart to get a rid, adds some appropriate bits
                    // to the token and then adds this to the signature.

                    var rid = (token & 0x00FFFFFF); //This is RidFromToken;
                    var type = (token & unchecked((int)0xFF000000)); //This is TypeFromToken;

                    if (rid > 0x3FFFFFF)
                    {
                        // token is too big to be compressed    
                        throw new ArgumentException("Integer or token was too large to be encoded.");
                    }

                    rid = (rid << 2);

                    // TypeDef is encoded with low bits 00  
                    // TypeRef is encoded with low bits 01  
                    // TypeSpec is encoded with low bits 10    
                    if (type == 0x01000000 /* MetadataTokenType.TypeRef */)
                    {
                        //if type is mdtTypeRef
                        rid |= 0x1;
                    }
                    else if (type == 0x1b000000 /* MetadataTokenType.TypeSpec */)
                    {
                        //if type is mdtTypeSpec
                        rid |= 0x2;
                    }

                    AddData(rid);
                }

                void InternalAddTypeToken(int clsToken, byte CorType)
                {
                    // Add a type token into signature. CorType will be either CorElementType.Class or CorElementType.ValueType
                    AddElementType(CorType);
                    AddToken(clsToken);
                }

                void AddOneArgTypeHelper(Type clsArgument) { AddOneArgTypeHelperWorker(clsArgument, false); }
                void AddOneArgTypeHelperWorker(Type clsArgument, bool lastWasGenericInst)
                {
                    if (clsArgument.IsGenericType && (!clsArgument.IsGenericTypeDefinition || !lastWasGenericInst))
                    {
                        AddElementType(0x15 /* CorElementType.GenericInst */);

                        AddOneArgTypeHelperWorker(clsArgument.GetGenericTypeDefinition(), true);

                        var genargs = clsArgument.GetGenericArguments();

                        AddData(genargs.Length);

                        foreach (var t in genargs)
                            AddOneArgTypeHelper(t);
                    }
                    else if (clsArgument.IsByRef)
                    {
                        AddElementType(0x10 /* CorElementType.ByRef */);
                        clsArgument = clsArgument.GetElementType() ?? clsArgument;
                        AddOneArgTypeHelper(clsArgument);
                    }
                    else if (clsArgument.IsPointer)
                    {
                        AddElementType(0x0F /* CorElementType.Ptr */);
                        AddOneArgTypeHelper(clsArgument.GetElementType() ?? clsArgument);
                    }
                    else if (clsArgument.IsArray)
                    {
#if false
                        if (clsArgument.IsArray && clsArgument == clsArgument.GetElementType().MakeArrayType()) { // .IsSZArray unavailable.
                            AddElementType(0x1D /* CorElementType.SzArray */);

                            AddOneArgTypeHelper(clsArgument.GetElementType());
                        } else
#endif
                        {
                            AddElementType(0x14 /* CorElementType.Array */);

                            AddOneArgTypeHelper(clsArgument.GetElementType() ?? clsArgument);

                            // put the rank information
                            var rank = clsArgument.GetArrayRank();
                            AddData(rank);     // rank
                            AddData(0);     // upper bounds
                            AddData(rank);  // lower bound
                            for (var i = 0; i < rank; i++)
                                AddData(0);
                        }
                    }
                    else
                    {
                        // This isn't 100% accurate, but... oh well.
                        byte type = 0; // 0 is reserved anyway.

                        for (var i = 0; i < CorElementTypes.Length; i++)
                        {
                            if (clsArgument == CorElementTypes[i])
                            {
                                type = (byte)i;
                                break;
                            }
                        }

                        if (type == 0)
                        {
                            if (clsArgument == typeof(object))
                            {
                                type = 0x1C /* CorElementType.Object */;
                            }
                            else if (clsArgument.IsValueType)
                            {
                                type = 0x11 /* CorElementType.ValueType */;
                            }
                            else
                            {
                                // Let's hope for the best.
                                type = 0x12 /* CorElementType.Class */;
                            }
                        }

                        if (type <= 0x0E /* CorElementType.String */ ||
                            type == 0x16 /* CorElementType.TypedByRef */ ||
                            type == 0x18 /* CorElementType.I */ ||
                            type == 0x19 /* CorElementType.U */ ||
                            type == 0x1C /* CorElementType.Object */
                        )
                        {
                            AddElementType(type);
                        }
                        // https://github.com/dotnet/runtime/blob/4e0dc086de5952b87e0fdac43690edb5acc4bd6b/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/SignatureHelper.cs#L449
                        // https://referencesource.microsoft.com/#mscorlib/system/reflection/emit/signaturehelper.cs,491
                        // btw dynamic method il generator emitcalli never gives it a module, so it is always null
                        // https://github.com/dotnet/runtime/blob/4e0dc086de5952b87e0fdac43690edb5acc4bd6b/src/coreclr/System.Private.CoreLib/src/System/Reflection/Emit/DynamicILGenerator.cs#L450
                        // https://referencesource.microsoft.com/#mscorlib/system/reflection/emit/dynamicilgenerator.cs,538
                        else if (true || tokenCreator/*signatureHelper.module*/ == null)
                        {
                            InternalAddRuntimeType(clsArgument);
                        }
                        else if (clsArgument.IsValueType)
                        {
                            InternalAddTypeToken(tokenCreator.GetTokenForType(clsArgument), 0x11 /* CorElementType.ValueType */);
                        }
                        else
                        {
                            InternalAddTypeToken(tokenCreator.GetTokenForType(clsArgument), 0x12 /* CorElementType.Class */);
                        }
                    }
                }
                unsafe void InternalAddRuntimeType(Type type)
                {
                    // Add a runtime type into the signature.

                    AddElementType(0x21/*CorElementType.ELEMENT_TYPE_INTERNAL*/);

                    IntPtr handle = type.TypeHandle.Value;

                    // Internal types must have their pointer written into the signature directly (we don't
                    // want to convert to little-endian format on big-endian machines because the value is
                    // going to be extracted and used directly as a pointer (and only within this process)).
                    if (currSig + sizeof(void*) > signature!.Length)
                    {
                        signature = ExpandArray(signature);
                    }

                    byte* phandle = (byte*)&handle;
                    for (int i = 0; i < sizeof(void*); i++)
                        signature[currSig++] = phandle[i];
                }
            }
        }

        private sealed class MonoCallSiteEmitter : CallSiteEmitter
        {
            private FieldInfo SigHelper_callConv;
            private FieldInfo SigHelper_unmanagedCallConv;
            private FieldInfo SigHelper_arguments;
            private FieldInfo SigHelper_modreqs;
            private FieldInfo SigHelper_modopts;

            public MonoCallSiteEmitter()
            {
                var callConv = typeof(SignatureHelper).GetField("callConv", BindingFlags.Instance | BindingFlags.NonPublic);
                var unmanagedCallConv = typeof(SignatureHelper).GetField("unmanagedCallConv", BindingFlags.Instance | BindingFlags.NonPublic);
                var arguments = typeof(SignatureHelper).GetField("arguments", BindingFlags.Instance | BindingFlags.NonPublic);
                var modreqs = typeof(SignatureHelper).GetField("modreqs", BindingFlags.Instance | BindingFlags.NonPublic);
                var modopts = typeof(SignatureHelper).GetField("modopts", BindingFlags.Instance | BindingFlags.NonPublic);

                // if we hit this ctor, we should be running on Mono, which should mean these are all present
                Helpers.Assert(callConv is not null);
                Helpers.Assert(unmanagedCallConv is not null);
                Helpers.Assert(arguments is not null);
                Helpers.Assert(modreqs is not null);
                Helpers.Assert(modopts is not null);

                SigHelper_callConv = callConv;
                SigHelper_unmanagedCallConv = unmanagedCallConv;
                SigHelper_arguments = arguments;
                SigHelper_modreqs = modreqs;
                SigHelper_modopts = modopts;
            }

            public override void EmitCallSite(DynamicMethod dm, ILGenerator il, OpCode opcode, CallSite csite)
            {
                // On Mono, when its processing the tokens for a CallSite, it explicitly looks for a SignatureHelper, and so we CANNOT pass in 
                // a manually constructed signature. At all. Which sucks. It means that, on older Mono that have half-implemented SignatureHelpers,
                // there's nothing we can do.

                var modReq = new List<Type>();
                var modOpt = new List<Type>();

                // note: with the Mono signature helper, we can't represent modifiers on the return type
                ResolveWithModifiers(csite.ReturnType, out var rawRetType, out _, out _, modReq, modOpt);

                // there's not a standard, public API for the metadata callconv field either, so we have to set it manually
                var sigHelper = SignatureHelper.GetMethodSigHelper(CallingConventions.Standard, rawRetType);

                var arguments = new Type[csite.Parameters.Count];
                var modreqs = new Type[csite.Parameters.Count][];
                var modopts = new Type[csite.Parameters.Count][];

                var managedCallConv = csite.CallingConvention switch
                {
                    MethodCallingConvention.VarArg => CallingConventions.VarArgs,
                    _ => CallingConventions.Standard,
                };
                if (csite.HasThis) managedCallConv |= CallingConventions.HasThis;
                if (csite.ExplicitThis) managedCallConv |= CallingConventions.ExplicitThis;

                var unmanagedCallConv = csite.CallingConvention switch
                {
                    MethodCallingConvention.C => CallingConvention.Cdecl,
                    MethodCallingConvention.StdCall => CallingConvention.StdCall,
                    MethodCallingConvention.ThisCall => CallingConvention.ThisCall,
                    MethodCallingConvention.FastCall => CallingConvention.FastCall,
                    _ => (CallingConvention)0,
                };

                for (var i = 0; i < csite.Parameters.Count; i++)
                {
                    var param = csite.Parameters[i];

                    ResolveWithModifiers(param.ParameterType, out arguments[i], out modreqs[i], out modopts[i], modReq, modOpt);
                }

                // fill the signature helper
                SigHelper_callConv.SetValue(sigHelper, managedCallConv);
                SigHelper_unmanagedCallConv.SetValue(sigHelper, unmanagedCallConv);
                SigHelper_arguments.SetValue(sigHelper, arguments);
                SigHelper_modreqs.SetValue(sigHelper, modreqs);
                SigHelper_modopts.SetValue(sigHelper, modopts);

                // emit the sighelper
                _ILGen_make_room!.Invoke(il, new object[] { 6 });
                _ILGen_ll_emit!.Invoke(il, new object[] { opcode });
                _ILGen_emit_int!.Invoke(il, new object[] { DynamicMethod_AddRef!(dm, sigHelper) });
            }
        }

        private static readonly CallSiteEmitter callSiteEmitter = DynamicMethod_AddRef is not null ? new MonoCallSiteEmitter() : new NetCallSiteEmitter();

        internal static void _EmitCallSite(DynamicMethod dm, ILGenerator il, OpCode opcode, CallSite csite)
        {
            callSiteEmitter.EmitCallSite(dm, il, opcode, csite);
        }

    }
}
