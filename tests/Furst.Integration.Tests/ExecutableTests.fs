module Furst.Integration.ExecutableTests

open Xunit
open Furst.Integration.TestHelper

[<Fact>]
let ``Simple executable returns main value as exit code`` () =
    let fixture = createProject "simple" "executable" [
        "main.fu", """
let main args =
  42
"""
    ]
    let result = runProject fixture
    cleanup fixture
    Assert.Equal(42, result.ExitCode)

[<Fact>]
let ``Executable with function call returns computed result`` () =
    let fixture = createProject "withcall" "executable" [
        "main.fu", """
let add x y =
  x + y

let main args =
  add 13 29
"""
    ]
    let result = runProject fixture
    cleanup fixture
    Assert.Equal(42, result.ExitCode)

[<Fact>]
let ``Multi-file executable with cross-file call`` () =
    let fixture = createProject "multifile" "executable" [
        "math.fu", """
let add x y =
  x + y
"""
        "main.fu", """
let main args =
  Math.add 20 22
"""
    ]
    let result = runProject fixture
    cleanup fixture
    Assert.Equal(42, result.ExitCode)

// -- Type system: typed parameters --

[<Fact>]
let ``Typed i32 function compiles and returns correct result`` () =
    let fixture = createProject "typed_i32" "executable" [
        "main.fu", """
let sum (a: i32) (b: i32) =
  a + b

let main args =
  sum 20 22
"""
    ]
    let result = runProject fixture
    cleanup fixture
    Assert.Equal(42, result.ExitCode)

[<Fact>]
let ``Typed i32 function with chained ops`` () =
    let fixture = createProject "typed_chain" "executable" [
        "main.fu", """
let calc (a: i32) (b: i32) (c: i32) =
  a + b + c

let main args =
  calc 10 20 12
"""
    ]
    let result = runProject fixture
    cleanup fixture
    Assert.Equal(42, result.ExitCode)

[<Fact>]
let ``Mixed typed and inferred params`` () =
    let fixture = createProject "mixed_params" "executable" [
        "main.fu", """
let add (a: i32) b =
  a + b

let main args =
  add 20 22
"""
    ]
    let result = runProject fixture
    cleanup fixture
    Assert.Equal(42, result.ExitCode)

// -- Type system: inferred types --

[<Fact>]
let ``Inferred function called from typed context`` () =
    let fixture = createProject "inferred_ctx" "executable" [
        "main.fu", """
let add x y =
  x + y

let apply (a: i32) (b: i32) =
  add a b

let main args =
  apply 20 22
"""
    ]
    let result = runProject fixture
    cleanup fixture
    Assert.Equal(42, result.ExitCode)

[<Fact>]
let ``Inferred let binding in function body`` () =
    let fixture = createProject "inferred_let" "executable" [
        "main.fu", """
let compute (a: i32) (b: i32) =
  let sum = a + b
  sum

let main args =
  compute 20 22
"""
    ]
    let result = runProject fixture
    cleanup fixture
    Assert.Equal(42, result.ExitCode)

[<Fact>]
let ``Double arithmetic in non-main function`` () =
    let fixture = createProject "double_arith" "executable" [
        "main.fu", """
let addFloats x y =
  x + y

let main args =
  let r = addFloats 1.5 2.5
  42
"""
    ]
    let result = runProject fixture
    cleanup fixture
    Assert.Equal(42, result.ExitCode)

[<Fact>]
let ``Returning double from main fails compilation`` () =
    let fixture = createProject "double_main" "executable" [
        "main.fu", """
let addFloats x y =
  x + y

let main args =
  addFloats 20.5 21.5
"""
    ]
    let result = buildProject fixture
    cleanup fixture
    Assert.NotEqual(0, result.ExitCode)
