using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MonoMod.RuntimeDetour.HookGen {
    class HookGenerator {

        const string ObsoleteMessageBackCompat = "This method only exists for backwards-compatibility purposes.";

        readonly static Regex NameVerifyRegex = new Regex("[^a-zA-Z]", RegexOptions.Compiled);

        public MonoModder Modder;

        public ModuleDefinition OutputModule;

        public string Namespace;
        public bool HookOrig;
        public bool HookPrivate;

        public ModuleDefinition module_RuntimeDetour;

        public TypeReference t_MulticastDelegate;
        public TypeReference t_IAsyncResult;
        public TypeReference t_AsyncCallback;
        public TypeReference t_MethodBase;
        public TypeReference t_RuntimeMethodHandle;
        public TypeReference t_EditorBrowsableState;

        public TypeReference t_HookEndpointManager;
        public TypeDefinition td_HookExtensions;
        public TypeReference t_HookExtensions;
        public TypeDefinition td_HookEndpoint;
        public TypeReference t_HookEndpoint;

        public MethodReference m_Object_ctor;
        public MethodReference m_ObsoleteAttribute_ctor;
        public MethodReference m_EditorBrowsableAttribute_ctor;

        public MethodReference m_GetMethodFromHandle;
        public MethodReference m_Get;
        public MethodReference m_Set;
        public MethodReference m_Add;
        public MethodReference m_Remove;

        public string HookExtName;
        public string HookWrapperName;
        public TypeDefinition td_HookExtWrapper;
        public TypeDefinition td_HookWrapper;
        public TypeDefinition td_ILManipulator;
        public MethodDefinition md_HookWrapper_ctor;
        public FieldDefinition fd_HookWrapper_Endpoint;

        public HookGenerator(MonoModder modder, string name) {
            Modder = modder;

            OutputModule = ModuleDefinition.CreateModule(name, new ModuleParameters {
                Architecture = modder.Module.Architecture,
                AssemblyResolver = modder.Module.AssemblyResolver,
                Kind = ModuleKind.Dll,
                Runtime = modder.Module.Runtime
            });

            Namespace = Environment.GetEnvironmentVariable("MONOMOD_HOOKGEN_NAMESPACE");
            if (string.IsNullOrEmpty(Namespace))
                Namespace = "On";
            HookOrig = Environment.GetEnvironmentVariable("MONOMOD_HOOKGEN_ORIG") == "1";
            HookPrivate = Environment.GetEnvironmentVariable("MONOMOD_HOOKGEN_PRIVATE") == "1";
            HookExtName = Environment.GetEnvironmentVariable("MONOMOD_HOOKGEN_EXTENSIONS");
            if (string.IsNullOrEmpty(HookExtName))
                HookExtName = $"ᴴᵒᵒᵏː{NameVerifyRegex.Replace(modder.Module.Assembly.Name.Name, "_")}ːExt";
            HookWrapperName = Environment.GetEnvironmentVariable("MONOMOD_HOOKGEN_WRAPPER");
            if (string.IsNullOrEmpty(HookWrapperName))
                HookWrapperName = $"ᴴᵒᵒᵏː{NameVerifyRegex.Replace(modder.Module.Assembly.Name.Name, "_")}";

            modder.MapDependency(modder.Module, "MonoMod.RuntimeDetour");
            if (!modder.DependencyCache.TryGetValue("MonoMod.RuntimeDetour", out module_RuntimeDetour))
                throw new FileNotFoundException("MonoMod.RuntimeDetour not found!");

            t_MulticastDelegate = OutputModule.ImportReference(modder.FindType("System.MulticastDelegate"));
            t_IAsyncResult = OutputModule.ImportReference(modder.FindType("System.IAsyncResult"));
            t_AsyncCallback = OutputModule.ImportReference(modder.FindType("System.AsyncCallback"));
            t_MethodBase = OutputModule.ImportReference(modder.FindType("System.Reflection.MethodBase"));
            t_RuntimeMethodHandle = OutputModule.ImportReference(modder.FindType("System.RuntimeMethodHandle"));
            t_EditorBrowsableState = OutputModule.ImportReference(modder.FindType("System.ComponentModel.EditorBrowsableState"));

            TypeDefinition td_HookEndpointManager = module_RuntimeDetour.GetType("MonoMod.RuntimeDetour.HookGen.HookEndpointManager");
            td_HookExtensions = module_RuntimeDetour.GetType("MonoMod.RuntimeDetour.HookGen.HookExtensions");
            t_HookExtensions = OutputModule.ImportReference(td_HookExtensions);
            t_HookEndpointManager = OutputModule.ImportReference(td_HookEndpointManager);
            td_HookEndpoint = module_RuntimeDetour.GetType("MonoMod.RuntimeDetour.HookGen.HookEndpoint`1");
            t_HookEndpoint = OutputModule.ImportReference(td_HookEndpoint);

            m_Object_ctor = OutputModule.ImportReference(modder.FindType("System.Object").Resolve().FindMethod("System.Void .ctor()"));
            m_ObsoleteAttribute_ctor = OutputModule.ImportReference(modder.FindType("System.ObsoleteAttribute").Resolve().FindMethod("System.Void .ctor(System.String,System.Boolean)"));
            m_EditorBrowsableAttribute_ctor = OutputModule.ImportReference(modder.FindType("System.ComponentModel.EditorBrowsableAttribute").Resolve().FindMethod("System.Void .ctor(System.ComponentModel.EditorBrowsableState)"));

            m_GetMethodFromHandle = OutputModule.ImportReference(
                new MethodReference("GetMethodFromHandle", t_MethodBase, t_MethodBase) {
                    Parameters = {
                        new ParameterDefinition(t_RuntimeMethodHandle)
                    }
                }
            );
            m_Get = OutputModule.ImportReference(td_HookEndpointManager.FindMethod("Get"));
            m_Set = OutputModule.ImportReference(td_HookEndpointManager.FindMethod("Set"));
            m_Add = OutputModule.ImportReference(td_HookEndpointManager.FindMethod("Add"));
            m_Remove = OutputModule.ImportReference(td_HookEndpointManager.FindMethod("Remove"));

        }

        public void Generate() {
            ILProcessor il;

            // Generate the hook wrappers before generating anything else.
            // This is required to prevent mods from depending on HookGen itself.
            if (td_HookExtWrapper == null) {
                Modder.LogVerbose($"[HookGen] Generating hook extensions wrapper {HookExtName}");
                int namespaceIndex = HookExtName.LastIndexOf(".");
                td_HookExtWrapper = new TypeDefinition(
                    namespaceIndex < 0 ? "" : HookExtName.Substring(0, namespaceIndex),
                    namespaceIndex < 0 ? HookExtName : HookExtName.Substring(namespaceIndex + 1),
                    td_HookExtensions.Attributes,
                    OutputModule.TypeSystem.Object
                );

                foreach (CustomAttribute attrib in td_HookExtensions.CustomAttributes)
                    td_HookExtWrapper.CustomAttributes.Add(attrib.Relink(Relinker, td_HookExtWrapper));

                // Proxy all public methods, events and properties from HookExtensions to HookExtWrapper.
                GenerateProxy(td_HookExtensions, td_HookExtWrapper, null, null, null);

                OutputModule.Types.Add(td_HookExtWrapper);
            }

            if (td_HookWrapper == null) {
                Modder.LogVerbose($"[HookGen] Generating hook wrapper {HookWrapperName}");
                int namespaceIndex = HookWrapperName.LastIndexOf(".");
                td_HookWrapper = new TypeDefinition(
                    namespaceIndex < 0 ? "" : HookWrapperName.Substring(0, namespaceIndex),
                    namespaceIndex < 0 ? HookWrapperName : HookWrapperName.Substring(namespaceIndex + 1),
                    td_HookEndpoint.Attributes,
                    OutputModule.TypeSystem.Object
                );
                if (!td_HookWrapper.Name.EndsWith("`1"))
                    td_HookWrapper.Name += "`1";

                foreach (CustomAttribute attrib in td_HookEndpoint.CustomAttributes)
                    td_HookWrapper.CustomAttributes.Add(attrib.Relink(Relinker, td_HookWrapper));

                td_HookWrapper.CustomAttributes.Add(GenerateEditorBrowsable(EditorBrowsableState.Never));

                foreach (GenericParameter genParam in td_HookEndpoint.GenericParameters)
                    td_HookWrapper.GenericParameters.Add(genParam.Relink(Relinker, td_HookWrapper));

                // Generate the nested delegate type.
                MethodDefinition md_ILManipulator_Invoke = td_HookEndpoint.NestedTypes.FirstOrDefault(n => n.Name == "ILManipulator").FindMethod("Invoke");
                md_ILManipulator_Invoke.IsStatic = true; // Prevent "self" parameter from being generated in new delegate type.
                td_ILManipulator = GenerateDelegateFor(md_ILManipulator_Invoke);
                td_ILManipulator.Name = "ILManipulator";
                td_HookWrapper.NestedTypes.Add(td_ILManipulator);

                // Generate all needed GenericInstanceType references.
                GenericInstanceType tg_HookWrapper = new GenericInstanceType(td_HookWrapper);
                foreach (GenericParameter genParam in td_HookWrapper.GenericParameters)
                    tg_HookWrapper.GenericArguments.Add(genParam);

                GenericInstanceType tg_HookEndpoint = new GenericInstanceType(t_HookEndpoint);
                foreach (GenericParameter genParam in td_HookWrapper.GenericParameters)
                    tg_HookEndpoint.GenericArguments.Add(genParam);

                GenericInstanceType tg_ILManipulator = new GenericInstanceType(td_ILManipulator);
                foreach (GenericParameter genParam in td_HookWrapper.GenericParameters) {
                    td_ILManipulator.GenericParameters.Add(genParam.Clone());
                    tg_ILManipulator.GenericArguments.Add(genParam);
                }

                // Generate the internal Endpoint field.
                fd_HookWrapper_Endpoint = new FieldDefinition("Endpoint", FieldAttributes.Assembly, tg_HookEndpoint);
                td_HookWrapper.Fields.Add(fd_HookWrapper_Endpoint);

                FieldReference fg_HookWrapper_Endpoint = fd_HookWrapper_Endpoint.Relink((mtp, ctx) => mtp, td_HookWrapper) as FieldReference;
                fg_HookWrapper_Endpoint.DeclaringType = tg_HookWrapper;

                // Generate a constructor.
                md_HookWrapper_ctor = new MethodDefinition(".ctor",
                    MethodAttributes.Assembly | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                    OutputModule.TypeSystem.Void
                );
                md_HookWrapper_ctor.Body = new MethodBody(md_HookWrapper_ctor);
                il = md_HookWrapper_ctor.Body.GetILProcessor();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, OutputModule.ImportReference(m_Object_ctor));
                il.Emit(OpCodes.Ret);
                td_HookWrapper.Methods.Add(md_HookWrapper_ctor);

                MethodReference mg_HookWrapper_ctor = md_HookWrapper_ctor.Relink((mtp, ctx) => mtp, td_HookWrapper);
                mg_HookWrapper_ctor.DeclaringType = tg_HookWrapper;

                // Proxy all public methods, events and properties from HookEndpoint to HookWrapper.
                GenerateProxy(td_HookEndpoint, td_HookWrapper, tg_HookEndpoint, mg_HookWrapper_ctor, fg_HookWrapper_Endpoint);

                // Modify the parameters of Add, Remove, Modify and Unmodify
                td_HookWrapper.FindMethod("Add").Parameters[0].ParameterType = td_HookWrapper.GenericParameters[0];
                td_HookWrapper.FindMethod("Remove").Parameters[0].ParameterType = td_HookWrapper.GenericParameters[0];

                // Generate the IL event.
                MethodDefinition add_IL = new MethodDefinition(
                    "add_IL",
                    MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.ReuseSlot,
                    OutputModule.TypeSystem.Void
                ) {
                    HasThis = true
                };
                td_HookWrapper.Methods.Add(add_IL);
                add_IL.Parameters.Add(new ParameterDefinition(null, ParameterAttributes.None, tg_ILManipulator));
                add_IL.Body = new MethodBody(add_IL);
                il = add_IL.Body.GetILProcessor();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                MethodReference mg_Modify = td_HookWrapper.FindMethod("Modify").Relink((mtp, ctx) => mtp, add_IL);
                mg_Modify.DeclaringType = tg_HookWrapper;
                il.Emit(OpCodes.Call, mg_Modify);
                il.Emit(OpCodes.Ret);

                MethodDefinition remove_IL = new MethodDefinition(
                    "remove_IL",
                    MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.ReuseSlot,
                    OutputModule.TypeSystem.Void
                ) {
                    HasThis = true
                };
                td_HookWrapper.Methods.Add(remove_IL);
                remove_IL.Parameters.Add(new ParameterDefinition(null, ParameterAttributes.None, tg_ILManipulator));
                remove_IL.Body = new MethodBody(remove_IL);
                il = remove_IL.Body.GetILProcessor();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                MethodReference mg_Unmodify = td_HookWrapper.FindMethod("Unmodify").Relink((mtp, ctx) => mtp, add_IL);
                mg_Unmodify.DeclaringType = tg_HookWrapper;
                il.Emit(OpCodes.Call, mg_Unmodify);
                il.Emit(OpCodes.Ret);

                EventDefinition evt_IL = new EventDefinition("IL", EventAttributes.None, tg_ILManipulator);
                evt_IL.AddMethod = add_IL;
                evt_IL.RemoveMethod = remove_IL;
                td_HookWrapper.Events.Add(evt_IL);

                // Generate a helper Hook event.
                // ... Don't.
                // Property, indexer, or event 'ᴴᵒᵒᵏːCeleste<Player.hook_GetTrailColor>.Hook' is not supported by the language; try directly calling accessor methods 'ᴴᵒᵒᵏːCeleste<Player.hook_GetTrailColor>.add_Hook(Player.hook_GetTrailColor)' or 'ᴴᵒᵒᵏːCeleste<Player.hook_GetTrailColor>.remove_Hook(Player.hook_GetTrailColor)'

                OutputModule.Types.Add(td_HookWrapper);
            }

            foreach (TypeDefinition type in Modder.Module.Types) {
                TypeDefinition hookType = GenerateFor(type);
                if (hookType == null)
                    continue;
                OutputModule.Types.Add(hookType);
            }
        }

        private void GenerateProxy(TypeDefinition from, TypeDefinition to, TypeReference fromRef, MethodReference wrapperCtor, FieldReference wrapperField) {
            ILProcessor il;
            foreach (MethodDefinition method in from.Methods) {
                if (method.IsRuntimeSpecialName || !method.IsPublic)
                    continue;

                MethodDefinition proxy = new MethodDefinition(method.Name, method.Attributes, method.ReturnType);
                to.Methods.Add(proxy);

                foreach (GenericParameter genParam in method.GenericParameters)
                    proxy.GenericParameters.Add(genParam.Relink(WrappedRelinker, proxy));

                foreach (ParameterDefinition param in method.Parameters)
                    proxy.Parameters.Add(param.Relink(WrappedRelinker, proxy));

                foreach (CustomAttribute attrib in method.CustomAttributes)
                    proxy.CustomAttributes.Add(attrib.Relink(WrappedRelinker, proxy));

                proxy.ReturnType = proxy.ReturnType?.Relink(WrappedRelinker, proxy);

                proxy.Body = new MethodBody(proxy);
                il = proxy.Body.GetILProcessor();

                if (method.ReturnType.GetElementType().FullName == from.FullName) {
                    il.Emit(OpCodes.Newobj, wrapperCtor);
                    il.Emit(OpCodes.Dup);
                }

                MethodReference methodRef = method.Relink(Relinker, proxy);
                methodRef.DeclaringType = fromRef ?? methodRef.DeclaringType;

                if (proxy.GenericParameters.Count != 0) {
                    GenericInstanceMethod methodRefGen = new GenericInstanceMethod(methodRef);
                    foreach (GenericParameter genParam in proxy.GenericParameters)
                        methodRefGen.GenericArguments.Add(genParam);
                    methodRef = methodRefGen;
                }

                if (!method.IsStatic) {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, wrapperField);
                    for (int i = 0; i < method.Parameters.Count; i++) {
                        il.Emit(OpCodes.Ldarg, i + 1);
                        if (method.Parameters[i].ParameterType.GetElementType().FullName == from.FullName)
                            il.Emit(OpCodes.Ldfld, wrapperField);
                    }
                    il.Emit(OpCodes.Callvirt, methodRef);
                } else {
                    for (int i = 0; i < method.Parameters.Count; i++) {
                        il.Emit(OpCodes.Ldarg, i);
                        if (method.Parameters[i].ParameterType.GetElementType().FullName == from.FullName)
                            il.Emit(OpCodes.Ldfld, wrapperField);
                    }
                    il.Emit(OpCodes.Call, methodRef);
                }

                if (method.ReturnType.GetElementType().FullName == t_HookEndpoint.FullName)
                    il.Emit(OpCodes.Stfld, wrapperField);
                il.Emit(OpCodes.Ret);

            }

            foreach (PropertyDefinition prop in from.Properties) {
                PropertyDefinition proxy = new PropertyDefinition(prop.Name, prop.Attributes, prop.PropertyType.Relink(WrappedRelinker, to));
                to.Properties.Add(proxy);

                MethodDefinition proxyMethod;

                if (prop.GetMethod != null) {
                    if ((proxyMethod = to.FindMethod(prop.GetMethod.GetFindableID(withType: false))) == null)
                        goto Next;
                    proxy.GetMethod = proxyMethod;
                }

                if (prop.SetMethod != null) {
                    if ((proxyMethod = to.FindMethod(prop.SetMethod.GetFindableID(withType: false))) == null)
                        goto Next;
                    proxy.SetMethod = proxyMethod;
                }

                foreach (MethodDefinition method in prop.OtherMethods) {
                    if ((proxyMethod = to.FindMethod(method.GetFindableID(withType: false))) == null)
                        goto Next;
                    proxy.OtherMethods.Add(proxyMethod);
                }

                Next: continue;
            }

            foreach (EventDefinition evt in from.Events) {
                EventDefinition proxy = new EventDefinition(evt.Name, evt.Attributes, evt.EventType.Relink(WrappedRelinker, to));
                to.Events.Add(proxy);

                MethodDefinition proxyMethod;

                if (evt.AddMethod != null) {
                    if ((proxyMethod = to.FindMethod(evt.AddMethod.GetFindableID(withType: false))) == null)
                        goto Next;
                    proxy.AddMethod = proxyMethod;
                }

                if (evt.RemoveMethod != null) {
                    if ((proxyMethod = to.FindMethod(evt.RemoveMethod.GetFindableID(withType: false))) == null)
                        goto Next;
                    proxy.RemoveMethod = proxyMethod;
                }

                if (evt.InvokeMethod != null) {
                    if ((proxyMethod = to.FindMethod(evt.InvokeMethod.GetFindableID(withType: false))) == null)
                        goto Next;
                    proxy.InvokeMethod = proxyMethod;
                }

                foreach (MethodDefinition method in evt.OtherMethods) {
                    if ((proxyMethod = to.FindMethod(method.GetFindableID(withType: false))) == null)
                        goto Next;
                    proxy.OtherMethods.Add(proxyMethod);
                }

                Next: continue;
            }
        }

        public TypeDefinition GenerateFor(TypeDefinition type) {
            if (type.HasGenericParameters ||
                type.IsRuntimeSpecialName ||
                type.Name.StartsWith("<"))
                return null;

            if (!HookPrivate && type.IsNotPublic)
                return null;

            Modder.LogVerbose($"[HookGen] Generating for type {type.FullName}");

            TypeDefinition hookType = new TypeDefinition(
                type.IsNested ? null : (Namespace + (string.IsNullOrEmpty(type.Namespace) ? "" : ("." + type.Namespace))),
                type.Name,
                (type.IsNested ? TypeAttributes.NestedPublic : TypeAttributes.Public) | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class,
                OutputModule.TypeSystem.Object
            );

            bool add = false;

            foreach (MethodDefinition method in type.Methods)
                add |= GenerateFor(hookType, method);

            foreach (TypeDefinition nested in type.NestedTypes) {
                TypeDefinition hookNested = GenerateFor(nested);
                if (hookNested == null)
                    continue;
                add = true;
                hookType.NestedTypes.Add(hookNested);
            }

            if (!add)
                return null;
            return hookType;
        }

        public bool GenerateFor(TypeDefinition hookType, MethodDefinition method) {
            if (method.HasGenericParameters ||
                (method.IsSpecialName && !method.IsConstructor))
                return false;

            if (!HookOrig && method.Name.StartsWith("orig_"))
                return false;
            if (!HookPrivate && method.IsPrivate)
                return false;

            int index = method.DeclaringType.Methods.Where(other => !other.HasGenericParameters && other.Name == method.Name).ToList().IndexOf(method);
            string suffix = "";
            if (index != 0) {
                suffix = index.ToString();
                do {
                    suffix = "_" + suffix;
                } while (method.DeclaringType.Methods.Any(other => !other.HasGenericParameters && other.Name == (method.Name + suffix)));
            }
            string name = method.Name;
            if (name.StartsWith("."))
                name = name.Substring(1);
            name = name + suffix;

            // TODO: Fix possible conflict when other members with the same names exist.

            TypeDefinition delOrig = GenerateDelegateFor(method);
            delOrig.Name = "orig_" + name;
            delOrig.CustomAttributes.Add(GenerateEditorBrowsable(EditorBrowsableState.Never));
            hookType.NestedTypes.Add(delOrig);

            TypeDefinition delHook = GenerateDelegateFor(method);
            delHook.Name = "hook_" + name;
            MethodDefinition delHookInvoke = delHook.FindMethod("Invoke");
            delHookInvoke.Parameters.Insert(0, new ParameterDefinition("orig", ParameterAttributes.None, delOrig));
            MethodDefinition delHookBeginInvoke = delHook.FindMethod("BeginInvoke");
            delHookBeginInvoke.Parameters.Insert(0, new ParameterDefinition("orig", ParameterAttributes.None, delOrig));
            delHook.CustomAttributes.Add(GenerateEditorBrowsable(EditorBrowsableState.Never));
            hookType.NestedTypes.Add(delHook);

            GenericInstanceType wrapperType = new GenericInstanceType(td_HookWrapper);
            wrapperType.GenericArguments.Add(delHook);
            GenericInstanceMethod endpointMethod;

            MethodReference mg_HookWrapper_ctor = md_HookWrapper_ctor.Relink((mtp, ctx) => mtp, td_HookWrapper);
            mg_HookWrapper_ctor.DeclaringType = wrapperType;

            FieldReference fg_HookWrapper_Endpoint = fd_HookWrapper_Endpoint.Relink((mtp, ctx) => mtp, td_HookWrapper) as FieldReference;
            fg_HookWrapper_Endpoint.DeclaringType = wrapperType;

            ILProcessor il;

            MethodDefinition get = new MethodDefinition(
                "get_" + name,
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.Static,
                wrapperType
            );
            get.Body = new MethodBody(get);
            il = get.Body.GetILProcessor();
            il.Emit(OpCodes.Newobj, mg_HookWrapper_ctor);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldtoken, OutputModule.ImportReference(method));
            il.Emit(OpCodes.Call, m_GetMethodFromHandle);
            endpointMethod = new GenericInstanceMethod(m_Get);
            endpointMethod.GenericArguments.Add(delHook);
            il.Emit(OpCodes.Call, endpointMethod);
            il.Emit(OpCodes.Stfld, fg_HookWrapper_Endpoint);
            il.Emit(OpCodes.Ret);
            hookType.Methods.Add(get);

            MethodDefinition set = new MethodDefinition(
                "set_" + name,
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.Static,
                OutputModule.TypeSystem.Void
            );
            set.Parameters.Add(new ParameterDefinition(null, ParameterAttributes.None, wrapperType));
            set.Body = new MethodBody(set);
            il = set.Body.GetILProcessor();
            il.Emit(OpCodes.Ldtoken, OutputModule.ImportReference(method));
            il.Emit(OpCodes.Call, m_GetMethodFromHandle);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, fg_HookWrapper_Endpoint);
            endpointMethod = new GenericInstanceMethod(m_Set);
            endpointMethod.GenericArguments.Add(delHook);
            il.Emit(OpCodes.Call, endpointMethod);
            il.Emit(OpCodes.Ret);
            hookType.Methods.Add(set);

            PropertyDefinition prop = new PropertyDefinition(name, PropertyAttributes.None, wrapperType) {
                GetMethod = get,
                SetMethod = set
            };
            hookType.Properties.Add(prop);

            // Legacy event proxies.

            MethodDefinition add = new MethodDefinition(
                "add_" + name,
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.Static,
                OutputModule.TypeSystem.Void
            );
            add.CustomAttributes.Add(GenerateObsolete(ObsoleteMessageBackCompat, true));
            add.CustomAttributes.Add(GenerateEditorBrowsable(EditorBrowsableState.Never));
            add.Parameters.Add(new ParameterDefinition(null, ParameterAttributes.None, delHook));
            add.Body = new MethodBody(add);
            il = add.Body.GetILProcessor();
            il.Emit(OpCodes.Ldtoken, OutputModule.ImportReference(method));
            il.Emit(OpCodes.Call, m_GetMethodFromHandle);
            il.Emit(OpCodes.Ldarg_0);
            endpointMethod = new GenericInstanceMethod(m_Add);
            endpointMethod.GenericArguments.Add(delHook);
            il.Emit(OpCodes.Call, endpointMethod);
            il.Emit(OpCodes.Ret);
            hookType.Methods.Add(add);

            MethodDefinition remove = new MethodDefinition(
                "remove_" + name,
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.Static,
                OutputModule.TypeSystem.Void
            );
            remove.CustomAttributes.Add(GenerateObsolete(ObsoleteMessageBackCompat, true));
            remove.CustomAttributes.Add(GenerateEditorBrowsable(EditorBrowsableState.Never));
            remove.Parameters.Add(new ParameterDefinition(null, ParameterAttributes.None, delHook));
            remove.Body = new MethodBody(remove);
            il = remove.Body.GetILProcessor();
            il.Emit(OpCodes.Ldtoken, OutputModule.ImportReference(method));
            il.Emit(OpCodes.Call, m_GetMethodFromHandle);
            il.Emit(OpCodes.Ldarg_0);
            endpointMethod = new GenericInstanceMethod(m_Remove);
            endpointMethod.GenericArguments.Add(delHook);
            il.Emit(OpCodes.Call, endpointMethod);
            il.Emit(OpCodes.Ret);
            hookType.Methods.Add(remove);

            return true;
        }

        public TypeDefinition GenerateDelegateFor(MethodDefinition method) {
            int index = method.DeclaringType.Methods.Where(other => !other.HasGenericParameters && other.Name == method.Name).ToList().IndexOf(method);
            string suffix = "";
            if (index != 0) {
                suffix = index.ToString();
                do {
                    suffix = "_" + suffix;
                } while (method.DeclaringType.Methods.Any(other => !other.HasGenericParameters && other.Name == (method.Name + suffix)));
            }
            string name = method.Name;
            if (name.StartsWith("."))
                name = name.Substring(1);
            name = "d_" + name+ suffix;

            TypeDefinition del = new TypeDefinition(
                null, null,
                TypeAttributes.NestedPublic | TypeAttributes.Sealed | TypeAttributes.Class,
                t_MulticastDelegate
            );

            MethodDefinition ctor = new MethodDefinition(
                ".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName | MethodAttributes.ReuseSlot,
                OutputModule.TypeSystem.Void
            ) {
                ImplAttributes = MethodImplAttributes.Runtime | MethodImplAttributes.Managed,
                HasThis = true
            };
            ctor.Parameters.Add(new ParameterDefinition(OutputModule.TypeSystem.Object));
            ctor.Parameters.Add(new ParameterDefinition(OutputModule.TypeSystem.IntPtr));
            ctor.Body = new MethodBody(ctor);
            del.Methods.Add(ctor);

            MethodDefinition invoke = new MethodDefinition(
                "Invoke",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                OutputModule.ImportReference(method.ReturnType)
            ) {
                ImplAttributes = MethodImplAttributes.Runtime | MethodImplAttributes.Managed,
                HasThis = true
            };
            if (!method.IsStatic)
                invoke.Parameters.Add(new ParameterDefinition("self", ParameterAttributes.None, OutputModule.ImportReference(method.DeclaringType)));
            foreach (ParameterDefinition param in method.Parameters)
                invoke.Parameters.Add(new ParameterDefinition(
                    param.Name,
                    param.Attributes & ~ParameterAttributes.Optional & ~ParameterAttributes.HasDefault,
                    OutputModule.ImportReference(param.ParameterType)
                ));
            foreach (ParameterDefinition param in method.Parameters) {
                // Check if the declaring type is accessible.
                // If not, use its base type instead.
                // Note: This will break down with type specifications!
                TypeDefinition paramType = param.ParameterType?.SafeResolve();
                TypeReference paramTypeRef = null;
                Retry:
                if (paramType == null)
                    continue;

                for (TypeDefinition parent = paramType; parent != null; parent = parent.DeclaringType) {
                    if (parent.IsNestedPublic || parent.IsPublic)
                        continue;

                    if (paramType.IsEnum) {
                        paramTypeRef = paramType.FindField("value__").FieldType;
                        break;
                    }

                    paramTypeRef = paramType.BaseType;
                    paramType = paramType.BaseType?.SafeResolve();
                    goto Retry;
                }

                // If paramTypeRef is null because the type is accessible, don't change it.
                if (paramTypeRef != null)
                    param.ParameterType = OutputModule.ImportReference(paramTypeRef);
            }
            invoke.Body = new MethodBody(invoke);
            del.Methods.Add(invoke);

            MethodDefinition invokeBegin = new MethodDefinition(
                "BeginInvoke",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                t_IAsyncResult
            ) {
                ImplAttributes = MethodImplAttributes.Runtime | MethodImplAttributes.Managed,
                HasThis = true
            };
            foreach (ParameterDefinition param in invoke.Parameters)
                invokeBegin.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes, param.ParameterType));
            invokeBegin.Parameters.Add(new ParameterDefinition("callback", ParameterAttributes.None, t_AsyncCallback));
            invokeBegin.Parameters.Add(new ParameterDefinition(null, ParameterAttributes.None, OutputModule.TypeSystem.Object));
            invokeBegin.Body = new MethodBody(invokeBegin);
            del.Methods.Add(invokeBegin);

            MethodDefinition invokeEnd = new MethodDefinition(
                "EndInvoke",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                OutputModule.TypeSystem.Object
            ) {
                ImplAttributes = MethodImplAttributes.Runtime | MethodImplAttributes.Managed,
                HasThis = true
            };
            invokeEnd.Parameters.Add(new ParameterDefinition("result", ParameterAttributes.None, t_IAsyncResult));
            invokeEnd.Body = new MethodBody(invokeEnd);
            del.Methods.Add(invokeEnd);

            return del;
        }

        CustomAttribute GenerateObsolete(string message, bool error) {
            CustomAttribute attrib = new CustomAttribute(m_ObsoleteAttribute_ctor);
            attrib.ConstructorArguments.Add(new CustomAttributeArgument(OutputModule.TypeSystem.String, message));
            attrib.ConstructorArguments.Add(new CustomAttributeArgument(OutputModule.TypeSystem.Boolean, error));
            return attrib;
        }

        CustomAttribute GenerateEditorBrowsable(EditorBrowsableState state) {
            CustomAttribute attrib = new CustomAttribute(m_EditorBrowsableAttribute_ctor);
            attrib.ConstructorArguments.Add(new CustomAttributeArgument(t_EditorBrowsableState, state));
            return attrib;
        }

        IMetadataTokenProvider Relinker(IMetadataTokenProvider mtp, IGenericParameterProvider context) {
            return OutputModule.ImportReference(Modder.Relinker(mtp, context));
        }

        IMetadataTokenProvider WrappedRelinker(IMetadataTokenProvider mtp, IGenericParameterProvider context) {
            if (mtp is TypeReference type) {
                if (type.FullName == t_HookEndpoint.FullName)
                    return td_HookWrapper;
                if (type.DeclaringType?.FullName == t_HookEndpoint.FullName && type.Name == td_ILManipulator.Name)
                    return td_ILManipulator;
            }

            return Relinker(mtp, context);
        }

    }
}
