;; Closes the WASI preview 1 imports of a wasi-libc guest so that only the
;; morphir_bytes_v1 imports stay. No environment, a clock that stays at 0,
;; zero bytes for random_get, no output, and a trap for proc_exit.
(module
  (import "guest" "memory" (memory 0))
  (func (export "environ_sizes_get") (param $count i32) (param $size i32) (result i32)
    (i32.store (local.get $count) (i32.const 0))
    (i32.store (local.get $size) (i32.const 0))
    (i32.const 0))
  (func (export "environ_get") (param i32 i32) (result i32) (i32.const 0))
  (func (export "args_sizes_get") (param $count i32) (param $size i32) (result i32)
    (i32.store (local.get $count) (i32.const 0))
    (i32.store (local.get $size) (i32.const 0))
    (i32.const 0))
  (func (export "args_get") (param i32 i32) (result i32) (i32.const 0))
  (func (export "clock_time_get") (param i32 i64 i32) (result i32)
    (i64.store (local.get 2) (i64.const 0))
    (i32.const 0))
  (func (export "random_get") (param $buf i32) (param $len i32) (result i32)
    (memory.fill (local.get $buf) (i32.const 0) (local.get $len))
    (i32.const 0))
  (func (export "fd_write") (param i32 i32 i32 i32) (result i32) (i32.const 8)) ;; EBADF
  (func (export "proc_exit") (param i32) (unreachable)))
