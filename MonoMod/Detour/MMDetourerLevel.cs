using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Mdb;
using Mono.Cecil.Pdb;
using Mono.Collections.Generic;
using MonoMod.InlineRT;
using StringInject;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MonoMod.NET40Shim;
using MonoMod.Helpers;
using System.Reflection;
using System.Runtime.InteropServices;
using TypeAttributes = Mono.Cecil.TypeAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using PropertyAttributes = Mono.Cecil.PropertyAttributes;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace MonoMod.Detour {
    public sealed class MonoModDetourerLevel {

        internal readonly MonoModDetourer _MMD;

        public readonly string Name;

        internal MonoModDetourerLevel(MonoModDetourer mmd, string name) {
            _MMD = mmd;
            Name = name;
        }

        public void RegisterTrampoline(TypeDefinition targetType, MethodDefinition method) {

        }

        public void RegisterDetour(TypeDefinition targetType, MethodDefinition method) {

        }

        public void Apply() {

        }

        public void Revert() {

        }

    }
}
