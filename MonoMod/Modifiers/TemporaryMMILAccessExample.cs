using MMILAccess;
using MonoMod;

namespace AccessExample {
    internal sealed class Something {
        private int SomeInt;
        private Something Thing { get; set; }

        public Something() { }
        public Something(string foo, int bar) { SomeInt = bar; }

        private void DoSomething(string test) { }
        public void Add(ref int a) { a += SomeInt; }

        public static string StaticA() { return "something"; }
        public static int StaticB(int a, int b) { return a + b; }
        public static int StaticC(params string[] a) { return a.Length; }
    }

    internal static class Example {

        public static void TestA() {
            Something e = new StaticAccess<Something>("System.Void .ctor()").New();

            // int someInt = e.SomeInt;
            int someInt = new Access<Something>(e, "SomeInt").Get<int>();

            // e = new Example(Example.StaticA(), Example.StaticB(Example.StaticB(2, 4), Example.StaticB(8, Example.StaticC("a", "b")));
            e = new StaticAccess<Something>("System.Void .ctor(System.String,System.Int32)")
                .New(
                    Something.StaticA(),
                    new StaticAccess<Something>("System.Void StaticB(System.Int32,System.Int32)")
                        .Call<int>(Something.StaticB(2, 4), Something.StaticB(8, Something.StaticC("a", "b"))
                )
            );

            // someInt = e.SomeInt;
            someInt = new Access<Something>(e, "SomeInt").Get<int>();

            // e = new Example("something!", 42);
            e = (Something) new StaticAccess("Example", "System.Void .ctor(System.String,System.Int32)").New("something!", 42);

            // e.SomeInt = someInt;
            new Access<Something>(e, "SomeInt").Set(someInt);

            // Example thing = e.Thing;
            Something thing = new Access(e, "Example", "Thing").Get<Something>();

            // e.DoSomething("hooray!");
            new Access<Something>(e, "DoSomething").Call("hooray!");

            // e.Add(ref someInt);
            object[] args = new object[] { someInt };
            new Access<Something>(e, "Add").Call("Add", args);
            someInt = (int) args[0];

        }

    }
}