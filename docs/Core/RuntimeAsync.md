# Runtime Async

Runtime async is a newish runtime feature that moves async state machine generation from
Roslyn (the C# compiler) into the runtime and JIT. This lets async methods use custom calling
conventions and be more efficient in the non-suspending case.

- [Spec](https://github.com/dotnet/runtime/blob/main/docs/design/specs/runtime-async.md)
- [Tracking issue](https://github.com/dotnet/runtime/issues/109632)
- [BOTR ABI section](https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/botr/clr-abi.md#async)

The implementation of this functionality in CoreCLR is fairly unique. Every method which, by signature,
MIGHT be runtime async (i.e. it returns `Task`, `Task<T>`, `ValueTask`, or `ValueTask<T>` before generic
substitution) gets 2 `MethodDesc`s: one for the "normal" or "ordinary" entrypoint, and one for the async
entrypoint (which uses the new async ABI).

### Non-runtime-async async method

For a method which is not flagged as having an async implementation (i.e. it returns a runtime-async task
type, but is not actually an async method to the runtime), the call graph looks like this: (this drawing
represents a C# compiler-generated async method with its own state machine machinery)

```
Non-runtime async code                                                            
      │                                                                           
      │         Runtime async code                                                
      │                │                                                          
      ▼                ▼                                                          
┌──────────────┐ ┌─────────────┐                                                  
│              │ │             │                                                  
│ non-runtime  │ │runtime async│                                                  
│ async method │ │   abi impl  │                                                  
│              │ │             │                                                  
└──────┬───────┘ └──────┬──────┘                                                  
       │                │                                                         
   dispatches       dispatches                                                    
       │                │                                                         
       └──┐    ┌────────┘                                                         
          ▼    ▼                 ┌─────────────┐                                  
         ┌──────┐────start──────►│             ├──►other normal methods           
         │prolog├──resume 1─────►│ async state │◄───┘                             
         └────┬─┴──resume─2─────►│   machine   ├──►other non-runtime async methods
           ▲  └──────...────────►│             │◄───┘    │                        
           │                     └─────────────┘         │                        
           │      suspend◄───────────────────────────────┘                        
           │        ...                                                           
           └──────resume                                                          
```

Synchronous code calls into the non-`async` entrypoint, which has its normal IL, and (in the case of a
compiler-generated async method) manually dispatches an async state machine. In the diagram, `prolog`
is the entrypoint and prologue of the generated state machine's `MoveNext()` method, before any of the
user written code is generated. It is typically just a `switch` that jumps into the correct place.

When such a method suspends, it registers the state machine as the continuation delegate at the point
of suspension, which when called, calls into that `MoveNext()` prolog again, **bypassing the original
entrypoint**. This is useful for MonoMod, because this means that when MonoMod detours the original
dispatcher, it doesn't get hit by continuations from previously in-flight invocations. In-flight
invocations always go directly to the correct variant automatically.

The async variant that uses the `async` callconv is more interesting. When the runtime asks the JIT to
compile this variant, **it hands the JIT exactly the same IL it does for the ordinary variant**. The
JIT is also told that it's generating an async callconv method, so, after the whole body of the original
method, it artificially injects a suspend-point exactly as if the original method had a call to 
`AsyncHelpers.Await()` right at the end, then continues on to emit the method *again*, but this time as
a state machine.

### Runtime-async method

For a method which is flagged as having an async implementation, the call graph looks like this:

```
Non-runtime async code                                                                
      │                                                                               
      │            Runtime async code                                                 
      ▼                  │                                                            
┌─────────────┐          ▼                    ┌──────────────┐                        
│             │       ┌──────┐────start──────►│              ├───►other normal methods
│Task<T> thunk├──────►│prolog├──resume 1─────►│ async method │◄────┘                  
│             │       └────┬─┴──resume─2─────►│     impl     ├───►other async2 methods
└─────────────┘         ▲  └──────...────────►│              │◄────┘    │             
                        │                     └──────────────┘       suspend          
                        │                        ┌────────┐             │             
                        │      suspend◄──────────┼ suspend│◄────────────┘             
                        │        ...             │ epilog │                           
                        └──────resume            └────────┘                           
```

When synchronous code calls this method, it hits the sync variant, which is a thin runtime-generated thunk
that invokes the async variant (here marked `prolog`) with the async calling convention, then wraps the
resulting continuation up into the appropriate task type for return to the caller. This is not functionally
different from the classical async case.

When async-callconv code calls this method, it calls the async entrypoint directly. This then immediately
starts the method, instead of needing to go through a bunch of dispatching machinery. 

When the async method suspends, it constructs a `Continuation` object, which it chains onto whatever
continuation it recieved from the suspending method, then returns that continuation to its caller via a
special return register. When this chain of continuations eventually bubbles up to a launch point (like a
synchronous thunk, for instance) it gets wrapped in a task. The innermost continuation got registered as
the continuation callback of whatever supension primitive did the initial suspend, and when it completes,
it resumes that innermost method by calling **the same original entrypoint prologue** with the resuming
continuation. (The runtime then has further logic to unwind the continuation chain.)

Critically, this means that if we detour the async entrypoint (as we need to if we want to catch every call
to the method), our detour will recieve `Continuation` objects belonging to the original method, if any calls
were already in-flight when the detour was applied. We *fundamentally cannot* deal with this, since the JIT
decides on the layout of this continuation object during construction, and the JIT is not deterministic. Worse,
when the JIT decides on a layout, what it actually reports to the VM is only: a) size in bytes; and b) which
machine words are object references. So even if we wanted to introspect these, we couldn't, since not even the
VM knows what is stored in there!

(We do not technically need to detour the ordinary entrypoint for these methods. The `IRuntime` implementation
reports this fact, however currently, the detour factory does not make use of it, since it is much simpler to
just always do so, and there's not really a downside aside from generating a few extra DMDs.)

## Detour structures

An ordinary method detour looks like this:

```
    │any caller│                           
    └────┬─────┘                           
         │        ┌─────────┐    ┌─────────
         └──────► │abi fixup├───►│hook body
                  └─────────┘    └─────────
         │                                 
                                           
         │                                 
         ▼                                 
┌───────────────┐                          
│  real method  │                          
```

(`abi fixup` is technically optional; we only insert it if one is necessary, for instance->static or similar.)

We can continue to do this for ordinary variants, since they continue to use the normal method calling convention.
For the async variant, because of the aforementioned continuation callback problem, we must instead do this:

```
    │any caller│                                 
    └────┬─────┘                                 
         │       ┌────────────┐                  
         └──────►│ detour stub├───no continuation
                 └──────┬─────┘         │        
         │              │               ▼        
               has continuation?   ┌─────────┐   
         │              │          │abi fixup│   
             ┌──────────┘          └────┬────┘   
         │   │                          │        
         ▼   ▼                          ▼        
┌───────────────┐                  ┌─────────┐   
│  real method  │                  │hook body│   
```

We must inject a stub before doing our ABI fixup, and in that stub we need to check the continuation argument.
If that argument is non-null, we must immediately call back to the original method (needing an alternate entrypoint!)
with all arguments intact, and make sure we don't accidentally touch the special return register. (We use a `tail.`
prefix on this call.) If that argument is null, we can instead jump to the ABI fixup and call of our target.
That target, however, MUST be `async` ABI. Our logic can safely be `null` vs. `not null` because when the detour
target async method suspends, its continuation jumps to its own entrypoint, *not* this original method entrypoint.

Since we always inject this stub, we do the ABI fixup inline in the path where we call the real target.

This stub is logically connected to the detoured method body, and so must be regenerated each and every time a
method is recompiled, since the runtime will call back into old code versions for continuations sometimes. If we
used the same one and updated the original target, an old-codeversion continuation would end up in a new codeversion,
to catastrophic results.

The stub calls both variants using `calli` with a **normal callconv** signature. When we call to the original method,
we do so using an indirection cell that can be updated by the detouring process. Currently, we do NOT do this when
calling the target method, instead hardcoding a pointer to its stable entrypoint (i.e. the one that gets updated by 
the codeversion manager).

# Other Notes

Below are some other notes that I took while researching this that may be useful for future work. It also contains
some more specifics than what I talk about above, for those interested.

---

Resumption is done with the following JIT pattern:

```cs
if (continuation is not null)
{
    switch (continuation.State)
    {
    case 0:
        goto Resume_0;
    case 1:
        goto Resume_1;
    // and so on...
    }
}

// normal entry point

AsyncCall1();
if (suspended)
{
    return with continuation;
}
Resume_0:
// ...

AsyncCall2();
if (suspended)
{
    return with continuation;
}
Resume_1:
// ...

// and so on...
```
Therefore, when we detour an async method for which something is already in-flight, we also intercept any resumptions.

Resumption happens through the managed function pointer `System.Runtime.CompilerServices.Continuation::ResumeInfo->Resume`
which is a runtime-generated JITted method, generated fresh for each compiled async-variant method (that suspends, anyway).
Thus, in principle, we can use this to identify which method a continuation belongs to.

This resume takes a continuation and does an async calli into the ***NORMAL*** entrypoint of the method. Thus, when we detour
an async method, we will also intercept resumes of that method, and the detour's resume code WILL NOT be able to handle that
properly. Fortunately, at the detour entrypoint, we will ONLY get those resumes, since resumes belonging to the detour will
instead taret the detour method's proper entrypoint. Therefore, if we get a non-null continuation in the resume path, we know
we need to forward that continuation back to the original method.

This does mean we need to use alt entrypoints for these detours though AFAIK, since we have no way of knowing for sure the JIT
will want identical suspend data across e.g. tiering.