using MonoMod.Utils;
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using Xunit;

namespace MonoMod.UnitTest
{
    public class MonoCorlibInternalTest
    {
        [Theory]
        [InlineData(4, false, false, false, 75)]
        [InlineData(8, false, false, false, 107)]
        [InlineData(4, true, false, false, 83)]
        [InlineData(8, true, false, false, 115)]
        [InlineData(4, false, true, false, 79)]
        [InlineData(8, false, true, false, 115)]
        [InlineData(4, true, true, false, 87)]
        [InlineData(8, true, true, false, 123)]
        [InlineData(4, false, true, true, 83)]
        [InlineData(8, false, true, true, 115)]
        [InlineData(4, true, true, true, 91)]
        [InlineData(8, true, true, true, 123)]
        public void TestCorlibInternalOffset(int pointerSize, bool isCoreBCL, bool hasArch, bool hasNameFlags, int expected)
        {
            var method = typeof(Extensions).GetMethod("GetMonoCorlibInternalOffset", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.Equal(expected, (int)method.Invoke(null, new object[] { pointerSize, isCoreBCL, hasArch, hasNameFlags }));
        }

        [Fact]
        public void TestSetMonoCorlibInternal()
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("MonoMod.UnitTest.CorlibInternal"), AssemblyBuilderAccess.Run);
            if (PlatformDetection.Runtime is not RuntimeKind.Mono)
            {
                assembly.SetMonoCorlibInternal(true);
                Assert.Contains(AppDomain.CurrentDomain.GetAssemblies(), a => a.FullName == assembly.FullName);
                return;
            }

            var field = assembly.GetType().GetField("dynamic_assembly", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                ?? assembly.GetType().GetField("_mono_assembly", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            var value = field.GetValue(assembly);
            var pointer = value is UIntPtr unsigned ? (IntPtr)(long)unsigned : (IntPtr)value;
            var hasArch = (bool)typeof(Extensions).GetField("_MonoAssemblyNameHasArch", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var hasNameFlags = (bool)typeof(Extensions).GetNestedType("MonoAssemblyNameLayout", BindingFlags.NonPublic)
                .GetField("HasNameFlags").GetValue(null);
            var isCoreBCL = typeof(object).Assembly.GetName().Name == "System.Private.CoreLib";
            var offset = (int)typeof(Extensions).GetMethod("GetMonoCorlibInternalOffset", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { IntPtr.Size, isCoreBCL, hasArch, hasNameFlags });
            var before = new byte[offset + 1];
            Marshal.Copy(pointer, before, 0, before.Length);
            try
            {
                assembly.SetMonoCorlibInternal(true);
                var after = new byte[before.Length];
                Marshal.Copy(pointer, after, 0, after.Length);
                Assert.Single(before.Where((b, i) => b != after[i]));
                Assert.Equal(1, after[offset]);
                Assert.DoesNotContain(assembly, AppDomain.CurrentDomain.GetAssemblies());

                assembly.SetMonoCorlibInternal(false);
                Assert.Contains(assembly, AppDomain.CurrentDomain.GetAssemblies());
                Marshal.Copy(pointer, after, 0, after.Length);
                Assert.Equal(before, after);
            }
            finally
            {
                Marshal.WriteByte(pointer, offset, before[offset]);
            }
        }
    }
}
