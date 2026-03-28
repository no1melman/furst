module Furst.Integration.ModuleTests

open Xunit
open Furst.Integration.TestHelper

[<Fact>]
let ``Explicit mod with qualified access`` () =
    let fixture = createProject "modqual" "executable" [
        "math.fu", """
mod Math

let add x y =
  x + y
"""
        "main.fu", """
let main =
  Math.add 20 22
"""
    ]
    let result = runProject fixture
    cleanup fixture
    Assert.Equal(42, result.ExitCode)

[<Fact>]
let ``Open brings mod into scope for short name access`` () =
    let fixture = createProject "modopen" "executable" [
        "math.fu", """
mod Math

let double x =
  x + x
"""
        "main.fu", """
open Math

let main =
  double 21
"""
    ]
    let result = runProject fixture
    cleanup fixture
    Assert.Equal(42, result.ExitCode)

[<Fact>]
let ``Additive mod merging across files`` () =
    let fixture = createProject "additive" "executable" [
        "math_add.fu", """
mod Math

let add x y =
  x + y
"""
        "math_sub.fu", """
mod Math

let sub x y =
  x - y
"""
        "main.fu", """
open Math

let main =
  add 40 (sub 5 3)
"""
    ]
    let result = runProject fixture
    cleanup fixture
    Assert.Equal(42, result.ExitCode)

[<Fact>]
let ``Private function accessible within same mod`` () =
    let fixture = createProject "private" "executable" [
        "main.fu", """
private let secret x =
  x + 1

let main =
  secret 41
"""
    ]
    let result = runProject fixture
    cleanup fixture
    Assert.Equal(42, result.ExitCode)
