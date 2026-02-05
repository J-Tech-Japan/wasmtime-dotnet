#if WASMTIME_DEV

using System;
using System.Runtime.InteropServices;

namespace Wasmtime.Component
{
    /// <summary>
    /// Represents a component model function that can be called.
    /// </summary>
    public class ComponentFunc
    {
        /// <summary>
        /// Calls the function with the given arguments.
        /// After processing the results, <see cref="PostReturn"/> must be called.
        /// For most cases, use <see cref="Invoke(Store, ComponentVal[], int)"/> instead which handles this automatically.
        /// </summary>
        /// <param name="store">The store this function belongs to.</param>
        /// <param name="args">The arguments to pass to the function.</param>
        /// <param name="resultCount">The number of results expected from the function.</param>
        /// <returns>The results of the function call.</returns>
        public ComponentVal[] Call(Store store, ComponentVal[] args, int resultCount)
        {
            if (store is null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            var nativeArgs = new ComponentVal.NativeComponentVal[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                args[i].ToNative(ref nativeArgs[i]);
            }

            var nativeResults = new ComponentVal.NativeComponentVal[resultCount];

            unsafe
            {
                fixed (ComponentVal.NativeComponentVal* argsPtr = nativeArgs)
                fixed (ComponentVal.NativeComponentVal* resultsPtr = nativeResults)
                {
                    var error = Native.wasmtime_component_func_call(
                        func,
                        store.Context.handle,
                        argsPtr,
                        (nuint)args.Length,
                        resultsPtr,
                        (nuint)resultCount);
                    GC.KeepAlive(store);

                    if (error != IntPtr.Zero)
                    {
                        throw WasmtimeException.FromOwnedError(error);
                    }
                }
            }

            var results = new ComponentVal[resultCount];
            for (int i = 0; i < resultCount; i++)
            {
                results[i] = ComponentVal.FromNative(ref nativeResults[i]);
            }

            return results;
        }

        /// <summary>
        /// Performs the post-return cleanup after a <see cref="Call"/>.
        /// This must be called after processing the results of a <see cref="Call"/>.
        /// </summary>
        /// <param name="store">The store this function belongs to.</param>
        public void PostReturn(Store store)
        {
            if (store is null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            var error = Native.wasmtime_component_func_post_return(
                func,
                store.Context.handle);
            GC.KeepAlive(store);

            if (error != IntPtr.Zero)
            {
                throw WasmtimeException.FromOwnedError(error);
            }
        }

        /// <summary>
        /// Calls the function with the given arguments, automatically handling post-return.
        /// This is the preferred way to call a component function.
        /// </summary>
        /// <param name="store">The store this function belongs to.</param>
        /// <param name="args">The arguments to pass to the function.</param>
        /// <param name="resultCount">The number of results expected (default 1).</param>
        /// <returns>The results of the function call.</returns>
        public ComponentVal[] Invoke(Store store, ComponentVal[] args, int resultCount = 1)
        {
            var results = Call(store, args, resultCount);
            try
            {
                PostReturn(store);
            }
            catch
            {
                foreach (var result in results)
                {
                    result.Dispose();
                }
                throw;
            }
            return results;
        }

        /// <summary>
        /// Calls the function with no arguments, automatically handling post-return.
        /// </summary>
        /// <param name="store">The store this function belongs to.</param>
        /// <param name="resultCount">The number of results expected (default 1).</param>
        /// <returns>The results of the function call.</returns>
        public ComponentVal[] Invoke(Store store, int resultCount = 1)
        {
            return Invoke(store, Array.Empty<ComponentVal>(), resultCount);
        }

        /// <summary>
        /// Calls the function with no arguments and no results, automatically handling post-return.
        /// </summary>
        /// <param name="store">The store this function belongs to.</param>
        /// <param name="args">The arguments to pass to the function.</param>
        public void InvokeVoid(Store store, ComponentVal[] args)
        {
            var results = Call(store, args, 0);
            PostReturn(store);
        }

        internal ComponentFunc(Store store, NativeComponentFunc func)
        {
            this.store = store;
            this.func = func;
        }

        /// <summary>
        /// Native representation of a component function (24 bytes).
        /// The C struct uses an anonymous inner struct which adds padding:
        ///   struct { uint64_t store_id; uint32_t __private1; }; // 16 bytes (12 + 4 padding)
        ///   uint32_t __private2;                                // at offset 16
        /// Total: 24 bytes (20 + 4 trailing padding for 8-byte alignment).
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Size = 24)]
        internal struct NativeComponentFunc
        {
            [FieldOffset(0)] public ulong store_id;
            [FieldOffset(8)] public uint __private1;
            [FieldOffset(16)] public uint __private2;
        }

        private static class Native
        {
            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern unsafe IntPtr wasmtime_component_func_call(
                in NativeComponentFunc func,
                IntPtr context,
                ComponentVal.NativeComponentVal* args,
                nuint args_size,
                ComponentVal.NativeComponentVal* results,
                nuint results_size);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr wasmtime_component_func_post_return(
                in NativeComponentFunc func,
                IntPtr context);
        }

        private readonly Store store;
        private readonly NativeComponentFunc func;
    }
}

#endif
