namespace MonoMod.Packer.Driver {
    internal enum DefaultCorlibKind {
        Default = 0,
        Custom,

        Mscorlib2,
        Mscorlib4,

        SPCorlib4,
        SPCorlib5,
        SPCorlib6,
        SPCorlib7,

        Runtime4020,
        Runtime4100,
        Runtime4210,
        Runtime4220,
        Net5,
        Net6,
        Net7,

        Standard20,
        Standard21,

        Standard13 = Runtime4020,
        Standard14 = Standard13,
        Standard15 = Runtime4100,
        Standard16 = Standard15,
        Standard17 = Standard15,

        Core21 = Runtime4210,
        Core31 = Runtime4220,
    }
}
