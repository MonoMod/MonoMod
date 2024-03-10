using Mono.Cecil.Cil;
using MonoMod.Core;
using MonoMod.Core.Platforms;
using MonoMod.Logs;
using MonoMod.Utils;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MonoMod.RuntimeDetour
{
    /// <summary>
    /// A single method hook from a source to a target, optionally allowing the target to call the original method.
    /// </summary>
    /// <remarks>
    /// <see cref="Hook"/>s, like other kinds of detours, are automatically undone when the garbage collector collects the object,
    /// or the object is disposed. Use <see cref="DetourInfo"/> to get an object which represents the hook without
    /// extending its lifetime.
    /// </remarks>
    public sealed class Hook : IDetour, IDisposable
    {

        private const bool ApplyByDefault = true;

        // Note: We don't provide all variants with IDetourFactory because providing IDetourFactory is expected to be fairly rare
        #region Constructor overloads
        #region No targetObj
        /// <summary>
        /// Constructs a <see cref="Hook"/> using the methods specified by expression trees. Each expression tree must consist only of
        /// a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        public Hook(Expression<Action> source, Expression<Action> target)
            : this(Helpers.ThrowIfNull(source).Body, Helpers.ThrowIfNull(target).Body) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> using the methods specified by expression trees. Each expression tree must consist only of
        /// a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        public Hook(Expression source, Expression target)
            : this(((MethodCallExpression)Helpers.ThrowIfNull(source)).Method,
                  ((MethodCallExpression)Helpers.ThrowIfNull(target)).Method)
        { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> detouring <paramref name="source"/> to <paramref name="target"/>.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        public Hook(MethodBase source, MethodInfo target)
            : this(source, target, DetourContext.GetDefaultConfig()) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> using the methods specified by expression trees. Each expression tree must consist only of
        /// a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public Hook(Expression<Action> source, Expression<Action> target, bool applyByDefault)
            : this(Helpers.ThrowIfNull(source).Body, Helpers.ThrowIfNull(target).Body, applyByDefault) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> using the methods specified by expression trees. Each expression tree must consist only of
        /// a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public Hook(Expression source, Expression target, bool applyByDefault)
            : this(((MethodCallExpression)Helpers.ThrowIfNull(source)).Method,
                  ((MethodCallExpression)Helpers.ThrowIfNull(target)).Method, applyByDefault)
        { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> detouring <paramref name="source"/> to <paramref name="target"/>.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public Hook(MethodBase source, MethodInfo target, bool applyByDefault)
            : this(source, target, DetourContext.GetDefaultConfig(), applyByDefault) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> using the provided <see cref="DetourConfig"/> and methods specified by expression trees. Each expression tree must consist only of
        /// a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="Hook"/>.</param>
        public Hook(Expression<Action> source, Expression<Action> target, DetourConfig? config)
            : this(Helpers.ThrowIfNull(source).Body, Helpers.ThrowIfNull(target).Body, config) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> using the provided <see cref="DetourConfig"/> and methods specified by expression trees. Each expression tree must consist only of
        /// a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="Hook"/>.</param>
        public Hook(Expression source, Expression target, DetourConfig? config)
            : this(((MethodCallExpression)Helpers.ThrowIfNull(source)).Method,
                  ((MethodCallExpression)Helpers.ThrowIfNull(target)).Method, config)
        { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> detouring <paramref name="source"/> to <paramref name="target"/>, using the provided <see cref="DetourConfig"/>.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="Hook"/>.</param>
        public Hook(MethodBase source, MethodInfo target, DetourConfig? config)
            : this(source, target, config, ApplyByDefault) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> using the provided <see cref="DetourConfig"/> and methods specified by expression trees. Each expression tree must consist only of
        /// a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="Hook"/>.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public Hook(Expression<Action> source, Expression<Action> target, DetourConfig? config, bool applyByDefault)
            : this(Helpers.ThrowIfNull(source).Body, Helpers.ThrowIfNull(target).Body, config, applyByDefault) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> using the provided <see cref="DetourConfig"/> and methods specified by expression trees. Each expression tree must consist only of
        /// a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="Hook"/>.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public Hook(Expression source, Expression target, DetourConfig? config, bool applyByDefault)
            : this(((MethodCallExpression)Helpers.ThrowIfNull(source)).Method,
                  ((MethodCallExpression)Helpers.ThrowIfNull(target)).Method, config, applyByDefault)
        { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> detouring <paramref name="source"/> to <paramref name="target"/>, using the provided <see cref="DetourConfig"/>.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="Hook"/>.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public Hook(MethodBase source, MethodInfo target, DetourConfig? config, bool applyByDefault)
            : this(source, target, DetourContext.GetDefaultFactory(), config, applyByDefault) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> detouring <paramref name="source"/> to <paramref name="target"/>, using the provided <see cref="DetourConfig"/>
        /// and <see cref="IDetourFactory"/>.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        /// <param name="factory">The <see cref="IDetourFactory"/> to use when manipulating this <see cref="Hook"/>.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="Hook"/>.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public Hook(MethodBase source, MethodInfo target, IDetourFactory factory, DetourConfig? config, bool applyByDefault)
            : this(source, target, null, factory, config, applyByDefault) { }
        #endregion
        #region With targetObj
        /// <summary>
        /// Constructs a <see cref="Hook"/> using the methods specified by expression trees, and specified target object. Each expression tree must consist only of
        /// a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        /// <param name="targetObj">The <see langword="this"/> object to call the target method on.</param>
        public Hook(Expression<Action> source, Expression<Action> target, object? targetObj)
            : this(Helpers.ThrowIfNull(source).Body, Helpers.ThrowIfNull(target).Body, targetObj) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> using the methods specified by expression trees, and specified target object. Each expression tree must consist only of
        /// a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        /// <param name="targetObj">The <see langword="this"/> object to call the target method on.</param>
        public Hook(Expression source, Expression target, object? targetObj)
            : this(((MethodCallExpression)Helpers.ThrowIfNull(source)).Method,
                  ((MethodCallExpression)Helpers.ThrowIfNull(target)).Method, targetObj)
        { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> detouring <paramref name="source"/> to <paramref name="target"/>, calling <paramref name="target"/> on <paramref name="targetObj"/>.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        /// <param name="targetObj">The <see langword="this"/> object to call the target method on.</param>
        public Hook(MethodBase source, MethodInfo target, object? targetObj)
            : this(source, target, targetObj, DetourContext.GetDefaultConfig()) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> using the methods specified by expression trees, and specified target object. Each expression tree must consist only of
        /// a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        /// <param name="targetObj">The <see langword="this"/> object to call the target method on.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public Hook(Expression<Action> source, Expression<Action> target, object? targetObj, bool applyByDefault)
            : this(Helpers.ThrowIfNull(source).Body, Helpers.ThrowIfNull(target).Body, targetObj, applyByDefault) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> using the methods specified by expression trees, and specified target object. Each expression tree must consist only of
        /// a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        /// <param name="targetObj">The <see langword="this"/> object to call the target method on.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public Hook(Expression source, Expression target, object? targetObj, bool applyByDefault)
            : this(((MethodCallExpression)Helpers.ThrowIfNull(source)).Method,
                  ((MethodCallExpression)Helpers.ThrowIfNull(target)).Method, targetObj, applyByDefault)
        { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> detouring <paramref name="source"/> to <paramref name="target"/>, calling <paramref name="target"/> on <paramref name="targetObj"/>.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        /// <param name="targetObj">The <see langword="this"/> object to call the target method on.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public Hook(MethodBase source, MethodInfo target, object? targetObj, bool applyByDefault)
            : this(source, target, targetObj, DetourContext.GetDefaultConfig(), applyByDefault) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> using the provided <see cref="DetourConfig"/>, methods specified by expression trees, and specified target object.
        /// Each expression tree must consist only of a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        /// <param name="targetObj">The <see langword="this"/> object to call the target method on.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="Hook"/>.</param>
        public Hook(Expression<Action> source, Expression<Action> target, object? targetObj, DetourConfig? config)
            : this(Helpers.ThrowIfNull(source).Body, Helpers.ThrowIfNull(target).Body, targetObj, config) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> using the provided <see cref="DetourConfig"/>, methods specified by expression trees, and specified target object.
        /// Each expression tree must consist only of a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        /// <param name="targetObj">The <see langword="this"/> object to call the target method on.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="Hook"/>.</param>
        public Hook(Expression source, Expression target, object? targetObj, DetourConfig? config)
            : this(((MethodCallExpression)Helpers.ThrowIfNull(source)).Method,
                  ((MethodCallExpression)Helpers.ThrowIfNull(target)).Method, targetObj, config)
        { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> detouring <paramref name="source"/> to <paramref name="target"/>, calling <paramref name="target"/> on <paramref name="targetObj"/>,
        /// using the provided <see cref="DetourConfig"/>..
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        /// <param name="targetObj">The <see langword="this"/> object to call the target method on.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="Hook"/>.</param>
        public Hook(MethodBase source, MethodInfo target, object? targetObj, DetourConfig? config)
            : this(source, target, targetObj, config, ApplyByDefault) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> using the provided <see cref="DetourConfig"/>, methods specified by expression trees, and specified target object.
        /// Each expression tree must consist only of a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        /// <param name="targetObj">The <see langword="this"/> object to call the target method on.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="Hook"/>.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public Hook(Expression<Action> source, Expression<Action> target, object? targetObj, DetourConfig? config, bool applyByDefault)
            : this(Helpers.ThrowIfNull(source).Body, Helpers.ThrowIfNull(target).Body, targetObj, config, applyByDefault) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> using the provided <see cref="DetourConfig"/>, methods specified by expression trees, and specified target object.
        /// Each expression tree must consist only of a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        /// <param name="targetObj">The <see langword="this"/> object to call the target method on.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="Hook"/>.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public Hook(Expression source, Expression target, object? targetObj, DetourConfig? config, bool applyByDefault)
            : this(((MethodCallExpression)Helpers.ThrowIfNull(source)).Method,
                  ((MethodCallExpression)Helpers.ThrowIfNull(target)).Method, targetObj, config, applyByDefault)
        { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> detouring <paramref name="source"/> to <paramref name="target"/>, calling <paramref name="target"/> on <paramref name="targetObj"/>,
        /// using the provided <see cref="DetourConfig"/>.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        /// <param name="targetObj">The <see langword="this"/> object to call the target method on.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="Hook"/>.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public Hook(MethodBase source, MethodInfo target, object? targetObj, DetourConfig? config, bool applyByDefault)
            : this(source, target, targetObj, DetourContext.GetDefaultFactory(), config, applyByDefault) { }
        #endregion
        #region Delegate target
        /// <summary>
        /// Constructs a <see cref="Hook"/> detouring the method specified by the provided expression tree to the provided delegate.
        /// The expression tree must consist only of a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The method to detour.</param>
        /// <param name="target">The target delegate.</param>
        public Hook(Expression<Action> source, Delegate target)
            : this(Helpers.ThrowIfNull(source).Body, target) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> detouring the method specified by the provided expression tree to the provided delegate.
        /// The expression tree must consist only of a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The method to detour.</param>
        /// <param name="target">The target delegate.</param>
        public Hook(Expression source, Delegate target)
            : this(((MethodCallExpression)Helpers.ThrowIfNull(source)).Method, target) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> detouring the provided method to the provided delegate.
        /// </summary>
        /// <param name="source">The method to detour.</param>
        /// <param name="target">The target delegate.</param>
        public Hook(MethodBase source, Delegate target)
            : this(source, target, DetourContext.GetDefaultConfig()) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> detouring the method specified by the provided expression tree to the provided delegate.
        /// The expression tree must consist only of a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The method to detour.</param>
        /// <param name="target">The target delegate.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public Hook(Expression<Action> source, Delegate target, bool applyByDefault)
            : this(Helpers.ThrowIfNull(source).Body, target, applyByDefault) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> detouring the method specified by the provided expression tree to the provided delegate.
        /// The expression tree must consist only of a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The method to detour.</param>
        /// <param name="target">The target delegate.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public Hook(Expression source, Delegate target, bool applyByDefault)
            : this(((MethodCallExpression)Helpers.ThrowIfNull(source)).Method, target, applyByDefault) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> detouring the provided method to the provided delegate.
        /// </summary>
        /// <param name="source">The method to detour.</param>
        /// <param name="target">The target delegate.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public Hook(MethodBase source, Delegate target, bool applyByDefault)
            : this(source, target, DetourContext.GetDefaultConfig(), applyByDefault) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> detouring the method specified by the provided expression tree to the provided delegate.
        /// The expression tree must consist only of a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The method to detour.</param>
        /// <param name="target">The target delegate.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="Hook"/>.</param>
        public Hook(Expression<Action> source, Delegate target, DetourConfig? config)
            : this(Helpers.ThrowIfNull(source).Body, target, config) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> detouring the method specified by the provided expression tree to the provided delegate.
        /// The expression tree must consist only of a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The method to detour.</param>
        /// <param name="target">The target delegate.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="Hook"/>.</param>
        public Hook(Expression source, Delegate target, DetourConfig? config)
            : this(((MethodCallExpression)Helpers.ThrowIfNull(source)).Method, target, config) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> detouring the provided method to the provided delegate, using the provided <see cref="DetourConfig"/>.
        /// </summary>
        /// <param name="source">The method to detour.</param>
        /// <param name="target">The target delegate.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="Hook"/>.</param>
        public Hook(MethodBase source, Delegate target, DetourConfig? config)
            : this(source, target, config, ApplyByDefault) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> detouring the method specified by the provided expression tree to the provided delegate.
        /// The expression tree must consist only of a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The method to detour.</param>
        /// <param name="target">The target delegate.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="Hook"/>.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public Hook(Expression<Action> source, Delegate target, DetourConfig? config, bool applyByDefault)
            : this(Helpers.ThrowIfNull(source).Body, target, config, applyByDefault) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> detouring the method specified by the provided expression tree to the provided delegate.
        /// The expression tree must consist only of a single methodcall, which will be the method used for that parameter.
        /// </summary>
        /// <param name="source">The method to detour.</param>
        /// <param name="target">The target delegate.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="Hook"/>.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public Hook(Expression source, Delegate target, DetourConfig? config, bool applyByDefault)
            : this(((MethodCallExpression)Helpers.ThrowIfNull(source)).Method, target, config, applyByDefault) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> detouring the provided method to the provided delegate, using the provided <see cref="DetourConfig"/>.
        /// </summary>
        /// <param name="source">The method to detour.</param>
        /// <param name="target">The target delegate.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="Hook"/>.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public Hook(MethodBase source, Delegate target, DetourConfig? config, bool applyByDefault)
            : this(source, target, DetourContext.GetDefaultFactory(), config, applyByDefault) { }

        /// <summary>
        /// Constructs a <see cref="Hook"/> detouring the provided method to the provided delegate, using the provided <see cref="DetourConfig"/>
        /// and <see cref="IDetourFactory"/>.
        /// </summary>
        /// <param name="source">The method to detour.</param>
        /// <param name="target">The target delegate.</param>
        /// <param name="factory">The <see cref="IDetourFactory"/> to use when manipulating this <see cref="Hook"/>.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="Hook"/>.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public Hook(MethodBase source, Delegate target, IDetourFactory factory, DetourConfig? config, bool applyByDefault)
            : this(source, GetDelegateHookInfo(Helpers.ThrowIfNull(target), out var targetObj), targetObj, factory, config, applyByDefault) { }
        #endregion
        #endregion

        private static MethodInfo GetDelegateHookInfo(Delegate del, out object? target)
        {
            if (del.GetInvocationList().Length == 1)
            {
                target = del.Target;
                return del.Method;
            }
            else
            {
                target = del;
                return del.GetType().GetMethod("Invoke") ?? throw new InvalidOperationException("Could not get Invoke method of delegate");
            }
        }

        private readonly IDetourFactory factory;
        IDetourFactory IDetourBase.Factory => factory;

        /// <summary>
        /// Gets the <see cref="DetourConfig"/> associated with this <see cref="Hook"/>, if any.
        /// </summary>
        public DetourConfig? Config { get; }

        /// <summary>
        /// Gets the method which is being hooked.
        /// </summary>
        public MethodBase Source { get; }
        /// <summary>
        /// Gets the method which is the target of the hook.
        /// </summary>
        public MethodInfo Target { get; }
        MethodInfo IDetour.PublicTarget => Target;

        private readonly MethodInfo realTarget;
        MethodInfo IDetour.InvokeTarget => realTarget;


        [SuppressMessage("Reliability", "CA2002:Do not lock on objects with weak identity",
            Justification = "This type is never available externally, and will never be locked on externally.")]
        private sealed class TrampolineData : IDetourTrampoline, IDisposable
        {

            private readonly MethodInfo trampoline;
            private bool alive, hasOwnership;

            public MethodBase TrampolineMethod => trampoline;

            public TrampolineData(MethodSignature sig)
            {
                trampoline = TrampolinePool.Rent(sig);
                alive = hasOwnership = true;
            }

            public void Dispose()
            {
                lock (this)
                {
                    if (!alive)
                    {
                        return;
                    }
                    alive = false;

                    if (hasOwnership)
                    {
                        TrampolinePool.Return(trampoline);
                    }
                }
            }

            public void StealTrampolineOwnership()
            {
                lock (this)
                {
                    Helpers.Assert(alive && hasOwnership);
                    hasOwnership = false;
                }
            }

            public void ReturnTrampolineOwnership()
            {
                lock (this)
                {
                    Helpers.Assert(!hasOwnership);

                    if (!alive)
                    {
                        TrampolinePool.Return(trampoline);
                    }
                    else
                    {
                        hasOwnership = true;
                    }
                }
            }

        }

        private readonly TrampolineData trampoline;
        IDetourTrampoline IDetour.NextTrampoline => trampoline;

        private readonly DetourManager.ManagedDetourState state;
        private readonly DetourManager.SingleManagedDetourState detour;

        private readonly DataScope<DynamicReferenceCell> delegateObjectScope;

        /// <summary>
        /// Constructs a <see cref="Hook"/> using the specified source and target methods, specified target object, specified <see cref="IDetourFactory"/> and <see cref="DetourConfig"/>,
        /// specifying whether or not the hook should be applied when the constructor exits.
        /// </summary>
        /// <param name="source">The source method.</param>
        /// <param name="target">The target method.</param>
        /// <param name="targetObject">The <see langword="this"/> object to call the target method on.</param>
        /// <param name="factory">The <see cref="IDetourFactory"/> to use when manipulating this <see cref="Hook"/>.</param>
        /// <param name="config">The <see cref="DetourConfig"/> to use for this <see cref="Hook"/>.</param>
        /// <param name="applyByDefault">Whether or not this hook should be applied when the constructor finishes.</param>
        public Hook(MethodBase source, MethodInfo target, object? targetObject, IDetourFactory factory, DetourConfig? config, bool applyByDefault)
        {
            Helpers.ThrowIfArgumentNull(source);
            Helpers.ThrowIfArgumentNull(target);
            Helpers.ThrowIfArgumentNull(factory);

            this.factory = factory;
            Config = config;
            Source = PlatformTriple.Current.GetIdentifiable(source);
            Target = target;

            realTarget = PrepareRealTarget(targetObject, out trampoline, out delegateObjectScope);

            MMDbgLog.Trace($"Creating Hook from {Source} to {Target}");

            state = DetourManager.GetDetourState(source);
            detour = new(this);

            if (applyByDefault)
            {
                Apply();
            }
        }

        private sealed class HookData
        {
            public readonly object? Target;
            public readonly Delegate? InvokeNext;
            public HookData(object? target, Delegate? invokeNext)
            {
                Target = target;
                InvokeNext = invokeNext;
            }
        }

        private static readonly FieldInfo HookData_Target = typeof(HookData).GetField(nameof(HookData.Target))!;
        private static readonly FieldInfo HookData_InvokeNext = typeof(HookData).GetField(nameof(HookData.InvokeNext))!;

        private MethodInfo PrepareRealTarget(object? target, out TrampolineData trampoline, out DataScope<DynamicReferenceCell> scope)
        {
            var srcSig = MethodSignature.ForMethod(Source);
            var dstSig = MethodSignature.ForMethod(Target, ignoreThis: true); // the dest sig we don't want to consider its this param

            if (target is null && !Target.IsStatic)
            {
                throw new ArgumentException("Target method is nonstatic, but no target object was provided");
            }

            if (target is not null && Target.IsStatic)
            {
                throw new ArgumentException("Target method is static, but a target object was provided");
            }

            Type? nextDelegateType = null;
            if (dstSig.ParameterCount == srcSig.ParameterCount + 1)
            {
                // the dest method has a delegate as its first parameter
                nextDelegateType = dstSig.FirstParameter;
                Helpers.DAssert(nextDelegateType is not null);
                dstSig = new MethodSignature(dstSig.ReturnType, dstSig.Parameters.Skip(1));
            }

            if (!srcSig.IsCompatibleWith(dstSig))
            {
                throw new ArgumentException("Target method is not compatible with source method");
            }

            var trampSig = srcSig;

            var delegateInvoke = nextDelegateType?.GetMethod("Invoke");
            if (delegateInvoke is not null)
            {
                // we want to check that the delegate invoke is also compatible with the source sig
                var invokeSig = MethodSignature.ForMethod(delegateInvoke, ignoreThis: true);
                // if it takes a delegate parameter, the trampoline signature should match that delegate
                trampSig = invokeSig;
            }

            if (!trampSig.IsCompatibleWith(srcSig))
            {
                throw new ArgumentException("Target method's delegate parameter is not compatible with the source method");
            }

            trampoline = new TrampolineData(trampSig);
            // note: even in the below case, where it'll never be used, we still need to *get* a trampoline because the DetourManager
            //     expects to have one available to it

            if (target is null && nextDelegateType is null)
            {
                // if both the target and the next delegate type are null, then no proxy method is needed,
                // and the target method can be used as-is
                scope = default;
                return Target;
            }

            var hookData = new HookData(target,
                nextDelegateType is not null
                ? trampoline.TrampolineMethod.CreateDelegate(nextDelegateType)
                : null);

            using (var dmd = srcSig.CreateDmd(DebugFormatter.Format($"Hook<{Target.GetID()}>")))
            {
                var il = dmd.GetILProcessor();
                var module = dmd.Module!;
                var method = dmd.Definition!;

                var dataLoc = new VariableDefinition(module.ImportReference(typeof(HookData)));
                il.Body.Variables.Add(dataLoc);

                scope = il.EmitNewTypedReference(hookData, out _);
                il.Emit(OpCodes.Stloc, dataLoc);

                // first load the target object, if needed
                if (!Target.IsStatic)
                {
                    il.Emit(OpCodes.Ldloc, dataLoc);
                    il.Emit(OpCodes.Ldfld, module.ImportReference(HookData_Target));

                    var declType = Target.DeclaringType;
                    if (declType is not null)
                    {
                        if (declType.IsValueType)
                        {
                            il.Emit(OpCodes.Unbox, module.ImportReference(declType));
                        }
                        else
                        {
                            // the cast should be redundant
                            //il.Emit(OpCodes.Castclass, module.ImportReference(declType));
                        }
                    }
                }

                // then load the delegate, if needed
                if (nextDelegateType is not null)
                {
                    il.Emit(OpCodes.Ldloc, dataLoc);
                    il.Emit(OpCodes.Ldfld, module.ImportReference(HookData_InvokeNext));
                }

                // then load all of our arguments
                foreach (var p in method.Parameters)
                {
                    il.Emit(OpCodes.Ldarg, p.Index);
                }

                // then call our target method
                il.Emit(OpCodes.Call, Target);
                il.Emit(OpCodes.Ret);

                return dmd.Generate();
            }
        }

        private void CheckDisposed()
        {
            if (disposedValue)
                throw new ObjectDisposedException(ToString());
        }

        /// <summary>
        /// Applies this hook if it was not already applied.
        /// </summary>
        public void Apply()
        {
            CheckDisposed();

            var lockTaken = false;
            try
            {
                state.detourLock.Enter(ref lockTaken);
                if (IsApplied)
                    return;
                MMDbgLog.Trace($"Applying Hook from {Source} to {Target}");
                state.AddDetour(detour, !lockTaken);
            }
            finally
            {
                if (lockTaken)
                    state.detourLock.Exit(true);
            }
        }

        /// <summary>
        /// Undoes this hook if it was applied.
        /// </summary>
        public void Undo()
        {
            CheckDisposed();

            var lockTaken = false;
            try
            {
                state.detourLock.Enter(ref lockTaken);
                if (!IsApplied)
                    return;
                MMDbgLog.Trace($"Undoing Hook from {Source} to {Target}");
                state.RemoveDetour(detour, !lockTaken);
            }
            finally
            {
                if (lockTaken)
                    state.detourLock.Exit(true);
            }
        }

        private bool disposedValue;
        /// <summary>
        /// Gets whether or not this hook is valid and can be manipulated.
        /// </summary>
        public bool IsValid => !disposedValue;
        /// <summary>
        /// Gets whether or not this hook is currently applied.
        /// </summary>
        public bool IsApplied => detour.IsApplied;
        /// <summary>
        /// Gets the <see cref="DetourInfo"/> associated with this hook.
        /// </summary>
        public DetourInfo DetourInfo => state.Info.GetDetourInfo(detour);

        private void Dispose(bool disposing)
        {
            if (!disposedValue && detour is not null)
            {
                detour.IsValid = false;
                if (!(AppDomain.CurrentDomain.IsFinalizingForUnload() || Environment.HasShutdownStarted))
                    Undo();
                delegateObjectScope.Dispose();

                if (disposing)
                {
                    trampoline.Dispose();
                }

                disposedValue = true;
            }
        }

        /// <summary>
        /// Cleans up and undoes the hook, if needed.
        /// </summary>
        ~Hook()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
