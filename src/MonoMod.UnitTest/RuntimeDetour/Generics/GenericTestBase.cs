extern alias New;
using New::MonoMod.RuntimeDetour.Generics;
using System;
using System.Reflection;
using Xunit.Abstractions;

namespace MonoMod.UnitTest.RuntimeDetour.Generics
{
    public abstract class GenericTestBase(ITestOutputHelper helper) : TestBase(helper)
    {
        protected const BindingFlags AllDecl = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        protected static MethodInfo GetMethod(Type t, string name) => t.GetMethod(name, AllDecl) ?? throw new MissingMethodException(t.FullName, name);

        protected static bool IsShared(MethodInfo openMethod, params Type[] typeArgs) => GenericHelper.MethodIsShared(openMethod.MakeGenericMethod(typeArgs));
    }
}
