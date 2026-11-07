;; A morphir-bytes-v1 probe guest for the WASM runtime spike.
;;
;; Imports: exactly the three morphir-bytes-v1 byte callbacks.
;; Export:  handle () -> i32.
;;
;; The first input byte selects the behavior:
;;   (no input)  return 0 (exit).
;;   '!'         loop forever. The host must stop the call with fuel or a deadline.
;;   '^'         grow memory by 4096 pages (256 MiB). The total is then above the
;;               256 MiB ceiling, so a host that applies the ceiling makes the
;;               grow fail. The response is "N" (refused) or "Y" (grown).
;;   '+'         grow memory by 1024 pages (64 MiB), below the ceiling.
;;               The response is "Y" or "N".
;;   other       echo the input and return its length.
(module
  (import "morphir_bytes_v1" "input_len" (func $input_len (result i32)))
  (import "morphir_bytes_v1" "input_byte" (func $input_byte (param i32) (result i32)))
  (import "morphir_bytes_v1" "output_byte" (func $output_byte (param i32)))
  (memory 1)
  (func $grow (param $pages i32) (result i32)
    (if (i32.eq (memory.grow (local.get $pages)) (i32.const -1))
      (then (call $output_byte (i32.const 78)))   ;; 'N'
      (else (call $output_byte (i32.const 89))))  ;; 'Y'
    (i32.const 1))
  (func (export "handle") (result i32)
    (local $n i32) (local $i i32) (local $c i32)
    (local.set $n (call $input_len))
    (if (i32.eqz (local.get $n)) (then (return (i32.const 0))))
    (local.set $c (call $input_byte (i32.const 0)))
    (if (i32.eq (local.get $c) (i32.const 33))   ;; '!'
      (then (loop $spin (br $spin))))
    (if (i32.eq (local.get $c) (i32.const 94))   ;; '^'
      (then (return (call $grow (i32.const 4096)))))
    (if (i32.eq (local.get $c) (i32.const 43))   ;; '+'
      (then (return (call $grow (i32.const 1024)))))
    (block $done
      (loop $echo
        (br_if $done (i32.ge_u (local.get $i) (local.get $n)))
        (call $output_byte (call $input_byte (local.get $i)))
        (local.set $i (i32.add (local.get $i) (i32.const 1)))
        (br $echo)))
    (local.get $n)))
