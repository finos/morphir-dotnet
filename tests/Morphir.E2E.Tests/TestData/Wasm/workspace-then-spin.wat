;; A guest for the worker's interruption check (add-wasm-extension-host, task 4.1, which closes the Ctrl+C part of
;; task 2.5). Its handle asks for the session's private workspace, so that the host creates the directory, and then
;; loops forever. Only a bound or an interruption ends the call. Written for the tests of morphir-dotnet; not part
;; of the pinned corpus. The .wasm beside this file is Wasmtime's conversion of this text; a test in
;; Morphir.Tooling.Tests checks it.
(module
  (import "extism:host/user" "morphir_get_workspace_info" (func $workspace_info (result i64)))
  (func (export "handle") (result i32)
    (drop (call $workspace_info))
    (loop $spin (br $spin))
    (i32.const 1)))