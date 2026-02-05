;; A component that imports a host function "add-one" (s32) -> (s32)
;; and exports a function "add-two" that calls add-one twice.
(component
  (import "add-one" (func $add-one (param "a" s32) (result s32)))

  (core func $add-one-lowered (canon lower (func $add-one)))

  (core module $m
    (import "" "add-one" (func $add-one (param i32) (result i32)))

    (func (export "add-two") (param i32) (result i32)
      local.get 0
      call $add-one
      call $add-one
    )
  )

  (core instance $i (instantiate $m
    (with "" (instance
      (export "add-one" (func $add-one-lowered))
    ))
  ))

  (func (export "add-two") (param "a" s32) (result s32)
    (canon lift (core func $i "add-two"))
  )
)
