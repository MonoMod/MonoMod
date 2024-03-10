#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test
#pragma warning disable CA2201 // Do not raise reserved exception types
#pragma warning disable CA1031 // Do not catch general exception types

extern alias New;
using New::MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;
using Xunit.Abstractions;
#if NETFRAMEWORK
using System.Data.SqlClient;
using MonoMod.Utils;
#endif

namespace MonoMod.UnitTest
{
    [Collection("RuntimeDetour")]
    public unsafe class DetourExtTest : TestBase
    {
        public DetourExtTest(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void TestDetoursExt()
        {
            lock (TestObject.Lock)
            {
                // The following use cases are not meant to be usage examples.
                // Please take a look at DetourTest and HookTest instead.

                // Just to verify that having a first chance exception handler doesn't introduce any conflicts.
                AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;

#if false // NativeDetour doesn't exist in new RuntimeDetour
                using (NativeDetour d = new NativeDetour(
                    // .GetNativeStart() to enforce a native detour.
                    typeof(TestObject).GetMethod("TestStaticMethod").Pin().GetNativeStart(),
                    typeof(DetourExtTest).GetMethod("TestStaticMethod_A")
                )) {
                    int staticResult = d.GenerateTrampoline<Func<int, int, int>>()(2, 3);
                    Console.WriteLine($"TestStaticMethod(2, 3): {staticResult}");
                    Assert.Equal(6, staticResult);

                    staticResult = TestObject.TestStaticMethod(2, 3);
                    Console.WriteLine($"TestStaticMethod(2, 3): {staticResult}");
                    Assert.Equal(12, staticResult);
                }

                // We can't create a backup for this.
                MethodBase dm;
                using (DynamicMethodDefinition dmd = new DynamicMethodDefinition(typeof(TestObject).GetMethod("TestStaticMethod"))) {
                    dm = dmd.Generate();
                }
                using (NativeDetour d = new NativeDetour(
                    dm,
                    typeof(DetourExtTest).GetMethod("TestStaticMethod_A")
                )) {
                    int staticResult = d.GenerateTrampoline<Func<int, int, int>>()(2, 3);
                    Console.WriteLine($"TestStaticMethod(2, 3): {staticResult}");
                    Assert.Equal(6, staticResult);

                    // FIXME: dm.Invoke can fail with a release build in mono 5.X!
                    // staticResult = (int) dm.Invoke(null, new object[] { 2, 3 });
                    staticResult = ((Func<int, int, int>) dm.CreateDelegate<Func<int, int, int>>())(2, 3);
                    Console.WriteLine($"TestStaticMethod(2, 3): {staticResult}");
                    Assert.Equal(12, staticResult);
                }
#endif

                // This wasn't provided by anyone and instead is just an internal test.
#if false // See above
                MethodInfo dummyA = typeof(DetourExtTest).GetMethod("DummyA").Pin();
                MethodInfo dummyB = typeof(DetourExtTest).GetMethod("DummyB").Pin();
                MethodInfo dummyC = (MethodInfo) dm;
                IntPtr dummyAPtr = dummyA.GetNativeStart();
                Assert.True(DetourHelper.Runtime.TryMemAllocScratchCloseTo(dummyAPtr, out IntPtr allocAPtr, -1) != 0);
                Assert.NotEqual(IntPtr.Zero, allocAPtr);
                IntPtr dummyBPtr = dummyB.GetNativeStart();
                Assert.True(DetourHelper.Runtime.TryMemAllocScratchCloseTo(dummyBPtr, out IntPtr allocBPtr, -1) != 0);
                Assert.NotEqual(IntPtr.Zero, allocBPtr);
                IntPtr dummyCPtr = dummyC.GetNativeStart();
                Assert.True(DetourHelper.Runtime.TryMemAllocScratchCloseTo(dummyCPtr, out IntPtr allocCPtr, -1) != 0);
                Assert.NotEqual(IntPtr.Zero, allocCPtr);
                Console.WriteLine($"dummyAPtr: 0x{(long) dummyAPtr:X16}");
                Console.WriteLine($"allocAPtr: 0x{(long) allocAPtr:X16}");
                Console.WriteLine($"dummyBPtr: 0x{(long) dummyBPtr:X16}");
                Console.WriteLine($"allocBPtr: 0x{(long) allocBPtr:X16}");
                Console.WriteLine($"dummyCPtr: 0x{(long) dummyCPtr:X16}");
                Console.WriteLine($"allocCPtr: 0x{(long) allocCPtr:X16}");
                // Close scratch allocs should ideally be within a 1 GiB range of the original method.
                Assert.True(Math.Abs((long) dummyAPtr - (long) allocAPtr) < 1024 * 1024 * 1024, "dummyAPtr and allocAPtr are too far apart.");
                Assert.True(Math.Abs((long) dummyBPtr - (long) allocBPtr) < 1024 * 1024 * 1024, "dummyBPtr and allocBPtr are too far apart.");
                Assert.True(Math.Abs((long) dummyCPtr - (long) allocCPtr) < 1024 * 1024 * 1024, "dummyCPtr and allocCPtr are too far apart.");
#endif

                // This was provided by Chicken Bones (tModLoader).
                // GetEncoding behaves differently on .NET Core and even between .NET Framework versions,
                // which is why this test only applies to Mono, preferably on Linux to verify if flagging
                // regions of code as read-writable and then read-executable works for AOT'd code.
#if false
                using (var h = new Hook(
                    typeof(Encoding).GetMethod("GetEncoding", new Type[] { typeof(string) }),
                    new Func<Func<string, Encoding>, string, Encoding>((orig, name) => {
                        if (name == "IBM437")
                            return null;
                        return orig(name);
                    })
                )) {
                    Assert.Null(Encoding.GetEncoding("IBM437"));
                }
#endif

                // This was provided by a Harmony user.
                // TextWriter's methods (including all overrides) were unable to be hooked on some runtimes.
                // FIXME: .NET 5 introduces similar behavior for macOS and Linux, but RD isn't ready for that. See DetourRuntimeNETPlatform for more info.
#if true
                using (var ms = new MemoryStream())
                {

                    using (var writer = new StreamWriter(ms, Encoding.UTF8, 1024, true))
                    {
                        // In case anyone needs to debug this mess anytime in the future ever again:
                        /*/
                        MethodBase m = typeof(StreamWriter).GetMethod("Write", new Type[] { typeof(string) });
                        Console.WriteLine($"meth: 0x{(long) m?.MethodHandle.Value:X16}");
                        Console.WriteLine($"getf: 0x{(long) m?.MethodHandle.GetFunctionPointer():X16}");
                        Console.WriteLine($"fptr: 0x{(long) m?.GetLdftnPointer():X16}");
                        Console.WriteLine($"nats: 0x{(long) m?.GetNativeStart():X16}");
                        /**/

                        // Debugger.Break();
                        writer.Write("A");

                        using (var h = new Hook(
                            typeof(StreamWriter).GetMethod("Write", new Type[] { typeof(string) }),
                            new Action<Action<StreamWriter, string>, StreamWriter, string>((orig, self, value) =>
                            {
                                orig(self, "-");
                            })
                        ))
                        {
                            // Debugger.Break();
                            writer.Write("B");
                        }

                        writer.Write("C");
                    }

                    ms.Seek(0, SeekOrigin.Begin);

                    using (var reader = new StreamReader(ms, Encoding.UTF8, false, 1024, true))
                    {
                        Assert.Equal("A-C", reader.ReadToEnd());
                    }

                }
#endif

#if NETFRAMEWORK && true
                using (var cmd = new SqlCommand("A"))
                    Assert.Equal("A", cmd.CommandText);

                using (var h = new Hook(
                    typeof(SqlCommand).GetConstructor(new Type[] { typeof(string) }),
                    new Action<Action<SqlCommand, string>, SqlCommand, string>((orig, self, value) => {
                        orig(self, "-");
                    })
                )) {
                    using (var cmd = new SqlCommand("B"))
                        Assert.Equal("-", cmd.CommandText);
                }

                using (var cmd = new SqlCommand("C"))
                    Assert.Equal("C", cmd.CommandText);
#endif


                // This was provided by tModLoader.
                // The .NET Framework codepath failed on making the method writable the for a single user.
#if NETFRAMEWORK && true
                try {
                    throw new Exception();
                } catch (Exception e) {
                    Assert.NotEqual("", e.StackTrace.Trim());
                }

                using (var h = PlatformDetection.Runtime is RuntimeKind.Mono ?
                    // Mono
                    new Hook(
                        typeof(Exception).GetMethod("GetStackTrace", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance),
                        new Func<Func<Exception, bool, string>, Exception, bool, string>((orig, self, fNeedFileInfo) => {
                            return "";
                        })
                    ) :
                    // .NET
                    new Hook(
                        typeof(StackTrace).GetConstructor(new[] { typeof(Exception), typeof(bool) }),
                        new Action<Action<StackTrace, Exception, bool>, StackTrace, Exception, bool>((orig, self, e, fNeedFileInfo) => {
                            orig(self, e, fNeedFileInfo);
                            DynamicData.Set(self, new {
                                frames = Array.Empty<StackFrame>(),
                                m_iNumOfFrames = 0,
                                m_iMethodsToSkip = 0
                            });
                        })
                    )) {

                    try {
                        throw new Exception();
                    } catch (Exception e) {
                        Assert.Equal("", e.StackTrace.Trim());
                    }
                }

                try {
                    throw new Exception();
                } catch (Exception e) {
                    Assert.NotEqual("", e.StackTrace.Trim());
                }
#endif

                // This was provided by a Harmony user.
                // Theoretically this should be a DynamicMethodDefinition test but who knows what else this will unearth.
#if true
                try
                {
                    _ = new Thrower(1);
                }
                catch (Exception e)
                {
                    Assert.Equal("1", e.Message);
                }

                using (var h = new Hook(
                    typeof(Thrower).GetConstructor(new Type[] { typeof(int) }),
                    new Action<Action<Thrower, int>, Thrower, int>((orig, self, a) =>
                    {
                        try
                        {
                            orig(self, a + 2);
                        }
                        catch (Exception e)
                        {
                            throw new Exception($"{a} + 2 = {e.Message}");
                        }
                    })
                ))
                {
                    try
                    {
                        _ = new Thrower(1);
                    }
                    catch (Exception e)
                    {
                        Assert.Equal("1 + 2 = 3", e.Message);
                    }
                }

                try
                {
                    _ = new Thrower(1);
                }
                catch (Exception e)
                {
                    Assert.Equal("1", e.Message);
                }
#endif

                // This was provided by tModLoader.
#if true
                using (var h = new Hook(
                    typeof(Process).GetMethod("Start", BindingFlags.Public | BindingFlags.Instance),
                    new Func<Func<Process, bool>, Process, bool>((orig, self) =>
                    {
                        return orig(self);
                    })
                ))
                {
                }
#endif

                // This was provided by WEGFan from the Everest team.
                // It should be preferably tested on x86, as it's where the struct size caused problems.
#if true
                Assert.Equal(new TwoInts()
                {
                    A = 11111111,
                    B = 22222222
                }, DummyTwoInts());

                using (var h = new Hook(
                    typeof(DetourExtTest).GetMethod("DummyTwoInts", BindingFlags.NonPublic | BindingFlags.Instance),
                    new Func<Func<DetourExtTest, TwoInts>, DetourExtTest, TwoInts>((orig, self) =>
                    {
                        var rv = orig(self);
                        rv.A *= 2;
                        rv.B *= 3;
                        return rv;
                    })
                ))
                {
                    Assert.Equal(new TwoInts()
                    {
                        A = 11111111 * 2,
                        B = 22222222 * 3
                    }, DummyTwoInts());
                }

                Assert.Equal(new TwoInts()
                {
                    A = 11111111,
                    B = 22222222
                }, DummyTwoInts());
#endif

                // This was provided by a Harmony user.
                // The "struct virtual override" edge case fix itself has got a weird edge case with "struct interface implementations".
                // Note that .NET Framework differs too heavily and .NET Core 2.1 and older inline the getter.
#if NET5_0_OR_GREATER && false // These cases are to do with generics, not anything with struct overrides.
                Assert.Equal(
                    new KeyValuePair<int, int>(default, default),
                    new Dictionary<int, int>().GetEnumerator().Current
                );

                using (var h = new Hook(
                    typeof(Dictionary<int, int>.Enumerator).GetMethod("get_Current"),
                    new hook_DictionaryEnumeratorCurrentIntInt(DictionaryEnumeratorCurrentIntInt)
                )) {
                    Assert.Equal(
                        new KeyValuePair<int, int>(1, 1),
                        new Dictionary<int, int>().GetEnumerator().Current
                    );
                }

                Assert.Equal(
                    new KeyValuePair<int, int>(default, default),
                    new Dictionary<int, int>().GetEnumerator().Current
                );

                Assert.Equal(
                    new KeyValuePair<string, int>(default, default),
                    new Dictionary<string, int>().GetEnumerator().Current
                );

                using (var h = new Hook(
                    typeof(Dictionary<string, int>.Enumerator).GetMethod("get_Current"),
                    new hook_DictionaryEnumeratorCurrentStringInt(DictionaryEnumeratorCurrentStringInt)
                )) {
                    Assert.Equal(
                        new KeyValuePair<string, int>("1", 1),
                        new Dictionary<string, int>().GetEnumerator().Current
                    );
                }

                Assert.Equal(
                    new KeyValuePair<string, int>(default, default),
                    new Dictionary<string, int>().GetEnumerator().Current
                );
#endif

                // This is based off of something provided by a Harmony user.
                // It should be preferably tested on x86, as it's where the edge case with this certain return size occurred.
#if true
                Assert.Equal(
                    11111111,
                    new TwoInts()
                    {
                        A = 11111111,
                        B = 22222222
                    }.Magic
                );

                using (var h = new Hook(
                    typeof(TwoInts).GetMethod("get_Magic", BindingFlags.Public | BindingFlags.Instance),
                    new Func<Func<IntPtr, int>, IntPtr, int>((orig, self) =>
                    {
                        var rv = orig(self);
                        rv = rv * 2 + ((TwoInts*)self)->B;
                        return rv;
                    })
                ))
                {
                    Assert.Equal(
                        11111111 * 2 + 22222222,
                        new TwoInts()
                        {
                            A = 11111111,
                            B = 22222222
                        }.Magic
                    );
                }

                Assert.Equal(
                    11111111,
                    new TwoInts()
                    {
                        A = 11111111,
                        B = 22222222
                    }.Magic
                );
#endif


                AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;

            }
        }

        private void OnFirstChanceException(object sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            // nop
        }

        internal static int TestStaticMethod_A(int a, int b)
        {
            return a * b * 2;
        }

        internal class Thrower
        {
#pragma warning disable CS0649 // Not initialized
            public int b;
#pragma warning restore CS0649

            public Thrower(int a)
            {
                throw new Exception(a.ToString(CultureInfo.InvariantCulture));
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining | /* AggressiveOptimization */ ((MethodImplOptions)512))]
        internal static int DummyA(int a, int b)
        {
            return a * b * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining | /* AggressiveOptimization */ ((MethodImplOptions)512))]
        internal static int DummyB(int a, int b)
        {
            return a * b * 2;
        }

        internal struct TwoInts
        {
            public int A;
            public int B;
            public int Magic
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                get
                {
                    return A;
                }
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            public override string ToString()
                => $"({A}, {B})";
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private TwoInts DummyTwoInts()
        {
            return new TwoInts()
            {
                A = 11111111,
                B = 22222222
            };
        }

        private delegate KeyValuePair<int, int> orig_DictionaryEnumeratorCurrentIntInt(ref Dictionary<int, int>.Enumerator self);
        private delegate KeyValuePair<int, int> hook_DictionaryEnumeratorCurrentIntInt(orig_DictionaryEnumeratorCurrentIntInt orig, ref Dictionary<int, int>.Enumerator self);
        private static KeyValuePair<int, int> DictionaryEnumeratorCurrentIntInt(orig_DictionaryEnumeratorCurrentIntInt orig, ref Dictionary<int, int>.Enumerator self)
        {
            return new KeyValuePair<int, int>(1, 1);
        }

        private delegate KeyValuePair<string, int> orig_DictionaryEnumeratorCurrentStringInt(ref Dictionary<string, int>.Enumerator self);
        private delegate KeyValuePair<string, int> hook_DictionaryEnumeratorCurrentStringInt(orig_DictionaryEnumeratorCurrentStringInt orig, ref Dictionary<string, int>.Enumerator self);
        private static KeyValuePair<string, int> DictionaryEnumeratorCurrentStringInt(orig_DictionaryEnumeratorCurrentStringInt orig, ref Dictionary<string, int>.Enumerator self)
        {
            return new KeyValuePair<string, int>("1", 1);
        }

    }
}
