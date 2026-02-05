
wasmtime-dotnet 側で「実際に動かしてみたい」と言われたときに渡すべき情報は、
  最低限これです（このリポジトリの現状に合わせています）。

  1. 生成物(再現手順込み)
  - ビルド+検証ワンコマンド: ./src/poc/scripts/test-ts-component-wasm.sh 1.0.0
      - WasmServer 用に配置されるパス: src/poc/wasm-modules/typescript/1.0.0/


  - apply-event(instance-id: u32, event-type: string, payload-json: string)
  - execute-list-query(...) -> string
  - serialize-state(instance-id: u32) -> string
  - restore-state(instance-id: u32, state-json: string)
  - serialize-event(event-type: string, payload-json: string) -> string
  - deserialize-event(event-type: string, json: string) -> string
  - get-event-types() -> list<string>

  確認コマンド（エクスポート一覧とimport要件を共有するのに便利）:

  - wasm-tools component wit src/poc/wasm-modules/typescript/1.0.0/module.wasm

  3. 実行シーケンス（最小の動作確認）

  - id = create-instance("WeatherForecastProjector")
  - apply-event(id, "WeatherForecastCreated", "{\"forecastId\":\"...\",...}")
  - stateJson = serialize-state(id)（または execute-*-query）
  - get-event-types() が list<string> で返る

  4. WASI preview2 の import 要件
  この TS component は jco componentize 生成物なので、wasm-tools component
  wit ... に出る通り wasi:*@0.2.x を多数 import します（例: wasi:random,
  wasi:clocks, wasi:cli/stdin/stdout/stderr, wasi:http など）。

  - wasmtime-dotnet 側で「component を instantiate して export 呼び出し」をする
    場合、これら import を満たす実装（またはスタブ）が必要です。
  - もし wasi:http 等が邪魔なら、ビルド時に jco componentize の --disable http
    fetch-event ... を使って import を減らせます（必要ならこちらで build script
    をその前提に変更します）。

  5. 期待する “host API” の形（wasmtime-dotnet 側の受け入れ条件）

  - 「component の instantiate」と「export 関数を canonical ABI で呼び出す」API
    が必要
  - 文字列・list<string>・u32 の lift/lower が必須（今回のWITに含まれるため）

  必要なら、上の(3)のシーケンスを C# の擬似コード（APIがどうあるべきか）に落と
  して渡す形にもできます。