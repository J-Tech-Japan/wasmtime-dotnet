# handover0131

## 概要
- Windows で `MemoryAccessTests.ItGrows` 実行時に `testhost` がクラッシュ。
- `.dmp` 解析では `Wasmtime.Memory..ctor` の finally で `wasm_memorytype_delete` が呼ばれた直後に `wasmtime_wat2wasm` 付近まで壊れたスタックが見え、P/Invoke の呼び出し規約不一致（cdecl vs stdcall）によるスタック破壊が疑われた。

## 変更内容
- Wasmtime C API は cdecl のため、`DllImport(Engine.LibraryName)` / `DllImport(LibraryName)` に `CallingConvention = CallingConvention.Cdecl` を一括で明示。
- 対象ファイル: `src/*.cs` 内の Wasmtime ネイティブ呼び出し全般（Engine/Memory/Module/Store/Function/Linker/Config/Global/Table/Instance/Caller/Externs/Value/Import/Export/TrapException/WasiConfiguration/WasmtimeException）。

## 期待効果
- Windows/x86 での呼び出し規約ズレによるスタック破壊の排除。
- `wasm_memorytype_delete` 付近のクラッシュ再現が止まる見込み。

## 次のアクション
1. Windows 側で pull → 再ビルド。
2. `tests/Wasmtime.Tests.csproj` を実行して `MemoryAccessTests.ItGrows` の再現確認。
3. もしまだ落ちる場合は、新しい `.dmp` と `!analyze -v` 出力を取得。

## 補足
- .dmp の記録では `coreclr!Thread::DoAppropriateAptStateWait` / `Wasmtime.Memory..ctor` / `wasm_memorytype_delete` が関係していた。
- 既存の P/Invoke は呼び出し規約の明示がなく、Windows のデフォルト（StdCall）になっていた可能性がある。
