module Furst.Integration.ExecutableTests

open Xunit
open Furst.Integration.TestHelper

[<Fact>]
let ``Simple executable returns main value as exit code`` () =
    let fixture = createProject "simple" "executable" [
        "main.fu", """
let main =
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

let main =
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
let main =
  Math.add 20 22
"""
    ]
    let result = runProject fixture
    cleanup fixture
    Assert.Equal(42, result.ExitCode)
