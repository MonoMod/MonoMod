using MMILExt;

internal sealed class Example {
    private int Some;
    private Example Thing { get; set; }

    public Example() { }
    public Example(string foo, int bar) { Some = bar; }

    private void Wow(string test) { }

    public static string StaticA() { return "something"; }
    public static int StaticB(int a, int b) { return a + b; }
}

internal static class ExampleTest {

    public static void TestA() {
        Example e = (Example) MMIL.Access.Call<Example>(null, "System.Void .ctor()");

        int some = (int) e.MMILGet("Some");

        e = (Example) MMIL.Access.Call<Example>(
            null, "System.Void .ctor(System.String,System.Int32)",
            Example.StaticA(), (int) MMIL.Access.Call<Example>(
                null, "System.Void StaticB(System.Int32,System.Int32)",
                Example.StaticB(2, 4), Example.StaticB(8, 16)
            )
        );

        some = (int) e.MMILGet("Some");

        // gets replaced with newobj
        e = (Example) MMIL.Access.CallT(
            null, "Example", "System.Void .ctor(System.String,System.Int32)",
            "something!", 42
        );

        e.MMILSet("Some", some);

        Example thing = (Example) e.MMILGet("Thing");

        e.MMILCall("Wow", "hooray!");

    }

    public static void TestB() {
        Example e = (Example) MMIL.Access.Call<Example>(null, "System.Void .ctor()");

        int some = (int) e.MMILGet("Some");

        e = (Example) MMIL.Access.Call<Example>(
            null, "System.Void .ctor(System.String,System.Int32)",
            Example.StaticA(), (int) MMIL.Access.Call<Example>(
                null, "System.Void StaticB(System.Int32,System.Int32)",
                Example.StaticB(2, 4), Example.StaticB(8, 16)
            )
        );

        some = (int) e.MMILGet("Some");

        // gets replaced with newobj
        e = (Example) MMIL.Access.CallT(
            null, "Example", "System.Void .ctor(System.String,System.Int32)",
            "something!", 42
        );

        e.MMILSet("Some", some);

        Example thing = (Example) e.MMILGet("Thing");

        e.MMILCall("Wow", "hooray!");

    }

}