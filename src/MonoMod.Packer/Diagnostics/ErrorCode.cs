namespace MonoMod.Packer.Diagnostics {
    internal enum ErrorCode {
        None = 0,

        DBG_ModuleSkipped,
        DBG_SkippedCorelibModule,

        WRN_CouldNotFindCorLibReference,
        ERR_CouldNotResolveCorLib,
        WRN_CouldNotResolveAssembly,


        _Count,
    }
}
