#if NETCOREAPP3_0_OR_GREATER // ALCs are too new and too specific to test everywhere.

#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

using Xunit;
using MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MonoMod.Utils;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MC = Mono.Cecil;
using CIL = Mono.Cecil.Cil;
using System.Runtime.Loader;

namespace MonoMod.UnitTest {
    [Collection("RuntimeDetour")]
    public class AssemblyLoadContextHookTest {

        public static bool IsNonALC;
        public static int LastID1;
        public static int LastID2;

        [Fact]
        public void TestAssemblyLoadContextHook() {
            IsNonALC = true;

            WaitForWeakReferenceToDie(TestAssemblyLoadContextHookStep(0, 0));
            WaitForWeakReferenceToDie(TestAssemblyLoadContextHookStep(1, 1));
            WaitForWeakReferenceToDie(TestAssemblyLoadContextHookStep(1, 2));
        }

        private void WaitForWeakReferenceToDie(WeakReference weakref) {
            for (int i = 0; i < 10 && weakref.IsAlive; i++) {
                GC.Collect();
                GC.Collect();
            }
            // FIXME: Fix assembly load context (un)loadability!
            // Assert.False(weakref.IsAlive);
        }

        private WeakReference TestAssemblyLoadContextHookStep(int id1, int id2) {
            AssemblyLoadContext alc = new TestAssemblyLoadContext($"Test Context #{id1}");

            Assembly asm = alc.LoadFromAssemblyPath(Assembly.GetExecutingAssembly().Location);
            Type typeOrig = typeof(AssemblyLoadContextHookTest);
            Type type = asm.GetType(typeOrig.FullName);
            Assert.NotEqual(typeOrig, type);

            type.GetMethod("TestAssemblyLoadContextHookLoaded", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { this, id1, id2 });

            alc.Unload();

            Assert.Equal(id1, LastID1);
            Assert.Equal(id2, LastID2);

            return new WeakReference(alc);
        }

        private class TestAssemblyLoadContext : AssemblyLoadContext {

            public TestAssemblyLoadContext(string name)
                : base(name, isCollectible: true) {
            }

            protected override Assembly Load(AssemblyName name) {
                return null;
            }

        }

        // Everything below this comment should only run in the loaded ALCs.

        // This method runs in the loaded ALC.
        public static void TestAssemblyLoadContextHookLoaded(object loader, int id1, int id2) {
            Assert.NotEqual(typeof(AssemblyLoadContextHookTest), loader.GetType());
            MethodInfo method = loader.GetType().GetMethod("TestStaticMethod");
            using (Hook h = new Hook(
                method,
                new Action<Action<object, int, int>, object, int, int>((orig, hloader, hid1, hid2) => {
                    orig(loader, id1, id2);
                })
            )) {
                method.Invoke(null, new object[] { null, -1, -1 });
            }
        }

        // Only the non-ALC variant of this should be hooked and invoked.
        public static void TestStaticMethod(AssemblyLoadContextHookTest loader, int id1, int id2) {
            Assert.NotNull(loader);
            Assert.Equal(typeof(AssemblyLoadContextHookTest), loader.GetType());
            Assert.True(IsNonALC);
            Assert.NotEqual(-1, id1);
            Assert.NotEqual(-1, id2);
            LastID1 = id1;
            LastID2 = id2;
        }

    }
}

#endif
