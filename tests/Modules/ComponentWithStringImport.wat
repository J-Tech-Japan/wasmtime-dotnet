;; A component that imports "reverse-string" (string) -> (string)
;; and exports "process" that calls reverse-string.
;;
;; The canonical ABI for string->string lowering uses:
;;   (param i32 i32 i32) where the third i32 is the retptr
;;   The function writes (ptr, len) to the retptr and has no return.
(component
  (import "reverse-string" (func $reverse-string (param "input" string) (result string)))

  ;; Shared memory module for canonical ABI
  (core module $mem_mod
    (memory (export "memory") 1)

    (global $offset (mut i32) (i32.const 1024))
    (func (export "realloc") (param $old_ptr i32) (param $old_size i32) (param $align i32) (param $new_size i32) (result i32)
      (local $ret i32)
      (local.set $ret
        (i32.and
          (i32.add (global.get $offset) (i32.sub (local.get $align) (i32.const 1)))
          (i32.xor (i32.sub (local.get $align) (i32.const 1)) (i32.const -1))
        )
      )
      (global.set $offset (i32.add (local.get $ret) (local.get $new_size)))
      (local.get $ret)
    )
  )

  (core instance $mem (instantiate $mem_mod))

  ;; Canon lower the imported function using shared memory
  (core func $reverse-string-lowered (canon lower
    (func $reverse-string)
    (memory $mem "memory")
    (realloc (func $mem "realloc"))
  ))

  ;; Main module that uses the imported function
  ;; Lowered string->string: (param i32 i32 i32) with no result
  ;; The third param is the retptr where (ptr, len) is written
  (core module $main
    (import "env" "memory" (memory 1))
    (import "env" "realloc" (func $realloc (param i32 i32 i32 i32) (result i32)))
    (import "env" "reverse-string" (func $reverse-string (param i32 i32 i32)))

    ;; process: lowered as (param i32 i32) (result i32)
    ;; returns retptr where (ptr, len) pair is stored
    (func (export "process") (param $ptr i32) (param $len i32) (result i32)
      (local $retarea i32)
      ;; Allocate 8 bytes for the return (ptr, len) pair
      (local.set $retarea (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 8)))
      ;; Call reverse-string with retptr
      (call $reverse-string (local.get $ptr) (local.get $len) (local.get $retarea))
      ;; Return retptr
      (local.get $retarea)
    )
  )

  (core instance $i (instantiate $main
    (with "env" (instance
      (export "memory" (memory $mem "memory"))
      (export "realloc" (func $mem "realloc"))
      (export "reverse-string" (func $reverse-string-lowered))
    ))
  ))

  (func (export "process") (param "input" string) (result string)
    (canon lift (core func $i "process") (memory $mem "memory") (realloc (func $mem "realloc")))
  )
)
