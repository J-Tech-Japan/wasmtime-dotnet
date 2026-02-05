# 実装計画 — Phase 1 タスクリスト

## 前提

- ベースブランチ: `pr/interop-devbuild`
- 新ブランチ: `feature/component-model` (pr/interop-devbuildから分岐)
- `#if WASMTIME_DEV` でComponent Model関連コードを囲む (devビルドのみ)

---

## Phase 1 タスク (最小実行可能)

### Task 1: ディレクトリ構造とComponentValKind

**ファイル**: `src/Component/ComponentValKind.cs`

- `Wasmtime.Component` 名前空間を作成
- `ComponentValKind` enum を定義 (val.hのdefineに対応)

### Task 2: Component クラス

**ファイル**: `src/Component/Component.cs`

- SafeHandle (`Component.Handle`) でネイティブハンドルを管理
- `wasmtime_component_new` でバイナリからコンパイル
- `wasmtime_component_delete` で解放
- ファクトリメソッド: `FromFile`, `FromBytes`
- `#if WASMTIME_DEV` で囲む

### Task 3: ComponentExportIndex クラス

**ファイル**: `src/Component/ComponentExportIndex.cs`

- SafeHandle でネイティブハンドルを管理
- `wasmtime_component_get_export_index` で export を検索
- `wasmtime_component_export_index_delete` で解放

### Task 4: ComponentLinker クラス

**ファイル**: `src/Component/ComponentLinker.cs`

- SafeHandle でネイティブハンドルを管理
- `wasmtime_component_linker_new` / `_delete`
- `Instantiate()` で ComponentInstance を生成
- `AddWasiP2()` で WASI を追加
- `DefineUnknownImportsAsTraps()` でフォールバック

### Task 5: ComponentInstance クラス

**ファイル**: `src/Component/ComponentInstance.cs`

- `NativeComponentInstance` struct (store_id + __private)
- `GetExportIndex()` でexportを検索
- `GetFunc()` で ComponentFunc を取得
- 便利メソッド: `GetFunc(store, name)` (名前で直接取得)

### Task 6: ComponentFunc クラス

**ファイル**: `src/Component/ComponentFunc.cs`

- `NativeComponentFunc` struct (store_id + __private1 + __private2)
- `Call()` で関数を呼び出し
- `PostReturn()` で post-return 処理
- `Invoke()` = Call + PostReturn の一括実行

### Task 7: ComponentVal クラス (プリミティブ型のみ)

**ファイル**: `src/Component/ComponentVal.cs`

- `NativeComponentVal` / `NativeComponentValUnion` struct
- プリミティブ型のファクトリ: Bool, S32, U32, S64, U64, F32, F64, String
- ネイティブ構造体との変換: `ToNative()` / `FromNative()`
- `wasmtime_component_val_delete` による解放

### Task 8: テスト用 Component wasm の作成

**ファイル**: テスト用wasmバイナリ

- Rustで最小のComponentを作成 (add関数)
- `wasm-tools component new` でComponent化
- テストプロジェクトに配置

### Task 9: 基本テスト

**ファイル**: `tests/ComponentModelTests.cs`

- コンポーネントのロードテスト
- インスタンス化テスト
- 関数呼び出しテスト (s32 + s32 -> s32)

---

## Phase 2 タスク (WASI統合)

### Task 10: WASI preview2 コンポーネント実行

- `wasmtime_component_linker_add_wasip2` テスト
- WASIコンポーネント (hello world) の実行テスト

### Task 11: Store.SetWasiConfiguration との統合

- WASI設定がComponent Linkerと連携するか確認
- 必要なら新しいWASI設定メソッドを追加

---

## Phase 3 タスク (ホスト関数定義)

### Task 12: ComponentLinkerInstance

- `Root()` → `AddFunc()` でホスト関数定義
- コールバックのマーシャリング

### Task 13: リソース型の基本サポート

- `ComponentResourceType` ラッパー
- `AddResource()` でリソース定義

---

## 依存関係

```
Task 1 (ValKind)
  ↓
Task 2 (Component) ← Task 8 (テスト用wasm)
  ↓
Task 3 (ExportIndex)
  ↓
Task 4 (ComponentLinker)
  ↓
Task 5 (ComponentInstance)
  ↓
Task 6 (ComponentFunc) ← Task 7 (ComponentVal)
  ↓
Task 9 (テスト)
  ↓
Task 10-11 (WASI統合)
  ↓
Task 12-13 (ホスト関数)
```

---

## csproj変更

`Wasmtime.csproj` に新規ファイルを追加。
`src/Component/` ディレクトリ以下のファイルは自動的にコンパイルされる (SDK-style)。

条件コンパイル:

```xml
<!-- 既存の DevBuild 条件に含まれる -->
<DefineConstants Condition="'$(DevBuild)'=='true'">
  $(DefineConstants);WASMTIME_DEV
</DefineConstants>
```

各 Component ファイルの冒頭:

```csharp
#if WASMTIME_DEV

namespace Wasmtime.Component
{
    // ...
}

#endif
```

---

## 比較: Core Module API vs Component Model API

```
// Core Module (既存)
Engine → Module → Linker.Instantiate() → Instance → GetFunction<int,int>("add")

// Component Model (新規)
Engine → Component → ComponentLinker.Instantiate() → ComponentInstance
    → GetFunc("add") → Invoke(ComponentVal.FromS32(1), ComponentVal.FromS32(2))
```

主な違い:
- Component Model は **名前付きパラメータ** (WIT型)
- Component Model は **string, list, record** 等の高レベル型をネイティブサポート
- Component Model は **Canonical ABI** による自動マーシャリング
- Core Module は生の i32/i64/f32/f64/v128 のみ
