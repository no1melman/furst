module BasicTypes

type TypeDefinitions =
  | I32
  | I64
  | Float
  | Double
  | String
  | Inferred
//| UserDefined of string

let i32Type = "i32"
let i64Type = "i64"
let doubleType = "double"
let floatType = "float"
let stringType = "string"
