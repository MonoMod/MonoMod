using MonoMod.Core.Utils;
using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.ConstrainedExecution;
using System.Threading;

var platArch = PlatformDetection.Architecture;
var platOs = PlatformDetection.OS;
var platRuntime = PlatformDetection.Runtime;
var platRuntimeVer = PlatformDetection.RuntimeVersion;

Console.WriteLine($"Running on {platOs} {platArch} {platRuntime} {platRuntimeVer}");

// Dependent handle testing
unsafe {
    int targetRefcount = 0, dependentRefcount = 0;

    using var handle = FirstGCAndCheck(&targetRefcount, &dependentRefcount);

    do {
        DoGc();
        DoGc();
    } while (targetRefcount > 0 || dependentRefcount > 0);

    Debug.Assert(targetRefcount == 0);
    Debug.Assert(dependentRefcount == 0);

    CheckDependent2(handle);
}

static unsafe (RefCounter target, DependentHandle handle) CreateHandle(int* targetRefcount, int* dependentRefcount) {
    RefCounter target = new RefCounter(targetRefcount);
    RefCounter? refValue = new RefCounter(dependentRefcount);

    Debug.Assert(*targetRefcount == 1);
    Debug.Assert(*dependentRefcount == 1);

    var depHandle = new DependentHandle(target, refValue);
    refValue = null;

    return (target, depHandle);
}

static unsafe DependentHandle FirstGCAndCheck(int* targetRefcount, int* dependentRefcount) {
    var (target, handle) = CreateHandle(targetRefcount, dependentRefcount);

    DoGc();
    DoGc();

    Debug.Assert(*targetRefcount == 1);
    Debug.Assert(*dependentRefcount == 1);

    CheckDependent1(handle, dependentRefcount);

    GC.KeepAlive(target);

    return handle;
}

static unsafe void CheckDependent1(DependentHandle handle, int* refcount) {
    object? val = handle.Dependent;
    Debug.Assert(val is RefCounter);
    Debug.Assert(((RefCounter) val!).counter == refcount);
    val = null;
}

static void CheckDependent2(DependentHandle handle) {
    object? val = handle.Dependent;
    Debug.Assert(val is null);
    val = null;
}

static void DoGc() {
    GC.GetTotalMemory(true);
    GC.AddMemoryPressure(0x1000);
    for (int i = 0; i < GC.MaxGeneration; i++) {
        GC.Collect(i, GCCollectionMode.Forced);
        GC.WaitForPendingFinalizers();
    }
}

internal unsafe class RefCounter : CriticalFinalizerObject {
    public int* counter;

    public RefCounter(int* counter) {
        this.counter = counter;
        Interlocked.Increment(ref *counter);
    }

    ~RefCounter() {
        Interlocked.Decrement(ref *counter);
    }
}