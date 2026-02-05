#if WASMTIME_DEV

using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Wasmtime.Component
{
    /// <summary>
    /// Represents a WebAssembly component.
    /// </summary>
    public class Component : IDisposable
    {
        /// <summary>
        /// Creates a <see cref="Component"/> from a byte span.
        /// </summary>
        /// <param name="engine">The engine to use for compilation.</param>
        /// <param name="bytes">The bytes of the WebAssembly component binary.</param>
        /// <returns>A new <see cref="Component"/>.</returns>
        public static Component FromBytes(Engine engine, ReadOnlySpan<byte> bytes)
        {
            if (engine is null)
            {
                throw new ArgumentNullException(nameof(engine));
            }

            unsafe
            {
                fixed (byte* ptr = bytes)
                {
                    var error = Native.wasmtime_component_new(engine.NativeHandle, ptr, (nuint)bytes.Length, out var handle);
                    if (error != IntPtr.Zero)
                    {
                        throw new WasmtimeException(
                            $"Failed to compile component: {WasmtimeException.FromOwnedError(error).Message}");
                    }

                    return new Component(handle);
                }
            }
        }

        /// <summary>
        /// Creates a <see cref="Component"/> from a file.
        /// </summary>
        /// <param name="engine">The engine to use for compilation.</param>
        /// <param name="path">The path to the WebAssembly component file.</param>
        /// <returns>A new <see cref="Component"/>.</returns>
        public static Component FromFile(Engine engine, string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Component file not found: {path}", path);
            }

            return FromBytes(engine, File.ReadAllBytes(path));
        }

        /// <summary>
        /// Looks up a specific export of this component by name.
        /// </summary>
        /// <param name="name">The name of the export to look up.</param>
        /// <returns>The export index if found, otherwise null.</returns>
        public ComponentExportIndex? GetExportIndex(string name)
        {
            return GetExportIndex(null, name);
        }

        /// <summary>
        /// Looks up a specific export of this component by name, optionally nested within an instance.
        /// </summary>
        /// <param name="instanceIndex">Optional instance index to scope the lookup.</param>
        /// <param name="name">The name of the export to look up.</param>
        /// <returns>The export index if found, otherwise null.</returns>
        public ComponentExportIndex? GetExportIndex(ComponentExportIndex? instanceIndex, string name)
        {
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
                    var result = Native.wasmtime_component_get_export_index(
                        NativeHandle,
                        instanceHandle,
                        namePtr,
                        (nuint)nameBytes.Length);

                    if (result == IntPtr.Zero)
                    {
                        return null;
                    }

                    return new ComponentExportIndex(result);
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            handle.Dispose();
        }

        internal Component(IntPtr handle)
        {
            this.handle = new Handle(handle);
        }

        internal Handle NativeHandle
        {
            get
            {
                if (handle.IsInvalid || handle.IsClosed)
                {
                    throw new ObjectDisposedException(typeof(Component).FullName);
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
                Native.wasmtime_component_delete(handle);
                return true;
            }
        }

        internal static class Native
        {
            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern unsafe IntPtr wasmtime_component_new(
                Engine.Handle engine, byte* buf, nuint len, out IntPtr component_out);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void wasmtime_component_delete(IntPtr component);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern unsafe IntPtr wasmtime_component_get_export_index(
                Handle component,
                IntPtr instance_export_index,
                byte* name, nuint name_len);
        }

        private readonly Handle handle;
    }
}

#endif
