"""Runs a command under LLDB, saving a core dump if it crashes or hangs.

Loaded by .github/workflows/run-with-dumps.ps1 via
    lldb -x -b -o "command script import <this file>"
and configured through the environment:
    RWD_COMMAND          JSON array: [executable, arg1, ...]
    DUMPS_PATH           directory to write dumps into
    RWD_TIMEOUT_SECONDS  wall-clock limit before a hang dump is taken (default 600)

The target is launched directly (not through a wrapper like coreutils' timeout) because
debugserver on macOS cannot follow forks. LLDB exits with the target's exit code, 128+signo
for fatal signals, or 124 for hangs.
"""

import json
import os
import signal
import sys
import threading
import time
import traceback

import lldb

CRASH_BREAKPOINT = "mono_handle_native_crash"
# Signals that mean the process is going down, and the dump name tag for each.
FATAL_SIGNALS = {"SIGABRT": "abort", "SIGILL": "illegal-instruction"}
# Signals the runtime uses internally (NREs, GC suspend, etc); these must reach it without stopping.
# SIGSTOP is deliberately absent: SBProcess.Stop() relies on it stopping the process.
PASS_SIGNALS = "SIGSEGV SIGBUS SIGFPE SIGPIPE SIGUSR1 SIGUSR2 SIGXCPU SIGINT SIGTERM SIGHUP SIGQUIT SIGCHLD"
HANG_KILL_GRACE_SECONDS = 60
# Extra time on top of the timeout and kill grace before we give up on LLDB itself (e.g. a Launch() or SaveCore() that
# never returns), so a wedged debugger can never hang the job.
WATCHDOG_EXTRA_SECONDS = 300


def create_crash_breakpoint(target):
    # Prefer an address breakpoint on the exact code symbol. Resolving by name goes through debug info, and LLDB 20
    # mis-resolves Mono's hot/cold-split mono_handle_native_crash (from mono-runtime-dbg) to an address in the middle of
    # another function; the int3 it writes there crashes Mono at startup.
    for ctx in target.FindSymbols(CRASH_BREAKPOINT, lldb.eSymbolTypeCode):
        symbol = ctx.GetSymbol()
        if symbol.GetName() == CRASH_BREAKPOINT:
            log(f"{CRASH_BREAKPOINT} breakpoint at symbol address {symbol.GetStartAddress()}")
            return target.BreakpointCreateBySBAddress(symbol.GetStartAddress())
    # Not known yet (e.g. the runtime lives in a shared library that isn't loaded before launch); resolve by name later.
    log(f"{CRASH_BREAKPOINT} not found before launch; using a pending name breakpoint")
    return target.BreakpointCreateByName(CRASH_BREAKPOINT)


def log(msg):
    sys.stdout.write(f"[run-with-dumps] {msg}\n")
    sys.stdout.flush()


def run(debugger):
    command = json.loads(os.environ["RWD_COMMAND"])
    dumps_path = os.environ["DUMPS_PATH"]
    timeout = float(os.environ.get("RWD_TIMEOUT_SECONDS", "600"))
    watchdog_seconds = timeout + HANG_KILL_GRACE_SECONDS + WATCHDOG_EXTRA_SECONDS
    launched_pid = None

    def watchdog():
        log(f"LLDB unresponsive {watchdog_seconds}s after start; giving up")
        if launched_pid is not None:
            try:
                os.kill(launched_pid, signal.SIGKILL)
            except OSError:
                pass
        sys.stdout.flush()
        os._exit(124)

    timer = threading.Timer(watchdog_seconds, watchdog)
    timer.daemon = True
    timer.start()
    is_darwin = sys.platform == "darwin"
    if is_darwin:
        flavor, style, ext = "mach-o", lldb.eSaveCoreDirtyOnly, "core"
    else:
        flavor, style, ext = "minidump", lldb.eSaveCoreFull, "dmp"

    os.makedirs(dumps_path, exist_ok=True)
    interpreter = debugger.GetCommandInterpreter()

    def cmd(text):
        result = lldb.SBCommandReturnObject()
        interpreter.HandleCommand(text, result)
        output = (result.GetOutput() or "") + (result.GetError() or "")
        if output:
            sys.stdout.write(output)
            sys.stdout.flush()
        return result.Succeeded()

    debugger.SetAsync(True)
    target = debugger.CreateTarget(command[0])
    if not target.IsValid():
        log(f"could not create target for {command[0]}")
        return 1
    log(f"target {command[0]} ({target.GetTriple()})")

    if is_darwin:
        # deliver Mach exceptions (which the runtime uses for NREs) as BSD signals, so the signal config below applies
        cmd("settings set platform.plugin.darwin.ignored-exceptions EXC_BAD_ACCESS|EXC_BAD_INSTRUCTION|EXC_ARITHMETIC")
        # The default thread formats include libdispatch queue info, and fetching it has crashed Apple's LLDB mid-backtrace
        for setting in ("thread-format", "thread-stop-format"):
            cmd(f'settings set {setting} "thread #${{thread.index}}: tid = ${{thread.id%tid}}{{, name = \'${{thread.name}}\'}}'
                '{, stop reason = ${thread.stop-reason}}\\n"')
    cmd(f"process handle -s false -p true -n false {PASS_SIGNALS}")
    if not is_darwin:
        cmd("process handle -s false -p true -n false SIGPWR")
    cmd(f"process handle -s true -p true -n true {' '.join(FATAL_SIGNALS)}")

    crash_bp = create_crash_breakpoint(target)

    info = lldb.SBLaunchInfo(command[1:])
    info.SetWorkingDirectory(os.getcwd())
    info.SetEnvironmentEntries([f"{k}={v}" for k, v in os.environ.items()], False)
    # ASLR can't be disabled in Docker (personality() is blocked), and we want normal layout anyway
    info.SetLaunchFlags(info.GetLaunchFlags() & ~lldb.eLaunchFlagDisableASLR)
    # Process events must come to a listener of our own; the debugger's listener is drained by LLDB's own event thread.
    listener = lldb.SBListener("run-with-dumps")
    info.SetListener(listener)

    error = lldb.SBError()
    log("launching")
    process = target.Launch(info, error)
    if not error.Success() or not process.IsValid():
        log(f"launch failed: {error}")
        return 1
    pid = process.GetProcessID()
    launched_pid = pid
    log(f"launched pid {pid}: {command}")

    def pump():
        for read in (process.GetSTDOUT, process.GetSTDERR):
            while True:
                data = read(65536)
                if not data:
                    break
                sys.stdout.write(data)
        sys.stdout.flush()

    def save_core(tag):
        path = os.path.join(dumps_path, f"{tag}_{pid}.{ext}")
        log(f"saving {flavor} core to {path}")
        err = process.SaveCore(path, flavor, style)
        if err.Fail():
            log(f"save-core failed: {err.GetCString()}; retrying with stack-only style")
            # a failed attempt can leave a partial file behind, which the retry would write over without truncating
            if os.path.exists(path):
                os.remove(path)
            err = process.SaveCore(path, flavor, lldb.eSaveCoreStackOnly)
        if err.Fail():
            log(f"save-core failed: {err.GetCString()}")
        else:
            log(f"saved {path} ({os.path.getsize(path)} bytes)")

    deadline = time.monotonic() + timeout
    hang_requested_at = None
    native_crash_dumped = False

    def on_stop():
        nonlocal native_crash_dumped, deadline
        pump()
        # Always save the dump before anything else that talks to LLDB (like a backtrace), in case LLDB falls over.
        if hang_requested_at is not None:
            if native_crash_dumped:
                # the process already crashed and was dumped; it's just stuck in its crash handler
                log("already dumped at the native crash; not taking a hang dump")
            else:
                save_core("hang")
            cmd("thread backtrace all")
            process.Kill()
            return 124

        fatal = None
        for thread in process:
            reason = thread.GetStopReason()
            if reason == lldb.eStopReasonBreakpoint and thread.GetStopReasonDataAtIndex(0) == crash_bp.GetID():
                log(f"{CRASH_BREAKPOINT} hit")
                save_core("native-crash")
                native_crash_dumped = True
                # Mono's crash handler should abort shortly; don't wait out the whole timeout if it gets stuck
                deadline = min(deadline, time.monotonic() + HANG_KILL_GRACE_SECONDS)
                process.Continue()
                return None
            if reason == lldb.eStopReasonSignal and fatal is None:
                signo = thread.GetStopReasonDataAtIndex(0)
                name = process.GetUnixSignals().GetSignalAsCString(signo)
                if name in FATAL_SIGNALS:
                    fatal = (signo, name)

        if fatal is not None:
            signo, name = fatal
            log(f"process received fatal signal {name}")
            if not native_crash_dumped:
                save_core(FATAL_SIGNALS[name])
            cmd("thread backtrace all")
            process.Kill()
            return 128 + signo

        process.Continue()
        return None

    event = lldb.SBEvent()
    while True:
        if listener.WaitForEvent(1, event) and lldb.SBProcess.EventIsProcessEvent(event):
            event_type = event.GetType()
            if event_type & (lldb.SBProcess.eBroadcastBitSTDOUT | lldb.SBProcess.eBroadcastBitSTDERR):
                pump()
            if event_type & lldb.SBProcess.eBroadcastBitStateChanged and not lldb.SBProcess.GetRestartedFromEvent(event):
                state = lldb.SBProcess.GetStateFromEvent(event)
                if state == lldb.eStateExited:
                    pump()
                    status = process.GetExitStatus()
                    log(f"process exited with status {status}")
                    return status
                if state == lldb.eStateCrashed:
                    pump()
                    log("process crashed")
                    save_core("crashed")
                    process.Kill()
                    return 1
                if state == lldb.eStateDetached:
                    pump()
                    log("process detached unexpectedly")
                    return 1
                if state == lldb.eStateStopped:
                    result = on_stop()
                    if result is not None:
                        return result

        now = time.monotonic()
        if hang_requested_at is None and now >= deadline:
            log(f"timed out after {timeout}s; interrupting for hang dump")
            hang_requested_at = now
            process.Stop()
        elif hang_requested_at is not None and now >= hang_requested_at + HANG_KILL_GRACE_SECONDS:
            pump()
            log("process did not stop after interrupt; killing without a dump")
            process.Kill()
            return 124


def __lldb_init_module(debugger, internal_dict):
    try:
        code = run(debugger)
    except Exception:
        traceback.print_exc()
        code = 1
    sys.stdout.flush()
    sys.stderr.flush()
    os._exit(code & 0xFF if code >= 0 else 1)
