module Furst.Tests.Compile.VisibilityErrorTests

open Xunit
open Furst.Tests.CompileHelper

[<Fact(Skip = "Needs forward-ref checking for let bindings after parser fix")>]
let ``Private function in another module is not accessible`` () =
    let result = compileSource [
        "internal.fu", """
mod Secret

private let hidden =
  42
"""
        "main.fu", """
let run =
  Secret.hidden
"""
    ]
    match result with
    | Ok _ -> Assert.Fail("Expected error for private cross-module access")
    | Error msg -> Assert.Contains("forward reference", msg)
