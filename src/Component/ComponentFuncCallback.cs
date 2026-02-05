#if WASMTIME_DEV

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Wasmtime.Component
{
    /// <summary>
    /// Represents a callback for a host-defined component function.
    /// </summary>
    /// <param name="args">The arguments passed to the function.</param>
    /// <param name="results">The results to be returned from the function. Must be populated by the callback.</param>
    public delegate void ComponentFuncCallback(IReadOnlyList<ComponentVal> args, IList<ComponentVal> results);

    /// <summary>
    /// Provides native callback support for component model host functions.
    /// </summary>
    internal static class ComponentFuncCallbackHelper
    {
        /// <summary>
        /// Native callback delegate matching wasmtime_component_func_callback_t.
        /// Returns IntPtr.Zero on success, or a wasmtime_error_t* on failure.
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate IntPtr NativeComponentFuncCallback(
            IntPtr data,
            IntPtr context,
            IntPtr func_type,
            IntPtr args,
            nuint nargs,
            IntPtr results,
            nuint nresults);

        /// <summary>
        /// Finalizer delegate for cleaning up GCHandle data.
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void Finalizer(IntPtr data);

        /// <summary>
        /// Static finalizer instance that frees GCHandle.
        /// </summary>
        internal static readonly Finalizer FinalizerInstance = (p) => GCHandle.FromIntPtr(p).Free();

        /// <summary>
        /// Creates a native callback and GCHandle pair from a managed callback.
        /// </summary>
        /// <param name="callback">The managed callback.</param>
        /// <returns>The native callback delegate and the GCHandle IntPtr for data parameter.</returns>
        internal static (NativeComponentFuncCallback nativeCallback, IntPtr data) CreateNativeCallback(ComponentFuncCallback callback)
        {
            // Bundle the managed callback and the native delegate together so the native delegate is kept alive
            var callbackBundle = new CallbackBundle(callback);

            NativeComponentFuncCallback nativeCallback = callbackBundle.Invoke;
            // Store the native delegate in the bundle to prevent it from being GC'd
            callbackBundle.NativeDelegate = nativeCallback;

            var handle = GCHandle.Alloc(callbackBundle);
            return (nativeCallback, GCHandle.ToIntPtr(handle));
        }

        private class CallbackBundle
        {
            public ComponentFuncCallback ManagedCallback { get; }
            public NativeComponentFuncCallback? NativeDelegate { get; set; }

            public CallbackBundle(ComponentFuncCallback callback)
            {
                ManagedCallback = callback;
            }

            public IntPtr Invoke(
                IntPtr data,
                IntPtr context,
                IntPtr func_type,
                IntPtr argsPtr,
                nuint nargs,
                IntPtr resultsPtr,
                nuint nresults)
            {
                try
                {
                    int argCount = (int)nargs;
                    int resultCount = (int)nresults;

                    // Convert native args to managed
                    var managedArgs = new ComponentVal[argCount];
                    unsafe
                    {
                        var args = (ComponentVal.NativeComponentVal*)argsPtr;
                        for (int i = 0; i < argCount; i++)
                        {
                            managedArgs[i] = ComponentVal.FromNative(ref args[i]);
                        }
                    }

                    // Create results list
                    var managedResults = new ComponentVal[resultCount];

                    // Invoke managed callback
                    ManagedCallback(managedArgs, managedResults);

                    // Convert managed results to native
                    unsafe
                    {
                        var results = (ComponentVal.NativeComponentVal*)resultsPtr;
                        for (int i = 0; i < resultCount; i++)
                        {
                            if (managedResults[i] == null)
                            {
                                throw new InvalidOperationException(
                                    $"Component function callback did not set result at index {i}.");
                            }
                            managedResults[i].ToNative(ref results[i]);
                        }
                    }

                    return IntPtr.Zero;
                }
                catch (Exception ex)
                {
                    return HandleCallbackException(ex);
                }
            }
        }

        private static unsafe IntPtr HandleCallbackException(Exception ex)
        {
            try
            {
                Function.CallbackErrorCause = ex is WasmtimeException wasmtimeException
                    ? wasmtimeException.InnerException
                    : ex;

                var bytes = Encoding.UTF8.GetBytes(ex.Message);
                fixed (byte* ptr = bytes)
                {
                    return Native.wasmtime_trap_new(ptr, (nuint)bytes.Length);
                }
            }
            catch (Exception separateException)
            {
                Environment.FailFast(separateException.Message, separateException);
                throw;
            }
        }

        private static class Native
        {
            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static unsafe extern IntPtr wasmtime_trap_new(byte* bytes, nuint len);
        }
    }
}

#endif
