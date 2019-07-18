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
        private static List<DetourContext> _Contexts;
        private static List<DetourContext> Contexts => _Contexts ?? (_Contexts = new List<DetourContext>());

        [ThreadStatic]
        private static DetourContext Last;
        internal static DetourContext Current {
            get {
                if (Last?.IsValid ?? false)
                    return Last;

                List<DetourContext> ctxs = Contexts;
                for (int i = ctxs.Count - 1; i > -1; i--) {
                    DetourContext ctx = ctxs[i];
                    if (!ctx.IsValid)
                        ctxs.RemoveAt(i);
                    else
                        return Last = ctx;
                }

                return null;
            }
        }

        private MethodBase Creator = null;

        public int Priority;
        private readonly string _FallbackID;
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

                StackTrace stack = new StackTrace();
                int frameCount = stack.FrameCount;

                for (int i = 0; i < frameCount; i++)
                    if (stack.GetFrame(i).GetMethod() == Creator)
                        return true;

                return false;
            }
        }

        public DetourContext(int priority, string id) {
            StackTrace stack = new StackTrace();
            int frameCount = stack.FrameCount;
            for (int i = 0; i < frameCount; i++) {
                MethodBase caller = stack.GetFrame(i).GetMethod();
                if (caller?.DeclaringType == typeof(DetourContext))
                    continue;
                Creator = caller;
                break;
            }
            _FallbackID = Creator?.DeclaringType?.Assembly?.GetName().Name ?? Creator?.GetID(simple: true);

            Last = this;
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
            Last = null;
            Contexts.Remove(this);
        }
    }
}
