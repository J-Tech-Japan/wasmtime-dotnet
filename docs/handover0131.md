# handover0131

## 概要
- Windows で `MemoryAccessTests.ItGrows` 実行時に `testhost` がクラッシュ。
- `.dmp` 解析では `Wasmtime.Memory..ctor` の finally で `wasm_memorytype_delete` が呼ばれた直後に `wasmtime_wat2wasm` 付近まで壊れたスタックが見え、P/Invoke の呼び出し規約不一致（cdecl vs stdcall）によるスタック破壊が疑われた。

## 変更内容

### 1. P/Invoke 呼び出し規約の修正
- Wasmtime C API は cdecl のため、`DllImport(Engine.LibraryName)` / `DllImport(LibraryName)` に `CallingConvention = CallingConvention.Cdecl` を一括で明示。
- 対象ファイル: `src/*.cs` 内の Wasmtime ネイティブ呼び出し全般（Engine/Memory/Module/Store/Function/Linker/Config/Global/Table/Instance/Caller/Externs/Value/Import/Export/TrapException/WasiConfiguration/WasmtimeException）。

### 2. コールバックデリゲートの呼び出し規約の修正
- `Function.cs`, `Store.cs`, `Value.cs` のコールバックデリゲートに `[UnmanagedFunctionPointer(CallingConvention.Cdecl)]` を追加。
- 対象デリゲート:
  - `Function.Native.Finalizer`
  - `Function.Native.WasmtimeFuncCallback`
  - `Function.Native.WasmtimeFuncUncheckedCallback`
  - `Store.Native.Finalizer`
  - `Value.Native.Finalizer`

### 3. 構造体サイズの修正 (wasmtime dev 対応)
wasmtime dev ブランチで構造体サイズが変更されたため、以下を修正:

**Value.cs:**
- `AnyRef`: 16バイト → 24バイト (`IntPtr __private3` を追加)
- `ExternRef`: 16バイト → 24バイト (`IntPtr __private3` を追加)
- `Value`: 24バイト → 32バイト (`[StructLayout(LayoutKind.Explicit, Size = 32)]` に変更)

### 4. TrapCode enum の修正 (wasmtime dev 対応)
wasmtime dev で enum 値が変更されたため、`TrapException.cs` の `TrapCode` を更新:

- `AlwaysTrapAdapter` を削除（wasmtime dev で廃止）
- `OutOfFuel` を 12 → 11 に変更
- 後続のすべての値を再ナンバリング
- 14個の新しいトラップコードを追加:
  - `UnhandledTag = 19`
  - `ContinuationAlreadyConsumed = 20`
  - `DisabledOpCode = 21`
  - `AsyncDeadlock = 22`
  - `CannotLeaveComponent = 23`
  - `CannotBlockSyncTask = 24`
  - `InvalidChar = 25`
  - `DebugAssertStringEncodingFinished = 26`
  - `DebugAssertEqualCodeUnits = 27`
  - `DebugAssertPointerAligned = 28`
  - `DebugAssertUpperBitsUnset = 29`
  - `StringOutOfBounds = 30`
  - `ListOutOfBounds = 31`
  - `InvalidDiscriminant = 32`
  - `UnalignedPointer = 33`

## テスト結果
- **265 passed**, 2 skipped, 0 failed
- スキップされた2件は `Memory64AccessTests` (想定どおり)

## 確認済み問題
1. **MemoryAccessTests.ItGrows クラッシュ** → P/Invoke 呼び出し規約修正で解決
2. **"unknown wasmtime_valkind_t: 9" クラッシュ** → 構造体サイズ修正で解決
3. **FuelConsumption テスト失敗** → TrapCode enum 修正で解決

## 補足
- .dmp の記録では `coreclr!Thread::DoAppropriateAptStateWait` / `Wasmtime.Memory..ctor` / `wasm_memorytype_delete` が関係していた。
- 既存の P/Invoke は呼び出し規約の明示がなく、Windows のデフォルト（StdCall）になっていた可能性がある。
- プロジェクトは `DevBuild=true` で wasmtime dev ブランチを使用しているため、構造体サイズと enum 値が v35.0.0 とは異なる。
