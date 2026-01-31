extern alias New;
using New::MonoMod.RuntimeDetour;
using System;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest.Github
{
    public class Issue242 : TestBase
    {
        public Issue242(ITestOutputHelper helper) : base(helper)
        {
        }

        // Reproduce the exact struct from the issue
#pragma warning disable CS0649
        private struct SomeStruct
        {
            public double n1;
            public double n2;
            public double n3;
            public double n4;
        }
#pragma warning restore CS0649


        private class Mainclass
        {
            public virtual SomeStruct Method(string s)
            {
                Console.WriteLine("Mainclass.Method called");
                return default;
            }
        }

        private sealed class Subclass : Mainclass
        {
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1508:Avoid dead conditional code",
                Justification = "The test is specifically verifying that our hook behavior doens't cause `this` to become null")]
            public override SomeStruct Method(string s)
            {
                Console.WriteLine("Subclass.Method called");
                var me = this;
                Console.WriteLine("this = " + me);
                
                // This is the critical test - if 'this' is null, this would throw NullReferenceException
                // We're testing that this SHOULD NOT throw
                if (this == null)
                {
                    throw new InvalidOperationException("this instance became null - this is the bug!");
                }
                
                return default;
            }
        }

        [Fact]
        public void InstanceMethodReturningStructShouldNotMakeThisNull()
        {
            var instance = new Subclass();

            // This should work fine without any patches
            _ = instance.Method("test");

            using var hook = new ILHook(((Delegate)instance.Method).Method, c => { });

            // Test for correct behavior: this should NOT throw NullReferenceException
            // If the bug exists, this will fail because 'this' becomes null (and so the method throws)
            // If the bug is fixed, this will pass because 'this' remains valid
            _ = instance.Method("test");
        }
    }
}