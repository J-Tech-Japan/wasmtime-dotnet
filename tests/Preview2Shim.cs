#if WASMTIME_DEV

using System;
using System.Runtime.InteropServices;

namespace Wasmtime.Tests
{
    /// <summary>
    /// P/Invoke wrapper for the wasmtime-preview2-shim native library.
    /// This provides WASI P2 + HTTP support for component instantiation
    /// using the Rust wasmtime API directly (bypassing C API limitations).
    /// </summary>
    internal static class Preview2Shim
    {
        private const string LibraryName = "wasmtime_preview2_shim";

        /// <summary>
        /// Instantiate a component with WASI P2 + HTTP support.
        /// Returns an opaque handle, or IntPtr.Zero on error.
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr wasmtime_preview2_instantiate_component(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string componentPath,
            [MarshalAs(UnmanagedType.I1)] bool inheritStdio,
            out IntPtr errorMessageOut);

        /// <summary>
        /// Call an exported function on a component instance.
        /// Args and results are JSON strings.
        /// Returns 0 on success.
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int wasmtime_preview2_call_func(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string funcName,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string argsJson,
            out IntPtr resultJsonOut,
            out IntPtr errorMessageOut);

        /// <summary>
        /// Free a component instance handle.
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void wasmtime_preview2_free_instance(IntPtr handle);

        /// <summary>
        /// Free a string allocated by the shim.
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void wasmtime_preview2_free_string(IntPtr ptr);

        /// <summary>
        /// Read a string from a native pointer and free it.
        /// </summary>
        public static string ReadAndFreeString(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return null;
            try
            {
                return Marshal.PtrToStringUTF8(ptr);
            }
            finally
            {
                wasmtime_preview2_free_string(ptr);
            }
        }

        /// <summary>
        /// Instantiate a component with error checking.
        /// </summary>
        public static IntPtr InstantiateOrThrow(string componentPath, bool inheritStdio = true)
        {
            var handle = wasmtime_preview2_instantiate_component(
                componentPath, inheritStdio, out var errorPtr);

            if (handle == IntPtr.Zero)
            {
                var error = ReadAndFreeString(errorPtr);
                throw new WasmtimeException(error ?? "failed to instantiate component");
            }

            return handle;
        }

        /// <summary>
        /// Call an exported function with error checking.
        /// </summary>
        public static string CallFuncOrThrow(IntPtr handle, string funcName, string argsJson = "[]")
        {
            var rc = wasmtime_preview2_call_func(
                handle, funcName, argsJson, out var resultPtr, out var errorPtr);

            if (rc != 0)
            {
                var error = ReadAndFreeString(errorPtr);
                throw new WasmtimeException(error ?? "failed to call function");
            }

            return ReadAndFreeString(resultPtr);
        }
    }
}

#endif
