#if NET5_0_OR_GREATER
using MonoMod.Core.Platforms;
using System;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace MonoMod.UnitTest.Github
{
    [Collection("RuntimeDetour")]
    public class Issue314
    {
        [Fact]
        public void DynamicMethodTokenResolutionExceptionRemainsCatchable()
        {
            Assert.NotNull(PlatformTriple.Current);

            var invalidMethod = new DynamicMethod(
                "BadToken",
                typeof(void),
                Type.EmptyTypes,
                typeof(Issue314).Module,
                skipVisibility: true);

            var invalidIL = invalidMethod.GetDynamicILInfo();
            invalidIL.SetLocalSignature(SignatureHelper.GetLocalVarSigHelper().GetSignature());
            // call <bogus MethodDef token 0x06FFFFFF>; ret
            invalidIL.SetCode(new byte[] { 0x28, 0xFF, 0xFF, 0xFF, 0x06, 0x2A }, maxStackSize: 8);

            var invocationException = Assert.Throws<TargetInvocationException>(() => invalidMethod.Invoke(null, null));
            Assert.IsType<InvalidProgramException>(invocationException.InnerException);

            var validMethod = new DynamicMethod("Valid", typeof(int), Type.EmptyTypes);
            var validIL = validMethod.GetILGenerator();
            validIL.Emit(OpCodes.Ldc_I4, 42);
            validIL.Emit(OpCodes.Ret);

            Assert.Equal(42, validMethod.CreateDelegate<Func<int>>()());
        }
    }
}
#endif
