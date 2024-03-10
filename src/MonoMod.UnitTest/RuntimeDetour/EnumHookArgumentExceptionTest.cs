extern alias New;
using New::MonoMod.RuntimeDetour;
using System;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest.RuntimeDetour
{
    public class EnumHookArgumentExceptionTest : TestBase
    {
        public EnumHookArgumentExceptionTest(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void EnumHookArgumentTest()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                _ = new Hook(() => HookTarget(default), static (ValueType @enum) =>
                {
                    _ = @enum;
                });
            });

            Assert.Throws<ArgumentException>(() =>
            {
                _ = new Hook(() => HookTarget2(default), static (ValueType @enum) =>
                {
                    _ = @enum;
                });
            });

            Assert.Throws<ArgumentException>(() =>
            {
                _ = new Hook(() => HookTarget(default), static (Enum @enum) =>
                {
                    _ = @enum;
                });
            });
        }

        private enum TestEnum
        {
            None,
        }

        private struct TestStruct
        {

        }

        private static void HookTarget(TestEnum @enum)
        {
            _ = @enum;
        }

        private static void HookTarget2(TestStruct @struct)
        {
            _ = @struct;
        }
    }
}
