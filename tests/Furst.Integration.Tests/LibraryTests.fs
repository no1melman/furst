module Furst.Integration.LibraryTests

open Xunit
open Furst.Integration.TestHelper

[<Fact>]
let ``Library project builds successfully`` () =
    let fixture = createLibraryProject "mylib" "MyLib" [
        "utils.fu", """
let double x =
  x + x
"""
    ]
    let result = buildProject fixture
    cleanup fixture
    Assert.Equal(0, result.ExitCode)
