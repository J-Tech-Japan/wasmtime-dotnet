#if WASMTIME_DEV

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Wasmtime.Component;
using Xunit;

namespace Wasmtime.Tests
{
    public class ComponentHostFuncTests : IDisposable
    {
        private Engine Engine { get; }

        public ComponentHostFuncTests()
        {
            Engine = new Engine();
        }

        public void Dispose()
        {
            Engine.Dispose();
        }

        [Fact]
        public void ItCanDefineHostFunctionWithS32()
        {
            using var component = Wasmtime.Component.Component.FromFile(
                Engine, Path.Combine("Modules", "ComponentWithImport.wasm"));
            using var linker = new ComponentLinker(Engine);

            // Define "add-one" host function in root namespace
            using (var root = linker.Root())
            {
                root.AddFunc("add-one", (args, results) =>
                {
                    var a = args[0].GetS32();
                    results[0] = ComponentVal.FromS32(a + 1);
                });
            }

            using var store = new Store(Engine);
            var instance = linker.Instantiate(store, component);

            // add-two calls add-one twice: add-one(add-one(5)) = 7
            var func = instance.GetFunc(store, "add-two")!;
            var result = func.Invoke(store, new[] { ComponentVal.FromS32(5) });

            result.Should().HaveCount(1);
            result[0].Kind.Should().Be(ComponentValKind.S32);
            result[0].GetS32().Should().Be(7);
        }

        [Fact]
        public void ItCanDefineHostFunctionWithNegativeValues()
        {
            using var component = Wasmtime.Component.Component.FromFile(
                Engine, Path.Combine("Modules", "ComponentWithImport.wasm"));
            using var linker = new ComponentLinker(Engine);

            using (var root = linker.Root())
            {
                root.AddFunc("add-one", (args, results) =>
                {
                    var a = args[0].GetS32();
                    results[0] = ComponentVal.FromS32(a + 1);
                });
            }

            using var store = new Store(Engine);
            var instance = linker.Instantiate(store, component);

            var func = instance.GetFunc(store, "add-two")!;
            var result = func.Invoke(store, new[] { ComponentVal.FromS32(-3) });

            result[0].GetS32().Should().Be(-1);
        }

        [Fact]
        public void ItCanDefineHostFunctionWithStrings()
        {
            using var component = Wasmtime.Component.Component.FromFile(
                Engine, Path.Combine("Modules", "ComponentWithStringImport.wasm"));
            using var linker = new ComponentLinker(Engine);

            using (var root = linker.Root())
            {
                root.AddFunc("reverse-string", (args, results) =>
                {
                    var input = args[0].GetString();
                    var reversed = new string(input.Reverse().ToArray());
                    results[0] = ComponentVal.FromString(reversed);
                });
            }

            using var store = new Store(Engine);
            var instance = linker.Instantiate(store, component);

            var func = instance.GetFunc(store, "process")!;
            var result = func.Invoke(store, new[] { ComponentVal.FromString("hello") });

            result.Should().HaveCount(1);
            result[0].Kind.Should().Be(ComponentValKind.String);
            result[0].GetString().Should().Be("olleh");
        }

        [Fact]
        public void ItCanDefineHostFunctionWithUnicodeStrings()
        {
            using var component = Wasmtime.Component.Component.FromFile(
                Engine, Path.Combine("Modules", "ComponentWithStringImport.wasm"));
            using var linker = new ComponentLinker(Engine);

            using (var root = linker.Root())
            {
                root.AddFunc("reverse-string", (args, results) =>
                {
                    var input = args[0].GetString();
                    var reversed = new string(input.Reverse().ToArray());
                    results[0] = ComponentVal.FromString(reversed);
                });
            }

            using var store = new Store(Engine);
            var instance = linker.Instantiate(store, component);

            var func = instance.GetFunc(store, "process")!;
            var result = func.Invoke(store, new[] { ComponentVal.FromString("こんにちは") });

            result[0].GetString().Should().Be("はちにんこ");
        }

        [Fact]
        public void ItCanDefineNestedInstanceFunction()
        {
            using var component = Wasmtime.Component.Component.FromFile(
                Engine, Path.Combine("Modules", "ComponentWithNestedImport.wasm"));
            using var linker = new ComponentLinker(Engine);

            using (var root = linker.Root())
            {
                using (var mathInstance = root.AddInstance("math"))
                {
                    mathInstance.AddFunc("double", (args, results) =>
                    {
                        var a = args[0].GetS32();
                        results[0] = ComponentVal.FromS32(a * 2);
                    });
                }
            }

            using var store = new Store(Engine);
            var instance = linker.Instantiate(store, component);

            // quadruple calls double twice: double(double(3)) = 12
            var func = instance.GetFunc(store, "quadruple")!;
            var result = func.Invoke(store, new[] { ComponentVal.FromS32(3) });

            result[0].GetS32().Should().Be(12);
        }

        [Fact]
        public void ItPropagatesCallbackExceptions()
        {
            using var component = Wasmtime.Component.Component.FromFile(
                Engine, Path.Combine("Modules", "ComponentWithImport.wasm"));
            using var linker = new ComponentLinker(Engine);

            using (var root = linker.Root())
            {
                root.AddFunc("add-one", (args, results) =>
                {
                    throw new InvalidOperationException("Test error from host");
                });
            }

            using var store = new Store(Engine);
            var instance = linker.Instantiate(store, component);

            var func = instance.GetFunc(store, "add-two")!;

            var act = () => func.Invoke(store, new[] { ComponentVal.FromS32(1) });
            act.Should().Throw<WasmtimeException>();
        }

        [Fact]
        public void ItCanGetLinkerRoot()
        {
            using var linker = new ComponentLinker(Engine);
            using var root = linker.Root();

            root.Should().NotBeNull();
        }
    }
}

#endif
