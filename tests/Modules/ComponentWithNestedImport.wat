;; A component that imports a nested instance "math" with function "double" (s32) -> (s32)
;; and exports a function "quadruple" that calls math.double twice.
(component
  (import "math" (instance $math
    (export "double" (func (param "a" s32) (result s32)))
  ))

  (alias export $math "double" (func $double))

  (core func $double-lowered (canon lower (func $double)))

  (core module $m
    (import "" "double" (func $double (param i32) (result i32)))

    (func (export "quadruple") (param i32) (result i32)
      local.get 0
      call $double
      call $double
    )
  )

  (core instance $i (instantiate $m
    (with "" (instance
      (export "double" (func $double-lowered))
    ))
  ))

  (func (export "quadruple") (param "a" s32) (result s32)
    (canon lift (core func $i "quadruple"))
  )
)
