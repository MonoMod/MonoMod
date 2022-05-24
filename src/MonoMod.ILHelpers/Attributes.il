#include "version.h"

.class private auto ansi sealed beforefieldinit System.Runtime.Versioning.NonVersionableAttribute
       extends [CORE_ASSEMBLY]System.Attribute
{
  .custom instance void [CORE_ASSEMBLY]System.AttributeUsageAttribute::.ctor(valuetype [CORE_ASSEMBLY]System.AttributeTargets) = ( 01 00 6C 00 00 00 02 00 54 02 0D 41 6C 6C 6F 77   // ..l.....T..Allow
                                                                                                                                     4D 75 6C 74 69 70 6C 65 00 54 02 09 49 6E 68 65   // Multiple.T..Inhe
                                                                                                                                     72 69 74 65 64 00 )                               // rited.
  .method public hidebysig specialname rtspecialname
          instance void  .ctor() cil managed
  {
        .maxstack 8
        ldarg.0
        call instance void [CORE_ASSEMBLY]System.Attribute::.ctor()
        ret
  } // end of method NonVersionableAttribute::.ctor

} // end of class System.Runtime.Versioning.NonVersionableAttribute

#ifdef netcoreapp
#else
.class private auto ansi sealed beforefieldinit System.Runtime.CompilerServices.IsReadOnlyAttribute
       extends [CORE_ASSEMBLY]System.Attribute
{
  .method public hidebysig specialname rtspecialname
          instance void .ctor () cil managed
  {
        .maxstack 8
        ldarg.0
        call instance void [CORE_ASSEMBLY]System.Attribute::.ctor()
        ret
  } // end of method IsReadOnlyAttribute::.ctor

} // end of class System.Runtime.CompilerServices.IsReadOnlyAttribute
#endif

.class private auto ansi sealed beforefieldinit Microsoft.CodeAnalysis.EmbeddedAttribute
    extends [CORE_ASSEMBLY]System.Attribute
{
    .custom instance void [CORE_ASSEMBLY]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = (
        01 00 00 00
    )
    .custom instance void Microsoft.CodeAnalysis.EmbeddedAttribute::.ctor() = (
        01 00 00 00
    )
    .method public hidebysig specialname rtspecialname 
        instance void .ctor () cil managed 
    {
        .maxstack 8
        IL_0000: ldarg.0
        IL_0001: call instance void [CORE_ASSEMBLY]System.Attribute::.ctor()
        IL_0006: ret
    } // end of method EmbeddedAttribute::.ctor

} // end of class Microsoft.CodeAnalysis.EmbeddedAttribute

.class private auto ansi sealed beforefieldinit System.Runtime.CompilerServices.NativeIntegerAttribute
    extends [CORE_ASSEMBLY]System.Attribute
{
    .custom instance void [CORE_ASSEMBLY]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = (
        01 00 00 00
    )
    .custom instance void Microsoft.CodeAnalysis.EmbeddedAttribute::.ctor() = (
        01 00 00 00
    )
    .custom instance void [CORE_ASSEMBLY]System.AttributeUsageAttribute::.ctor(valuetype [CORE_ASSEMBLY]System.AttributeTargets) = (
        01 00 84 6b 00 00 02 00 54 02 0d 41 6c 6c 6f 77
        4d 75 6c 74 69 70 6c 65 00 54 02 09 49 6e 68 65
        72 69 74 65 64 00
    )
    .field public initonly bool[] TransformFlags

    .method public hidebysig specialname rtspecialname 
        instance void .ctor () cil managed 
    {
        .maxstack 8
        IL_0000: ldarg.0
        IL_0001: call instance void [CORE_ASSEMBLY]System.Attribute::.ctor()
        IL_0006: ldarg.0
        IL_0007: ldc.i4.1
        IL_0008: newarr [CORE_ASSEMBLY]System.Boolean
        IL_000d: dup
        IL_000e: ldc.i4.0
        IL_000f: ldc.i4.1
        IL_0010: stelem.i1
        IL_0011: stfld bool[] System.Runtime.CompilerServices.NativeIntegerAttribute::TransformFlags
        IL_0016: ret
    } // end of method NativeIntegerAttribute::.ctor

    .method public hidebysig specialname rtspecialname 
        instance void .ctor (
            bool[] ''
        ) cil managed 
    {
        .maxstack 8
        IL_0000: ldarg.0
        IL_0001: call instance void [CORE_ASSEMBLY]System.Attribute::.ctor()
        IL_0006: ldarg.0
        IL_0007: ldarg.1
        IL_0008: stfld bool[] System.Runtime.CompilerServices.NativeIntegerAttribute::TransformFlags
        IL_000d: ret
    } // end of method NativeIntegerAttribute::.ctor

} // end of class System.Runtime.CompilerServices.NativeIntegerAttribute

.class /* 02000003 */ private auto ansi sealed beforefieldinit System.Runtime.CompilerServices.NullableAttribute
	extends [CORE_ASSEMBLY]System.Attribute
{
	.custom instance void [CORE_ASSEMBLY]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = (
		01 00 00 00
	)
	.custom instance void Microsoft.CodeAnalysis.EmbeddedAttribute::.ctor() = (
		01 00 00 00
	)
	.custom instance void [CORE_ASSEMBLY]System.AttributeUsageAttribute::.ctor(valuetype [CORE_ASSEMBLY]System.AttributeTargets) = (
		01 00 84 6b 00 00 02 00 54 02 0d 41 6c 6c 6f 77
		4d 75 6c 74 69 70 6c 65 00 54 02 09 49 6e 68 65
		72 69 74 65 64 00
	)
	// Fields
	.field /* 04000001 */ public initonly uint8[] NullableFlags

	// Methods
	.method /* 06000002 */ public hidebysig specialname rtspecialname 
		instance void .ctor (
			uint8 ''
		) cil managed 
	{
		// Method begins at RVA 0x2059
		// Header size: 1
		// Code size: 24 (0x18)
		.maxstack 8

		// {
		IL_0000: ldarg.0
		// (no C# code)
		IL_0001: call instance void [CORE_ASSEMBLY]System.Attribute::.ctor() /* 0A00001C */
		// NullableFlags = new byte[1] { P_0 };
		IL_0006: nop
		IL_0007: ldarg.0
		IL_0008: ldc.i4.1
		IL_0009: newarr [CORE_ASSEMBLY]System.Byte /* 01000043 */
		IL_000e: dup
		IL_000f: ldc.i4.0
		IL_0010: ldarg.1
		IL_0011: stelem.i1
		IL_0012: stfld uint8[] System.Runtime.CompilerServices.NullableAttribute::NullableFlags /* 04000001 */
		// }
		IL_0017: ret
	} // end of method NullableAttribute::.ctor

	.method /* 06000003 */ public hidebysig specialname rtspecialname 
		instance void .ctor (
			uint8[] ''
		) cil managed 
	{
		// Method begins at RVA 0x2072
		// Header size: 1
		// Code size: 15 (0xf)
		.maxstack 8

		// {
		IL_0000: ldarg.0
		// (no C# code)
		IL_0001: call instance void [CORE_ASSEMBLY]System.Attribute::.ctor() /* 0A00001C */
		// NullableFlags = P_0;
		IL_0006: nop
		IL_0007: ldarg.0
		IL_0008: ldarg.1
		IL_0009: stfld uint8[] System.Runtime.CompilerServices.NullableAttribute::NullableFlags /* 04000001 */
		// }
		IL_000e: ret
	} // end of method NullableAttribute::.ctor

} // end of class System.Runtime.CompilerServices.NullableAttribute

.class /* 02000004 */ private auto ansi sealed beforefieldinit System.Runtime.CompilerServices.NullableContextAttribute
    extends [CORE_ASSEMBLY]System.Attribute
{
    .custom instance void [CORE_ASSEMBLY]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = (
        01 00 00 00
    )
    .custom instance void Microsoft.CodeAnalysis.EmbeddedAttribute::.ctor() = (
        01 00 00 00
    )
    .custom instance void [CORE_ASSEMBLY]System.AttributeUsageAttribute::.ctor(valuetype [CORE_ASSEMBLY]System.AttributeTargets) = (
        01 00 4c 14 00 00 02 00 54 02 0d 41 6c 6c 6f 77
        4d 75 6c 74 69 70 6c 65 00 54 02 09 49 6e 68 65
        72 69 74 65 64 00
    )
    // Fields
    .field /* 04000002 */ public initonly uint8 Flag

    // Methods
    .method /* 06000004 */ public hidebysig specialname rtspecialname 
        instance void .ctor (
            uint8 ''
        ) cil managed 
    {
        // Method begins at RVA 0x2082
        // Header size: 1
        // Code size: 15 (0xf)
        .maxstack 8

        IL_0000: ldarg.0
        IL_0001: call instance void [CORE_ASSEMBLY]System.Attribute::.ctor() /* 0A00001C */
        IL_0006: nop
        IL_0007: ldarg.0
        IL_0008: ldarg.1
        IL_0009: stfld uint8 System.Runtime.CompilerServices.NullableContextAttribute::Flag /* 04000002 */
        IL_000e: ret
    } // end of method NullableContextAttribute::.ctor

} // end of class System.Runtime.CompilerServices.NullableContextAttribute
