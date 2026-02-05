# P/Invoke マッピング一覧

wasmtime C API Component Model ヘッダから .NET P/Invoke 宣言へのマッピング。

## component/component.h

| C API | .NET クラス | 用途 |
|-------|-----------|------|
| `wasmtime_component_new` | `Component.Native` | バイナリからコンポーネントをコンパイル |
| `wasmtime_component_delete` | `Component.Handle.ReleaseHandle` | コンポーネントの解放 |
| `wasmtime_component_serialize` | `Component.Serialize()` | シリアライズ |
| `wasmtime_component_deserialize` | `Component.Deserialize()` | デシリアライズ |
| `wasmtime_component_deserialize_file` | `Component.DeserializeFile()` | ファイルからデシリアライズ |
| `wasmtime_component_clone` | (内部使用) | 参照カウントの増加 |
| `wasmtime_component_type` | `Component.Type` | 型情報の取得 |
| `wasmtime_component_get_export_index` | `Component.GetExportIndex()` | Export検索 |
| `wasmtime_component_export_index_clone` | `ComponentExportIndex` 内部 | Indexのクローン |
| `wasmtime_component_export_index_delete` | `ComponentExportIndex.Handle.ReleaseHandle` | Indexの解放 |

## component/linker.h

| C API | .NET クラス | 用途 |
|-------|-----------|------|
| `wasmtime_component_linker_new` | `ComponentLinker` コンストラクタ | リンカー生成 |
| `wasmtime_component_linker_delete` | `ComponentLinker.Handle.ReleaseHandle` | リンカー解放 |
| `wasmtime_component_linker_allow_shadowing` | `ComponentLinker.AllowShadowing` | シャドウイング設定 |
| `wasmtime_component_linker_root` | `ComponentLinker.Root()` | ルートインスタンス取得 |
| `wasmtime_component_linker_instantiate` | `ComponentLinker.Instantiate()` | インスタンス化 |
| `wasmtime_component_linker_define_unknown_imports_as_traps` | `ComponentLinker.DefineUnknownImportsAsTraps()` | トラップフォールバック |
| `wasmtime_component_linker_add_wasip2` | `ComponentLinker.AddWasiP2()` | WASI preview2 追加 |
| `wasmtime_component_linker_instance_add_instance` | `ComponentLinkerInstance.AddInstance()` | ネストインスタンス |
| `wasmtime_component_linker_instance_add_module` | `ComponentLinkerInstance.AddModule()` | モジュール追加 |
| `wasmtime_component_linker_instance_add_func` | `ComponentLinkerInstance.AddFunc()` | 関数追加 |
| `wasmtime_component_linker_instance_add_resource` | `ComponentLinkerInstance.AddResource()` | リソース追加 |
| `wasmtime_component_linker_instance_delete` | `ComponentLinkerInstance.Handle.ReleaseHandle` | インスタンス解放 |

## component/instance.h

| C API | .NET クラス | 用途 |
|-------|-----------|------|
| `wasmtime_component_instance_get_export_index` | `ComponentInstance.GetExportIndex()` | Export検索 |
| `wasmtime_component_instance_get_func` | `ComponentInstance.GetFunc()` | 関数取得 |

## component/func.h

| C API | .NET クラス | 用途 |
|-------|-----------|------|
| `wasmtime_component_func_call` | `ComponentFunc.Call()` | 関数呼び出し |
| `wasmtime_component_func_post_return` | `ComponentFunc.PostReturn()` | post-return処理 |
| `wasmtime_component_func_type` | `ComponentFunc.GetFuncType()` | 型情報取得 |

## component/val.h — リソース関連

| C API | .NET クラス | 用途 |
|-------|-----------|------|
| `wasmtime_component_resource_any_type` | (Phase 3) | リソース型取得 |
| `wasmtime_component_resource_any_clone` | (Phase 3) | リソースクローン |
| `wasmtime_component_resource_any_owned` | (Phase 3) | own/borrow判定 |
| `wasmtime_component_resource_any_drop` | (Phase 3) | リソースドロップ |
| `wasmtime_component_resource_any_delete` | (Phase 3) | メモリ解放 |
| `wasmtime_component_resource_host_new` | (Phase 3) | ホストリソース生成 |
| `wasmtime_component_resource_host_clone` | (Phase 3) | ホストリソースクローン |
| `wasmtime_component_resource_host_rep` | (Phase 3) | rep取得 |
| `wasmtime_component_resource_host_type` | (Phase 3) | 型取得 |
| `wasmtime_component_resource_host_owned` | (Phase 3) | own/borrow判定 |
| `wasmtime_component_resource_host_delete` | (Phase 3) | メモリ解放 |
| `wasmtime_component_resource_any_to_host` | (Phase 3) | any→host変換 |
| `wasmtime_component_resource_host_to_any` | (Phase 3) | host→any変換 |

## component/val.h — 値操作

| C API | .NET クラス | 用途 |
|-------|-----------|------|
| `wasmtime_component_val_new` | `ComponentVal` 内部 | ヒープ上に値を確保 |
| `wasmtime_component_val_free` | `ComponentVal` 内部 | ヒープ上の値を解放 |
| `wasmtime_component_val_clone` | `ComponentVal` 内部 | 値の深コピー |
| `wasmtime_component_val_delete` | `ComponentVal.Dispose()` | 値のメモリ解放 |
| `wasmtime_component_vallist_new` | `ComponentVal.FromList()` | リスト生成 |
| `wasmtime_component_vallist_delete` | ComponentVal内部 | リスト解放 |
| `wasmtime_component_valrecord_new` | `ComponentVal.FromRecord()` | レコード生成 |
| `wasmtime_component_valrecord_delete` | ComponentVal内部 | レコード解放 |
| `wasmtime_component_valtuple_new` | `ComponentVal.FromTuple()` | タプル生成 |
| `wasmtime_component_valtuple_delete` | ComponentVal内部 | タプル解放 |
| `wasmtime_component_valflags_new` | `ComponentVal.FromFlags()` | フラグ生成 |
| `wasmtime_component_valflags_delete` | ComponentVal内部 | フラグ解放 |

## component/types/ — 型イントロスペクション (Phase 4)

| カテゴリ | C API パターン | .NET クラス |
|----------|---------------|-----------|
| Func型 | `wasmtime_component_func_type_*` | `ComponentFuncType` |
| Val型 | `wasmtime_component_valtype_*` | `ComponentValType` |
| List型 | `wasmtime_component_list_type_*` | `ComponentListType` |
| Record型 | `wasmtime_component_record_type_*` | `ComponentRecordType` |
| Tuple型 | `wasmtime_component_tuple_type_*` | `ComponentTupleType` |
| Variant型 | `wasmtime_component_variant_type_*` | `ComponentVariantType` |
| Enum型 | `wasmtime_component_enum_type_*` | `ComponentEnumType` |
| Option型 | `wasmtime_component_option_type_*` | `ComponentOptionType` |
| Result型 | `wasmtime_component_result_type_*` | `ComponentResultType` |
| Flags型 | `wasmtime_component_flags_type_*` | `ComponentFlagsType` |
| Resource型 | `wasmtime_component_resource_type_*` | `ComponentResourceType` |
| Future型 | `wasmtime_component_future_type_*` | `ComponentFutureType` |
| Stream型 | `wasmtime_component_stream_type_*` | `ComponentStreamType` |

---

## P/Invoke宣言テンプレート

```csharp
// 全てのDllImportに CallingConvention.Cdecl を明示
// Engine.LibraryName = "wasmtime" を使用

[DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
public static extern IntPtr wasmtime_component_new(
    Engine.Handle engine,
    byte* buf,
    nuint len,
    out IntPtr component_out);

[DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
public static extern void wasmtime_component_delete(IntPtr component);

[DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
public static extern IntPtr wasmtime_component_linker_new(Engine.Handle engine);

[DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
public static extern void wasmtime_component_linker_delete(IntPtr linker);

[DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
public static extern IntPtr wasmtime_component_linker_instantiate(
    IntPtr linker,
    IntPtr context,
    IntPtr component,
    out NativeComponentInstance instance_out);

[DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
public static extern IntPtr wasmtime_component_func_call(
    in NativeComponentFunc func,
    IntPtr context,
    NativeComponentVal* args,
    nuint args_size,
    NativeComponentVal* results,
    nuint results_size);

[DllImport(Engine.LibraryName, CallingConvention = CallingConvention.Cdecl)]
public static extern IntPtr wasmtime_component_func_post_return(
    in NativeComponentFunc func,
    IntPtr context);
```
