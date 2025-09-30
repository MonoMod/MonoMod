.arch armv8-a

#if defined(__APPLE__)
.build_version macos, 12, 0 sdk_version 12, 0
.subsections_via_symbols
#endif

// DWARF/EH constants

.set DW_EH_PE_absptr, 0x00
.set DW_EH_PE_uleb128, 0x01
.set DW_EH_PE_udata2, 0x02
.set DW_EH_PE_udata4, 0x03
.set DW_EH_PE_udata8, 0x04
.set DW_EH_PE_sleb128, 0x09
.set DW_EH_PE_sdata2, 0x0A
.set DW_EH_PE_sdata4, 0x0B
.set DW_EH_PE_sdata8, 0x0C

.set DW_EH_PE_pcrel, 0x10
.set DW_EH_PE_textrel, 0x20
.set DW_EH_PE_datarel, 0x30
.set DW_EH_PE_funcrel, 0x40
.set DW_EH_PE_aligned, 0x50
.set DW_EH_PE_indirect, 0x80

.set DW_EH_PE_omit, 0xff

// _UA_* are actually log2() of the real values, for use with the TBZ instruction
.set _UA_SEARCH_PHASE, 0
.set _UA_CLEANUP_PHASE, 1
.set _UA_HANDLER_FRAME, 2
.set _UA_FORCE_UNWIND, 3

.set _URC_HANDLER_FOUND, 6
.set _URC_INSTALL_CONTEXT, 7
.set _URC_CONTINUE_UNWIND, 8

.set DW_REG_x0, 0

// Common defines and macros

#if defined(__APPLE__)
#define C_FUNC(name) _##name
#define EXTERNAL_C_FUNC(name) C_FUNC(name)
#define LOCAL_LABEL(name) L##name
#define PERSONALITY(name) .cfi_personality DW_EH_PE_pcrel | DW_EH_PE_indirect | DW_EH_PE_sdata4, name
#define LSDA(name) .cfi_lsda DW_EH_PE_pcrel, name
#else
#define C_FUNC(name) name
#define EXTERNAL_C_FUNC(name) C_FUNC(name)
#define LOCAL_LABEL(name) .L##name
#define PERSONALITY(name) .cfi_personality DW_EH_PE_pcrel | DW_EH_PE_indirect | DW_EH_PE_sdata4, _ref##name
#define LSDA(name) .cfi_lsda DW_EH_PE_pcrel | DW_EH_PE_sdata4, name
#endif

.macro LOAD_EXTERNAL Reg, Name
#if defined(__APPLE__)
    adrp \Reg, C_FUNC(\Name)@GOTPAGE
    ldr \Reg, [\Reg, C_FUNC(\Name)@GOTPAGEOFF]
#else
    adrp \Reg, :got:C_FUNC(\Name)
    ldr \Reg, [\Reg, :got_lo12:C_FUNC(\Name)]
#endif
.endm

.macro LOAD_INTERNAL Reg, Name
    adrp \Reg, C_FUNC(\Name)
    add \Reg, \Reg, :lo12:C_FUNC(\Name)
.endm
