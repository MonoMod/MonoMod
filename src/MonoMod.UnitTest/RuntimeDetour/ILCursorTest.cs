extern alias New;

using MonoMod.Cil;
using New::MonoMod.RuntimeDetour;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    [Collection("RuntimeDetour")]
    public class ILCursorTest : TestBase
    {
        private class HookTest : IDisposable
        {
            private ILHook _hook;

            public HookTest(MethodBase method, Action<ILCursor> editor)
            {
                _hook = new ILHook(method, il =>
                {
                    StripNops(il);
                    editor(new ILCursor(il));
                });
            }

            // Required for test to work in debug mode
            private void StripNops(ILContext il)
            {
                var c = new ILCursor(il);
                while (c.TryGotoNext(i => i.MatchNop()))
                    c.Remove();
            }

            public void Dispose() => _hook.Dispose();
        }

        public ILCursorTest(ITestOutputHelper helper) : base(helper)
        {
        }

        private static void EmitAppendTest(ILCursor c)
        {
            c.EmitLdloc(0);
            c.EmitLdstr("TEST");
            c.EmitCallvirt(new Func<string, StringBuilder>(new StringBuilder().Append).Method);
            c.EmitPop();
        }

        private static void EmitThrowEx(ILCursor c)
        {
            c.EmitNewobj(typeof(Exception).GetConstructor([]));
            c.EmitThrow();
        }

        private static string TestTryCatchFinallyILEdit(Action<ILCursor> editor)
        {
            var method = new Func<string>(TryCatchFinally).Method;
            Assert.Equal("trywhenfinallyreturn", TryCatchFinally());

            using (new HookTest(method, editor))
            {
                return TryCatchFinally();
            }
        }

        [Fact]
        public void TestMoveBeforeTry()
        {
            // moves before the try, but into the if block, and thus doesn't get executed
            Assert.Equal("trywhenfinallyreturn",
                TestTryCatchFinallyILEdit(c =>
                {
                    c.GotoNext(MoveType.Before, i => i.MatchLdloc(0), i => i.MatchLdstr("try"));
                    EmitThrowEx(c);
                })
            );

            // Moves before the try-catch, so the exception is unhandled
            Assert.Throws<Exception>(() =>
            {
                TestTryCatchFinallyILEdit(c =>
                {
                    c.GotoNext(MoveType.Before, i => i.MatchLdloc(0), i => i.MatchLdstr("try"));
                    c.MoveAfterLabels(intoEHRanges: false);
                    EmitThrowEx(c);
                });
            });
        }

        [Fact]
        public void TestMoveIntoTryStart()
        {
            // Moves into the try-handler, triggering the general catch clause
            Assert.Equal("catchfinallyreturn",
                TestTryCatchFinallyILEdit(c =>
                {
                    c.GotoNext(MoveType.AfterLabel, i => i.MatchLdloc(0), i => i.MatchLdstr("try"));
                    EmitThrowEx(c);
                })
            );
        }

        [Fact]
        public void TestMoveIntoHandlerFilter()
        {
            // Moves into the filter clause, even through the filter isn't triggered
            Assert.Equal("TESTcatchfinallyreturn",
                TestTryCatchFinallyILEdit(c =>
                {
                    c.GotoNext(MoveType.AfterLabel, i => i.MatchLdloc(0), i => i.MatchLdstr("try"));
                    EmitThrowEx(c);
                    c.GotoNext(MoveType.AfterLabel, i => i.MatchIsinst(out _));
                    EmitAppendTest(c);
                })
            );
        }

        [Fact]
        public void TestMoveIntoHandler()
        {
            // Deliberately moves into the catch clause with filter
            Assert.Equal("tryTESTwhenfinallyreturn",
                TestTryCatchFinallyILEdit(c =>
                {
                    c.Goto(c.Body.ExceptionHandlers.Single(eh => eh.FilterStart != null).HandlerStart, MoveType.AfterLabel);
                    EmitAppendTest(c);
                })
            );
        }

        [Fact]
        public void TestMoveAfterHandlers()
        {
            // After the finally block
            Assert.Equal("trywhenfinallyTESTreturn",
                TestTryCatchFinallyILEdit(c =>
                {
                    c.GotoNext(MoveType.AfterLabel,
                        i => i.MatchLdloc(0),
                        i => i.MatchLdstr("return"));
                    EmitAppendTest(c);
                })
            );
        }

        [Fact]
        public void TestRemoveMaintainsLabelPosition()
        {
            // throw statement inserted into try block
            Assert.Equal("catchfinallyreturn",
                TestTryCatchFinallyILEdit(c =>
                {
                    c.GotoNext(MoveType.Before, i => i.MatchLdloc(0), i => i.MatchLdstr("try"));
                    c.MoveAfterLabels();
                    c.RemoveRange(4);
                    EmitThrowEx(c);
                })
            );

            // throw statement inserted before the try block
            Assert.Throws<Exception>(() =>
            {
                TestTryCatchFinallyILEdit(c =>
                {
                    c.GotoNext(MoveType.Before, i => i.MatchLdloc(0), i => i.MatchLdstr("try"));
                    c.MoveAfterLabels(intoEHRanges: false);
                    c.RemoveRange(4);
                    EmitThrowEx(c);
                });
            });

            // throw statement inserted in unreachable if statement
            Assert.Equal("whenfinallyreturn",
                TestTryCatchFinallyILEdit(c =>
                {
                    c.GotoNext(MoveType.Before, i => i.MatchLdloc(0), i => i.MatchLdstr("try"));
                    c.RemoveRange(4);
                    EmitThrowEx(c);
                })
            );
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string TryCatchFinally()
        {
            var sb = new StringBuilder();
            if (sb.GetType() != typeof(StringBuilder))
            {
                sb.Append("unreachable");
            }
            try
            {
                sb.Append("try");
                throw new ArgumentException("test");
            }
            catch (Exception ex) when (ex is ArgumentException)
            {
                sb.Append("when");
            }
            catch
            {
                sb.Append("catch");
            }
            finally
            {
                sb.Append("finally");
            }
            sb.Append("return");
            return sb.ToString();
        }
    }
}
