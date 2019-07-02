#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

using Xunit;
using MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MonoMod.Utils;
using System.Reflection.Emit;
using System.Text;
using System.Collections.Generic;

namespace MonoMod.UnitTest {
    [Collection("RuntimeDetour")]
    public class DetourOrderTest {
        [Fact]
        public void TestDetoursOrder() {
            // The following use cases are not meant to be usage examples.
            // Please take a look at DetourTest and HookTest instead.

            MethodInfo original = typeof(DetourOrderTest).GetMethod("TestMethod");

            // Hooks behave as if they were "stacked" on top of each other.
            // In other words, the default "hook order" is reverse of the running order.
            // The expected list takes this into account.
            string[] order = {
                "Default",
                "AfterA",
                "A",
                "BeforeA"
            };
            string[] expected = {
                order[1],
                order[2],
                order[3],
                order[0]
            };
            List<string> actual = new List<string>();

            using (new Hook(
                original,
                new Func<Func<int, int, int>, int, int, int>((orig, a, b) => {
                    actual.Add(order[0]);
                    return orig(a, b) * a;
                })
            ))
            using (new Hook(
                original,
                new Func<Func<int, int, int>, int, int, int>((orig, a, b) => {
                    actual.Add(order[1]);
                    return orig(a, b) + b;
                }),
                new HookConfig() {
                    After = new [] { "A" }
                }
            ))
            using (new DetourContext("A"))
            using (new Hook(
                original,
                new Func<Func<int, int, int>, int, int, int>((orig, a, b) => {
                    actual.Add(order[2]);
                    return orig(a, b) + a;
                })
            ))
            using (new Hook(
                original,
                new Func<Func<int, int, int>, int, int, int>((orig, a, b) => {
                    actual.Add(order[3]);
                    return orig(a, b) * b;
                }),
                new HookConfig() {
                    Before = new[] { "A" }
                }
            )) {
                Assert.Equal(17, TestMethod(2, 3));
                Assert.Equal(expected, actual);
            }

        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int TestMethod(int a, int b) {
            return 2;
        }

    }
}
