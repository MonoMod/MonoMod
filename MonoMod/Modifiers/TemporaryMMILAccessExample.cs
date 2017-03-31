using MMILExt;
using MonoMod;

internal sealed class Example {
    private int SomeInt;
    private Example Thing { get; set; }

    public Example() { }
    public Example(string foo, int bar) { SomeInt = bar; }

    private void DoSomething(string test) { }
    public void Add(ref int a) { a += SomeInt; }

    public static string StaticA() { return "something"; }
    public static int StaticB(int a, int b) { return a + b; }
    public static int StaticC(params string[] a) { return a.Length; }
}

internal static class ExampleTest {

    public static void TestA() {
        Example e = new StaticAccess<Example>("System.Void .ctor()").New();

        // int someInt = e.SomeInt;
        int someInt = new Access<Example>(e, "SomeInt").Get<int>();

        // e = new Example(Example.StaticA(), Example.StaticB(Example.StaticB(2, 4), Example.StaticB(8, Example.StaticC("a", "b")));
        e = new StaticAccess<Example>("System.Void .ctor(System.String,System.Int32)")
            .New(
                Example.StaticA(),
                new StaticAccess<Example>("System.Void StaticB(System.Int32,System.Int32)")
                    .Call<int>(Example.StaticB(2, 4), Example.StaticB(8, Example.StaticC("a", "b"))
            )
        );

        // someInt = e.SomeInt;
        someInt = new Access<Example>(e, "SomeInt").Get<int>();

        // e = new Example("something!", 42);
        e = (Example) new StaticAccess("Example", "System.Void .ctor(System.String,System.Int32)").New("something!", 42);

        // e.SomeInt = someInt;
        new Access<Example>(e, "SomeInt").Set(someInt);

        // Example thing = e.Thing;
        Example thing = new Access(e, "Example", "Thing").Get<Example>();

        // e.DoSomething("hooray!");
        new Access<Example>(e, "DoSomething").Call("hooray!");

        // e.Add(ref someInt);
        object[] args = new object[] { someInt };
        new Access<Example>(e, "Add").Call("Add", args);
        someInt = (int) args[0];

    }

}