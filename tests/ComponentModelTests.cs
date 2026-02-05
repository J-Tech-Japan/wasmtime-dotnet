#if WASMTIME_DEV

using System;
using System.IO;
using FluentAssertions;
using Wasmtime.Component;
using Xunit;

namespace Wasmtime.Tests
{
    public class ComponentModelTests : IDisposable
    {
        private Engine Engine { get; }

        public ComponentModelTests()
        {
            Engine = new Engine();
        }

        public void Dispose()
        {
            Engine.Dispose();
        }

        [Fact]
        public void ItCanLoadComponentFromFile()
        {
            using var component = Wasmtime.Component.Component.FromFile(
                Engine, Path.Combine("Modules", "ComponentAdd.wasm"));

            component.Should().NotBeNull();
        }

        [Fact]
        public void ItCanLoadComponentFromBytes()
        {
            var bytes = File.ReadAllBytes(Path.Combine("Modules", "ComponentAdd.wasm"));
            using var component = Wasmtime.Component.Component.FromBytes(Engine, bytes);

            component.Should().NotBeNull();
        }

        [Fact]
        public void ItThrowsOnInvalidComponent()
        {
            // A core module is not a component
            var act = () => Wasmtime.Component.Component.FromFile(
                Engine, Path.Combine("Modules", "hello.wasm"));

            act.Should().Throw<WasmtimeException>();
        }

        [Fact]
        public void ItThrowsOnMissingFile()
        {
            var act = () => Wasmtime.Component.Component.FromFile(
                Engine, "nonexistent.wasm");

            act.Should().Throw<FileNotFoundException>();
        }

        [Fact]
        public void ItCanInstantiateComponent()
        {
            using var component = Wasmtime.Component.Component.FromFile(
                Engine, Path.Combine("Modules", "ComponentAdd.wasm"));
            using var linker = new ComponentLinker(Engine);
            using var store = new Store(Engine);

            var instance = linker.Instantiate(store, component);

            instance.Should().NotBeNull();
        }

        [Fact]
        public void ItCanGetExportedFunction()
        {
            using var component = Wasmtime.Component.Component.FromFile(
                Engine, Path.Combine("Modules", "ComponentAdd.wasm"));
            using var linker = new ComponentLinker(Engine);
            using var store = new Store(Engine);

            var instance = linker.Instantiate(store, component);
            var func = instance.GetFunc(store, "add");

            func.Should().NotBeNull();
        }

        [Fact]
        public void ItReturnsNullForMissingFunction()
        {
            using var component = Wasmtime.Component.Component.FromFile(
                Engine, Path.Combine("Modules", "ComponentAdd.wasm"));
            using var linker = new ComponentLinker(Engine);
            using var store = new Store(Engine);

            var instance = linker.Instantiate(store, component);
            var func = instance.GetFunc(store, "nonexistent");

            func.Should().BeNull();
        }

        [Fact]
        public void ItCanCallAddFunction()
        {
            using var component = Wasmtime.Component.Component.FromFile(
                Engine, Path.Combine("Modules", "ComponentAdd.wasm"));
            using var linker = new ComponentLinker(Engine);
            using var store = new Store(Engine);

            var instance = linker.Instantiate(store, component);
            var func = instance.GetFunc(store, "add")!;

            var results = func.Invoke(store,
                new[] { ComponentVal.FromS32(3), ComponentVal.FromS32(4) });

            results.Should().HaveCount(1);
            results[0].Kind.Should().Be(ComponentValKind.S32);
            results[0].GetS32().Should().Be(7);
        }

        [Fact]
        public void ItCanCallAddFunctionWithDifferentValues()
        {
            using var component = Wasmtime.Component.Component.FromFile(
                Engine, Path.Combine("Modules", "ComponentAdd.wasm"));
            using var linker = new ComponentLinker(Engine);
            using var store = new Store(Engine);

            var instance = linker.Instantiate(store, component);
            var func = instance.GetFunc(store, "add")!;

            var results = func.Invoke(store,
                new[] { ComponentVal.FromS32(-10), ComponentVal.FromS32(25) });

            results[0].GetS32().Should().Be(15);
        }

        [Fact]
        public void ItCanCallStringLengthFunction()
        {
            using var component = Wasmtime.Component.Component.FromFile(
                Engine, Path.Combine("Modules", "ComponentString.wasm"));
            using var linker = new ComponentLinker(Engine);
            using var store = new Store(Engine);

            var instance = linker.Instantiate(store, component);
            var func = instance.GetFunc(store, "string-length")!;

            var results = func.Invoke(store,
                new[] { ComponentVal.FromString("hello") });

            results.Should().HaveCount(1);
            results[0].Kind.Should().Be(ComponentValKind.U32);
            results[0].GetU32().Should().Be(5u);
        }

        [Fact]
        public void ItCanCallGreetFunction()
        {
            using var component = Wasmtime.Component.Component.FromFile(
                Engine, Path.Combine("Modules", "ComponentString.wasm"));
            using var linker = new ComponentLinker(Engine);
            using var store = new Store(Engine);

            var instance = linker.Instantiate(store, component);
            var func = instance.GetFunc(store, "greet")!;

            var results = func.Invoke(store,
                new[] { ComponentVal.FromString("World") });

            results.Should().HaveCount(1);
            results[0].Kind.Should().Be(ComponentValKind.String);
            results[0].GetString().Should().Be("Hello, World");
        }

        [Fact]
        public void ItCanCallGreetFunctionWithJapanese()
        {
            using var component = Wasmtime.Component.Component.FromFile(
                Engine, Path.Combine("Modules", "ComponentString.wasm"));
            using var linker = new ComponentLinker(Engine);
            using var store = new Store(Engine);

            var instance = linker.Instantiate(store, component);
            var func = instance.GetFunc(store, "greet")!;

            var results = func.Invoke(store,
                new[] { ComponentVal.FromString("世界") });

            results[0].GetString().Should().Be("Hello, 世界");
        }

        [Fact]
        public void ItCanDefineUnknownImportsAsTraps()
        {
            using var component = Wasmtime.Component.Component.FromFile(
                Engine, Path.Combine("Modules", "ComponentAdd.wasm"));
            using var linker = new ComponentLinker(Engine);

            // Should not throw - ComponentAdd has no imports
            linker.DefineUnknownImportsAsTraps(component);
        }

        [Fact]
        public void ItCanGetExportIndex()
        {
            using var component = Wasmtime.Component.Component.FromFile(
                Engine, Path.Combine("Modules", "ComponentAdd.wasm"));

            var exportIndex = component.GetExportIndex("add");
            exportIndex.Should().NotBeNull();
            exportIndex!.Dispose();
        }

        [Fact]
        public void ItReturnsNullForMissingExportIndex()
        {
            using var component = Wasmtime.Component.Component.FromFile(
                Engine, Path.Combine("Modules", "ComponentAdd.wasm"));

            var exportIndex = component.GetExportIndex("nonexistent");
            exportIndex.Should().BeNull();
        }
    }
}

#endif
