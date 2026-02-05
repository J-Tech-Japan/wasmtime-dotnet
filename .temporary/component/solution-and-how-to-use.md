# 全言語 WASM 実行ソリューション

## 概要

C#, Rust, Go, TypeScript の 4 言語で生成された WASM モジュールを .NET から実行するための構成。
各言語の WASM 生成方法（ABI）が異なるため、2 つの実行パスを使い分ける。

---

## 各言語の WASM 特性

| 言語 | ABI | モジュール形式 | WASI 依存 | wasi:http | サイズ | 状態 |
|------|-----|---------------|-----------|-----------|--------|------|
| TypeScript | Component Model (WIT) | コンポーネント | P2 (@0.2.3) | 必要 | 11 MB | 動作確認済み |
| C# | C-ABI (Component内包) | コンポーネント (export なし) | P2 (@0.2.3), sockets | 不要 | 9.5 MB | 要テスト |
| Rust | C-ABI | コアモジュール | なし | 不要 | 265 KB | 要テスト |
| Go | C-ABI | コアモジュール (TinyGo) | preview1 | 不要 | — | module.wasm 未ビルド |

### 各言語の WASM エクスポート関数

**TypeScript (Component Model / WIT)**
```
create-instance(projector-type: string) -> u32
apply-event(instance-id: u32, event-type: string, payload-json: string)
serialize-state(instance-id: u32) -> string
restore-state(instance-id: u32, state-json: string)
execute-query(instance-id: u32, query-type: string, params-json: string) -> string
execute-list-query(instance-id: u32, query-type: string, params-json: string) -> string
serialize-event(event-type: string, payload-json: string) -> string
deserialize-event(event-type: string, json: string) -> string
get-event-types() -> list<string>
```

**C# / Rust / Go (C-ABI)**
```
alloc(size: i32) -> i32
dealloc(ptr: i32, len: i32)
create_instance(name_ptr: i32, name_len: i32) -> i32
apply_event(id: i32, type_ptr: i32, type_len: i32, payload_ptr: i32, payload_len: i32) -> i64
serialize_state(id: i32) -> i64
restore_state(id: i32, state_ptr: i32, state_len: i32)
execute_query(id: i32, type_ptr: i32, type_len: i32, params_ptr: i32, params_len: i32) -> i64
execute_list_query(id: i32, type_ptr: i32, type_len: i32, params_ptr: i32, params_len: i32) -> i64
serialize_event(type_ptr: i32, type_len: i32, payload_ptr: i32, payload_len: i32) -> i64
deserialize_event(type_ptr: i32, type_len: i32, json_ptr: i32, json_len: i32) -> i64
get_event_types() -> i64
```

C-ABI の `i64` 戻り値は `ptr (上位32bit) | len (下位32bit)` のパック形式。

---

## 実行パス

### パス 1: Component Model (TypeScript 用)

```
TypeScript WASM
     │
     ▼
Rust シム (wasmtime_preview2_shim)
  ├── WASI P2 (clocks, filesystem, cli, random, io)
  ├── WASI HTTP (types, outgoing-handler)
  └── wasmtime Rust API (Component Linker)
     │
     ▼
コンポーネントインスタンス
     │
     ▼
JSON 形式で関数呼び出し
  引数: ["WeatherForecastProjector"]
  結果: [42]
```

**C# コード例:**
```csharp
// インスタンス化
var handle = Preview2Shim.InstantiateOrThrow(modulePath, inheritStdio: true);

// 関数呼び出し (JSON)
var result = Preview2Shim.CallFuncOrThrow(handle, "create-instance",
    "[\"WeatherForecastProjector\"]");
// result = "[42]"

var id = JsonDocument.Parse(result).RootElement[0].GetUInt32();

// apply-event
Preview2Shim.CallFuncOrThrow(handle, "apply-event",
    JsonSerializer.Serialize(new object[] { id, "EventType", "{...}" }));

// serialize-state
var stateResult = Preview2Shim.CallFuncOrThrow(handle, "serialize-state",
    JsonSerializer.Serialize(new object[] { id }));
// stateResult = "[\"{ json state ... }\"]"

// クリーンアップ
Preview2Shim.wasmtime_preview2_free_instance(handle);
```

### パス 2: Core Module API (Rust / Go 用)

```
Rust/Go WASM (コアモジュール)
     │
     ▼
wasmtime-dotnet Core Module API
  ├── Engine + Store + Linker
  ├── WASI preview1 (Go の場合)
  └── Module.FromFile()
     │
     ▼
モジュールインスタンス
     │
     ▼
C-ABI 関数呼び出し (alloc/dealloc + ptr/len)
```

**C# コード例:**
```csharp
using var engine = new Engine();
using var module = Module.FromFile(engine, modulePath);
using var linker = new Linker(engine);
using var store = new Store(engine);

// Go の場合は WASI 設定が必要
// linker.DefineWasi();
// store.SetWasiConfiguration(new WasiConfiguration()
//     .WithInheritedStandardOutput()
//     .WithInheritedStandardError());

var instance = linker.Instantiate(store, module);

var memory = instance.GetMemory(store, "memory");
var alloc = instance.GetFunction<int, int>(store, "alloc");
var dealloc = instance.GetAction<int, int>(store, "dealloc");
var createInstance = instance.GetFunction<int, int, int>(store, "create_instance");

// 文字列を WASM メモリに書き込み
var nameBytes = Encoding.UTF8.GetBytes("WeatherForecastProjector");
var namePtr = alloc(nameBytes.Length);
memory.WriteSpan(store, namePtr, nameBytes);

// 関数呼び出し
var id = createInstance(namePtr, nameBytes.Length);

// メモリ解放
dealloc(namePtr, nameBytes.Length);
```

### パス 3: C-ABI wrapped in Component (C# WASM 用)

```
C# WASM (コンポーネント内に C-ABI コアモジュール)
     │
     ▼
ComponentCoreExtractor (コアモジュール抽出)
     │
     ▼
パス 2 と同様 (Core Module API)
```

**C# コード例:**
```csharp
// コンポーネントからコアモジュールを抽出
var tempPath = Path.GetTempFileName();
ComponentCoreExtractor.ExtractMainModule(componentPath, tempPath);

// 以降はパス 2 と同じ
using var engine = new Engine();
using var module = Module.FromFile(engine, tempPath);
// ... Core Module API で実行
```

**または Rust シムの extract_core_module を使用:**
```csharp
Preview2Shim.ExtractCoreModule(componentPath, outputPath);
```

---

## 統一的なアプローチ（推奨）

全言語を統一的に扱うには、以下の 2 段階で対応:

### 短期: ABI に応じた分岐

```csharp
public class WasmModuleRunner : IDisposable
{
    private readonly string _abi;

    // manifest.json の "abi" フィールドで分岐
    public static WasmModuleRunner Create(string manifestPath)
    {
        var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var abi = manifest.RootElement.GetProperty("abi").GetString();
        var moduleFile = manifest.RootElement.GetProperty("moduleFile").GetString();
        var modulePath = Path.Combine(Path.GetDirectoryName(manifestPath), moduleFile);

        return abi switch
        {
            "component" => new ComponentModelRunner(modulePath),
            "c-abi" => new CAbiRunner(modulePath),
            _ => throw new NotSupportedException($"Unknown ABI: {abi}")
        };
    }

    // 共通インターフェース
    public abstract uint CreateInstance(string projectorType);
    public abstract void ApplyEvent(uint id, string eventType, string payloadJson);
    public abstract string SerializeState(uint id);
    public abstract string[] GetEventTypes();
    // ...
}
```

### 長期: 全言語を Component Model に移行

全言語の WASM を Component Model (WIT) で生成すれば、Rust シム経由で統一的に実行できる。
- TypeScript: 既に Component Model (jco componentize)
- C#: WIT export を追加 (NativeAOT-LLVM + wit-bindgen-csharp)
- Rust: `cargo component` で WIT ベースのコンポーネント生成
- Go: `wit-bindgen-go` + `wasm-tools component new`

---

## wasmtime-dotnet のセットアップ

### 必要なブランチ

```bash
git clone https://github.com/<your-org>/wasmtime-dotnet.git
cd wasmtime-dotnet
git checkout feature/component-model
```

**feature/component-model ブランチに含まれるもの:**
- `src/Component/` — Component Model C# API (Phase 1 + 2A + 2B)
  - Component, ComponentLinker, ComponentInstance, ComponentFunc
  - ComponentVal (全基本型 + string, list サポート)
  - ComponentLinkerInstance (ホスト関数定義)
  - ComponentFuncCallback (コールバックトランポリン)
- `native/wasmtime-preview2-shim/` — Rust シム (WASI P2 + HTTP)
- `tests/Preview2Shim.cs` — Rust シムの C# P/Invoke ラッパー
- `tests/ComponentTsIntegrationTests.cs` — TS 統合テスト

### ビルド手順

```bash
# 1. Rust シムのビルド (WASI P2 + HTTP サポート)
cd native/wasmtime-preview2-shim
cargo build --release
cd ../..

# 2. .NET ビルド (DevBuild=true で Component Model 有効化)
dotnet build src -p:DevBuild=true

# 3. テスト実行
dotnet test tests -p:DevBuild=true
```

### 必要な環境

- .NET 9.0 SDK
- Rust 1.86+ (Rust シムビルド用)
- wasmtime dev ビルドの C API (自動ダウンロード)

### DevBuild フラグについて

- `DevBuild=true` (デフォルト): wasmtime dev 版を使用、`WASMTIME_DEV` が定義され Component Model API が有効
- `DevBuild=false`: wasmtime v35.0.0 stable を使用、Component Model API は利用不可

**重要**: Component Model API は wasmtime の dev ビルドでのみ利用可能。
stable リリース (v35.0.0) の C API にはまだ Component Model サポートが含まれていない。

---

## ファイル構成

```
wasmtime-dotnet/
├── src/
│   ├── Component/                      # Component Model C# API
│   │   ├── Component.cs                # コンポーネント読み込み
│   │   ├── ComponentLinker.cs          # リンカー (WASI P2, 未知インポート)
│   │   ├── ComponentLinkerInstance.cs  # ホスト関数定義
│   │   ├── ComponentInstance.cs        # インスタンス管理
│   │   ├── ComponentFunc.cs            # 関数呼び出し
│   │   ├── ComponentVal.cs             # 値マーシャリング
│   │   ├── ComponentValKind.cs         # 値型列挙
│   │   ├── ComponentExportIndex.cs     # エクスポートインデックス
│   │   └── ComponentFuncCallback.cs    # コールバックトランポリン
│   ├── ComponentCoreExtractor.cs       # コアモジュール抽出
│   └── Preview2ComponentRunner.cs      # CLI コマンド実行
│
├── native/
│   └── wasmtime-preview2-shim/         # Rust シム
│       ├── Cargo.toml
│       └── src/lib.rs
│           ├── wasmtime_preview2_run_component()     # CLI command 実行
│           ├── wasmtime_preview2_instantiate_component()  # 汎用インスタンス化
│           ├── wasmtime_preview2_call_func()          # JSON で関数呼び出し
│           ├── wasmtime_preview2_free_instance()       # インスタンス解放
│           ├── wasmtime_preview2_free_string()         # 文字列解放
│           └── wasmtime_preview2_extract_core_module() # コアモジュール抽出
│
├── tests/
│   ├── Preview2Shim.cs                 # Rust シムの P/Invoke ラッパー
│   ├── ComponentModelTests.cs          # Phase 1 テスト (15件)
│   ├── ComponentHostFuncTests.cs       # Phase 2A ホスト関数テスト
│   ├── ComponentWasiTests.cs           # Phase 2A WASI テスト
│   └── ComponentTsIntegrationTests.cs  # TS 統合テスト (6件)
│
└── .temporary/component/
    └── solution-and-how-to-use.md      # このドキュメント
```

---

## 既知の制限事項と注意点

### 1. DefineUnknownImportsAsTraps のバグ (wasmtime #10663)

wasmtime C API の `DefineUnknownImportsAsTraps` は、semver の正確一致でトラップを定義するため、
`AddWasiP2` で定義した実装を上書きしてしまう。

**影響**: Component Model コンポーネント (TS, C#) で WASI が必要な場合、C API だけでは不十分。
**回避策**: Rust シムを使用（`define_unknown_imports_as_traps` を呼ばず、WASI P2 + HTTP を先に定義）。

### 2. C API dev ビルドに wasi:http がない

wasmtime C API の dev ビルドは `WASMTIME_FEATURE_WASI` は有効だが `WASMTIME_FEATURE_WASI_HTTP` は無効。
TypeScript コンポーネントは wasi:http をインポートするため、C API だけではインスタンス化できない。

**回避策**: Rust シム (`wasmtime-wasi-http` crate) を使用。

### 3. C# WASM は Component Model だが WIT エクスポートがない

.NET の `wasm32-wasip2` ターゲットで生成された WASM はコンポーネントだが、
WIT のトップレベル world にはエクスポートがない。C-ABI 関数はコアモジュール内にのみ存在。

**対処**: `ComponentCoreExtractor.ExtractMainModule()` でコアモジュールを抽出してから Core Module API で実行。
**長期**: NativeAOT-LLVM + wit-bindgen で WIT エクスポートを追加。

### 4. Go WASM の module.wasm が存在しない

Go のモジュールは manifest.json のみ存在し、module.wasm がビルドされていない。

**対処**: TinyGo でビルドが必要:
```bash
cd src/poc/go
tinygo build -o ../wasm-modules/go/1.0.0/module.wasm -target=wasi ./wasm/main.go
```

### 5. Rust シムと C API は別の wasmtime ランタイム

Rust シム (`wasmtime_preview2_shim`) は独自の `wasmtime::Engine` を内部で作成する。
C API の `Engine` / `Store` / `Linker` とは完全に独立している。
同じプロセス内で両方を使うことは可能だが、リソースの共有はできない。

---

## テスト結果 (現在の状態)

```
全 298 テスト合格 (296 passed, 2 skipped)

内訳:
  既存 Core Module テスト: 266 合格
  Component Model Phase 1: 15 合格 (add, string, greet, unicode, exports)
  Component Host Function: 7 合格 (s32, strings, unicode, nested, exceptions)
  Component WASI: 3 合格 (AddWasiP2, WASI config, host+WASI共存)
  TS 統合テスト: 6 合格 (instantiate, create, apply, serialize, eventTypes, fullWorkflow)
  スキップ: 2 (既存)
```

---

## 次のステップ

### すぐにできること
1. **Rust C-ABI モジュールの実行テスト** — Core Module API で直接実行
2. **C# コンポーネントのコアモジュール抽出テスト** — ComponentCoreExtractor + Core Module API
3. **Go モジュールのビルド** — TinyGo + WASI target

### 中期的な改善
4. **統一ランナーの実装** — manifest.json の abi フィールドで自動分岐
5. **C-ABI ヘルパーの作成** — alloc/dealloc + ptr/len パック/アンパックの共通コード
6. **C# WASM に WIT エクスポート追加** — Component Model 統一のための第一歩

### 長期的な方向性
7. **全言語を Component Model に移行** — WIT ベースの統一インターフェース
8. **Rust シムに sockets サポート追加** — C# コンポーネントの直接実行
9. **wasmtime stable リリースの Component Model 対応** — dev ビルド依存の解消
