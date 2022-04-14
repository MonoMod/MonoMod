#include "version.h"

#ifdef NET35
#define FUNC_ASSEMBLY "System.Core"
.assembly extern FUNC_ASSEMBLY
{
  .publickeytoken = CORE_PUBKEY_TOKEN
  .ver 3:5:0:0
}
#else
#define FUNC_ASSEMBLY "CORE_ASSEMBLY"
#endif

.class public abstract auto ansi sealed beforefieldinit MonoMod.ILHelpers
       extends [CORE_ASSEMBLY]System.Object
{

  .method public hidebysig static !!T TailCallDelegatePtr<T>(native int source) cil managed aggressiveinlining
  {
        .maxstack 8
        ldarg.0
        tail.
        calli !!T()
        ret
  } // end of method Unsafe::Read

  .method public hidebysig static !!T TailCallFunc<T>(class [FUNC_ASSEMBLY]System.Func`1<!!T> func) cil managed aggressiveinlining
  {
        .maxstack 8
        ldarg.0
        tail.
        callvirt instance !0 class [FUNC_ASSEMBLY]System.Func`1<!!T>::Invoke()
        ret
  }
}