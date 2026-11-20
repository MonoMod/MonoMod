// clang -O3 -dynamiclib helper_macos_arm64.cc -o helper_macos_arm64.dylib
// -std=c++20 -lc++ -Wall -mmacosx-version-min=11
//
// DISABLE_JWP_DETECTION=1      Disables detection of the current thread's write
// protection via a system register and falls back to the suboptimal
//                              path where we assume it must always be toggled
//                              during patching and use a separate thread for
//                              managed hook dispatch
//
// FORCE_NATIVE_JIT_HOOK_USE=1  Forces the use of the native jit hooks even if
// write protection isn't supported on the platform.
//

#include <cstdint>
#include <pthread.h>
#include <thread>

#if !defined(DISABLE_JWP_DETECTION)
class PthreadJitWriteProtectScope {
public:
  PthreadJitWriteProtectScope(bool protect) {
    if (pthread_jit_write_protect_supported_np()) {
      requested = protect;
      actual = get_jit_write_protect();
      if (actual != requested)
        pthread_jit_write_protect_np(requested);
    } else {
      requested = protect;
      actual = protect;
    }
  }

  ~PthreadJitWriteProtectScope() {
    if (actual != requested)
      pthread_jit_write_protect_np(actual);
  }

private:
  // TODO: Consider alternatives such as locating the PAL_JitWriteProtect TLS
  // enabledCount var from libcoreclr
  bool get_jit_write_protect() {
    uint64_t v;
    __asm__ __volatile__("isb sy\n"
                         "mrs %0, S3_6_c15_c1_5\n"
                         : "=r"(v)::"memory");
    return (v & 0x0000000000200000ULL) == 0;
  }

  bool requested;
  bool actual;
};
#endif

namespace JitHooks {

static thread_local int compileMethod_Entrancy = 0;
static thread_local void *savedEntryRw;

class CompileMethodHookTracker {
public:
  CompileMethodHookTracker() noexcept {
    lastErrNo = errno;
    ++compileMethod_Entrancy;
  }

  ~CompileMethodHookTracker() noexcept {
    --compileMethod_Entrancy;
    errno = lastErrNo;
  }

  int entrancy() noexcept { return compileMethod_Entrancy; }

private:
  int lastErrNo;
};

namespace Core {

typedef int (*ICorJitCompiler_compileMethod)(void *pThis, void *comp,
                                             void *info, unsigned flags,
                                             uint8_t **nativeEntry,
                                             uint32_t *nativeSizeOfCode);
typedef int (*ICorJitCompiler_compileMethod_hook_post)(
    void *pThis, void *comp, void *info, unsigned flags, uint8_t **nativeEntry,
    uint32_t *nativeSizeOfCode, int res, void *entryRw);

typedef void (*ICorJitInfo_allocMem)(void *pThis, void *pArgs);

struct JitHookConfig {
  ICorJitCompiler_compileMethod compileMethod;
  ICorJitCompiler_compileMethod compileMethodHook;
  ICorJitCompiler_compileMethod_hook_post compileMethodHookPost;
  ICorJitInfo_allocMem allocMem;
  ICorJitInfo_allocMem allocMemHook;
};

static struct JitHookConfig jitHookConfig;

static int ICorJitCompiler_compileMethod_hook(void *pThis, void *comp,
                                              void *info, unsigned flags,
                                              uint8_t **nativeEntry,
                                              uint32_t *nativeSizeOfCode) {
  CompileMethodHookTracker tracker;
  int entrancy = tracker.entrancy();
  if (entrancy == 1) {
    savedEntryRw = nullptr;
  }

  int res = jitHookConfig.compileMethod(pThis, comp, info, flags, nativeEntry,
                                        nativeSizeOfCode);
  if (entrancy == 1 && res == 0) {
    try {
#if defined(DISABLE_JWP_DETECTION)
      // Since the MAP_JIT W^X write protection is thread local, we can use a
      // new thread to invoke the managed post method.
      std::thread hook_post_thread([&]() {
        try {
          CompileMethodHookTracker tracker;
          res = jitHookConfig.compileMethodHookPost(
              pThis, comp, info, flags, nativeEntry, nativeSizeOfCode, res,
              savedEntryRw);
        } catch (...) {
          // Just catch and eat all exceptions, we don't want this thread to
          // abort unexpectedly
        }
      });

      hook_post_thread.join();
#else
      PthreadJitWriteProtectScope jwps(true);
      res = jitHookConfig.compileMethodHookPost(pThis, comp, info, flags,
                                                nativeEntry, nativeSizeOfCode,
                                                res, savedEntryRw);
#endif
    } catch (...) {
      // Just catch and eat all exceptions as we don't want anything making
      // their way back to the compileMethod callers
    }
  }

  return res;
}

template <typename TAllocMemArgs> struct AllocMemArgsFns;

template <typename TAllocMemArgs> class TemplatedHolder {
  static void ICorJitInfo_allocMem_hook(void *pThis, void *pArgs) {
    jitHookConfig.allocMem(pThis, pArgs);
    if (compileMethod_Entrancy == 1) {
      savedEntryRw =
          AllocMemArgsFns<TAllocMemArgs>::getEntryRw((TAllocMemArgs *)pArgs);
    }
  }

public:
  // NOTE: this is NOT safe for multiple versions loaded at once from the same
  // binary. This is not a problem for MonoMod though, since we install exactly
  // 1 jit hook per (single) loaded binary.
  static JitHookConfig *getConfig() {
    if (jitHookConfig.compileMethodHook == nullptr) {
      jitHookConfig = JitHookConfig{
          .compileMethodHook = &ICorJitCompiler_compileMethod_hook,
          .allocMemHook =
              &TemplatedHolder<TAllocMemArgs>::ICorJitInfo_allocMem_hook,
      };
    }
    return &jitHookConfig;
  }
};

namespace V60 {

struct AllocMemArgs {
  // Input arguments
  uint32_t hotCodeSize;
  uint32_t coldCodeSize;
  uint32_t roDataSize;
  uint32_t xcptnsCount;
  uint32_t flag;

  // Output arguments
  void *hotCodeBlock;
  void *hotCodeBlockRW;
  void *coldCodeBlock;
  void *coldCodeBlockRW;
  void *roDataBlock;
  void *roDataBlockRW;
};

using Holder = TemplatedHolder<AllocMemArgs>;

} // namespace V60

template <> struct AllocMemArgsFns<V60::AllocMemArgs> {
  static void *getEntryRw(V60::AllocMemArgs const *pArgs) {
    return pArgs->hotCodeBlockRW;
  }
};

template class TemplatedHolder<V60::AllocMemArgs>;

namespace V110 {

enum class CorJitAllocMemFlag : uint32_t {
  HotCode = 1,
  ColdCode = 2,
  ReadonlyData = 4,
  HasPointersToCode = 8,
};

struct AllocMemChunk {
  uint32_t alignment;
  uint32_t size;
  CorJitAllocMemFlag flags;
  void *block;
  void *blockRw;
};

struct AllocMemArgs {
  AllocMemChunk *chunks;
  uint32_t chunksCount;
  uint32_t xcptnsCount;
};

using Holder = TemplatedHolder<AllocMemArgs>;

} // namespace V110

template <> struct AllocMemArgsFns<V110::AllocMemArgs> {
  static void *getEntryRw(V110::AllocMemArgs const *pArgs) {
    for (uint32_t i = 0; i < pArgs->chunksCount; i++) {
      auto *pChunk = &pArgs->chunks[i];
      // the JIT will allocate exactly one chunk marked HOT_CODE (currently
      // always the first) as its code chunk, which will be the entrypoint.
      if (pChunk->flags == V110::CorJitAllocMemFlag::HotCode) {
        return pChunk->blockRw;
      }
    }

    return nullptr;
  }
};

template class TemplatedHolder<V110::AllocMemArgs>;

} // namespace Core
} // namespace JitHooks

__attribute__((__visibility__("default"))) extern "C" void *
mmch_jit_hook_config(uint32_t runtime, int32_t runtimeMajMin) {
#if !defined(FORCE_NATIVE_JIT_HOOK_USE)
  if (!pthread_jit_write_protect_supported_np())
    return nullptr;
#endif

  if (runtime != 0x436F7265) // "Core" (BE)
  {
    return nullptr;
  }

  switch (runtimeMajMin) {
  case 60:
    return JitHooks::Core::V60::Holder::getConfig();
  case 110:
    return JitHooks::Core::V110::Holder::getConfig();
  default:
    return nullptr;
  }
}

__attribute__((__visibility__("default"))) extern "C" void
mmch_jit_memcpy(void *dst, const void *src, size_t n) {
#if defined(DISABLE_JWP_DETECTION)
  int jwp = pthread_jit_write_protect_supported_np();
  if (jwp)
    pthread_jit_write_protect_np(0);
#else
  PthreadJitWriteProtectScope jwps(false);
#endif

  memcpy(dst, src, n);

#if defined(DISABLE_JWP_DETECTION)
  if (jwp)
    pthread_jit_write_protect_np(1);
#endif
}
