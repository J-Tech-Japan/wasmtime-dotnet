#if WASMTIME_DEV

using System;
using System.Runtime.InteropServices;

namespace Wasmtime.Component
{
    /// <summary>
    /// Represents an instantiated WebAssembly component.
    /// </summary>
    public class ComponentInstance
    {
        /// <summary>
        /// Gets an exported function by name.
        /// </summary>
        /// <param name="store">The store that this instance belongs to.</param>
        /// <param name="name">The name of the exported function.</param>
        /// <returns>The function if found, otherwise null.</returns>
        public ComponentFunc? GetFunc(Store store, string name)
        {
            if (store is null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            // First get the export index from the component
            var exportIndex = GetExportIndex(store, name);
            if (exportIndex is null)
            {
                return null;
            }

            using (exportIndex)
            {
                return GetFunc(store, exportIndex);
            }
        }

        /// <summary>
        /// Gets an exported function by export index.
        /// </summary>
        /// <param name="store">The store that this instance belongs to.</param>
        /// <param name="exportIndex">The export index of the function.</param>
        /// <returns>The function if found, otherwise null.</returns>
        public ComponentFunc? GetFunc(Store store, ComponentExportIndex exportIndex)
        {
            if (store is null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (exportIndex is null)
            {
                throw new ArgumentNullException(nameof(exportIndex));
            }

            var found = Native.wasmtime_component_instance_get_func(
                instance,
                store.Context.handle,
                exportIndex.NativeHandle,
                out var func);
            GC.KeepAlive(store);

            if (!found)
            {
                return null;
            }

            return new ComponentFunc(store, func);
        }

        /// <summary>
        /// Gets an export index by name.
        /// </summary>
        /// <param name="store">The store that this instance belongs to.</param>
        /// <param name="name">The name of the export to look up.</param>
        /// <returns>The export index if found, otherwise null.</returns>
        public ComponentExportIndex? GetExportIndex(Store store, string name)
        {
            return GetExportIndex(store, null, name);
        }

        /// <summary>
        /// Gets an export index by name, optionally scoped to an instance.
        /// </summary>
        /// <param name="store">The store that this instance belongs to.</param>
        /// <param name="instanceIndex">Optional instance index to scope the lookup.</param>
        /// <param name="name">The name of the export to look up.</param>
        /// <returns>The export index if found, otherwise null.</returns>
        public ComponentExportIndex? GetExportIndex(Store store, ComponentExportIndex? instanceIndex, string name)
        {
            if (store is null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            using var nameBytes = name.ToUTF8(stackalloc byte[Math.Min(256, name.Length * 2)]);

            unsafe
            {
                fixed (byte* namePtr = nameBytes.Span)
                {
                    var instanceHandle = instanceIndex?.NativeHandle ?? IntPtr.Zero;
                    var result = Native.wasmtime_component_instance_get_export_index(
                        instance,
                        store.Context.handle,
                        instanceHandle,
                        namePtr,
                        (nuint)nameBytes.Length);
                    GC.KeepAlive(store);

                    if (result == IntPtr.Zero)
                    {
                        return null;
                    }

                    return new ComponentExportIndex(result);
                }
            }
        }

        internal ComponentInstance(Store store, NativeComponentInstance instance)
        {
            this.store = store;
            this.instance = instance;
        }

        /// <summary>
        /// Native representation of a component instance (16 bytes).
        /// C layout: uint64_t store_id (offset 0) + uint32_t __private (offset 8) + 4 padding = 16.
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Size = 16)]
        internal struct NativeComponentInstance
        {
            [FieldOffset(0)] public ulong store_id;
            [FieldOffset(8)] public uint __private;
        }

        private static class Native
        {
            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern unsafe IntPtr wasmtime_component_instance_get_export_index(
                in NativeComponentInstance instance,
                IntPtr context,
                IntPtr instance_export_index,
                byte* name, nuint name_len);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            [return: MarshalAs(UnmanagedType.I1)]
            public static extern bool wasmtime_component_instance_get_func(
                in NativeComponentInstance instance,
                IntPtr context,
                IntPtr export_index,
                out ComponentFunc.NativeComponentFunc func_out);
        }

        private readonly Store store;
        private readonly NativeComponentInstance instance;
    }
}

#endif
