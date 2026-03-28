module Furst.Tests.Compile.VisibilityTests

open Xunit
open Furst.Tests.CompileHelper

[<Fact>]
let ``Default visibility is public — cross-module qualified access works`` () =
    let result = compileSource [
        "lib.fu", """
mod Helpers

let double x =
  x + x
"""
        "main.fu", """
let run =
  Helpers.double 5
"""
    ]
    match result with
    | Ok _ -> ()
    | Error msg -> Assert.Fail($"Should compile: {msg}")

[<Fact>]
let ``Private function is callable within same module`` () =
    let result = compileSource [
        "app.fu", """
private let helper x =
  x + 1

let run =
  helper 5
"""
    ]
    match result with
    | Ok _ -> ()
    | Error msg -> Assert.Fail($"Should compile: {msg}")
