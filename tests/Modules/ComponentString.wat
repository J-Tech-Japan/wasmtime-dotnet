(component
  (core module $m
    (memory (export "memory") 1)

    ;; Bump allocator with alignment support
    (global $offset (mut i32) (i32.const 1024))
    (func $alloc (export "realloc") (param $old_ptr i32) (param $old_size i32) (param $align i32) (param $new_size i32) (result i32)
      (local $ret i32)
      ;; Align offset: ret = (offset + align - 1) & ~(align - 1)
      (local.set $ret
        (i32.and
          (i32.add (global.get $offset) (i32.sub (local.get $align) (i32.const 1)))
          (i32.xor (i32.sub (local.get $align) (i32.const 1)) (i32.const -1))
        )
      )
      (global.set $offset (i32.add (local.get $ret) (local.get $new_size)))
      (local.get $ret)
    )

    ;; string-length: returns the byte length of a string
    (func (export "string-length") (param $ptr i32) (param $len i32) (result i32)
      (local.get $len)
    )

    ;; greet: takes a name string, returns "Hello, " + name
    ;; canonical ABI lowered: (param i32 i32) (result i32)
    ;; The single i32 result is a pointer to a (ptr: i32, len: i32) pair in linear memory
    (func (export "greet") (param $name_ptr i32) (param $name_len i32) (result i32)
      (local $out_ptr i32)
      (local $out_len i32)
      (local $hello_len i32)
      (local $retarea i32)

      ;; "Hello, " is 7 bytes
      (local.set $hello_len (i32.const 7))
      (local.set $out_len (i32.add (local.get $hello_len) (local.get $name_len)))

      ;; Allocate output string buffer
      (local.set $out_ptr (call $alloc (i32.const 0) (i32.const 0) (i32.const 1) (local.get $out_len)))

      ;; Copy "Hello, " (H=0x48, e=0x65, l=0x6c, l=0x6c, o=0x6f, ,=0x2c, ' '=0x20)
      (i32.store8 (local.get $out_ptr) (i32.const 0x48))
      (i32.store8 (i32.add (local.get $out_ptr) (i32.const 1)) (i32.const 0x65))
      (i32.store8 (i32.add (local.get $out_ptr) (i32.const 2)) (i32.const 0x6c))
      (i32.store8 (i32.add (local.get $out_ptr) (i32.const 3)) (i32.const 0x6c))
      (i32.store8 (i32.add (local.get $out_ptr) (i32.const 4)) (i32.const 0x6f))
      (i32.store8 (i32.add (local.get $out_ptr) (i32.const 5)) (i32.const 0x2c))
      (i32.store8 (i32.add (local.get $out_ptr) (i32.const 6)) (i32.const 0x20))

      ;; Copy the name using memory.copy
      (memory.copy
        (i32.add (local.get $out_ptr) (local.get $hello_len))
        (local.get $name_ptr)
        (local.get $name_len)
      )

      ;; Allocate 8 bytes for the return (ptr, len) pair
      (local.set $retarea (call $alloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 8)))
      (i32.store (local.get $retarea) (local.get $out_ptr))
      (i32.store offset=4 (local.get $retarea) (local.get $out_len))

      ;; Return pointer to the (ptr, len) pair
      (local.get $retarea)
    )
  )

  (core instance $i (instantiate $m))

  (func (export "string-length") (param "input" string) (result u32)
    (canon lift (core func $i "string-length") (memory $i "memory") (realloc (func $i "realloc")))
  )

  (func (export "greet") (param "name" string) (result string)
    (canon lift (core func $i "greet") (memory $i "memory") (realloc (func $i "realloc")))
  )
)
