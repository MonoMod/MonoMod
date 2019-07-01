using System;
using System.Reflection;
using MonoMod.Utils;
using System.Collections.Generic;
using Mono.Cecil;
using MonoMod.Cil;
using System.Diagnostics;

namespace MonoMod.RuntimeDetour {
    public sealed class DetourContext : IDisposable {

        [ThreadStatic]
        internal static List<DetourContext> _Contexts;
        internal static List<DetourContext> Contexts => _Contexts ?? (_Contexts = new List<DetourContext>());

        internal static DetourContext Current {
            get {
                List<DetourContext> ctxs = Contexts;
                for (int i = ctxs.Count - 1; i > -1; i--) {
                    DetourContext ctx = ctxs[i];
                    if (!ctx.IsValid)
                        ctxs.RemoveAt(i);
                    else
                        return ctx;
                }

                return null;
            }
        }

        private MethodBase Creator;

        public int Priority;
        private string _FallbackID;
        private string _ID;
        public string ID {
            get => _ID ?? _FallbackID;
            set => _ID = string.IsNullOrEmpty(value) ? null : value;
        }
        public List<string> Before = new List<string>();
        public List<string> After = new List<string>();

        public DetourConfig DetourConfig => new DetourConfig {
            Priority = Priority,
            ID = ID,
            Before = Before,
            After = After
        };

        public HookConfig HookConfig => new HookConfig {
            Priority = Priority,
            ID = ID,
            Before = Before,
            After = After
        };

        public ILHookConfig ILHookConfig => new ILHookConfig {
            Priority = Priority,
            ID = ID,
            Before = Before,
            After = After
        };

        private bool IsDisposed;
        internal bool IsValid {
            get {
                if (IsDisposed)
                    return false;

                if (Creator == null)
                    return true;

#if !NETSTANDARD1_X
                StackTrace stack = new StackTrace();
                int frameCount = stack.FrameCount;

                for (int i = 0; i < frameCount; i++)
                    if (stack.GetFrame(i).GetMethod() == Creator)
                        return true;
#endif

                return false;
            }
        }

        public DetourContext(int priority, string id) {
#if !NETSTANDARD1_X
            Creator = new StackFrame(1).GetMethod();
#endif
            _FallbackID = Creator?.Module?.Assembly?.GetName().Name ?? Creator.GetFindableID(simple: true);

            Contexts.Add(this);

            Priority = priority;
            ID = id;
        }
        public DetourContext(string id)
            : this(0, id) {
        }
        public DetourContext(int priority)
            : this(priority, null) {
        }
        public DetourContext()
            : this(0, null) {
        }

        public void Dispose() {
            if (!IsDisposed)
                return;
            IsDisposed = true;
            Contexts.Remove(this);
        }
    }
}
