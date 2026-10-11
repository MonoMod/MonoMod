

## Normal Cases

### Non-runtime-async async method

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

### Runtime-async method
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

## Detour structures

### Ordinary method
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

### Runtime async method

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

note: abi fixup does NOT need to be an async-abi method, but the hook body DOES need to be,
and the ABI fixup MUST NOT touch the continuation return register before returning itself.


# temp notes

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