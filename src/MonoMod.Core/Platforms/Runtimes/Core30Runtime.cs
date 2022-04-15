using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.Core.Platforms.Runtimes {
    internal class Core30Runtime : CoreBaseRuntime {

        public override RuntimeFeature Features => base.Features | RuntimeFeature.DisableInlining;

        public unsafe override void DisableInlining(MethodBase method) {
            // https://github.com/dotnet/runtime/blob/89965be3ad2be404dc82bd9e688d5dd2a04bcb5f/src/coreclr/src/vm/method.hpp#L178
            // mdcNotInline = 0x2000
            // References to RuntimeMethodHandle (CORINFO_METHOD_HANDLE) pointing to MethodDesc
            // can be traced as far back as https://ntcore.com/files/netint_injection.htm

            var handle = GetMethodHandle(method);

            const int offset =
                2 // UINT16 m_wFlags3AndTokenRemainder
              + 1 // BYTE m_chunkIndex
              + 1 // BYTE m_chunkIndex
              + 2 // WORD m_wSlotNumber
              ;
            ushort* m_wFlags = (ushort*) (((byte*) handle.Value) + offset);
            *m_wFlags |= 0x2000;
        }
    }
}
