#include <stdint.h>
#include <stddef.h>

// We can't just use __builtin___clear_cache like we should, because that ends up pulling in ALL 1MB of compiler-rt
// (at least during our normal compilation with zig cc) which includes a load of fluff that we really don't want.
// So, instead, we copy the implementation compiler-rt uses for it on linux-arm64! YAY!

static void clear_cache(void* start, void* end)
{
  // https://github.com/llvm/llvm-project/blob/4f28393db7302dc6c5952fd154fc9e7037330f7c/compiler-rt/lib/builtins/clear_cache.c#L122-L153

  uint64_t xstart = (uint64_t)(uintptr_t)start;
  uint64_t xend = (uint64_t)(uintptr_t)end;

  // Get Cache Type Info.
  static uint64_t ctr_el0 = 0;
  if (ctr_el0 == 0)
  {
    __asm __volatile("mrs %0, ctr_el0" : "=r"(ctr_el0));
  }

  // The DC and IC instructions must use 64-bit registers so we don't use
  // uintptr_t in case this runs in an IPL32 environment.
  uint64_t addr;

  // If CTR_EL0.IDC is set, data cache cleaning to the point of unification
  // is not required for instruction to data coherence.
  if (((ctr_el0 >> 28) & 0x1) == 0x0)
  {
    const size_t dcache_line_size = 4 << ((ctr_el0 >> 16) & 15);
    for (addr = xstart & ~(dcache_line_size - 1); addr < xend; addr += dcache_line_size)
    {
      __asm __volatile("dc cvau, %0" ::"r"(addr));
    }
  }
  __asm __volatile("dsb ish");

  // If CTR_EL0.DIC is set, instruction cache invalidation to the point of
  // unification is not required for instruction to data coherence.
  if (((ctr_el0 >> 29) & 0x1) == 0x0)
  {
    const size_t icache_line_size = 4 << ((ctr_el0 >> 0) & 15);
    for (addr = xstart & ~(icache_line_size - 1); addr < xend; addr += icache_line_size)
    {
      __asm __volatile("ic ivau, %0" ::"r"(addr));
    }
    __asm __volatile("dsb ish");
  }
  __asm __volatile("isb sy");
}

void mmh_clear_cache(void* addr, size_t size)
{
    char* ptr = (char*)addr;
    clear_cache(ptr, ptr + size);
}