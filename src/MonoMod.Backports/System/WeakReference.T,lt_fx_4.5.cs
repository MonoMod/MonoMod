using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Permissions;

namespace System
{
    [Serializable]
    public sealed class WeakReference<T> : ISerializable
        where T : class
    {
        private readonly bool _trackResurrection;

        [NonSerialized]
        private GCHandle _handle;

        public WeakReference(T? target)
            : this(target, trackResurrection: false)
        {
            // Empty
        }

        public WeakReference(T? target, bool trackResurrection)
        {
            _trackResurrection = trackResurrection;
            SetTarget(target);
        }

        private WeakReference(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            _ = context;
            var value = (T)info.GetValue("TrackedObject", typeof(T));
            _trackResurrection = info.GetBoolean("TrackResurrection");
            SetTarget(value);
        }

        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.SerializationFormatter)]
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            TryGetTarget(out var value);
            info.AddValue("TrackedObject", value, typeof(T));
            info.AddValue("TrackResurrection", _trackResurrection);
        }

        [SecurityPermission(SecurityAction.Demand, UnmanagedCode = true)]
        public void SetTarget(T? value)
        {
            var oldHandle = _handle;
            _handle = GetNewHandle(value, _trackResurrection);
            if (!oldHandle.IsAllocated)
            {
                return;
            }

            oldHandle.Free();
            try
            {
                oldHandle.Free();
            }
            catch (InvalidOperationException exception)
            {
                // The handle was freed or never initialized.
                // Nothing to do.
                _ = exception;
            }
        }

        [SecurityPermission(SecurityAction.Demand, UnmanagedCode = true)]
        public bool TryGetTarget([NotNullWhen(true)] out T? target)
        {
            target = default;
            if (!_handle.IsAllocated)
            {
                return false;
            }

            try
            {
                var obj = _handle.Target;
                if (obj == null)
                {
                    return false;
                }

                target = obj as T;
                return target != null;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        [SecurityPermission(SecurityAction.Demand, UnmanagedCode = true)]
        private static GCHandle GetNewHandle(T? value, bool trackResurrection)
        {
            return GCHandle.Alloc(value, trackResurrection ? GCHandleType.WeakTrackResurrection : GCHandleType.Weak);
        }
    }
}