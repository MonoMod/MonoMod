#if NETCOREAPP3_0_OR_GREATER // ALCs are too new and too specific to test everywhere.

#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

extern alias New;

using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using New::MonoMod.RuntimeDetour;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    [Collection("RuntimeDetour")]
    public class AssemblyLoadContextHookTest : TestBase
    {

        internal static bool IsNonALC;
        internal static object LastLoader;
        internal static int LastID1 = -1;
        internal static int LastID2 = -1;

        public AssemblyLoadContextHookTest(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void TestAssemblyLoadContextHook()
        {
            if (PlatformDetection.Runtime is RuntimeKind.Mono
                && PlatformDetection.Corelib is CorelibKind.Core
                && PlatformDetection.RuntimeVersion < new Version(8, 0))
            {
                // on .NET mono < 8.0, ALCs are not supported
                return;
            }

            IsNonALC = true;

            WaitForWeakReferenceToDie(TestAssemblyLoadContextHookStep(0, 0, true));
            WaitForWeakReferenceToDie(TestAssemblyLoadContextHookStep(1, 1, true));
            WaitForWeakReferenceToDie(TestAssemblyLoadContextHookStep(1, 2, true));
            TestAssemblyLoadContextHookStep(0, 0, false);
            TestAssemblyLoadContextHookStep(1, 1, false);
            TestAssemblyLoadContextHookStep(1, 2, false);
        }

        private static void WaitForWeakReferenceToDie(WeakReference weakref)
        {
            // FIXME: Figure out why the reference stays alive with .NET Core 3.1, sometimes 3.0
#if NET5_0_OR_GREATER
            for (var i = 0; i < 60 && weakref.IsAlive; i++)
            {
                GC.Collect();
                GC.Collect();
                GC.WaitForFullGCComplete();
                GC.WaitForPendingFinalizers();
            }
            Assert.False(weakref.IsAlive);
#endif
        }

        internal void Verify(object loader, int id1, int id2)
        {
            Assert.Equal(loader, LastLoader);
            Assert.Equal(id1, LastID1);
            Assert.Equal(id2, LastID2);

            LastLoader = null;
            LastID1 = -1;
            LastID2 = -1;
        }

        private WeakReference TestAssemblyLoadContextHookStep(int id1, int id2, bool canCollect)
        {
            AssemblyLoadContext alc = new TestAssemblyLoadContext($"Test Context #{id1}");

            var asm = alc.LoadFromAssemblyPath(Assembly.GetExecutingAssembly().Location);
            var typeOrig = typeof(AssemblyLoadContextHookTest);
            var type = asm.GetType(typeOrig.FullName);
            Assert.NotEqual(typeOrig, type);

            Verify(null, -1, -1);

            type.GetMethod("TestAssemblyLoadContextHookLoaded", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { this, id1, id2, canCollect });

            alc.Unload();

            return new WeakReference(alc);
        }

        private class TestAssemblyLoadContext : AssemblyLoadContext
        {

            public TestAssemblyLoadContext(string name)
                : base(name, isCollectible: true)
            {
            }

            protected override Assembly Load(AssemblyName name)
            {
                return null;
            }

        }

        // Everything below this comment should only run in the loaded ALCs.

        // This method runs in the loaded ALC.
        public static void TestAssemblyLoadContextHookLoaded(object loader, int id1, int id2, bool canCollect)
        {
            Assert.NotEqual(typeof(AssemblyLoadContextHookTest), loader.GetType());
            var method = loader.GetType().GetMethod("TestStaticMethod");
            var verify = loader.GetType().GetMethod("Verify", BindingFlags.Instance | BindingFlags.NonPublic);

            object[] argsEmpty = { null, -1, -1 };
            object[] argsSet = { loader, id1, id2 };

            verify.Invoke(loader, argsEmpty);

            using (new Hook(
                method,
                new Action<Action<object, int, int>, object, int, int>((orig, hloader, hid1, hid2) =>
                {
                    orig(loader, id1, id2);
                })
            ))
            {
                method.Invoke(null, new object[] { null, -1, -1 });
            }
            verify.Invoke(loader, argsSet);

            using (new Hook(
                method,
                new Action<Action<object, int, int>, object, int, int>((orig, hloader, hid1, hid2) =>
                {
                    orig(loader, id1, id2);
                })
            ))
            {
                method.Invoke(null, new object[] { null, -1, -1 });
            }
            verify.Invoke(loader, argsSet);

            verify.Invoke(loader, argsEmpty);
            LastLoader = loader;
            LastID1 = id1;
            LastID2 = id2;
            verify.Invoke(loader, argsEmpty);

            ((Action<Action<object, int, int>, object, int, int>)((orig, hloader, hid1, hid2) => TestStaticMethodTarget(orig, hloader, hid1, hid2)))
                .Invoke((oloader, oid1, oid2) => method.Invoke(null, new object[] { oloader, oid1, oid2 }), null, -1, -1);
            verify.Invoke(loader, argsSet);

            using (new Hook(
                method,
                (Action<Action<object, int, int>, object, int, int>)((orig, hloader, hid1, hid2) => TestStaticMethodTarget(orig, hloader, hid1, hid2))
            ))
            {
                method.Invoke(null, new object[] { null, -1, -1 });
            }
            verify.Invoke(loader, argsSet);

            using (new Hook(
                method,
                new Action<Action<object, int, int>, object, int, int>((orig, hloader, hid1, hid2) => TestStaticMethodTarget(orig, hloader, hid1, hid2))
            ))
            {
                method.Invoke(null, new object[] { null, -1, -1 });
            }
            verify.Invoke(loader, argsSet);

            using (new Hook(
                method,
                (Action<Action<object, int, int>, object, int, int>)TestStaticMethodTarget
            ))
            {
                method.Invoke(null, new object[] { null, -1, -1 });
            }
            verify.Invoke(loader, argsSet);

            using (new Hook(
                method,
                new Action<Action<object, int, int>, object, int, int>(TestStaticMethodTarget)
            ))
            {
                method.Invoke(null, new object[] { null, -1, -1 });
            }
            verify.Invoke(loader, argsSet);

            using (new Hook(
                method,
                typeof(AssemblyLoadContextHookTest).GetMethod("TestStaticMethodTarget")
            ))
            {
                method.Invoke(null, new object[] { null, -1, -1 });
            }
            verify.Invoke(loader, argsSet);

            TestILHookReferences(loader.GetType(), loader.GetType());
            if (!canCollect)
            {
                // AssemblyLoadContextHookTest.ILHookTarget will be hooked,
                // so DetourManager will reference it, prevents it from being collected.
                TestILHookReferences(loader.GetType(), typeof(AssemblyLoadContextHookTest));
            }
        }

        public static void TestStaticMethodTarget(Action<object, int, int> orig, object loader, int id1, int id2)
        {
            Helpers.ThrowIfNull(orig)(LastLoader, LastID1, LastID2);
        }

        // Only the non-ALC variant of this should be hooked and invoked.
        public static void TestStaticMethod(AssemblyLoadContextHookTest loader, int id1, int id2)
        {
            Assert.True(IsNonALC);
            Assert.NotNull(loader);
            Helpers.ThrowIfArgumentNull(loader);
            Assert.Equal(typeof(AssemblyLoadContextHookTest), loader.GetType());
            Assert.NotEqual(-1, id1);
            Assert.NotEqual(-1, id2);
            loader.Verify(null, -1, -1);
            LastLoader = loader;
            LastID1 = id1;
            LastID2 = id2;
        }

        public static void TestILHookReferences(Type baseLoaderType, Type type)
        {
            var alc = type != baseLoaderType;
            var method = type.GetMethod(nameof(ILHookTarget));

            // make sure the method require hooks to be installed
            Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { type, alc }));

            var typeFromHandleDelegate = Type.GetTypeFromHandle;
            // using a type
            using (new ILHook(method, il => new ILCursor(il)
                .GotoNext(i => i.Match(OpCodes.Ldnull))
                .Remove()
                .Emit(OpCodes.Ldtoken, type)
                .EmitCall(typeFromHandleDelegate.Method)
            ))
                method.Invoke(null, new object[] { type, alc });

            // using a call
            using (new ILHook(method, il => new ILCursor(il)
                .GotoNext(i => i.Match(OpCodes.Ldnull))
                .Remove()
                .EmitCall(type.GetMethod(nameof(SimpleMethodToReference)))
            ))
                method.Invoke(null, new object[] { type, alc });

            // using a generic
            using (new ILHook(method, il => new ILCursor(il)
                .GotoNext(i => i.Match(OpCodes.Ldnull))
                .Remove()
                .EmitCall(type.GetMethod(nameof(GenericMethodToRference)).MakeGenericMethod(type))
            ))
                method.Invoke(null, new object[] { type, alc });

            // using a method from another module, parameterized on a generic from this module
            using (new ILHook(method, il => new ILCursor(il)
                .GotoNext(i => i.Match(OpCodes.Ldnull))
                .Remove()
                .EmitCall(baseLoaderType.GetMethod(nameof(GenericMethodToRference)).MakeGenericMethod(type))
            ))
                method.Invoke(null, new object[] { type, alc });
        }

        [SuppressMessage("Assertions", "xUnit2003:Do not use equality check to test for null value",
            Justification = "The ldnull needs to exist for the tests above to work")]
        public static void ILHookTarget(Type expectedLoaderType, bool alc)
        {
            Assert.Equal(IsNonALC, !alc);
            Assert.Equal(null, expectedLoaderType);
            Assert.Equal(typeof(AssemblyLoadContextHookTest), expectedLoaderType);
        }

        public static Type SimpleMethodToReference() => typeof(AssemblyLoadContextHookTest);
        public static Type GenericMethodToRference<T>() => typeof(T);

    }
}

#endif
