# Using RuntimeDetour

Creating a new detour looks like this most of the time:

<!-- #usage -->
```cs
// Create a Hook.
using (var d = new Hook(methodInfoFrom, methodInfoTo))
{
    // When the detour goes out-of-scope (and thus has Dispose() called), the detour is undone.
    // If the object is collected by the garbage collector, the detour is also undone.
}
```
<!-- #usage -->

<!-- #types -->
## Detour Types

There are 2 managed detour types that are available:

 1. `Hook` - This is effectively `Detour` but better. It sits as part of the same detour chain as `Detour`
    objects. The target of the hook may be a delegate, and so may be an instance method associated with some
    object. Hook targets may also take as their first parameter a delegate with a signature matching the detour
    source. This delegate, when called, will invoke the next detour in the chain, or the original if this is the last detour in the chain. **Note: this delegate should usually only be called while the hook method
    is on the stack. See [The Detour Chain](#the-detour-chain) for more information.**
 2. `ILHook` - This is a different kind of detour. If you're familiar with Harmony, this is effectively a
    transpiler. When you construct an ILHook, you provide a delegate which gets provided an `ILContext` which
    can be used to modify the IL of the method. If multiple ILHooks are present for the same method, the order
    the manipulators are invoked is the same as the order detours in a detour chain would be.

Each detour (`Hook` or `ILHook`) may have an associated `DetourConfig`. Each detour config must have
an ID--this will typically be the name of the mod which applies the hook. They also have a list of IDs which
detours associated with this config will run before, and a similar list that they will run after. If some config `A` wants to run before `B`, and `B` wants to run before `A`, the resulting order is unspecified. The
MonoMod debug log will make a note of this.

Detour configs may also have a priority. Detours with *any* priority will execute before any without, unless
one of the before or after fields caused it to be placed differently. The before and after fields take precedence over the priority field.

Any detours with no `DetourConfig` get run in an arbitrary order after all those with a `DetourConfig`.

<!-- #types -->
<!-- #chain -->
## The Detour Chain

All detours whose source method is the same are part of one *detour chain*. When the source method is called,
the first detour in the chain gets called. That detour then has complete control over how that function behaves.
It may, at any point, invoke the delegate (gotten from the delegate
parameter to a hook method) to invoke the next detour in the chain. It may pass any parameters, and do anything
with the return value. 

While within the original method invocation (i.e. in the detour chain, *with every prior detour on the
stack*), invoking the continuation delegate is safe, and will always invoke the next detour in the chain.
Modifying the detour chain for a method is thread safe, and modifications will wait until all existing 
invocations of the detour chain exit, and all new invocations of the chain will wait until the chain 
modification is complete before execution. Notably, though, **this means that invoking continuation delegates
while the detour chain is not on the stack is not thread-safe, nor is it guaranteed to actually invoke the
delegate chain as it exists at the moment of invocation.** Always invoke the original method, and never delay
invocation of the original delegate.

<!-- #chain -->

## Detour Order Calculation details

We define the calculation of detour order by defining how we insert a new detour into the chain.

There are two chains that we compute independently: the *config chain* and the *no-config chain*.

The *config chain* is computed as follows:

 1. Each detour with a `DetourConfig` is assigned a node. This node holds a list of detour nodes which must
    execute before this detour. These nodes form a graph.
 2. When inserting a node, we add this node to the before list of every existing node with an ID in this node
    config's before list.
     1. We also add every node with an ID in this node's after list to this node's before list
     2. The same is done in reverse as well.
     3. When inserting into any of the lists, nodes are inserted according to their priority value.
        Higher priority values get placed earlier in the list, and lower priorities get placed later in the
        list. Nodes which do not have a priority get placed at the end of the list, after all nodes with 
        priority.
 3. The new node is then added to the list of all nodes, in the same manner as above.
 4. After inserting the node into the graph, it is flattened by iterating through the list of all nodes,
    recursively adding each node in the current node's before list to the output order. After this step,
    all nodes are in the correct relative positions, using the priority field to further refine ordering.

The *no-config chain* is simply appended to whenever a new detour with no `DetourConfig` is applied.

The final detour chain is then just the *config chain* followed by the *no-config* chain.
