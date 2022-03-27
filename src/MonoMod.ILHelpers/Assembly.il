#define REFERENCE_CORE_ASSEMBLY
#include "version.h"

.assembly MonoMod.ILHelpers
{
  .custom instance void [CORE_ASSEMBLY]System.Runtime.CompilerServices.CompilationRelaxationsAttribute::.ctor(int32) = ( 01 00 08 00 00 00 00 00 )
  .custom instance void [CORE_ASSEMBLY]System.Runtime.CompilerServices.RuntimeCompatibilityAttribute::.ctor() = ( 01 00 01 00 54 02 16 57 72 61 70 4E 6F 6E 45 78   // ....T..WrapNonEx
                                                                                                                   63 65 70 74 69 6F 6E 54 68 72 6F 77 73 01 )       // ceptionThrows.

  // --- The following custom attribute is added automatically, do not uncomment -------
  //  .custom instance void [CORE_ASSEMBLY]System.Diagnostics.DebuggableAttribute::.ctor(valuetype [CORE_ASSEMBLY]System.Diagnostics.DebuggableAttribute/DebuggingModes) = ( 01 00 02 00 00 00 00 00 )

  .custom instance void [CORE_ASSEMBLY]System.Reflection.AssemblyFileVersionAttribute::.ctor(string) = FILE_VERSION
  .custom instance void [CORE_ASSEMBLY]System.Reflection.AssemblyInformationalVersionAttribute::.ctor(string) = INFORMATIONAL_VERSION
  .custom instance void [CORE_ASSEMBLY]System.Reflection.AssemblyTitleAttribute::.ctor(string) = {string('MonoMod.ILHelpers')}
  .custom instance void [CORE_ASSEMBLY]System.Reflection.AssemblyDescriptionAttribute::.ctor(string) = {string('MonoMod.ILHelpers')}
  .custom instance void [CORE_ASSEMBLY]System.Reflection.AssemblyMetadataAttribute::.ctor(string, string) = (
    01 00 15 2e 4e 45 54 46 72 61 6d 65 77 6f 72 6b
    41 73 73 65 6d 62 6c 79 00 00 00
  ) // ".NETFrameworkAssembly", ""
  .custom instance void [CORE_ASSEMBLY]System.Reflection.AssemblyMetadataAttribute::.ctor(string, string) = (
    01 00 0b 53 65 72 76 69 63 65 61 62 6c 65 04 54
    72 75 65 00 00
  ) // "Serviceable", "True"
  .custom instance void [CORE_ASSEMBLY]System.Reflection.AssemblyMetadataAttribute::.ctor(string, string) = (
    01 00 0b 49 73 54 72 69 6d 6d 61 62 6c 65 04 54
    72 75 65 00 00
  ) // "IsTrimmable", "True"
  .custom instance void [CORE_ASSEMBLY]System.Reflection.AssemblyCopyrightAttribute::.ctor(string) = ( 01 00 2F C2 A9 20 4D 69 63 72 6F 73 6F 66 74 20   // ../.. Microsoft
                                                                                        43 6F 72 70 6F 72 61 74 69 6F 6E 2E 20 20 41 6C   // Corporation.  Al
                                                                                        6C 20 72 69 67 68 74 73 20 72 65 73 65 72 76 65   // l rights reserve
                                                                                        64 2E 00 00 )                                     // d...
  .custom instance void [CORE_ASSEMBLY]System.Reflection.AssemblyCompanyAttribute::.ctor(string) = ( 01 00 15 4D 69 63 72 6F 73 6F 66 74 20 43 6F 72   // ...Microsoft Cor
                                                                                      70 6F 72 61 74 69 6F 6E 00 00 )                   // poration..
  .custom instance void [CORE_ASSEMBLY]System.Reflection.AssemblyProductAttribute::.ctor(string) = ( 01 00 1A 4D 69 63 72 6F 73 6F 66 74 C2 AE 20 2E   // ...Microsoft.. .
                                                                                      4E 45 54 20 46 72 61 6D 65 77 6F 72 6B 00 00 )    // NET Framework..
  .custom instance void [CORE_ASSEMBLY]System.CLSCompliantAttribute::.ctor(bool) = (
    01 00 00 00 00
  ) // false
  .hash algorithm 0x00008004
  .ver ASSEMBLY_VERSION
}
.module MonoMod.ILHelpers.dll
// MVID: {1E97D84A-565B-49C5-B60A-F31A1A4ACE13}
.imagebase 0x00400000
.file alignment 0x00000200
.stackreserve 0x00100000
.subsystem 0x0003       // WINDOWS_CUI
.corflags 0x00000001    //  ILONLY
// Image base: 0x02ED0000

