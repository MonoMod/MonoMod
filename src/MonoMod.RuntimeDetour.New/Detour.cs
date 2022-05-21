using MonoMod.Core.Platforms;
using MonoMod.Core.Utils;
using MonoMod.RuntimeDetour.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MonoMod.RuntimeDetour {

    public class Detour : IDetour {

        public const bool ApplyByDefault = true;

        #region Constructor overloads
        public Detour(Expression<Action> source, Expression<Action> target)
            : this(Helpers.ThrowIfNull(source).Body, Helpers.ThrowIfNull(target).Body) { }

        public Detour(Expression source, Expression target)
            : this(((MethodCallExpression) Helpers.ThrowIfNull(source)).Method,
                  ((MethodCallExpression) Helpers.ThrowIfNull(target)).Method) { }

        public Detour(MethodBase source, MethodBase target)
            : this(source, target, null) { }

        public Detour(Expression<Action> source, Expression<Action> target, bool applyByDefault)
            : this(Helpers.ThrowIfNull(source).Body, Helpers.ThrowIfNull(target).Body, applyByDefault) { }

        public Detour(Expression source, Expression target, bool applyByDefault)
            : this(((MethodCallExpression) Helpers.ThrowIfNull(source)).Method,
                  ((MethodCallExpression) Helpers.ThrowIfNull(target)).Method, applyByDefault) { }

        public Detour(MethodBase source, MethodBase target, bool applyByDefault)
            : this(source, target, null, applyByDefault) { }

        public Detour(Expression<Action> source, Expression<Action> target, DetourConfig? config)
            : this(Helpers.ThrowIfNull(source).Body, Helpers.ThrowIfNull(target).Body, config) { }

        public Detour(Expression source, Expression target, DetourConfig? config)
            : this(((MethodCallExpression) Helpers.ThrowIfNull(source)).Method,
                  ((MethodCallExpression) Helpers.ThrowIfNull(target)).Method, config) { }

        public Detour(MethodBase source, MethodBase target, DetourConfig? config)
            : this(source, target, config, ApplyByDefault) { }

        public Detour(Expression<Action> source, Expression<Action> target, DetourConfig? config, bool applyByDefault)
            : this(Helpers.ThrowIfNull(source).Body, Helpers.ThrowIfNull(target).Body, config, applyByDefault) { }

        public Detour(Expression source, Expression target, DetourConfig? config, bool applyByDefault)
            : this(((MethodCallExpression) Helpers.ThrowIfNull(source)).Method, 
                  ((MethodCallExpression) Helpers.ThrowIfNull(target)).Method, config, applyByDefault) { }
        #endregion

        public PlatformTriple Platform { get; }

        public DetourConfig? Config { get; }

        public MethodBase Source { get; }
        public MethodBase Target { get; }

        MethodInfo IDetour.InvokeTarget => throw new NotImplementedException();

        MethodBase IDetour.NextTrampoline => throw new NotImplementedException();

        public Detour(MethodBase source, MethodBase target, DetourConfig? config, bool applyByDefault) {
            Config = config;
            Platform = PlatformTriple.Current;

            Source = Platform.GetIdentifiable(source);
            Target = target;
        }

    }
}
