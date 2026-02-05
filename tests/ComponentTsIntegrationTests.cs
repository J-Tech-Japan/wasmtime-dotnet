#if WASMTIME_DEV

using System;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Wasmtime.Tests
{
    /// <summary>
    /// Integration tests using the actual TypeScript component (jco componentize output).
    /// These tests verify the full workflow described in sample.md.
    ///
    /// Uses the wasmtime-preview2-shim native library which provides WASI P2 + HTTP
    /// support via the Rust wasmtime API, bypassing C API limitations
    /// (wasmtime issue #10663 - DefineUnknownImportsAsTraps semver bug).
    /// </summary>
    public class ComponentTsIntegrationTests
    {
        private const string ModulePath =
            "/Users/tomohisa/dev/GitHub/SekibanAsAService/src/poc/wasm-modules/typescript/1.0.0/module.wasm";

        private bool ModuleExists => File.Exists(ModulePath);

        [Fact]
        public void ItCanInstantiateTsComponent()
        {
            if (!ModuleExists) return;

            var handle = Preview2Shim.InstantiateOrThrow(ModulePath);
            handle.Should().NotBe(IntPtr.Zero);
            Preview2Shim.wasmtime_preview2_free_instance(handle);
        }

        [Fact]
        public void ItCanCallCreateInstance()
        {
            if (!ModuleExists) return;

            var handle = Preview2Shim.InstantiateOrThrow(ModulePath);
            try
            {
                var resultJson = Preview2Shim.CallFuncOrThrow(
                    handle, "create-instance",
                    "[\"WeatherForecastProjector\"]");

                resultJson.Should().NotBeNullOrEmpty();
                var results = JsonDocument.Parse(resultJson).RootElement;
                results.GetArrayLength().Should().Be(1);
                results[0].GetUInt32().Should().BeGreaterOrEqualTo(0);
            }
            finally
            {
                Preview2Shim.wasmtime_preview2_free_instance(handle);
            }
        }

        [Fact]
        public void ItCanCallApplyEvent()
        {
            if (!ModuleExists) return;

            var handle = Preview2Shim.InstantiateOrThrow(ModulePath);
            try
            {
                // create-instance first
                var createJson = Preview2Shim.CallFuncOrThrow(
                    handle, "create-instance",
                    "[\"WeatherForecastProjector\"]");
                var id = JsonDocument.Parse(createJson).RootElement[0].GetUInt32();

                // apply-event
                var argsJson = JsonSerializer.Serialize(new object[] {
                    id,
                    "WeatherForecastCreated",
                    "{\"forecastId\":\"forecast-1\",\"location\":\"Tokyo\",\"date\":\"2025-01-15\",\"temperature\":15.5,\"condition\":\"Sunny\"}"
                });

                // Should not throw
                Preview2Shim.CallFuncOrThrow(handle, "apply-event", argsJson);
            }
            finally
            {
                Preview2Shim.wasmtime_preview2_free_instance(handle);
            }
        }

        [Fact]
        public void ItCanCallSerializeState()
        {
            if (!ModuleExists) return;

            var handle = Preview2Shim.InstantiateOrThrow(ModulePath);
            try
            {
                // create-instance
                var createJson = Preview2Shim.CallFuncOrThrow(
                    handle, "create-instance",
                    "[\"WeatherForecastProjector\"]");
                var id = JsonDocument.Parse(createJson).RootElement[0].GetUInt32();

                // apply-event
                var applyArgs = JsonSerializer.Serialize(new object[] {
                    id,
                    "WeatherForecastCreated",
                    "{\"forecastId\":\"forecast-1\",\"location\":\"Tokyo\",\"date\":\"2025-01-15\",\"temperature\":15.5,\"condition\":\"Sunny\"}"
                });
                Preview2Shim.CallFuncOrThrow(handle, "apply-event", applyArgs);

                // serialize-state
                var stateJson = Preview2Shim.CallFuncOrThrow(
                    handle, "serialize-state",
                    JsonSerializer.Serialize(new object[] { id }));
                var stateResults = JsonDocument.Parse(stateJson).RootElement;
                stateResults.GetArrayLength().Should().Be(1);
                var state = stateResults[0].GetString();
                state.Should().NotBeNullOrEmpty();
                state.Should().Contain("forecast-1");
            }
            finally
            {
                Preview2Shim.wasmtime_preview2_free_instance(handle);
            }
        }

        [Fact]
        public void ItCanCallGetEventTypes()
        {
            if (!ModuleExists) return;

            var handle = Preview2Shim.InstantiateOrThrow(ModulePath);
            try
            {
                var resultJson = Preview2Shim.CallFuncOrThrow(
                    handle, "get-event-types", "[]");

                var results = JsonDocument.Parse(resultJson).RootElement;
                results.GetArrayLength().Should().Be(1);
                results[0].GetArrayLength().Should().BeGreaterThan(0);
            }
            finally
            {
                Preview2Shim.wasmtime_preview2_free_instance(handle);
            }
        }

        [Fact]
        public void ItCanRunFullWorkflow()
        {
            if (!ModuleExists) return;

            var handle = Preview2Shim.InstantiateOrThrow(ModulePath);
            try
            {
                // 1. get-event-types
                var typesJson = Preview2Shim.CallFuncOrThrow(handle, "get-event-types", "[]");
                var eventTypes = JsonDocument.Parse(typesJson).RootElement[0];
                eventTypes.GetArrayLength().Should().BeGreaterThan(0);

                // 2. create-instance
                var createJson = Preview2Shim.CallFuncOrThrow(
                    handle, "create-instance",
                    "[\"WeatherForecastProjector\"]");
                var id = JsonDocument.Parse(createJson).RootElement[0].GetUInt32();

                // 3. apply-event
                var applyArgs = JsonSerializer.Serialize(new object[] {
                    id,
                    "WeatherForecastCreated",
                    "{\"forecastId\":\"forecast-1\",\"location\":\"Tokyo\",\"date\":\"2025-01-15\",\"temperature\":15.5,\"condition\":\"Sunny\"}"
                });
                Preview2Shim.CallFuncOrThrow(handle, "apply-event", applyArgs);

                // 4. serialize-state
                var stateJson = Preview2Shim.CallFuncOrThrow(
                    handle, "serialize-state",
                    JsonSerializer.Serialize(new object[] { id }));
                var state = JsonDocument.Parse(stateJson).RootElement[0].GetString();
                state.Should().Contain("forecast-1");

                // 5. restore-state
                var restoreArgs = JsonSerializer.Serialize(new object[] { id, state });
                Preview2Shim.CallFuncOrThrow(handle, "restore-state", restoreArgs);

                // 6. serialize-event / deserialize-event round-trip
                var serializeArgs = JsonSerializer.Serialize(new object[] {
                    "WeatherForecastCreated",
                    "{\"forecastId\":\"forecast-2\",\"location\":\"Osaka\",\"date\":\"2025-01-16\",\"temperature\":12.0,\"condition\":\"Cloudy\"}"
                });
                var serializedJson = Preview2Shim.CallFuncOrThrow(
                    handle, "serialize-event", serializeArgs);
                var serializedEvent = JsonDocument.Parse(serializedJson).RootElement[0].GetString();
                serializedEvent.Should().NotBeNullOrEmpty();

                var deserializeArgs = JsonSerializer.Serialize(new object[] {
                    "WeatherForecastCreated",
                    serializedEvent
                });
                var deserializedJson = Preview2Shim.CallFuncOrThrow(
                    handle, "deserialize-event", deserializeArgs);
                var deserializedPayload = JsonDocument.Parse(deserializedJson).RootElement[0].GetString();
                deserializedPayload.Should().NotBeNullOrEmpty();
            }
            finally
            {
                Preview2Shim.wasmtime_preview2_free_instance(handle);
            }
        }
    }
}

#endif
