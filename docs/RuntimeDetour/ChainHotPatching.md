## Detour Chain Hot Patching

MonoMod.RuntimeDetour supports hot-patching the detour chain even when it is in
use by the current thread. This means that hooks can add or remove new hooks
(including themselves) from their hook methods, which applies to methods called
by them or the hook target method as well.

Upon such a change, the detour chain is immediately modified: added hooks
ordered before the current hook are not called until the next invocation of the
method, but added hooks after the current one immediately become part of the
chain and are appropriately invoked when control is passed onto the next hook.

To ensure hooks can be removed safely, a mechanism called "trampoline stealing"
is utilized. When a piece of code removes a hook from a method while that same
method is currently on the call stack, its next trampoline (the optional first
parameter you can receive with `Hook`s) is "stolen" by the detour manager, and
not returned to the pool like normal. The detour manager will keep the
trampoline alive until the method completely leaves the call stack, after which
accesing the trampoline will become undefined behaviour like normal.

Only one thread can ever safely modify the detour chain at the same time. As
such, MonoMod.RuntimeDetour synchronizes both calls to hooked methods and
hooking functions: if the chain is currently being modified, then calls to the
hooked method (from other threads) will block until the modifications have been
made. This does not apply to nested calls to prevent deadlocks, and as such only
affects the first call to the hooked method on the call stack. Operations which
modify the detour chain will wait until all other threads have returned from the
target method before making any changes.