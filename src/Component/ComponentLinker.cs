#if WASMTIME_DEV

using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Wasmtime.Component
{
    /// <summary>
    /// Represents a component linker that can be used to define imports and instantiate components.
    /// </summary>
    public class ComponentLinker : IDisposable
    {
        /// <summary>
        /// Creates a new component linker for the given engine.
        /// </summary>
        /// <param name="engine">The engine to use for the linker.</param>
        public ComponentLinker(Engine engine)
        {
            if (engine is null)
            {
                throw new ArgumentNullException(nameof(engine));
            }

            handle = new Handle(Native.wasmtime_component_linker_new(engine.NativeHandle));
        }

        /// <summary>
        /// Configures whether or not the linker allows later definitions to shadow previous definitions.
        /// </summary>
        public bool AllowShadowing
        {
            set
            {
                Native.wasmtime_component_linker_allow_shadowing(NativeHandle, value);
            }
        }

        /// <summary>
        /// Instantiates a component with this linker.
        /// </summary>
        /// <param name="store">The store to create the instance in.</param>
        /// <param name="component">The component to instantiate.</param>
        /// <returns>A new <see cref="ComponentInstance"/>.</returns>
        public ComponentInstance Instantiate(Store store, Component component)
        {
            if (store is null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (component is null)
            {
                throw new ArgumentNullException(nameof(component));
            }

            var error = Native.wasmtime_component_linker_instantiate(
                NativeHandle,
                store.Context.handle,
                component.NativeHandle,
                out var instance);
            GC.KeepAlive(store);

            if (error != IntPtr.Zero)
            {
                throw WasmtimeException.FromOwnedError(error);
            }

            return new ComponentInstance(store, instance);
        }

        /// <summary>
        /// Defines all unknown imports of the component as trapping functions.
        /// This is useful during development and testing.
        /// </summary>
        /// <param name="component">The component whose unknown imports should be defined as traps.</param>
        public void DefineUnknownImportsAsTraps(Component component)
        {
            if (component is null)
            {
                throw new ArgumentNullException(nameof(component));
            }

            var error = Native.wasmtime_component_linker_define_unknown_imports_as_traps(
                NativeHandle, component.NativeHandle);

            if (error != IntPtr.Zero)
            {
                throw WasmtimeException.FromOwnedError(error);
            }
        }

        /// <summary>
        /// Adds WASI preview2 interfaces to this linker.
        /// </summary>
        public void AddWasiP2()
        {
            var error = Native.wasmtime_component_linker_add_wasip2(NativeHandle);

            if (error != IntPtr.Zero)
            {
                throw WasmtimeException.FromOwnedError(error);
            }
        }

        /// <summary>
        /// Returns the "root instance" of this linker, used to define names into the root namespace.
        /// </summary>
        /// <returns>A <see cref="ComponentLinkerInstance"/> for the root namespace.</returns>
        /// <remarks>
        /// This acquires exclusive access to the linker. The linker MUST NOT be accessed
        /// until the returned <see cref="ComponentLinkerInstance"/> is disposed.
        /// </remarks>
        public ComponentLinkerInstance Root()
        {
            var instancePtr = Native.wasmtime_component_linker_root(NativeHandle);
            return new ComponentLinkerInstance(instancePtr);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            handle.Dispose();
        }

        internal Handle NativeHandle
        {
            get
            {
                if (handle.IsInvalid || handle.IsClosed)
                {
                    throw new ObjectDisposedException(typeof(ComponentLinker).FullName);
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
                Native.wasmtime_component_linker_delete(handle);
                return true;
            }
        }

        private static class Native
        {
            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr wasmtime_component_linker_new(Engine.Handle engine);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void wasmtime_component_linker_allow_shadowing(Handle linker, [MarshalAs(UnmanagedType.I1)] bool allow);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr wasmtime_component_linker_instantiate(
                Handle linker,
                IntPtr context,
                Component.Handle component,
                out ComponentInstance.NativeComponentInstance instance_out);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr wasmtime_component_linker_define_unknown_imports_as_traps(
                Handle linker, Component.Handle component);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr wasmtime_component_linker_add_wasip2(Handle linker);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr wasmtime_component_linker_root(Handle linker);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void wasmtime_component_linker_delete(IntPtr linker);
        }

        private readonly Handle handle;
    }
}

#endif
