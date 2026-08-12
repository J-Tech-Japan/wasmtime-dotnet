use wasmtime::{bail, Result};
use std::ffi::{CStr, CString};
use std::ops::Range;
use std::os::raw::{c_char, c_int};
use wasmtime::{Engine, Store};
use wasmtime::component::{Component, Linker, ResourceTable, Val, types};
use wasmtime::error::Context;
use wasmtime_wasi::{DirPerms, FilePerms, WasiCtx, WasiCtxBuilder, WasiCtxView, WasiView};
use wasmtime_wasi_http::WasiHttpCtx;
use wasmtime_wasi_http::p2::{WasiHttpCtxView, WasiHttpView};
use wasmparser::{Parser, Payload};

#[repr(C)]
pub struct Preview2Result {
    pub exit_code: c_int,
    pub error_message: *mut c_char,
}

struct Preview2State {
    table: ResourceTable,
    wasi: WasiCtx,
    http: WasiHttpCtx,
}

impl WasiView for Preview2State {
    fn ctx(&mut self) -> WasiCtxView<'_> {
        WasiCtxView {
            ctx: &mut self.wasi,
            table: &mut self.table,
        }
    }
}

impl WasiHttpView for Preview2State {
    fn http(&mut self) -> WasiHttpCtxView<'_> {
        WasiHttpCtxView {
            ctx: &mut self.http,
            table: &mut self.table,
            hooks: Default::default(),
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn wasmtime_preview2_run_component(
    component_path: *const c_char,
    argv: *const *const c_char,
    argc: usize,
    env: *const *const c_char,
    envc: usize,
    preopens: *const *const c_char,
    preopen_count: usize,
    inherit_stdio: bool,
    inherit_env: bool,
    inherit_network: bool,
    exit_code_out: *mut c_int,
    error_message_out: *mut *mut c_char,
) -> c_int {
    let result = std::panic::catch_unwind(|| {
        run_component_inner(
            component_path,
            argv,
            argc,
            env,
            envc,
            preopens,
            preopen_count,
            inherit_stdio,
            inherit_env,
            inherit_network,
        )
    });

    match result {
        Ok(Ok(exit_code)) => {
            if !exit_code_out.is_null() {
                unsafe { *exit_code_out = exit_code };
            }
            if !error_message_out.is_null() {
                unsafe { *error_message_out = std::ptr::null_mut() };
            }
            0
        }
        Ok(Err(err)) => {
            write_error(err, error_message_out);
            1
        }
        Err(_) => {
            write_error(wasmtime::format_err!("panic while executing component"), error_message_out);
            2
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn wasmtime_preview2_free_error(ptr: *mut c_char) {
    if ptr.is_null() {
        return;
    }
    unsafe {
        let _ = CString::from_raw(ptr);
    }
}

#[no_mangle]
pub unsafe extern "C" fn wasmtime_preview2_extract_core_module(
    component_path: *const c_char,
    output_path: *const c_char,
    error_message_out: *mut *mut c_char,
) -> c_int {
    let result = std::panic::catch_unwind(|| {
        extract_core_module_inner(component_path, output_path)
    });

    match result {
        Ok(Ok(())) => {
            if !error_message_out.is_null() {
                unsafe { *error_message_out = std::ptr::null_mut() };
            }
            0
        }
        Ok(Err(err)) => {
            write_error(err, error_message_out);
            1
        }
        Err(_) => {
            write_error(wasmtime::format_err!("panic while extracting core module"), error_message_out);
            2
        }
    }
}

fn write_error(err: wasmtime::Error, error_message_out: *mut *mut c_char) {
    if error_message_out.is_null() {
        return;
    }
    let msg = format!("{err:#}");
    match CString::new(msg) {
        Ok(cstr) => unsafe {
            *error_message_out = cstr.into_raw();
        },
        Err(_) => unsafe {
            *error_message_out = std::ptr::null_mut();
        },
    }
}

unsafe fn run_component_inner(
    component_path: *const c_char,
    argv: *const *const c_char,
    argc: usize,
    env: *const *const c_char,
    envc: usize,
    preopens: *const *const c_char,
    preopen_count: usize,
    inherit_stdio: bool,
    inherit_env: bool,
    inherit_network: bool,
) -> Result<c_int> {
    if component_path.is_null() {
        bail!("component path is null");
    }

    let component_path = unsafe { CStr::from_ptr(component_path) }
        .to_str()
        .context("component path is not valid UTF-8")?;

    let args = unsafe { read_cstr_array(argv, argc)? };
    let envs = unsafe { read_cstr_array(env, envc)? };
    let preopen_specs = unsafe { read_cstr_array(preopens, preopen_count)? };

    let mut builder = WasiCtxBuilder::new();
    if inherit_stdio {
        builder.inherit_stdio();
    }
    if inherit_env {
        builder.inherit_env();
    }
    if inherit_network {
        builder.inherit_network();
    }

    if !args.is_empty() {
        builder.args(&args);
    }

    for env in envs {
        let (key, value) = split_env(&env);
        builder.env(key, value);
    }

    for spec in preopen_specs {
        let (host, guest) = split_preopen(&spec)?;
        builder.preopened_dir(host, guest, DirPerms::all(), FilePerms::all())?;
    }

    let engine = Engine::default();
    let component = Component::from_file(&engine, component_path)
        .with_context(|| format!("failed to load component: {component_path}"))?;

    let mut linker = Linker::<Preview2State>::new(&engine);
    wasmtime_wasi::p2::add_to_linker_sync(&mut linker)
        .context("failed to add WASI preview2 to linker")?;
    wasmtime_wasi_http::p2::add_only_http_to_linker_sync(&mut linker)
        .context("failed to add WASI HTTP to linker")?;

    let state = Preview2State {
        table: ResourceTable::new(),
        wasi: builder.build(),
        http: WasiHttpCtx::new(),
    };

    let mut store = Store::new(&engine, state);

    let command = wasmtime_wasi::p2::bindings::sync::Command::instantiate(&mut store, &component, &linker)
        .context("failed to instantiate component")?;

    let result = command
        .wasi_cli_run()
        .call_run(&mut store)
        .context("failed to call wasi:cli/run")?;

    Ok(match result {
        Ok(()) => 0,
        Err(()) => 1,
    })
}

unsafe fn extract_core_module_inner(
    component_path: *const c_char,
    output_path: *const c_char,
) -> Result<()> {
    if component_path.is_null() {
        bail!("component path is null");
    }
    if output_path.is_null() {
        bail!("output path is null");
    }

    let component_path = unsafe { CStr::from_ptr(component_path) }
        .to_str()
        .context("component path is not valid UTF-8")?;
    let output_path = unsafe { CStr::from_ptr(output_path) }
        .to_str()
        .context("output path is not valid UTF-8")?;

    let bytes = std::fs::read(component_path)
        .with_context(|| format!("failed to read component: {component_path}"))?;

    let mut ranges = Vec::new();
    collect_module_ranges(&bytes, 0, &mut ranges)
        .context("failed to parse component for core modules")?;

    let range = ranges
        .into_iter()
        .max_by_key(|r| r.end.saturating_sub(r.start))
        .ok_or_else(|| wasmtime::format_err!("no core modules found in component"))?;

    if range.end > bytes.len() || range.start >= range.end {
        bail!("core module range is out of bounds");
    }

    std::fs::write(output_path, &bytes[range])
        .with_context(|| format!("failed to write core module to {output_path}"))?;

    Ok(())
}

fn collect_module_ranges(
    bytes: &[u8],
    base_offset: usize,
    ranges: &mut Vec<Range<usize>>,
) -> Result<()> {
    for payload in Parser::new(0).parse_all(bytes) {
        match payload.context("failed to parse component payload")? {
            Payload::ModuleSection { unchecked_range, .. } => {
                let start = base_offset + unchecked_range.start;
                let end = base_offset + unchecked_range.end;
                if end > base_offset + bytes.len() {
                    bail!("module range is out of bounds");
                }
                ranges.push(start..end);
            }
            Payload::ComponentSection { unchecked_range, .. } => {
                let start = unchecked_range.start;
                let end = unchecked_range.end;
                if end > bytes.len() || start >= end {
                    bail!("nested component range is out of bounds");
                }
                collect_module_ranges(&bytes[start..end], base_offset + start, ranges)?;
            }
            _ => {}
        }
    }

    Ok(())
}

unsafe fn read_cstr_array(ptr: *const *const c_char, len: usize) -> Result<Vec<String>> {
    if len == 0 {
        return Ok(Vec::new());
    }
    if ptr.is_null() {
        bail!("string array pointer is null but length is non-zero");
    }

    let mut values = Vec::with_capacity(len);
    for i in 0..len {
        let item = unsafe { *ptr.add(i) };
        if item.is_null() {
            bail!("string array entry {i} is null");
        }
        let value = unsafe { CStr::from_ptr(item) }
            .to_str()
            .context("string array entry is not valid UTF-8")?;
        values.push(value.to_owned());
    }
    Ok(values)
}

fn split_env(env: &str) -> (&str, &str) {
    match env.split_once('=') {
        Some((k, v)) => (k, v),
        None => (env, ""),
    }
}

fn split_preopen(spec: &str) -> Result<(&str, &str)> {
    if let Some((host, guest)) = spec.split_once("::") {
        if host.is_empty() || guest.is_empty() {
            bail!("invalid preopen mapping '{spec}'");
        }
        return Ok((host, guest));
    }
    if let Some((host, guest)) = spec.split_once('=') {
        if host.is_empty() || guest.is_empty() {
            bail!("invalid preopen mapping '{spec}'");
        }
        return Ok((host, guest));
    }
    if spec.is_empty() {
        bail!("invalid preopen mapping '{spec}'");
    }
    Ok((spec, spec))
}

// ============================================================================
// Generic Component Instance API
// ============================================================================

/// Opaque handle for a component instance with WASI P2 + HTTP support.
pub struct ComponentHandle {
    store: Store<Preview2State>,
    instance: wasmtime::component::Instance,
}

/// Instantiate a component with WASI P2 + HTTP support.
/// Returns an opaque handle that can be used with `wasmtime_preview2_call_func`.
#[no_mangle]
pub unsafe extern "C" fn wasmtime_preview2_instantiate_component(
    component_path: *const c_char,
    inherit_stdio: bool,
    error_message_out: *mut *mut c_char,
) -> *mut ComponentHandle {
    let result = std::panic::catch_unwind(|| {
        instantiate_component_inner(component_path, inherit_stdio)
    });

    match result {
        Ok(Ok(handle)) => Box::into_raw(Box::new(handle)),
        Ok(Err(err)) => {
            write_error(err, error_message_out);
            std::ptr::null_mut()
        }
        Err(_) => {
            write_error(
                wasmtime::format_err!("panic while instantiating component"),
                error_message_out,
            );
            std::ptr::null_mut()
        }
    }
}

unsafe fn instantiate_component_inner(
    component_path: *const c_char,
    inherit_stdio: bool,
) -> Result<ComponentHandle> {
    if component_path.is_null() {
        bail!("component path is null");
    }

    let component_path = unsafe { CStr::from_ptr(component_path) }
        .to_str()
        .context("component path is not valid UTF-8")?;

    let engine = Engine::default();
    let component = Component::from_file(&engine, component_path)
        .with_context(|| format!("failed to load component: {component_path}"))?;

    let mut linker = Linker::<Preview2State>::new(&engine);
    wasmtime_wasi::p2::add_to_linker_sync(&mut linker)
        .context("failed to add WASI preview2 to linker")?;
    wasmtime_wasi_http::p2::add_only_http_to_linker_sync(&mut linker)
        .context("failed to add WASI HTTP to linker")?;

    let mut builder = WasiCtxBuilder::new();
    if inherit_stdio {
        builder.inherit_stdio();
    }

    let state = Preview2State {
        table: ResourceTable::new(),
        wasi: builder.build(),
        http: WasiHttpCtx::new(),
    };

    let mut store = Store::new(&engine, state);

    let instance = linker
        .instantiate(&mut store, &component)
        .context("failed to instantiate component")?;

    Ok(ComponentHandle { store, instance })
}

/// Call an exported function on a component instance.
/// Arguments and results are passed as JSON strings.
/// Args JSON format: array of values, e.g. `["hello", 42]`
/// Result JSON format: array of values, e.g. `["world"]`
#[no_mangle]
pub unsafe extern "C" fn wasmtime_preview2_call_func(
    handle: *mut ComponentHandle,
    func_name: *const c_char,
    args_json: *const c_char,
    result_json_out: *mut *mut c_char,
    error_message_out: *mut *mut c_char,
) -> c_int {
    if handle.is_null() {
        write_error(wasmtime::format_err!("handle is null"), error_message_out);
        return 1;
    }

    let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        let handle = unsafe { &mut *handle };
        call_func_inner(handle, func_name, args_json)
    }));

    match result {
        Ok(Ok(json)) => {
            match CString::new(json) {
                Ok(cstr) => {
                    if !result_json_out.is_null() {
                        unsafe { *result_json_out = cstr.into_raw() };
                    }
                    0
                }
                Err(e) => {
                    write_error(wasmtime::format_err!("result contains null byte: {e}"), error_message_out);
                    1
                }
            }
        }
        Ok(Err(err)) => {
            write_error(err, error_message_out);
            1
        }
        Err(_) => {
            write_error(wasmtime::format_err!("panic while calling function"), error_message_out);
            2
        }
    }
}

unsafe fn call_func_inner(
    handle: &mut ComponentHandle,
    func_name: *const c_char,
    args_json: *const c_char,
) -> Result<String> {
    if func_name.is_null() {
        bail!("function name is null");
    }

    let func_name = unsafe { CStr::from_ptr(func_name) }
        .to_str()
        .context("function name is not valid UTF-8")?;

    let args_str = if args_json.is_null() {
        "[]"
    } else {
        unsafe { CStr::from_ptr(args_json) }
            .to_str()
            .context("args JSON is not valid UTF-8")?
    };

    let json_args: Vec<serde_json::Value> =
        serde_json::from_str(args_str).context("failed to parse args JSON")?;

    // Find the exported function
    let func = handle
        .instance
        .get_func(&mut handle.store, func_name)
        .with_context(|| format!("function '{func_name}' not found in component exports"))?;

    // Get the function type to determine parameter and result types
    let func_type = func.ty(&handle.store);
    let param_types: Vec<_> = func_type.params().collect();
    let result_types: Vec<_> = func_type.results().collect();

    // Convert JSON args to Val
    let mut vals: Vec<Val> = Vec::with_capacity(json_args.len());
    for (i, (json_val, (_name, param_type))) in json_args.iter().zip(param_types.iter()).enumerate() {
        let val = json_to_val(json_val, param_type)
            .with_context(|| format!("failed to convert argument {i}"))?;
        vals.push(val);
    }

    // Prepare result slots
    let mut results: Vec<Val> = result_types
        .iter()
        .map(|_| Val::Bool(false)) // placeholder
        .collect();

    // Call the function
    func.call(&mut handle.store, &vals, &mut results)
        .with_context(|| format!("failed to call function '{func_name}'"))?;

    // Convert results to JSON
    let json_results: Vec<serde_json::Value> = results
        .iter()
        .map(val_to_json)
        .collect::<Result<Vec<_>>>()
        .context("failed to convert results to JSON")?;

    serde_json::to_string(&json_results).context("failed to serialize results to JSON")
}

fn json_to_val(json: &serde_json::Value, ty: &types::Type) -> Result<Val> {
    match ty {
        types::Type::Bool => {
            let b = json.as_bool().context("expected bool")?;
            Ok(Val::Bool(b))
        }
        types::Type::S8 => {
            let n = json.as_i64().context("expected integer")? as i8;
            Ok(Val::S8(n))
        }
        types::Type::U8 => {
            let n = json.as_u64().context("expected unsigned integer")? as u8;
            Ok(Val::U8(n))
        }
        types::Type::S16 => {
            let n = json.as_i64().context("expected integer")? as i16;
            Ok(Val::S16(n))
        }
        types::Type::U16 => {
            let n = json.as_u64().context("expected unsigned integer")? as u16;
            Ok(Val::U16(n))
        }
        types::Type::S32 => {
            let n = json.as_i64().context("expected integer")? as i32;
            Ok(Val::S32(n))
        }
        types::Type::U32 => {
            let n = json.as_u64().context("expected unsigned integer")? as u32;
            Ok(Val::U32(n))
        }
        types::Type::S64 => {
            let n = json.as_i64().context("expected integer")?;
            Ok(Val::S64(n))
        }
        types::Type::U64 => {
            let n = json.as_u64().context("expected unsigned integer")?;
            Ok(Val::U64(n))
        }
        types::Type::Float32 => {
            let n = json.as_f64().context("expected number")? as f32;
            Ok(Val::Float32(n))
        }
        types::Type::Float64 => {
            let n = json.as_f64().context("expected number")?;
            Ok(Val::Float64(n))
        }
        types::Type::Char => {
            let s = json.as_str().context("expected string for char")?;
            let c = s.chars().next().context("empty string for char")?;
            Ok(Val::Char(c))
        }
        types::Type::String => {
            let s = json.as_str().context("expected string")?;
            Ok(Val::String(s.to_owned()))
        }
        types::Type::List(list_type) => {
            let arr = json.as_array().context("expected array for list")?;
            let elem_type = list_type.ty();
            let vals: Vec<Val> = arr
                .iter()
                .enumerate()
                .map(|(i, v)| json_to_val(v, &elem_type).with_context(|| format!("list element {i}")))
                .collect::<Result<Vec<_>>>()?;
            Ok(Val::List(vals))
        }
        _ => {
            bail!("unsupported parameter type: {:?}", ty);
        }
    }
}

fn val_to_json(val: &Val) -> Result<serde_json::Value> {
    match val {
        Val::Bool(b) => Ok(serde_json::Value::Bool(*b)),
        Val::S8(n) => Ok(serde_json::json!(*n)),
        Val::U8(n) => Ok(serde_json::json!(*n)),
        Val::S16(n) => Ok(serde_json::json!(*n)),
        Val::U16(n) => Ok(serde_json::json!(*n)),
        Val::S32(n) => Ok(serde_json::json!(*n)),
        Val::U32(n) => Ok(serde_json::json!(*n)),
        Val::S64(n) => Ok(serde_json::json!(*n)),
        Val::U64(n) => Ok(serde_json::json!(*n)),
        Val::Float32(n) => Ok(serde_json::json!(*n)),
        Val::Float64(n) => Ok(serde_json::json!(*n)),
        Val::Char(c) => Ok(serde_json::json!(c.to_string())),
        Val::String(s) => Ok(serde_json::Value::String(s.clone())),
        Val::List(items) => {
            let arr: Vec<serde_json::Value> = items
                .iter()
                .map(val_to_json)
                .collect::<Result<Vec<_>>>()?;
            Ok(serde_json::Value::Array(arr))
        }
        Val::Option(opt) => match opt {
            Some(v) => val_to_json(v),
            None => Ok(serde_json::Value::Null),
        },
        _ => {
            bail!("unsupported result type: {:?}", val);
        }
    }
}

/// Free a component instance handle.
#[no_mangle]
pub unsafe extern "C" fn wasmtime_preview2_free_instance(handle: *mut ComponentHandle) {
    if !handle.is_null() {
        unsafe {
            let _ = Box::from_raw(handle);
        }
    }
}

/// Free a string allocated by the shim (for result_json or error messages).
#[no_mangle]
pub unsafe extern "C" fn wasmtime_preview2_free_string(ptr: *mut c_char) {
    if !ptr.is_null() {
        unsafe {
            let _ = CString::from_raw(ptr);
        }
    }
}
