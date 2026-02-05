# Component Model 統合設計書

## 概要

wasmtime-dotnet に WebAssembly Component Model のネイティブサポートを追加する。
現行の Core Module (C-ABI) APIと並行して使えるようにする。

### 目標

1. Core Module (C-ABI wasm) — 既存APIをそのまま維持
2. Component Model (component wasm) — 新しいAPIを `Wasmtime.Component` 名前空間に追加
3. 同一 `Engine` / `Store` を共有可能にする (wasmtime C API がそれを許容しているため)

---

## 現状分析

### 既存のCore Module API

```
Engine → Module → Linker → Instance → Function/Memory/Global/Table
              ↑                ↑
           Store            Store
```

- `Module`: `wasmtime_module_new` で .wasm バイナリをコンパイル
- `Linker`: `wasmtime_linker_*` でimportを定義 → `wasmtime_linker_instantiate` でインスタンス化
- `Instance`: export取得 (`wasmtime_instance_export_get`)
- `Function`: 呼び出し (`wasmtime_func_call`)

### 既存のComponent Model関連コード

- `Preview2ComponentRunner.cs` — 別プロセス的にコンポーネントを実行 (wasmtime_preview2_shim経由)
- `ComponentCoreExtractor.cs` — コンポーネントからコアモジュールを抽出

これらは「shimライブラリ」を使った間接的なアプローチ。
本設計では **wasmtime C APIのComponent Model機能を直接使う** 。

### wasmtime C API Component Model ヘッダ (利用可能)

dev版のwasmtimeライブラリに `WASMTIME_FEATURE_COMPONENT_MODEL` が有効化されている:

| ヘッダ | 内容 |
|--------|------|
| `component/component.h` | `wasmtime_component_t` (コンパイル/デシリアライズ/削除) |
| `component/linker.h` | `wasmtime_component_linker_t`, `wasmtime_component_linker_instance_t` |
| `component/instance.h` | `wasmtime_component_instance_t` (exportの取得) |
| `component/func.h` | `wasmtime_component_func_t` (関数呼び出し/post-return) |
| `component/val.h` | `wasmtime_component_val_t` (値の表現/変換) |
| `component/types.h` | 型メタデータ (func, val, instance, component, resource) |

---

## アーキテクチャ設計

### 名前空間構成

```
Wasmtime                              (既存 — Core Module)
  ├── Engine                          (共有)
  ├── Store                           (共有)
  ├── Config                          (共有、WithComponentModel追加)
  ├── Module
  ├── Linker
  ├── Instance
  ├── Function / Memory / Global / Table
  └── ...

Wasmtime.Component                    (新規 — Component Model)
  ├── Component                       (wasmtime_component_t ラッパー)
  ├── ComponentLinker                  (wasmtime_component_linker_t)
  ├── ComponentLinkerInstance          (wasmtime_component_linker_instance_t)
  ├── ComponentInstance               (wasmtime_component_instance_t)
  ├── ComponentFunc                   (wasmtime_component_func_t)
  ├── ComponentExportIndex            (wasmtime_component_export_index_t)
  ├── ComponentVal                    (wasmtime_component_val_t マーシャリング)
  ├── ComponentValKind                (enum: bool, s8..u64, f32, f64, char, string, list, record...)
  └── Types/                          (型イントロスペクション)
      ├── ComponentFuncType
      ├── ComponentValType
      ├── ComponentListType
      ├── ComponentRecordType
      ├── ComponentTupleType
      ├── ComponentVariantType
      ├── ComponentEnumType
      ├── ComponentOptionType
      ├── ComponentResultType
      ├── ComponentFlagsType
      └── ComponentResourceType
```

### ファイル構成 (新規ファイル)

```
src/
├── Component/
│   ├── Component.cs                 # wasmtime_component_t ラッパー
│   ├── ComponentLinker.cs           # wasmtime_component_linker_t ラッパー
│   ├── ComponentLinkerInstance.cs   # wasmtime_component_linker_instance_t ラッパー
│   ├── ComponentInstance.cs         # wasmtime_component_instance_t ラッパー
│   ├── ComponentFunc.cs             # wasmtime_component_func_t ラッパー
│   ├── ComponentExportIndex.cs      # wasmtime_component_export_index_t ラッパー
│   ├── ComponentVal.cs              # 値の表現とマーシャリング
│   ├── ComponentValKind.cs          # 値種別enum
│   └── Types/
│       ├── ComponentFuncType.cs
│       ├── ComponentValType.cs
│       └── ... (各型)
tests/
├── ComponentModelTests.cs           # Component Model 基本テスト
├── ComponentLinkerTests.cs          # リンカーテスト
└── ComponentFuncCallTests.cs        # 関数呼び出しテスト
```

---

## 詳細クラス設計

### 1. Component (wasmtime_component_t)

```csharp
namespace Wasmtime.Component
{
    /// <summary>
    /// コンパイル済みWebAssembly Componentを表す。
    /// Core ModuleのModule相当。
    /// </summary>
    public class Component : IDisposable
    {
        // コンストラクタ: バイナリからコンパイル
        public Component(Engine engine, byte[] bytes);
        public Component(Engine engine, ReadOnlySpan<byte> bytes);

        // ファクトリメソッド
        public static Component FromBytes(Engine engine, string name, byte[] bytes);
        public static Component FromFile(Engine engine, string path);

        // シリアライズ/デシリアライズ
        public byte[] Serialize();
        public static Component Deserialize(Engine engine, byte[] bytes);
        public static Component DeserializeFile(Engine engine, string path);

        // Export検索
        public ComponentExportIndex? GetExportIndex(string name);
        public ComponentExportIndex? GetExportIndex(
            ComponentExportIndex? instanceIndex, string name);

        // 型情報
        public ComponentType Type { get; }

        // IDisposable
        public void Dispose();

        // Internal
        internal Handle NativeHandle { get; }

        // P/Invoke
        internal static class Native
        {
            // wasmtime_component_new
            // wasmtime_component_delete
            // wasmtime_component_serialize
            // wasmtime_component_deserialize
            // wasmtime_component_deserialize_file
            // wasmtime_component_clone
            // wasmtime_component_type
            // wasmtime_component_get_export_index
        }
    }
}
```

### 2. ComponentLinker (wasmtime_component_linker_t)

```csharp
namespace Wasmtime.Component
{
    /// <summary>
    /// Component Modelのリンカー。
    /// ホスト関数やWASIインターフェースを定義し、Componentをインスタンス化する。
    /// </summary>
    public class ComponentLinker : IDisposable
    {
        public ComponentLinker(Engine engine);

        // シャドウイング設定
        public bool AllowShadowing { set; }

        // ルートインスタンスにアクセス
        // IMPORTANT: 返されたインスタンスをDispose()するまでLinkerにアクセスしてはいけない
        public ComponentLinkerInstance Root();

        // Componentをインスタンス化
        public ComponentInstance Instantiate(Store store, Component component);

        // 未知のimportをトラップとして定義
        public void DefineUnknownImportsAsTraps(Component component);

        // WASI preview2 インターフェースを追加
        public void AddWasiP2();

        public void Dispose();

        internal static class Native
        {
            // wasmtime_component_linker_new
            // wasmtime_component_linker_delete
            // wasmtime_component_linker_allow_shadowing
            // wasmtime_component_linker_root
            // wasmtime_component_linker_instantiate
            // wasmtime_component_linker_define_unknown_imports_as_traps
            // wasmtime_component_linker_add_wasip2
        }
    }
}
```

### 3. ComponentLinkerInstance (wasmtime_component_linker_instance_t)

```csharp
namespace Wasmtime.Component
{
    /// <summary>
    /// リンカー内のインスタンス定義。
    /// ネストされたインターフェース (例: wasi:http/handler) を定義するために使う。
    /// </summary>
    public class ComponentLinkerInstance : IDisposable
    {
        // ネストされたインスタンスを追加
        public ComponentLinkerInstance AddInstance(string name);

        // Core Moduleを追加
        public void AddModule(string name, Module module);

        // ホスト関数を追加
        public void AddFunc(string name, ComponentFuncCallback callback);

        // リソース型を追加
        public void AddResource(
            string name,
            ComponentResourceType resourceType,
            ComponentResourceDestructor destructor);

        public void Dispose();

        internal static class Native
        {
            // wasmtime_component_linker_instance_add_instance
            // wasmtime_component_linker_instance_add_module
            // wasmtime_component_linker_instance_add_func
            // wasmtime_component_linker_instance_add_resource
            // wasmtime_component_linker_instance_delete
        }
    }

    // コールバックデリゲート
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr ComponentFuncNativeCallback(
        IntPtr data,
        IntPtr context,
        IntPtr funcType,
        IntPtr args,
        nuint argsSize,
        IntPtr results,
        nuint resultsSize
    );

    // マネージドコールバック
    public delegate void ComponentFuncCallback(
        ComponentVal[] args,
        ComponentVal[] results
    );
}
```

### 4. ComponentInstance (wasmtime_component_instance_t)

```csharp
namespace Wasmtime.Component
{
    /// <summary>
    /// インスタンス化されたComponent。
    /// Export関数の取得と呼び出しが可能。
    /// </summary>
    public class ComponentInstance
    {
        // Export Index経由で関数を取得
        public ComponentFunc? GetFunc(
            Store store,
            ComponentExportIndex exportIndex);

        // 便利メソッド: 名前で直接関数を取得
        public ComponentFunc? GetFunc(Store store, string name);

        // ネストされたExport検索
        public ComponentExportIndex? GetExportIndex(
            Store store,
            string name);
        public ComponentExportIndex? GetExportIndex(
            Store store,
            ComponentExportIndex? instanceIndex,
            string name);

        internal static class Native
        {
            // wasmtime_component_instance_get_func
            // wasmtime_component_instance_get_export_index
        }
    }
}
```

### 5. ComponentFunc (wasmtime_component_func_t)

```csharp
namespace Wasmtime.Component
{
    /// <summary>
    /// Component Modelの関数。Core ModuleのFunctionに相当。
    /// Canonical ABIに基づく高レベル値 (string, list, record等) の受け渡しが可能。
    /// </summary>
    public class ComponentFunc
    {
        // 関数呼び出し
        public ComponentVal[] Call(Store store, params ComponentVal[] args);

        // Post-return処理 (Call成功後に必須)
        public void PostReturn(Store store);

        // 呼び出し + PostReturn を一括実行
        public ComponentVal[] Invoke(Store store, params ComponentVal[] args);

        // 型情報
        public ComponentFuncType GetFuncType(Store store);

        internal static class Native
        {
            // wasmtime_component_func_call
            // wasmtime_component_func_post_return
            // wasmtime_component_func_type
        }
    }
}
```

### 6. ComponentVal (wasmtime_component_val_t マーシャリング)

```csharp
namespace Wasmtime.Component
{
    /// <summary>
    /// Component Modelで使われる値の種別。
    /// Core ModuleのValueKindに相当。
    /// </summary>
    public enum ComponentValKind : byte
    {
        Bool = 0,
        S8 = 1,
        U8 = 2,
        S16 = 3,
        U16 = 4,
        S32 = 5,
        U32 = 6,
        S64 = 7,
        U64 = 8,
        F32 = 9,
        F64 = 10,
        Char = 11,
        String = 12,
        List = 13,
        Record = 14,
        Tuple = 15,
        Variant = 16,
        Enum = 17,
        Option = 18,
        Result = 19,
        Flags = 20,
        Resource = 21,
    }

    /// <summary>
    /// Component Modelの値を表す。
    /// .NETの型と Component Model の型のマーシャリングを行う。
    /// </summary>
    public class ComponentVal : IDisposable
    {
        public ComponentValKind Kind { get; }

        // プリミティブファクトリ
        public static ComponentVal FromBool(bool value);
        public static ComponentVal FromS8(sbyte value);
        public static ComponentVal FromU8(byte value);
        public static ComponentVal FromS16(short value);
        public static ComponentVal FromU16(ushort value);
        public static ComponentVal FromS32(int value);
        public static ComponentVal FromU32(uint value);
        public static ComponentVal FromS64(long value);
        public static ComponentVal FromU64(ulong value);
        public static ComponentVal FromF32(float value);
        public static ComponentVal FromF64(double value);
        public static ComponentVal FromChar(char value);
        public static ComponentVal FromString(string value);

        // 複合型ファクトリ
        public static ComponentVal FromList(ComponentVal[] items);
        public static ComponentVal FromRecord(
            (string Name, ComponentVal Value)[] fields);
        public static ComponentVal FromTuple(ComponentVal[] items);
        public static ComponentVal FromVariant(
            string discriminant, ComponentVal? payload);
        public static ComponentVal FromEnum(string name);
        public static ComponentVal FromOption(ComponentVal? value);
        public static ComponentVal FromResult(
            bool isOk, ComponentVal? value);
        public static ComponentVal FromFlags(string[] flags);

        // 値の取得
        public bool GetBool();
        public sbyte GetS8();
        public byte GetU8();
        public short GetS16();
        public ushort GetU16();
        public int GetS32();
        public uint GetU32();
        public long GetS64();
        public ulong GetU64();
        public float GetF32();
        public double GetF64();
        public char GetChar();
        public string GetString();
        public ComponentVal[] GetList();
        public (string Name, ComponentVal Value)[] GetRecord();
        public ComponentVal[] GetTuple();
        // ...

        // ネイティブ構造体との変換
        internal void ToNative(ref NativeComponentVal native);
        internal static ComponentVal FromNative(in NativeComponentVal native);

        public void Dispose();
    }

    // ネイティブ構造体のマーシャリング用
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeComponentVal
    {
        public ComponentValKind kind;
        public NativeComponentValUnion of;
    }
}
```

---

## Config拡張

既存の `Config` クラスに Component Model を有効化するメソッドを追加:

```csharp
// Config.cs に追加
public Config WithComponentModel(bool enable)
{
    Native.wasmtime_config_wasm_component_model_set(handle, enable);
    return this;
}
```

ただし、dev版wasmtimeではデフォルトで有効なため、明示的にON/OFFしたい場合のみ。
wasmtime C APIに `wasmtime_config_wasm_component_model_set` が存在するか要確認。
存在しない場合はEngine生成時にComponent Model対応を前提とする。

---

## 使用例

### Example 1: Component を WASI で実行

```csharp
using Wasmtime;
using Wasmtime.Component;

// Engine と Store は Core Module と共有
using var engine = new Engine();
using var store = new Store(engine);

// WASI preview2 をセットアップ
store.SetWasiConfiguration(new WasiConfiguration()
    .WithInheritedStandardOutput()
    .WithInheritedStandardError());

// Component を読み込み
using var component = Component.FromFile(engine, "hello.component.wasm");

// Linker で WASI を定義
using var linker = new ComponentLinker(engine);
linker.AddWasiP2();

// インスタンス化して実行
var instance = linker.Instantiate(store, component);

// wasi:cli/run@0.2.0 の run 関数を呼び出し
var runExport = component.GetExportIndex("wasi:cli/run@0.2.0");
var runFuncIndex = component.GetExportIndex(runExport, "run");
var runFunc = instance.GetFunc(store, runFuncIndex!);
var results = runFunc!.Invoke(store);
```

### Example 2: カスタムインターフェースのComponent呼び出し

```csharp
using Wasmtime;
using Wasmtime.Component;

using var engine = new Engine();
using var store = new Store(engine);

// WITで定義された add(a: s32, b: s32) -> s32 をexportするComponent
using var component = Component.FromFile(engine, "calculator.component.wasm");

using var linker = new ComponentLinker(engine);
linker.DefineUnknownImportsAsTraps(component);

var instance = linker.Instantiate(store, component);

// export関数を直接名前で取得
var addFunc = instance.GetFunc(store, "add");
var result = addFunc!.Invoke(store,
    ComponentVal.FromS32(40),
    ComponentVal.FromS32(2));

Console.WriteLine($"40 + 2 = {result[0].GetS32()}"); // 42
```

### Example 3: Core Module と Component を同一プログラムで使用

```csharp
using Wasmtime;
using Wasmtime.Component;

using var engine = new Engine();
using var store = new Store(engine);

// --- Core Module (C-ABI) ---
using var module = Module.FromFile(engine, "legacy.wasm");
using var coreLinker = new Linker(engine);
coreLinker.DefineWasi();
store.SetWasiConfiguration(new WasiConfiguration());
var coreInstance = coreLinker.Instantiate(store, module);
var legacyAdd = coreInstance.GetFunction<int, int, int>("add");
Console.WriteLine($"Core: {legacyAdd!(3, 4)}"); // 7

// --- Component Model ---
using var component = Component.FromFile(engine, "modern.component.wasm");
using var compLinker = new ComponentLinker(engine);
compLinker.AddWasiP2();
var compInstance = compLinker.Instantiate(store, component);
var modernAdd = compInstance.GetFunc(store, "add");
var result = modernAdd!.Invoke(store,
    ComponentVal.FromS32(3),
    ComponentVal.FromS32(4));
Console.WriteLine($"Component: {result[0].GetS32()}"); // 7
```

---

## 実装フェーズ

### Phase 1: 基盤 (最小実行可能)

1. **`Component.cs`** — コンポーネントのロード/コンパイル/Dispose
2. **`ComponentLinker.cs`** — 基本リンカー (new/delete/instantiate)
3. **`ComponentInstance.cs`** — export取得
4. **`ComponentFunc.cs`** — 関数呼び出し (call/post_return)
5. **`ComponentVal.cs`** — プリミティブ型のマーシャリング (bool, s32, u32, s64, u64, f32, f64, string)
6. **`ComponentValKind.cs`** — enum定義
7. **`ComponentExportIndex.cs`** — export検索

この段階で「.component.wasmを読み込み → インスタンス化 → export関数を呼び出し」が動く。

### Phase 2: WASI統合

8. **`ComponentLinker.AddWasiP2()`** — `wasmtime_component_linker_add_wasip2` のバインド
9. **`ComponentLinker.DefineUnknownImportsAsTraps()`** — トラップフォールバック
10. WASIコンポーネントの実行テスト

### Phase 3: ホスト関数定義

11. **`ComponentLinkerInstance.cs`** — ネストインスタンス/関数定義
12. **コールバックマーシャリング** — ホスト関数を定義してComponentから呼び出し
13. **リソース型** — ホスト定義リソースの基本サポート

### Phase 4: 複合型サポート

14. **list, record, tuple, variant, enum, option, result, flags** の完全マーシャリング
15. **型イントロスペクション** — ComponentFuncType, ComponentValType 等

### Phase 5: 高レベルAPI (将来)

16. WIT定義からの自動バインディング生成
17. Source Generator を使った型安全なインターフェース生成
18. Generic型パラメータによる型安全な呼び出しラッパー

---

## P/Invoke宣言の整理

すべてのP/Invoke宣言は `CallingConvention.Cdecl` を明示的に使用する
(pr/interop-devbuild ブランチで確立済みのパターンに従う)。

```csharp
[DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
public static extern wasmtime_error_t* wasmtime_component_new(
    Engine.Handle engine, byte* buf, nuint len,
    out IntPtr component_out);
```

SafeHandle パターンも既存のEngine.Handle, Module.Handle等と同一パターンで実装。

---

## ネイティブ構造体レイアウト

### wasmtime_component_instance_t

```csharp
[StructLayout(LayoutKind.Sequential)]
internal struct NativeComponentInstance
{
    public ulong store_id;
    public uint __private;
}
```

### wasmtime_component_func_t

```csharp
[StructLayout(LayoutKind.Sequential)]
internal struct NativeComponentFunc
{
    public ulong store_id;
    public uint __private1;
    public uint __private2;
}
```

### wasmtime_component_val_t

```csharp
[StructLayout(LayoutKind.Sequential)]
internal struct NativeComponentVal
{
    public byte kind; // ComponentValKind
    // union は kind に応じて解釈
    // 最大サイズ: wasm_name_t (ptr + size = 16 bytes)
    // → NativeComponentValUnionとして定義
}

[StructLayout(LayoutKind.Explicit)]
internal struct NativeComponentValUnion
{
    [FieldOffset(0)] public byte boolean;
    [FieldOffset(0)] public sbyte s8;
    [FieldOffset(0)] public byte u8;
    [FieldOffset(0)] public short s16;
    [FieldOffset(0)] public ushort u16;
    [FieldOffset(0)] public int s32;
    [FieldOffset(0)] public uint u32;
    [FieldOffset(0)] public long s64;
    [FieldOffset(0)] public ulong u64;
    [FieldOffset(0)] public float f32;
    [FieldOffset(0)] public double f64;
    [FieldOffset(0)] public uint character;

    // string, list, record, tuple, flags は
    // { size_t size; type* data; } 構造
    [FieldOffset(0)] public nuint vec_size;
    [FieldOffset(8)] public IntPtr vec_data; // 8 on 64-bit

    // variant: { wasm_name_t discriminant; wasmtime_component_val_t* val; }
    // result: { bool is_ok; wasmtime_component_val_t* val; }
    // option: wasmtime_component_val_t* (nullable)
    // resource: wasmtime_component_resource_any_t*
    [FieldOffset(0)] public IntPtr ptr;
}
```

---

## テスト戦略

### 最小テスト用のComponentバイナリ

テスト用に以下の .component.wasm を用意:

1. **hello-component.wasm** — wasi:cli/run の最小実装 (stdout に "hello" を出力)
2. **add-component.wasm** — `add(a: s32, b: s32) -> s32` をexport
3. **string-component.wasm** — `greet(name: string) -> string` をexport

Rustの `cargo component` ツールチェインまたは `wasm-tools component new` で作成。

### テストケース

```csharp
[Fact]
public void ItLoadsComponent()
{
    using var engine = new Engine();
    using var component = Component.FromFile(engine, "add-component.wasm");
    Assert.NotNull(component);
}

[Fact]
public void ItInstantiatesComponent()
{
    using var engine = new Engine();
    using var store = new Store(engine);
    using var component = Component.FromFile(engine, "add-component.wasm");
    using var linker = new ComponentLinker(engine);
    linker.DefineUnknownImportsAsTraps(component);
    var instance = linker.Instantiate(store, component);
    Assert.NotNull(instance);
}

[Fact]
public void ItCallsComponentFunction()
{
    using var engine = new Engine();
    using var store = new Store(engine);
    using var component = Component.FromFile(engine, "add-component.wasm");
    using var linker = new ComponentLinker(engine);
    linker.DefineUnknownImportsAsTraps(component);
    var instance = linker.Instantiate(store, component);
    var addFunc = instance.GetFunc(store, "add");
    Assert.NotNull(addFunc);
    var result = addFunc!.Invoke(store,
        ComponentVal.FromS32(40),
        ComponentVal.FromS32(2));
    Assert.Equal(42, result[0].GetS32());
}
```

---

## リスクと課題

### 1. dev版wasmtimeのみ対応

- Component Model C APIは安定版wasmtime (v35.0.0)には含まれていない可能性がある
- `min`ビルドでは `WASMTIME_FEATURE_COMPONENT_MODEL` が undefined
- → `#if WASMTIME_DEV` 条件コンパイルでComponent関連コードを囲む

### 2. ネイティブ構造体サイズの不安定性

- dev版のABIは変更される可能性がある
- → 既存パターン(ValueRaw.cs)に倣い、条件コンパイルで対応

### 3. 複合型のメモリ管理

- `wasmtime_component_val_t` は動的メモリ確保を伴う
- list, record等はヒープ上に割り当てられ、`wasmtime_component_val_delete` で解放が必要
- → IDisposableパターンとファイナライザで安全に管理

### 4. コールバック内のマーシャリング

- ホスト関数がComponentから呼ばれた時、引数/戻り値の変換が複雑
- → Phase 3で段階的に実装

---

## まとめ

| 観点 | Core Module (既存) | Component Model (新規) |
|------|-------------------|----------------------|
| エントリポイント | `Module` | `Component` |
| リンカー | `Linker` | `ComponentLinker` |
| インスタンス | `Instance` | `ComponentInstance` |
| 関数 | `Function` (型安全/Generic) | `ComponentFunc` (動的型) |
| 値 | `ValueKind` (i32/i64/f32/f64/v128/ref) | `ComponentValKind` (22種の型) |
| WASI | `DefineWasi()` (preview1) | `AddWasiP2()` (preview2) |
| 名前空間 | `Wasmtime` | `Wasmtime.Component` |
| 共有部分 | `Engine`, `Store`, `Config` | 同じ |

この設計により、既存のCore Module APIに一切の破壊的変更なく、
Component Modelの完全なサポートを段階的に追加できる。
