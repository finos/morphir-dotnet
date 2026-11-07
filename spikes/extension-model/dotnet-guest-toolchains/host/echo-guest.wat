;; Reference guest: echoes the input. Checks the test host only.
(module
  (import "morphir_bytes_v1" "input_len" (func $len (result i32)))
  (import "morphir_bytes_v1" "input_byte" (func $in (param i32) (result i32)))
  (import "morphir_bytes_v1" "output_byte" (func $out (param i32)))
  (func (export "handle") (result i32) (local $i i32)
    (block $done (loop $l
      (br_if $done (i32.ge_u (local.get $i) (call $len)))
      (call $out (call $in (local.get $i)))
      (local.set $i (i32.add (local.get $i) (i32.const 1)))
      (br $l)))
    (call $len)))
