using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Wasmtime
{
    /// <summary>
    /// Represents the Wasmtime linker that can be used to define imports
    /// and instantiate WebAssembly modules.
    /// </summary>
    public partial class Linker : IDisposable
    {
        private const int StackallocThreshold = 256;
        private const int WasiPreview2StdinHandle = 1;
        private const int WasiPreview2StdoutHandle = 2;
        private const int WasiPreview2StderrHandle = 3;
        private const int WasiPreview2PollableHandle = 1;
        private static readonly string[] WasiPreview2Versions = new[] { "0.2.6", "0.2.0" };
        private static readonly ConcurrentDictionary<string, int> WasiTraceCounts = new();

        /// <summary>
        /// Constructs a new linker from the given engine.
        /// </summary>
        /// <param name="engine">The Wasmtime engine to use for the linker.</param>
        public Linker(Engine engine)
        {
            if (engine is null)
            {
                throw new ArgumentNullException(nameof(engine));
            }

            handle = new Handle(Native.wasmtime_linker_new(engine.NativeHandle));
        }

        /// <summary>
        /// Configures whether or not the linker allows later definitions to shadow previous definitions.
        /// </summary>
        public bool AllowShadowing
        {
            set
            {
                Native.wasmtime_linker_allow_shadowing(handle, value);
            }
        }

        private void Define<T>(string module, string name, T item)
            where T : IExternal
        {
            if (module is null)
            {
                throw new ArgumentNullException(nameof(module));
            }

            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            var store = item.Store;
            if (store is null)
            {
                throw new ArgumentException($"The item is not associated with a store.");
            }

            var ext = item.AsExtern();

            using var nameBytes = name.ToUTF8(stackalloc byte[Math.Min(64, name.Length * 2)]);
            using var moduleBytes = module.ToUTF8(stackalloc byte[Math.Min(64, module.Length * 2)]);

            unsafe
            {
                fixed (byte* modulePtr = moduleBytes.Span, namePtr = nameBytes.Span)
                {
                    var error = Native.wasmtime_linker_define(handle, store.Context.handle, modulePtr, (UIntPtr)moduleBytes.Length, namePtr, (UIntPtr)nameBytes.Length, ext);
                    if (error != IntPtr.Zero)
                    {
                        throw WasmtimeException.FromOwnedError(error);
                    }
                }
            }

            GC.KeepAlive(store);
        }

        /// <summary>
        /// Defines an item in the linker.
        /// </summary>
        /// <param name="module">The module name of the item.</param>
        /// <param name="name">The name of the item.</param>
        /// <param name="function">The item being defined</param>
        public void Define(string module, string name, Function function)
        {
            Define<Function>(module, name, function);
        }

        /// <summary>
        /// Defines an item in the linker.
        /// </summary>
        /// <param name="module">The module name of the item.</param>
        /// <param name="name">The name of the item.</param>
        /// <param name="global">The item being defined</param>
        public void Define(string module, string name, Global global)
        {
            Define<Global>(module, name, global);
        }

        /// <summary>
        /// Defines an item in the linker.
        /// </summary>
        /// <param name="module">The module name of the item.</param>
        /// <param name="name">The name of the item.</param>
        /// <param name="global">The item being defined</param>
        public void Define<T>(string module, string name, Global.Accessor<T> global)
        {
            Define<Global.Accessor<T>>(module, name, global);
        }

        /// <summary>
        /// Defines an item in the linker.
        /// </summary>
        /// <param name="module">The module name of the item.</param>
        /// <param name="name">The name of the item.</param>
        /// <param name="memory">The item being defined</param>
        public void Define(string module, string name, Memory memory)
        {
            Define<Memory>(module, name, memory);
        }

        /// <summary>
        /// Defines an item in the linker.
        /// </summary>
        /// <param name="module">The module name of the item.</param>
        /// <param name="name">The name of the item.</param>
        /// <param name="table">The item being defined</param>
        public void Define(string module, string name, Table table)
        {
            Define<Table>(module, name, table);
        }

        /// <summary>
        /// Defines WASI functions in the linker.
        /// </summary>
        /// <remarks>
        /// When WASI functions are defined in the linker, a store must be configured with a WASI
        /// configuration.
        /// </remarks>
        public void DefineWasi()
        {
            var error = Native.wasmtime_linker_define_wasi(handle);
            if (error != IntPtr.Zero)
            {
                throw WasmtimeException.FromOwnedError(error);
            }
        }

        /// <summary>
        /// Defines preview1 adapter stubs expected by some extracted core modules.
        /// </summary>
        /// <remarks>
        /// These stubs return EBADF for invalid file descriptors.
        /// </remarks>
        public void DefineWasiPreview1AdapterStubs()
        {
            DefineFunction(
                "wasi_snapshot_preview1",
                "adapter_close_badfd",
                (Caller caller, int fd) => 8);

            DefineFunction(
                "wasi_snapshot_preview1",
                "adapter_open_badfd",
                (Caller caller, int fd) => 8);
        }

        /// <summary>
        /// Defines common WASI preview2 resource-drop stubs as no-ops.
        /// </summary>
        /// <remarks>
        /// These are useful when running extracted core modules from components.
        /// </remarks>
        public void DefineWasiPreview2ResourceDropStubs()
        {
            foreach (var version in WasiPreview2Versions)
            {
                DefineResourceDropStub(GetVersionedModule("wasi:io/error", version), "error");
                DefineResourceDropStub(GetVersionedModule("wasi:io/poll", version), "pollable");
                DefineResourceDropStub(GetVersionedModule("wasi:io/streams", version), "input-stream");
                DefineResourceDropStub(GetVersionedModule("wasi:io/streams", version), "output-stream");
                DefineResourceDropStub(GetVersionedModule("wasi:cli/terminal-input", version), "terminal-input");
                DefineResourceDropStub(GetVersionedModule("wasi:cli/terminal-output", version), "terminal-output");
                DefineResourceDropStub(GetVersionedModule("wasi:filesystem/types", version), "descriptor");
                DefineResourceDropStub(GetVersionedModule("wasi:filesystem/types", version), "directory-entry-stream");
                DefineResourceDropStub(GetVersionedModule("wasi:sockets/udp", version), "udp-socket");
                DefineResourceDropStub(GetVersionedModule("wasi:sockets/udp", version), "incoming-datagram-stream");
                DefineResourceDropStub(GetVersionedModule("wasi:sockets/udp", version), "outgoing-datagram-stream");
                DefineResourceDropStub(GetVersionedModule("wasi:sockets/tcp", version), "tcp-socket");
            }
        }

        /// <summary>
        /// Defines preview2 terminal getter stubs that return "none".
        /// </summary>
        /// <remarks>
        /// Extracted core modules lower these imports to an out-pointer for an optional resource handle.
        /// Returning "none" avoids requiring terminal support while still allowing execution.
        /// </remarks>
        public void DefineWasiPreview2TerminalStubs()
        {
            foreach (var version in WasiPreview2Versions)
            {
                DefineOptionalResourceGetterStub(GetVersionedModule("wasi:cli/terminal-stdin", version), "get-terminal-stdin");
                DefineOptionalResourceGetterStub(GetVersionedModule("wasi:cli/terminal-stdout", version), "get-terminal-stdout");
                DefineOptionalResourceGetterStub(GetVersionedModule("wasi:cli/terminal-stderr", version), "get-terminal-stderr");
            }
        }

        /// <summary>
        /// Defines preview2 CLI/environment stubs used by extracted core modules.
        /// </summary>
        public void DefineWasiPreview2CliStubs()
        {
            foreach (var version in WasiPreview2Versions)
            {
                DefineEmptyListGetterStub(GetVersionedModule("wasi:cli/environment", version), "get-environment");
                DefineFunction(
                    GetVersionedModule("wasi:cli/exit", version),
                    "exit",
                    (Caller caller, int exitStateAddress) =>
                    {
                        throw new InvalidOperationException(
                            "The WebAssembly component requested process exit via WASI preview2.");
                    });

                DefineFunction(GetVersionedModule("wasi:cli/stdin", version), "get-stdin", (Caller caller) => WasiPreview2StdinHandle);
                DefineFunction(GetVersionedModule("wasi:cli/stdout", version), "get-stdout", (Caller caller) => WasiPreview2StdoutHandle);
                DefineFunction(GetVersionedModule("wasi:cli/stderr", version), "get-stderr", (Caller caller) => WasiPreview2StderrHandle);
            }
        }

        /// <summary>
        /// Defines preview2 clock stubs backed by host time.
        /// </summary>
        public void DefineWasiPreview2ClockStubs()
        {
            foreach (var version in WasiPreview2Versions)
            {
                DefineFunction(
                    GetVersionedModule("wasi:clocks/monotonic-clock", version),
                    "now",
                    (Caller caller) =>
                        (long)(Stopwatch.GetTimestamp() * (1_000_000_000d / Stopwatch.Frequency)));

                DefineFunction(
                    GetVersionedModule("wasi:clocks/monotonic-clock", version),
                    "subscribe-instant",
                    (Caller caller, long when) => WasiPreview2PollableHandle);

                DefineFunction(
                    GetVersionedModule("wasi:clocks/monotonic-clock", version),
                    "subscribe-duration",
                    (Caller caller, long duration) => WasiPreview2PollableHandle);

                DefineFunction(
                    GetVersionedModule("wasi:clocks/wall-clock", version),
                    "now",
                    (Caller caller, int resultAddress) =>
                    {
                        var now = DateTimeOffset.UtcNow;
                        var memory = GetCallerMemory(caller);
                        var seconds = now.ToUnixTimeSeconds();
                        var nanoseconds = (int)((now.Ticks % TimeSpan.TicksPerSecond) * 100);
                        memory.WriteInt64(resultAddress, seconds);
                        memory.WriteInt32(resultAddress + 8, nanoseconds);
                    });
            }
        }

        /// <summary>
        /// Defines preview2 stream and poll stubs that provide inert handles and empty reads/writes.
        /// </summary>
        public void DefineWasiPreview2StreamStubs()
        {
            foreach (var version in WasiPreview2Versions)
            {
                DefineFunction(
                    GetVersionedModule("wasi:io/poll", version),
                    "[method]pollable.block",
                    (Caller caller, int pollableHandle) => { });

                DefineFunction(
                    GetVersionedModule("wasi:io/poll", version),
                    "poll",
                    (Caller caller, int pollablesAddress, int pollablesLength, int resultAddress) =>
                    {
                        var memory = GetCallerMemory(caller);
                        if (pollablesLength <= 0)
                        {
                            WriteListResult(memory, resultAddress, 0, 0);
                            return;
                        }

                        int pointer = AllocateGuestBuffer(caller, pollablesLength * sizeof(int));
                        for (int index = 0; index < pollablesLength; index++)
                        {
                            memory.WriteInt32(pointer + (index * sizeof(int)), index);
                        }

                        WriteListResult(memory, resultAddress, pointer, pollablesLength);
                    });

                DefineFunction(
                    GetVersionedModule("wasi:io/streams", version),
                    "[method]input-stream.blocking-read",
                    (Caller caller, int streamHandle, long maxBytes, int resultAddress) =>
                    {
                        ZeroGuestMemory(caller, resultAddress, 16);
                    });

                DefineFunction(
                    GetVersionedModule("wasi:io/streams", version),
                    "[method]input-stream.subscribe",
                    (Caller caller, int streamHandle) => WasiPreview2PollableHandle);

                DefineFunction(
                    GetVersionedModule("wasi:io/streams", version),
                    "[method]output-stream.check-write",
                    (Caller caller, int streamHandle, int resultAddress) =>
                    {
                        ZeroGuestMemory(caller, resultAddress, 16);
                    });

                DefineFunction(
                    GetVersionedModule("wasi:io/streams", version),
                    "[method]output-stream.write",
                    (Caller caller, int streamHandle, int bufferAddress, int bufferLength, int resultAddress) =>
                    {
                        ZeroGuestMemory(caller, resultAddress, 16);
                    });

                DefineFunction(
                    GetVersionedModule("wasi:io/streams", version),
                    "[method]output-stream.blocking-flush",
                    (Caller caller, int streamHandle, int resultAddress) =>
                    {
                        ZeroGuestMemory(caller, resultAddress, 16);
                    });

                DefineFunction(
                    GetVersionedModule("wasi:io/streams", version),
                    "[method]output-stream.subscribe",
                    (Caller caller, int streamHandle) => WasiPreview2PollableHandle);
            }
        }

        /// <summary>
        /// Defines preview2 filesystem stubs for the extracted .NET core module.
        /// </summary>
        public void DefineWasiPreview2FilesystemStubs()
        {
            foreach (var version in WasiPreview2Versions)
            {
                DefineEmptyListGetterStub(GetVersionedModule("wasi:filesystem/preopens", version), "get-directories");

                DefineFunction(
                    GetVersionedModule("wasi:filesystem/types", version),
                    "[method]descriptor.read-via-stream",
                    (Caller caller, int descriptorHandle, long offset, int resultAddress) =>
                    {
                        ZeroGuestMemory(caller, resultAddress, 16);
                    });
                DefineFunction(
                    GetVersionedModule("wasi:filesystem/types", version),
                    "[method]descriptor.write-via-stream",
                    (Caller caller, int descriptorHandle, long offset, int resultAddress) =>
                    {
                        ZeroGuestMemory(caller, resultAddress, 16);
                    });
                DefineFunction(
                    GetVersionedModule("wasi:filesystem/types", version),
                    "[method]descriptor.append-via-stream",
                    (Caller caller, int descriptorHandle, int resultAddress) =>
                    {
                        ZeroGuestMemory(caller, resultAddress, 16);
                    });
                DefineFunction(
                    GetVersionedModule("wasi:filesystem/types", version),
                    "[method]descriptor.advise",
                    (Caller caller, int descriptorHandle, long offset, long length, int advice, int resultAddress) =>
                    {
                        ZeroGuestMemory(caller, resultAddress, 16);
                    });
                DefineFunction(
                    GetVersionedModule("wasi:filesystem/types", version),
                    "[method]descriptor.get-flags",
                    (Caller caller, int descriptorHandle, int resultAddress) =>
                    {
                        ZeroGuestMemory(caller, resultAddress, 16);
                    });
                DefineFunction(
                    GetVersionedModule("wasi:filesystem/types", version),
                    "[method]descriptor.set-size",
                    (Caller caller, int descriptorHandle, long size, int resultAddress) =>
                    {
                        ZeroGuestMemory(caller, resultAddress, 16);
                    });
                DefineFunction(
                    GetVersionedModule("wasi:filesystem/types", version),
                    "[method]descriptor.read",
                    (Caller caller, int descriptorHandle, long length, long offset, int resultAddress) =>
                    {
                        ZeroGuestMemory(caller, resultAddress, 16);
                    });
                DefineFunction(
                    GetVersionedModule("wasi:filesystem/types", version),
                    "[method]descriptor.read-directory",
                    (Caller caller, int descriptorHandle, int resultAddress) =>
                    {
                        ZeroGuestMemory(caller, resultAddress, 16);
                    });
                DefineFunction(
                    GetVersionedModule("wasi:filesystem/types", version),
                    "[method]descriptor.stat",
                    (Caller caller, int descriptorHandle, int resultAddress) =>
                    {
                        ZeroGuestMemory(caller, resultAddress, 16);
                    });
                DefineFunction(
                    GetVersionedModule("wasi:filesystem/types", version),
                    "[method]descriptor.stat-at",
                    (Caller caller, int descriptorHandle, int pathAddress, int pathLength, int flags, int resultAddress) =>
                    {
                        ZeroGuestMemory(caller, resultAddress, 16);
                    });
                DefineFunction(
                    GetVersionedModule("wasi:filesystem/types", version),
                    "[method]descriptor.open-at",
                    (Caller caller, int descriptorHandle, int pathAddress, int pathLength, int openFlags, int descriptorFlags, int pathFlags, int resultAddress) =>
                    {
                        ZeroGuestMemory(caller, resultAddress, 16);
                    });
                DefineFunction(
                    GetVersionedModule("wasi:filesystem/types", version),
                    "[method]descriptor.readlink-at",
                    (Caller caller, int descriptorHandle, int pathAddress, int pathLength, int resultAddress) =>
                    {
                        ZeroGuestMemory(caller, resultAddress, 16);
                    });
                DefineFunction(
                    GetVersionedModule("wasi:filesystem/types", version),
                    "[method]descriptor.unlink-file-at",
                    (Caller caller, int descriptorHandle, int pathAddress, int pathLength, int resultAddress) =>
                    {
                        ZeroGuestMemory(caller, resultAddress, 16);
                    });
                DefineFunction(
                    GetVersionedModule("wasi:filesystem/types", version),
                    "[method]descriptor.metadata-hash",
                    (Caller caller, int descriptorHandle, int resultAddress) =>
                    {
                        ZeroGuestMemory(caller, resultAddress, 16);
                    });
                DefineFunction(
                    GetVersionedModule("wasi:filesystem/types", version),
                    "[method]descriptor.metadata-hash-at",
                    (Caller caller, int descriptorHandle, int pathAddress, int pathLength, int flags, int resultAddress) =>
                    {
                        ZeroGuestMemory(caller, resultAddress, 16);
                    });
                DefineFunction(
                    GetVersionedModule("wasi:filesystem/types", version),
                    "[method]directory-entry-stream.read-directory-entry",
                    (Caller caller, int streamHandle, int resultAddress) =>
                    {
                        ZeroGuestMemory(caller, resultAddress, 16);
                    });
            }
        }

        /// <summary>
        /// Defines preview2 random stubs backed by host randomness.
        /// </summary>
        public void DefineWasiPreview2RandomStubs()
        {
            foreach (var version in WasiPreview2Versions)
            {
                DefineFunction(
                    GetVersionedModule("wasi:random/random", version),
                    "get-random-bytes",
                    (Caller caller, long byteCount, int resultAddress) =>
                    {
                        if (byteCount < 0 || byteCount > int.MaxValue)
                        {
                            throw new ArgumentOutOfRangeException(nameof(byteCount));
                        }

                        var memory = GetCallerMemory(caller);
                        var length = (int)byteCount;
                        if (length == 0)
                        {
                            WriteListResult(memory, resultAddress, 0, 0);
                            return;
                        }

                        var pointer = AllocateGuestBuffer(caller, length);
                        var randomBytes = new byte[length];
                        using var randomNumberGenerator = RandomNumberGenerator.Create();
                        randomNumberGenerator.GetBytes(randomBytes);
                        randomBytes.CopyTo(memory.GetSpan(pointer, length));
                        WriteListResult(memory, resultAddress, pointer, length);
                    });
            }
        }

        /// <summary>
        /// Defines preview2 socket stubs required by extracted .NET core modules.
        /// </summary>
        public void DefineWasiPreview2SocketStubs()
        {
            foreach (var version in WasiPreview2Versions)
            {
                DefineFunction(
                    GetVersionedModule("wasi:sockets/tcp", version),
                    "[method]tcp-socket.finish-connect",
                    (Caller caller, int socketHandle, int resultAddress) =>
                    {
                        // Canonical ABI lowers result<(input-stream, output-stream), error-code>
                        // to an out-parameter. Zeroed memory represents inert success handles.
                        ZeroGuestMemory(caller, resultAddress, 24);
                    });
            }
        }

        private static string GetVersionedModule(string moduleBase, string version)
        {
            return $"{moduleBase}@{version}";
        }

        private static void TraceDefinedFunctionInvocation(string module, string name)
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("WASMTIME_PREVIEW2_TRACE"), "1", StringComparison.Ordinal))
            {
                return;
            }

            if (!module.StartsWith("wasi:", StringComparison.Ordinal))
            {
                return;
            }

            var key = $"{module}::{name}";
            var count = WasiTraceCounts.AddOrUpdate(key, 1, static (_, current) => current + 1);
            if (count <= 20 || count % 100 == 0)
            {
                Console.Error.WriteLine($"[wasmtime-preview2] {key} x{count}");
            }
        }

        /// <summary>
        /// Defines both preview1 adapter stubs and preview2 resource-drop stubs.
        /// </summary>
        public void DefineWasiPreview2Stubs()
        {
            DefineWasiPreview1AdapterStubs();
            DefineWasiPreview2ResourceDropStubs();
            DefineWasiPreview2TerminalStubs();
            DefineWasiPreview2CliStubs();
            DefineWasiPreview2ClockStubs();
            DefineWasiPreview2StreamStubs();
            DefineWasiPreview2FilesystemStubs();
            DefineWasiPreview2RandomStubs();
            DefineWasiPreview2SocketStubs();
        }

        private void DefineResourceDropStub(string module, string resourceName)
        {
            if (module is null)
            {
                throw new ArgumentNullException(nameof(module));
            }

            if (resourceName is null)
            {
                throw new ArgumentNullException(nameof(resourceName));
            }

            DefineFunction(
                module,
                $"[resource-drop]{resourceName}",
                (Caller caller, int handle) => { });
        }

        private void DefineOptionalResourceGetterStub(string module, string name)
        {
            if (module is null)
            {
                throw new ArgumentNullException(nameof(module));
            }

            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            DefineFunction(
                module,
                name,
                (Caller caller, int resultAddress) =>
                {
                    var memory = caller.GetMemory("memory");
                    if (memory is null)
                    {
                        throw new InvalidOperationException("The caller does not export a memory named 'memory'.");
                    }

                    memory.WriteByte(resultAddress, 0);
                    memory.WriteInt32(resultAddress + 4, 0);
                });
        }

        private void DefineEmptyListGetterStub(string module, string name)
        {
            if (module is null)
            {
                throw new ArgumentNullException(nameof(module));
            }

            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            DefineFunction(
                module,
                name,
                (Caller caller, int resultAddress) =>
                {
                    var memory = GetCallerMemory(caller);
                    WriteListResult(memory, resultAddress, 0, 0);
                });
        }

        private static Memory GetCallerMemory(Caller caller)
        {
            var memory = caller.GetMemory("memory");
            if (memory is null)
            {
                throw new InvalidOperationException("The caller does not export a memory named 'memory'.");
            }

            return memory;
        }

        private static void ZeroGuestMemory(Caller caller, int address, int bytes)
        {
            var memory = GetCallerMemory(caller);
            memory.GetSpan(address, bytes).Clear();
        }

        private static void WriteListResult(Memory memory, int resultAddress, int pointer, int length)
        {
            memory.WriteInt32(resultAddress, pointer);
            memory.WriteInt32(resultAddress + 4, length);
        }

        private static int AllocateGuestBuffer(Caller caller, int size)
        {
            if (size <= 0)
            {
                return 0;
            }

            var alloc = caller.GetFunction("alloc")?.WrapFunc<int, int>();
            if (alloc is null)
            {
                throw new InvalidOperationException("The caller does not export an 'alloc' function.");
            }

            var pointer = alloc(size);
            if (pointer == 0)
            {
                throw new InvalidOperationException("The guest allocator returned a null pointer.");
            }

            return pointer;
        }

        /// <summary>
        /// Defines an instance with the specified name in the linker.
        /// </summary>
        /// <param name="store">The store that owns the instance.</param>
        /// <param name="name">The name of the instance to define.</param>
        /// <param name="instance">The instance to define.</param>
        public void DefineInstance(Store store, string name, Instance instance)
        {
            if (store is null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (instance is null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            unsafe
            {
                var nameBytes = Encoding.UTF8.GetBytes(name);
                fixed (byte* namePtr = nameBytes)
                {
                    var error = Native.wasmtime_linker_define_instance(handle, store.Context.handle, namePtr, (UIntPtr)nameBytes.Length, instance.instance);
                    GC.KeepAlive(store);

                    if (error != IntPtr.Zero)
                    {
                        throw WasmtimeException.FromOwnedError(error);
                    }
                }
            }
        }

        /// <summary>
        /// Instantiates a module with imports from items defined in the linker.
        /// </summary>
        /// <param name="store">The store to instantiate in.</param>
        /// <param name="module">The module to instantiate.</param>
        /// <returns>Returns the new instance.</returns>
        public Instance Instantiate(Store store, Module module)
        {
            if (store is null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (module is null)
            {
                throw new ArgumentNullException(nameof(module));
            }

            var error = Native.wasmtime_linker_instantiate(handle, store.Context.handle, module.NativeHandle, out var instance, out var trap);
            GC.KeepAlive(store);

            if (error != IntPtr.Zero)
            {
                throw WasmtimeException.FromOwnedError(error);
            }

            if (trap != IntPtr.Zero)
            {
                throw TrapException.FromOwnedTrap(trap);
            }

            return new Instance(store, instance);
        }

        /// <summary>
        /// Defines automatic instantiations of a module in this linker.
        /// </summary>
        /// <param name="store">The store to instantiate in.</param>
        /// <param name="module">The module to automatically instantiate.</param>
        public void DefineModule(Store store, Module module)
        {
            if (store is null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (module is null)
            {
                throw new ArgumentNullException(nameof(module));
            }

            unsafe
            {
                var nameBytes = Encoding.UTF8.GetBytes(module.Name);
                fixed (byte* namePtr = nameBytes)
                {
                    var error = Native.wasmtime_linker_module(handle, store.Context.handle, namePtr, (UIntPtr)nameBytes.Length, module.NativeHandle);
                    GC.KeepAlive(store);

                    if (error != IntPtr.Zero)
                    {
                        throw WasmtimeException.FromOwnedError(error);
                    }
                }
            }
        }

        /// <summary>
        /// Gets the "default" function export for a module with the given name defined in the linker.
        /// </summary>
        /// <param name="store">The store for the function.</param>
        /// <param name="name">Tha name of the module to get the default function export.</param>
        /// <returns></returns>
        public Function GetDefaultFunction(Store store, string name)
        {
            if (store is null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            var context = store.Context;

            unsafe
            {
                var nameBytes = Encoding.UTF8.GetBytes(name);
                fixed (byte* namePtr = nameBytes)
                {
                    var error = Native.wasmtime_linker_get_default(handle, context.handle, namePtr, (UIntPtr)nameBytes.Length, out var func);
                    GC.KeepAlive(store);

                    if (error != IntPtr.Zero)
                    {
                        throw WasmtimeException.FromOwnedError(error);
                    }

                    return store.GetCachedExtern(func);
                }
            }
        }

        /// <summary>
        /// Gets an exported function from the linker.
        /// </summary>
        /// <param name="store">The store of the function.</param>
        /// <param name="module">The module of the exported function.</param>
        /// <param name="name">The name of the exported function.</param>
        /// <returns>Returns the function if a function of that name was exported or null if not.</returns>
        public Function? GetFunction(Store store, string module, string name)
        {
            if (store is null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            var context = store.Context;
            if (!TryGetExtern(context, module, name, out var ext) || ext.kind != ExternKind.Func)
            {
                return null;
            }

            GC.KeepAlive(store);

            return store.GetCachedExtern(ext.of.func);
        }

        /// <summary>
        /// Gets an exported table from the linker.
        /// </summary>
        /// <param name="store">The store of the table.</param>
        /// <param name="module">The module of the exported table.</param>
        /// <param name="name">The name of the exported table.</param>
        /// <returns>Returns the table if a table of that name was exported or null if not.</returns>
        public Table? GetTable(Store store, string module, string name)
        {
            if (store is null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            var context = store.Context;
            if (!TryGetExtern(context, module, name, out var ext) || ext.kind != ExternKind.Table)
            {
                return null;
            }

            GC.KeepAlive(store);

            return new Table(store, ext.of.table);
        }

        /// <summary>
        /// Gets an exported memory from the linker.
        /// </summary>
        /// <param name="store">The store of the memory.</param>
        /// <param name="module">The module of the exported memory.</param>
        /// <param name="name">The name of the exported memory.</param>
        /// <returns>Returns the memory if a memory of that name was exported or null if not.</returns>
        public Memory? GetMemory(Store store, string module, string name)
        {
            if (store is null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            var context = store.Context;
            if (!TryGetExtern(context, module, name, out var ext) || ext.kind != ExternKind.Memory)
            {
                return null;
            }

            GC.KeepAlive(store);

            return store.GetCachedExtern(ext.of.memory);
        }

        /// <summary>
        /// Gets an exported global from the linker.
        /// </summary>
        /// <param name="store">The store of the global.</param>
        /// <param name="module">The module of the exported global.</param>
        /// <param name="name">The name of the exported global.</param>
        /// <returns>Returns the global if a global of that name was exported or null if not.</returns>
        public Global? GetGlobal(Store store, string module, string name)
        {
            if (store is null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            var context = store.Context;
            if (!TryGetExtern(context, module, name, out var ext) || ext.kind != ExternKind.Global)
            {
                return null;
            }

            GC.KeepAlive(store);

            return new Global(store, ext.of.global);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            handle.Dispose();
        }

        /// <summary>
        /// Defines an function in the linker given an untyped callback.
        /// </summary>
        /// <remarks>Functions defined with this method are store-independent.</remarks>
        /// <param name="module">The module name of the function.</param>
        /// <param name="name">The name of the function.</param>
        /// <param name="callback">The callback for when the function is invoked.</param>
        /// <param name="parameterKinds">The function parameter kinds.</param>
        /// <param name="resultKinds">The function result kinds.</param>
        public void DefineFunction(string module, string name, Function.UntypedCallbackDelegate callback, IReadOnlyList<ValueKind> parameterKinds, IReadOnlyList<ValueKind> resultKinds)
        {
            if (module is null)
            {
                throw new ArgumentNullException(nameof(module));
            }

            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (callback is null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            unsafe
            {
                // Copy the lists to ensure they are not modified.
                parameterKinds = parameterKinds.ToArray();
                resultKinds = resultKinds.ToArray();
                Function.Native.WasmtimeFuncCallback func = (env, callerPtr, args, nargs, results, nresults) =>
                {
                    TraceDefinedFunctionInvocation(module, name);
                    return Function.InvokeUntypedCallback(callback, callerPtr, args, (int)nargs, results, (int)nresults, resultKinds);
                };

                using var nameBytes = name.ToUTF8(stackalloc byte[Math.Min(64, name.Length * 2)]);
                using var moduleBytes = module.ToUTF8(stackalloc byte[Math.Min(64, module.Length * 2)]);

                var funcType = Function.CreateFunctionType(parameterKinds, resultKinds);
                try
                {
                    fixed (byte* modulePtr = moduleBytes.Span, namePtr = nameBytes.Span)
                    {
                        var error = Native.wasmtime_linker_define_func(
                            handle,
                            modulePtr,
                            (nuint)moduleBytes.Length,
                            namePtr,
                            (nuint)nameBytes.Length,
                            funcType,
                            func,
                            GCHandle.ToIntPtr(GCHandle.Alloc(func)),
                            Function.Finalizer
                        );

                        if (error != IntPtr.Zero)
                        {
                            throw WasmtimeException.FromOwnedError(error);
                        }
                    }
                }
                finally
                {
                    Function.Native.wasm_functype_delete(funcType);
                }
            }
        }

        private bool TryGetExtern(StoreContext context, string module, string name, out Extern ext)
        {
            unsafe
            {
                using var moduleBytes = module.ToUTF8(stackalloc byte[Math.Min(64, module.Length * 2)]);
                using var nameBytes = name.ToUTF8(stackalloc byte[Math.Min(64, name.Length * 2)]);

                fixed (byte* modulePtr = moduleBytes.Span, namePtr = nameBytes.Span)
                {
                    return Native.wasmtime_linker_get(handle, context.handle, modulePtr, (UIntPtr)moduleBytes.Length, namePtr, (UIntPtr)nameBytes.Length, out ext);
                }
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
                Native.wasmtime_linker_delete(handle);
                return true;
            }
        }

        internal static class Native
        {
            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr wasmtime_linker_new(Engine.Handle engine);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void wasmtime_linker_delete(IntPtr linker);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void wasmtime_linker_allow_shadowing(Handle linker, [MarshalAs(UnmanagedType.I1)] bool allow);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static unsafe extern IntPtr wasmtime_linker_define(Handle linker, IntPtr context, byte* module, nuint moduleLen, byte* name, nuint nameLen, in Extern item);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr wasmtime_linker_define_wasi(Handle linker);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static unsafe extern IntPtr wasmtime_linker_define_instance(Handle linker, IntPtr context, byte* name, nuint len, in ExternInstance instance);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static unsafe extern IntPtr wasmtime_linker_define_func(Handle linker, byte* module, nuint moduleLen, byte* name, nuint nameLen, IntPtr type, Function.Native.WasmtimeFuncCallback callback, IntPtr data, Function.Native.Finalizer? finalizer);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static unsafe extern IntPtr wasmtime_linker_define_func_unchecked(Handle linker, byte* module, nuint moduleLen, byte* name, nuint nameLen, IntPtr type, Function.Native.WasmtimeFuncUncheckedCallback callback, IntPtr data, Function.Native.Finalizer? finalizer);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr wasmtime_linker_instantiate(Handle linker, IntPtr context, Module.Handle module, out ExternInstance instance, out IntPtr trap);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static unsafe extern IntPtr wasmtime_linker_module(Handle linker, IntPtr context, byte* name, nuint len, Module.Handle module);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static unsafe extern IntPtr wasmtime_linker_get_default(Handle linker, IntPtr context, byte* name, nuint len, out ExternFunc func);

            [DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
            [return: MarshalAs(UnmanagedType.I1)]
            public static unsafe extern bool wasmtime_linker_get(Handle linker, IntPtr context, byte* module, nuint moduleLen, byte* name, nuint nameLen, out Extern func);
        }

        private readonly Handle handle;
    }
}
