#if WASMTIME_DEV

using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Wasmtime.Component
{
    /// <summary>
    /// Represents a linker instance that can be used to define host functions and nested instances
    /// within a component linker.
    /// </summary>
    /// <remarks>
    /// This acquires exclusive access to the parent linker or linker instance.
    /// The parent MUST NOT be accessed until this instance is disposed.
    /// </remarks>
    public class ComponentLinkerInstance : IDisposable
    {
        /// <summary>
        /// Defines a host function within this linker instance.
        /// </summary>
        /// <param name="name">The name of the function to define.</param>
        /// <param name="callback">The callback to invoke when the function is called.</param>
        public void AddFunc(string name, ComponentFuncCallback callback)
        {
            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (callback is null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            var (nativeCallback, data) = ComponentFuncCallbackHelper.CreateNativeCallback(callback);

            unsafe
            {
                using var nameBytes = name.ToUTF8(stackalloc byte[Math.Min(256, name.Length * 2)]);
                fixed (byte* namePtr = nameBytes.Span)
                {
                    var error = Native.wasmtime_component_linker_instance_add_func(
                        NativeHandle,
                        namePtr,
                        (nuint)nameBytes.Length,
                        nativeCallback,
                        data,
                        ComponentFuncCallbackHelper.FinalizerInstance);

                    if (error != IntPtr.Zero)
                    {
                        throw WasmtimeException.FromOwnedError(error);
                    }
                }
            }
        }

        /// <summary>
        /// Defines a nested instance within this linker instance.
        /// </summary>
        /// <param name="name">The name of the nested instance.</param>
        /// <returns>A new <see cref="ComponentLinkerInstance"/> for the nested instance.</returns>
        /// <remarks>
        /// This acquires exclusive access to this instance.
        /// This instance MUST NOT be accessed until the returned instance is disposed.
        /// </remarks>
        public ComponentLinkerInstance AddInstance(string name)
        {
            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            unsafe
            {
                using var nameBytes = name.ToUTF8(stackalloc byte[Math.Min(256, name.Length * 2)]);
                fixed (byte* namePtr = nameBytes.Span)
                {
                    var error = Native.wasmtime_component_linker_instance_add_instance(
                        NativeHandle,
                        namePtr,
                        (nuint)nameBytes.Length,
                        out var instanceOut);

                    if (error != IntPtr.Zero)
                    {
                        throw WasmtimeException.FromOwnedError(error);
                    }

                    return new ComponentLinkerInstance(instanceOut);
                }
            }
        }

        /// <summary>
        /// Defines a core wasm module within this linker instance.
        /// </summary>
        /// <param name="name">The name of the module.</param>
        /// <param name="module">The core wasm module to define.</param>
        public void AddModule(string name, Module module)
        {
            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (module is null)
            {
                throw new ArgumentNullException(nameof(module));
            }

            unsafe
            {
                using var nameBytes = name.ToUTF8(stackalloc byte[Math.Min(256, name.Length * 2)]);
                fixed (byte* namePtr = nameBytes.Span)
                {
                    var error = Native.wasmtime_component_linker_instance_add_module(
                        NativeHandle,
                        namePtr,
                        (nuint)nameBytes.Length,
                        module.NativeHandle);

                    if (error != IntPtr.Zero)
                    {
                        throw WasmtimeException.FromOwnedError(error);
                    }
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            handle.Dispose();
        }

        internal ComponentLinkerInstance(IntPtr handle)
        {
            this.handle = new Handle(handle);
        }

        internal Handle NativeHandle
        {
            get
            {
                if (handle.IsInvalid || handle.IsClosed)
                {
                    throw new ObjectDisposedException(typeof(ComponentLinkerInstance).FullName);
                }

                return handle;
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
                Native.wasmtime_component_linker_instance_delete(handle);
                return true;
            }
        }

        private static class Native
        {
            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern unsafe IntPtr wasmtime_component_linker_instance_add_func(
                Handle linker_instance,
                byte* name,
                nuint name_len,
                ComponentFuncCallbackHelper.NativeComponentFuncCallback callback,
                IntPtr data,
                ComponentFuncCallbackHelper.Finalizer finalizer);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern unsafe IntPtr wasmtime_component_linker_instance_add_instance(
                Handle linker_instance,
                byte* name,
                nuint name_len,
                out IntPtr linker_instance_out);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern unsafe IntPtr wasmtime_component_linker_instance_add_module(
                Handle linker_instance,
                byte* name,
                nuint name_len,
                Module.Handle module);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void wasmtime_component_linker_instance_delete(IntPtr linker_instance);
        }

        private readonly Handle handle;
    }
}

#endif
