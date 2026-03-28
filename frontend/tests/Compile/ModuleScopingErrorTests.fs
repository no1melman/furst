module Furst.Tests.ModuleScopingErrorTests

open Xunit
open Furst.Tests.CompileHelper

[<Fact>]
let ``Unqualified cross-module name without open is rejected`` () =
    let result = compileSource [
        "math.fu", """
mod Math

let add x y =
  x + y
"""
        "main.fu", """
let run =
  add 1 2
"""
    ]
    match result with
    | Ok _ -> Assert.Fail("Expected error for unqualified cross-module access without open")
    | Error msg -> Assert.Contains("forward reference", msg)

[<Fact>]
let ``Open is shallow — does not bring sub-module symbols into scope`` () =
    let result = compileSource [
        "deep.fu", """
mod Collections.List

let map x =
  x
"""
        "main.fu", """
open Collections

let run =
  map 1
"""
    ]
    match result with
    | Ok _ -> Assert.Fail("Expected error — open is shallow, should not resolve sub-module")
    | Error msg -> Assert.Contains("forward reference", msg)

[<Fact>]
let ``Forward reference to undeclared function is rejected`` () =
    let result = compileSource [
        "app.fu", """
let run =
  helper 5

let helper x =
  x + 1
"""
    ]
    match result with
    | Ok _ -> Assert.Fail("Expected forward reference error")
    | Error msg -> Assert.Contains("forward reference", msg)

[<Fact>]
let ``Duplicate function in same module is rejected`` () =
    let result = compileSource [
        "app.fu", """
let foo =
  1

let foo =
  2
"""
    ]
    match result with
    | Ok _ -> Assert.Fail("Expected duplicate symbol error")
    | Error msg -> Assert.Contains("duplicate", msg)
