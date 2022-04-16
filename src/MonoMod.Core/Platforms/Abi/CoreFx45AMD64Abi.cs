using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonoMod.Core.Platforms.Abi {
    public class CoreFx45AMD64Abi : AbiBase {
        private readonly IAbi baseAbi;

        public CoreFx45AMD64Abi(IAbi baseAbi) => this.baseAbi = baseAbi;

        // https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/botr/clr-abi.md#the-this-pointer
        // > AMD64-only: Up to .NET Framework 4.5, the managed this pointer was treated just like the native
        // > this pointer (meaning it was the second argument when the call used a return buffer and was passed
        // > in RDX instead of RCX). Starting with .NET Framework 4.5, it is always the first argument.
        public override ReadOnlyMemory<SpecialArgumentKind> ArgumentOrder { get; } = new[] {
            SpecialArgumentKind.ThisPointer,
            SpecialArgumentKind.ReturnBuffer,
            SpecialArgumentKind.UserArguments,
        };

        protected override TypeClassification ClassifyCore(Type type, bool isReturn) {
            // argument classification is not special afaict
            return baseAbi.Classify(type, isReturn);
        }
    }
}
