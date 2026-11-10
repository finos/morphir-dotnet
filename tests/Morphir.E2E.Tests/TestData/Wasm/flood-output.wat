;; A guest for the worker's guest-output bound (add-wasm-extension-host, task 4.1, which closes the output part of
;; task 2.6). Each handle call writes 10 MiB to WASI stdout in 10,240 writes of 1 KiB, then logs 10 MiB through
;; log_info in 10,240 messages of 1 KiB, then answers {"jsonrpc":"2.0","id":1,"result":"flooded"}
;; whatever the request. Written for the tests of morphir-dotnet; not part of the pinned corpus.
;; The .wasm beside this file is Wasmtime's conversion of this text; a test in Morphir.Tooling.Tests checks it.
(module
  (import "extism:host/env" "alloc" (func $alloc (param i64) (result i64)))
  (import "extism:host/env" "store_u8" (func $store_u8 (param i64 i32)))
  (import "extism:host/env" "output_set" (func $output_set (param i64 i64)))
  (import "extism:host/env" "log_info" (func $log_info (param i64)))
  (import "wasi_snapshot_preview1" "fd_write" (func $fd_write (param i32 i32 i32 i32) (result i32)))
  (memory (export "memory") 1)
  ;; One iovec at 0: 1,024 bytes at 1024. fd_write stores the count it wrote at 16.
  (data (i32.const 0) "\00\04\00\00\00\04\00\00")
  (data (i32.const 1024) "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")
  (data (i32.const 4096) "{\22jsonrpc\22:\222.0\22,\22id\22:1,\22result\22:\22flooded\22}")
  ;; Copies $len bytes of guest memory at $ptr into a new kernel block.
  (func $block (param $ptr i32) (param $len i32) (result i64)
    (local $h i64) (local $i i32)
    (local.set $h (call $alloc (i64.extend_i32_u (local.get $len))))
    (block $copied
      (loop $copy
        (br_if $copied (i32.ge_u (local.get $i) (local.get $len)))
        (call $store_u8
          (i64.add (local.get $h) (i64.extend_i32_u (local.get $i)))
          (i32.load8_u (i32.add (local.get $ptr) (local.get $i))))
        (local.set $i (i32.add (local.get $i) (i32.const 1)))
        (br $copy)))
    (local.get $h))
  (func (export "handle") (result i32)
    (local $n i32) (local $h i64)
    (block $written
      (loop $write
        (br_if $written (i32.ge_u (local.get $n) (i32.const 10240)))
        (drop (call $fd_write (i32.const 1) (i32.const 0) (i32.const 1) (i32.const 16)))
        (local.set $n (i32.add (local.get $n) (i32.const 1)))
        (br $write)))
    (local.set $h (call $block (i32.const 1024) (i32.const 1024)))
    (local.set $n (i32.const 0))
    (block $logged
      (loop $log
        (br_if $logged (i32.ge_u (local.get $n) (i32.const 10240)))
        (call $log_info (local.get $h))
        (local.set $n (i32.add (local.get $n) (i32.const 1)))
        (br $log)))
    (call $output_set (call $block (i32.const 4096) (i32.const 43)) (i64.const 43))
    (i32.const 0)))