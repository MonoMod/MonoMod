using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using ExceptionHandler = Mono.Cecil.Cil.ExceptionHandler;

namespace MonoMod.Utils
{
    /// <summary>
    /// The relinker callback delegate type.
    /// </summary>
    /// <param name="mtp">The reference (metadata token provider) to relink.</param>
    /// <param name="context">The generic context provided to relink generic references.</param>
    /// <returns>A relinked reference.</returns>
    public delegate IMetadataTokenProvider Relinker(IMetadataTokenProvider mtp, IGenericParameterProvider? context);

    public static partial class Extensions
    {

        /// <summary>
        /// Clone the given method definition.
        /// </summary>
        /// <param name="o">The original method.</param>
        /// <param name="c">The method definition to apply the cloning process onto, or null to create a new method.</param>
        /// <returns>A clone of the original method.</returns>
        [return: NotNullIfNotNull("o")]
        public static MethodDefinition? Clone(this MethodDefinition? o, MethodDefinition? c = null)
        {
            if (o is null)
                return null;
            if (c is null)
                c = new MethodDefinition(o.Name, o.Attributes, o.ReturnType);
            c.Name = o.Name;
            c.Attributes = o.Attributes;
            c.ReturnType = o.ReturnType;
            c.DeclaringType = o.DeclaringType;
            c.MetadataToken = c.MetadataToken;
            c.Body = o.Body?.Clone(c);
            c.Attributes = o.Attributes;
            c.ImplAttributes = o.ImplAttributes;
            c.PInvokeInfo = o.PInvokeInfo;
            c.IsPreserveSig = o.IsPreserveSig;
            c.IsPInvokeImpl = o.IsPInvokeImpl;

            foreach (var genParam in o.GenericParameters)
                c.GenericParameters.Add(genParam.Clone());

            foreach (var param in o.Parameters)
                c.Parameters.Add(param.Clone());

            foreach (var attrib in o.CustomAttributes)
                c.CustomAttributes.Add(attrib.Clone());

            foreach (var @override in o.Overrides)
                c.Overrides.Add(@override);

            if (c.Body != null)
            {
                int foundIndex;
                foreach (var ci in c.Body.Instructions)
                {
                    if (ci.Operand is GenericParameter genParam && (foundIndex = o.GenericParameters.IndexOf(genParam)) != -1)
                    {
                        ci.Operand = c.GenericParameters[foundIndex];
                    }
                    else if (ci.Operand is ParameterDefinition param && (foundIndex = o.Parameters.IndexOf(param)) != -1)
                    {
                        ci.Operand = c.Parameters[foundIndex];
                    }
                }
            }

            return c;
        }

        /// <summary>
        /// Clone the given method body.
        /// </summary>
        /// <param name="bo">The original method body.</param>
        /// <param name="m">The method which will own the newly cloned method body.</param>
        /// <returns>A clone of the original method body.</returns>
        [return: NotNullIfNotNull("bo")]
        public static MethodBody? Clone(this MethodBody? bo, MethodDefinition m)
        {
            Helpers.ThrowIfArgumentNull(m);

            if (bo == null)
                return null;

            var bc = new MethodBody(m);
            bc.MaxStackSize = bo.MaxStackSize;
            bc.InitLocals = bo.InitLocals;
            bc.LocalVarToken = bo.LocalVarToken;

            bc.Instructions.AddRange(bo.Instructions.Select(o =>
            {
                var c = Instruction.Create(OpCodes.Nop);
                c.OpCode = o.OpCode;
                c.Operand = o.Operand;
                c.Offset = o.Offset;
                return c;
            }));

            foreach (var c in bc.Instructions)
            {
                if (c.Operand is Instruction target)
                {
                    c.Operand = bc.Instructions[bo.Instructions.IndexOf(target)];
                }
                else if (c.Operand is Instruction[] targets)
                {
                    c.Operand = targets.Select(i => bc.Instructions[bo.Instructions.IndexOf(i)]).ToArray();
                }
            }

            bc.ExceptionHandlers.AddRange(bo.ExceptionHandlers.Select(o =>
            {
                var c = new ExceptionHandler(o.HandlerType);
                c.TryStart = o.TryStart == null ? null : bc.Instructions[bo.Instructions.IndexOf(o.TryStart)];
                c.TryEnd = o.TryEnd == null ? null : bc.Instructions[bo.Instructions.IndexOf(o.TryEnd)];
                c.FilterStart = o.FilterStart == null ? null : bc.Instructions[bo.Instructions.IndexOf(o.FilterStart)];
                c.HandlerStart = o.HandlerStart == null ? null : bc.Instructions[bo.Instructions.IndexOf(o.HandlerStart)];
                c.HandlerEnd = o.HandlerEnd == null ? null : bc.Instructions[bo.Instructions.IndexOf(o.HandlerEnd)];
                c.CatchType = o.CatchType;
                return c;
            }));

            bc.Variables.AddRange(bo.Variables.Select(o =>
            {
                var c = new VariableDefinition(o.VariableType);
                return c;
            }));

            Instruction ResolveInstrOff(int off)
            {
                // Can't check cloned instruction offsets directly, as those can change for some reason
                for (var i = 0; i < bo.Instructions.Count; i++)
                    if (bo.Instructions[i].Offset == off)
                        return bc.Instructions[i];
                throw new ArgumentException($"Invalid instruction offset {off}");
            }

            m.CustomDebugInformations.AddRange(bo.Method.CustomDebugInformations.Select(o =>
            {
                if (o is AsyncMethodBodyDebugInformation ao)
                {
                    var c = new AsyncMethodBodyDebugInformation();
                    if (ao.CatchHandler.Offset >= 0)
                        c.CatchHandler = ao.CatchHandler.IsEndOfMethod ? new InstructionOffset() : new InstructionOffset(ResolveInstrOff(ao.CatchHandler.Offset));
                    c.Yields.AddRange(ao.Yields.Select(off => off.IsEndOfMethod ? new InstructionOffset() : new InstructionOffset(ResolveInstrOff(off.Offset))));
                    c.Resumes.AddRange(ao.Resumes.Select(off => off.IsEndOfMethod ? new InstructionOffset() : new InstructionOffset(ResolveInstrOff(off.Offset))));
                    c.ResumeMethods.AddRange(ao.ResumeMethods);
                    return c;
                }
                else if (o is StateMachineScopeDebugInformation so)
                {
                    var c = new StateMachineScopeDebugInformation();
                    c.Scopes.AddRange(so.Scopes.Select(s => new StateMachineScope(ResolveInstrOff(s.Start.Offset), s.End.IsEndOfMethod ? null : ResolveInstrOff(s.End.Offset))));
                    return c;
                }
                else
                    return o;
            }));

            m.DebugInformation.SequencePoints.AddRange(bo.Method.DebugInformation.SequencePoints.Select(o =>
            {
                var c = new SequencePoint(ResolveInstrOff(o.Offset), o.Document);
                c.StartLine = o.StartLine;
                c.StartColumn = o.StartColumn;
                c.EndLine = o.EndLine;
                c.EndColumn = o.EndColumn;
                return c;
            }));

            return bc;
        }

        private static readonly System.Reflection.FieldInfo f_GenericParameter_position
            = typeof(GenericParameter).GetField("position", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("No field 'position' on GenericParameter");
        private static readonly System.Reflection.FieldInfo f_GenericParameter_type
            = typeof(GenericParameter).GetField("type", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("No field 'type' on GenericParameter");
        /// <summary>
        /// Force-update a generic parameter's position and type.
        /// </summary>
        /// <param name="param">The generic parameter to update.</param>
        /// <param name="position">The new position.</param>
        /// <param name="type">The new type.</param>
        /// <returns>The updated generic parameter.</returns>
        public static GenericParameter Update(this GenericParameter param, int position, GenericParameterType type)
        {
            f_GenericParameter_position.SetValue(param, position);
            f_GenericParameter_type.SetValue(param, type);
            return param;
        }

        /// <summary>
        /// Resolve a given generic parameter in another context.
        /// </summary>
        /// <param name="provider">The new context.</param>
        /// <param name="orig">The original generic parameter.</param>
        /// <returns>A generic parameter provided by the given context which matches the original generic parameter.</returns>
        public static GenericParameter? ResolveGenericParameter(this IGenericParameterProvider provider, GenericParameter orig)
        {
            Helpers.ThrowIfArgumentNull(provider);
            Helpers.ThrowIfArgumentNull(orig);
            // This can be true for T[,].Get in "Enter the Gungeon"
            if (provider is GenericParameter genericParam && genericParam.Name == orig.Name)
                return genericParam;

            foreach (var param in provider.GenericParameters)
                if (param.Name == orig.Name)
                    return param;

            var index = orig.Position;
            if (provider is MethodReference && orig.DeclaringMethod != null)
            {
                if (index < provider.GenericParameters.Count)
                    return provider.GenericParameters[index];
                else
                    return orig.Clone().Update(index, GenericParameterType.Method);
            }

            if (provider is TypeReference && orig.DeclaringType != null)
                if (index < provider.GenericParameters.Count)
                    return provider.GenericParameters[index];
                else
                    return orig.Clone().Update(index, GenericParameterType.Type);

            return
                (provider as TypeSpecification)?.ElementType.ResolveGenericParameter(orig) ??
                (provider as MemberReference)?.DeclaringType?.ResolveGenericParameter(orig);
        }

        /// <summary>
        /// Relink the given member reference (metadata token provider).
        /// </summary>
        /// <param name="mtp">The reference to relink.</param>
        /// <param name="relinker">The relinker to use during the relinking process.</param>
        /// <param name="context">The generic context provided to relink generic references.</param>
        /// <returns>A relinked reference.</returns>
        [return: NotNullIfNotNull("mtp")]
        public static IMetadataTokenProvider? Relink(this IMetadataTokenProvider? mtp, Relinker relinker, IGenericParameterProvider context)
        {
            return mtp switch
            {
                TypeReference tr => tr.Relink(relinker, context),
#if !CECIL0_10
                GenericParameterConstraint constraint => constraint.Relink(relinker, context),
#endif
                MethodReference mr => mr.Relink(relinker, context),
                FieldReference fr => fr.Relink(relinker, context),
                ParameterDefinition pd => pd.Relink(relinker, context),
                CallSite cs => cs.Relink(relinker, context),
                null => null,
                _ => throw new InvalidOperationException($"MonoMod can't handle metadata token providers of the type {mtp.GetType()}")
            };
        }

        /// <summary>
        /// Relink the given type reference.
        /// </summary>
        /// <param name="type">The reference to relink.</param>
        /// <param name="relinker">The relinker to use during the relinking process.</param>
        /// <param name="context">The generic context provided to relink generic references.</param>
        /// <returns>A relinked reference.</returns>
        [return: NotNullIfNotNull("type")]
        public static TypeReference? Relink(this TypeReference? type, Relinker relinker, IGenericParameterProvider? context)
        {
            if (type is null)
                return null;
            Helpers.ThrowIfArgumentNull(relinker);

            if (type is TypeSpecification ts)
            {
                var relinkedElem = ts.ElementType.Relink(relinker, context);

                if (type.IsSentinel)
                    return new SentinelType(relinkedElem);

                if (type.IsByReference)
                    return new ByReferenceType(relinkedElem);

                if (type.IsPointer)
                    return new PointerType(relinkedElem);

                if (type.IsPinned)
                    return new PinnedType(relinkedElem);

                if (type.IsArray)
                {
                    var at = new ArrayType(relinkedElem, ((ArrayType)type).Rank);
                    for (var i = 0; i < at.Rank; i++)
                        // It's a struct.
                        at.Dimensions[i] = ((ArrayType)type).Dimensions[i];
                    return at;
                }

                if (type.IsRequiredModifier)
                    return new RequiredModifierType(((RequiredModifierType)type).ModifierType.Relink(relinker, context), relinkedElem);

                if (type.IsOptionalModifier)
                    return new OptionalModifierType(((OptionalModifierType)type).ModifierType.Relink(relinker, context), relinkedElem);

                if (type.IsGenericInstance)
                {
                    var git = new GenericInstanceType(relinkedElem);
                    foreach (var genArg in ((GenericInstanceType)type).GenericArguments)
                        git.GenericArguments.Add(genArg?.Relink(relinker, context));
                    return git;
                }

                if (type.IsFunctionPointer)
                {
                    var fp = (FunctionPointerType)type;
                    fp.ReturnType = fp.ReturnType.Relink(relinker, context);
                    for (var i = 0; i < fp.Parameters.Count; i++)
                        fp.Parameters[i].ParameterType = fp.Parameters[i].ParameterType.Relink(relinker, context);
                    return fp;
                }

                throw new NotSupportedException($"MonoMod can't handle TypeSpecification: {type.FullName} ({type.GetType()})");
            }

            if (type.IsGenericParameter && context != null)
            {
                var genParam = context.ResolveGenericParameter((GenericParameter)type)
                    ?? throw new RelinkTargetNotFoundException($"{RelinkTargetNotFoundException.DefaultMessage} {type.FullName} (context: {context})", type, context);
                for (var i = 0; i < genParam.Constraints.Count; i++)
                    if (!genParam.Constraints[i].GetConstraintType().IsGenericInstance) // That is somehow possible and causes a stack overflow.
                        genParam.Constraints[i] = genParam.Constraints[i].Relink(relinker, context);
                return genParam;
            }

            return (TypeReference)relinker(type, context);
        }

#if !CECIL0_10
        /// <summary>
        /// Relink the given type reference.
        /// </summary>
        /// <param name="constraint">The reference to relink.</param>
        /// <param name="relinker">The relinker to use during the relinking process.</param>
        /// <param name="context">The generic context provided to relink generic references.</param>
        /// <returns>A relinked reference.</returns>
        [return: NotNullIfNotNull("constraint")]
        public static GenericParameterConstraint? Relink(this GenericParameterConstraint? constraint, Relinker relinker, IGenericParameterProvider context)
        {
            if (constraint == null)
                return null;

            var relink = new GenericParameterConstraint(constraint.ConstraintType.Relink(relinker, context));

            foreach (var attrib in constraint.CustomAttributes)
                relink.CustomAttributes.Add(attrib.Relink(relinker, context));

            return relink;
        }
#endif

        /// <summary>
        /// Relink the given method reference.
        /// </summary>
        /// <param name="method">The reference to relink.</param>
        /// <param name="relinker">The relinker to use during the relinking process.</param>
        /// <param name="context">The generic context provided to relink generic references.</param>
        /// <returns>A relinked reference.</returns>
        public static IMetadataTokenProvider Relink(this MethodReference method, Relinker relinker, IGenericParameterProvider context)
        {
            Helpers.ThrowIfArgumentNull(method);
            Helpers.ThrowIfArgumentNull(relinker);

            if (method.IsGenericInstance)
            {
                var methodg = (GenericInstanceMethod)method;
                var gim = new GenericInstanceMethod((MethodReference)methodg.ElementMethod.Relink(relinker, context));
                foreach (var arg in methodg.GenericArguments)
                    // Generic arguments for the generic instance are often given by the next higher provider.
                    gim.GenericArguments.Add(arg.Relink(relinker, context));

                return (MethodReference)relinker(gim, context);
            }

            var relink = new MethodReference(method.Name, method.ReturnType, method.DeclaringType.Relink(relinker, context));

            relink.CallingConvention = method.CallingConvention;
            relink.ExplicitThis = method.ExplicitThis;
            relink.HasThis = method.HasThis;

            foreach (var param in method.GenericParameters)
                relink.GenericParameters.Add(param.Relink(relinker, context));

            relink.ReturnType = relink.ReturnType?.Relink(relinker, relink);

            foreach (var param in method.Parameters)
            {
                param.ParameterType = param.ParameterType.Relink(relinker, method);
                relink.Parameters.Add(param);
            }

            return (MethodReference)relinker(relink, context);
        }

        /// <summary>
        /// Relink the given callsite.
        /// </summary>
        /// <param name="method">The reference to relink.</param>
        /// <param name="relinker">The relinker to use during the relinking process.</param>
        /// <param name="context">The generic context provided to relink generic references.</param>
        /// <returns>A relinked reference.</returns>
        public static CallSite Relink(this CallSite method, Relinker relinker, IGenericParameterProvider context)
        {
            Helpers.ThrowIfArgumentNull(method);
            Helpers.ThrowIfArgumentNull(relinker);
            var relink = new CallSite(method.ReturnType);

            relink.CallingConvention = method.CallingConvention;
            relink.ExplicitThis = method.ExplicitThis;
            relink.HasThis = method.HasThis;

            relink.ReturnType = relink.ReturnType?.Relink(relinker, context);

            foreach (var param in method.Parameters)
            {
                param.ParameterType = param.ParameterType.Relink(relinker, context);
                relink.Parameters.Add(param);
            }

            return (CallSite)relinker(relink, context);
        }

        /// <summary>
        /// Relink the given field reference.
        /// </summary>
        /// <param name="field">The reference to relink.</param>
        /// <param name="relinker">The relinker to use during the relinking process.</param>
        /// <param name="context">The generic context provided to relink generic references.</param>
        /// <returns>A relinked reference.</returns>
        public static IMetadataTokenProvider Relink(this FieldReference field, Relinker relinker, IGenericParameterProvider context)
        {
            Helpers.ThrowIfArgumentNull(field);
            Helpers.ThrowIfArgumentNull(relinker);
            var declaringType = field.DeclaringType.Relink(relinker, context);
            return relinker(new FieldReference(field.Name, field.FieldType.Relink(relinker, declaringType), declaringType), context);
        }

        /// <summary>
        /// Relink the given parameter definition.
        /// </summary>
        /// <param name="param">The reference to relink.</param>
        /// <param name="relinker">The relinker to use during the relinking process.</param>
        /// <param name="context">The generic context provided to relink generic references.</param>
        /// <returns>A relinked reference.</returns>
        public static ParameterDefinition Relink(this ParameterDefinition param, Relinker relinker, IGenericParameterProvider context)
        {
            Helpers.ThrowIfArgumentNull(param);
            Helpers.ThrowIfArgumentNull(relinker);
            param = (param.Method as MethodReference)?.Parameters[param.Index] ?? param;
            var newParam = new ParameterDefinition(param.Name, param.Attributes, param.ParameterType.Relink(relinker, context))
            {
                IsIn = param.IsIn,
                IsLcid = param.IsLcid,
                IsOptional = param.IsOptional,
                IsOut = param.IsOut,
                IsReturnValue = param.IsReturnValue,
                MarshalInfo = param.MarshalInfo
            };
            if (param.HasConstant)
                newParam.Constant = param.Constant;
            return newParam;
        }

        /// <summary>
        /// Clone the given parameter definition.
        /// </summary>
        /// <param name="param">The original parameter definition.</param>
        /// <returns>A clone of the original parameter definition.</returns>
        public static ParameterDefinition Clone(this ParameterDefinition param)
        {
            Helpers.ThrowIfArgumentNull(param);
            var newParam = new ParameterDefinition(param.Name, param.Attributes, param.ParameterType)
            {
                IsIn = param.IsIn,
                IsLcid = param.IsLcid,
                IsOptional = param.IsOptional,
                IsOut = param.IsOut,
                IsReturnValue = param.IsReturnValue,
                MarshalInfo = param.MarshalInfo
            };
            if (param.HasConstant)
                newParam.Constant = param.Constant;
            foreach (var attrib in param.CustomAttributes)
                newParam.CustomAttributes.Add(attrib.Clone());
            return newParam;
        }

        /// <summary>
        /// Relink the given custom attribute.
        /// </summary>
        /// <param name="attrib">The reference to relink.</param>
        /// <param name="relinker">The relinker to use during the relinking process.</param>
        /// <param name="context">The generic context provided to relink generic references.</param>
        /// <returns>A relinked reference.</returns>
        public static CustomAttribute Relink(this CustomAttribute attrib, Relinker relinker, IGenericParameterProvider context)
        {
            Helpers.ThrowIfArgumentNull(attrib);
            Helpers.ThrowIfArgumentNull(relinker);
            var newAttrib = new CustomAttribute((MethodReference)attrib.Constructor.Relink(relinker, context));
            foreach (var attribArg in attrib.ConstructorArguments)
                newAttrib.ConstructorArguments.Add(new CustomAttributeArgument(attribArg.Type.Relink(relinker, context), attribArg.Value));
            foreach (var attribArg in attrib.Fields)
                newAttrib.Fields.Add(new CustomAttributeNamedArgument(attribArg.Name,
                    new CustomAttributeArgument(attribArg.Argument.Type.Relink(relinker, context), attribArg.Argument.Value))
                );
            foreach (var attribArg in attrib.Properties)
                newAttrib.Properties.Add(new CustomAttributeNamedArgument(attribArg.Name,
                    new CustomAttributeArgument(attribArg.Argument.Type.Relink(relinker, context), attribArg.Argument.Value))
                );
            return newAttrib;
        }

        /// <summary>
        /// Clone the given custom attribute.
        /// </summary>
        /// <param name="attrib">The original custom attribute.</param>
        /// <returns>A clone of the original custom attribute.</returns>
        public static CustomAttribute Clone(this CustomAttribute attrib)
        {
            Helpers.ThrowIfArgumentNull(attrib);
            var newAttrib = new CustomAttribute(attrib.Constructor);
            foreach (var attribArg in attrib.ConstructorArguments)
                newAttrib.ConstructorArguments.Add(new CustomAttributeArgument(attribArg.Type, attribArg.Value));
            foreach (var attribArg in attrib.Fields)
                newAttrib.Fields.Add(new CustomAttributeNamedArgument(attribArg.Name,
                    new CustomAttributeArgument(attribArg.Argument.Type, attribArg.Argument.Value))
                );
            foreach (var attribArg in attrib.Properties)
                newAttrib.Properties.Add(new CustomAttributeNamedArgument(attribArg.Name,
                    new CustomAttributeArgument(attribArg.Argument.Type, attribArg.Argument.Value))
                );
            return newAttrib;
        }

        /// <summary>
        /// Relink the given generic parameter reference.
        /// </summary>
        /// <param name="param">The reference to relink.</param>
        /// <param name="relinker">The relinker to use during the relinking process.</param>
        /// <param name="context">The generic context provided to relink generic references.</param>
        /// <returns>A relinked reference.</returns>
        public static GenericParameter Relink(this GenericParameter param, Relinker relinker, IGenericParameterProvider context)
        {
            Helpers.ThrowIfArgumentNull(param);
            Helpers.ThrowIfArgumentNull(relinker);
            var newParam = new GenericParameter(param.Name, param.Owner)
            {
                Attributes = param.Attributes
            }.Update(param.Position, param.Type);
            foreach (var attr in param.CustomAttributes)
                newParam.CustomAttributes.Add(attr.Relink(relinker, context));
#pragma warning disable IDE0008 // TypeReference in cecil 0.10, GenericParameterConstraint in cecil 0.11
            foreach (var constraint in param.Constraints)
#pragma warning restore IDE0008
                newParam.Constraints.Add(constraint.Relink(relinker, context));
            return newParam;
        }

        /// <summary>
        /// Clone the given generic parameter.
        /// </summary>
        /// <param name="param">The original generic parameter.</param>
        /// <returns>A clone of the original generic parameter.</returns>
        public static GenericParameter Clone(this GenericParameter param)
        {
            Helpers.ThrowIfArgumentNull(param);
            var newParam = new GenericParameter(param.Name, param.Owner)
            {
                Attributes = param.Attributes
            }.Update(param.Position, param.Type);
            foreach (var attr in param.CustomAttributes)
                newParam.CustomAttributes.Add(attr.Clone());
#pragma warning disable IDE0008 // TypeReference in cecil 0.10, GenericParameterConstraint in cecil 0.11
            foreach (var constraint in param.Constraints)
#pragma warning restore IDE0008
                newParam.Constraints.Add(constraint);
            return newParam;
        }

    }
}
