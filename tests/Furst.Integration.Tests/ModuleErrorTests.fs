module Furst.Integration.ModuleErrorTests

open Xunit
open Furst.Integration.TestHelper

[<Fact>]
let ``Unqualified cross-module access without open fails build`` () =
    let fixture = createProject "noqual" "executable" [
        "math.fu", """
mod Math

let add x y =
  x + y
"""
        "main.fu", """
let main =
  add 1 2
"""
    ]
    let result = buildProject fixture
    cleanup fixture
    Assert.NotEqual(0, result.ExitCode)

[<Fact>]
let ``Private cross-module access fails build`` () =
    let fixture = createProject "privxmod" "executable" [
        "secret.fu", """
mod Secret

private let hidden =
  42
"""
        "main.fu", """
let main =
  Secret.hidden
"""
    ]
    let result = buildProject fixture
    cleanup fixture
    Assert.NotEqual(0, result.ExitCode)

[<Fact>]
let ``Forward reference fails build`` () =
    let fixture = createProject "fwdref" "executable" [
        "main.fu", """
let main =
  helper 5

let helper x =
  x + 1
"""
    ]
    let result = buildProject fixture
    cleanup fixture
    Assert.NotEqual(0, result.ExitCode)
