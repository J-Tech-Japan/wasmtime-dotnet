#if WASMTIME_DEV

using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Wasmtime.Component
{
    /// <summary>
    /// Represents an export index for a component, used to efficiently look up exports.
    /// </summary>
    public class ComponentExportIndex : IDisposable
    {
        /// <inheritdoc/>
        public void Dispose()
        {
            handle.Dispose();
        }

        internal ComponentExportIndex(IntPtr handle)
        {
            this.handle = new Handle(handle);
        }

        internal IntPtr NativeHandle
        {
            get
            {
                if (handle.IsInvalid || handle.IsClosed)
                {
                    throw new ObjectDisposedException(typeof(ComponentExportIndex).FullName);
                }

                return handle.DangerousGetHandle();
            }
        }

        internal class Handle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public Handle(IntPtr handle)
                : base(true)
            {
                SetHandle(handle);
            }

            protected override bool ReleaseHandle()
            {
                Native.wasmtime_component_export_index_delete(handle);
                return true;
            }
        }

        private static class Native
        {
            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void wasmtime_component_export_index_delete(IntPtr export_index);
        }

        private readonly Handle handle;
    }
}

#endif
