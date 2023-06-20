namespace MonoMod.Packer.Diagnostics {
    internal enum ErrorCode {
        None = 0,

        DBG_ModuleSkipped,
        DBG_SkippedCorelibModule,

        WRN_CouldNotFindCorLibReference,
        ERR_CouldNotResolveCorLib,
        DBG_CouldNotResolveAssembly,

        WRN_MergingSystemObject,
        ERR_SystemObjectDefinitionHasBase,

        _Count,
    }
}
