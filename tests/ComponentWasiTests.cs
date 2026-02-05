#if WASMTIME_DEV

using System;
using System.IO;
using FluentAssertions;
using Wasmtime.Component;
using Xunit;

namespace Wasmtime.Tests
{
    /// <summary>
    /// Tests for WASI P2 integration with component model.
    /// </summary>
    public class ComponentWasiTests : IDisposable
    {
        private Engine Engine { get; }

        public ComponentWasiTests()
        {
            Engine = new Engine();
        }

        public void Dispose()
        {
            Engine.Dispose();
        }

        [Fact]
        public void ItCanAddWasiP2ToLinker()
        {
            using var linker = new ComponentLinker(Engine);

            // Should not throw
            linker.AddWasiP2();
        }

        [Fact]
        public void ItCanConfigureWasiAndAddWasiP2()
        {
            using var linker = new ComponentLinker(Engine);
            linker.AddWasiP2();

            using var store = new Store(Engine);
            store.SetWasiConfiguration(new WasiConfiguration()
                .WithInheritedStandardInput()
                .WithInheritedStandardOutput()
                .WithInheritedStandardError());

            // Store and linker are configured; further testing requires a WASI component
        }

        [Fact]
        public void ItCanCombineHostFuncsWithWasiP2()
        {
            using var component = Wasmtime.Component.Component.FromFile(
                Engine, Path.Combine("Modules", "ComponentWithImport.wasm"));
            using var linker = new ComponentLinker(Engine);

            // Define host function
            using (var root = linker.Root())
            {
                root.AddFunc("add-one", (args, results) =>
                {
                    var a = args[0].GetS32();
                    results[0] = ComponentVal.FromS32(a + 1);
                });
            }

            // Also add WASI P2 (should not interfere with host funcs)
            linker.AddWasiP2();

            using var store = new Store(Engine);
            store.SetWasiConfiguration(new WasiConfiguration()
                .WithInheritedStandardInput()
                .WithInheritedStandardOutput()
                .WithInheritedStandardError());

            var instance = linker.Instantiate(store, component);
            var func = instance.GetFunc(store, "add-two")!;
            var result = func.Invoke(store, new[] { ComponentVal.FromS32(10) });

            result[0].GetS32().Should().Be(12);
        }
    }
}

#endif
