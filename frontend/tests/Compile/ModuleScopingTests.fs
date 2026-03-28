module Furst.Tests.ModuleScopingTests

open Xunit
open Furst.Tests.CompileHelper

[<Fact>]
let ``Qualified access resolves cross-module function`` () =
    let result = compileSource [
        "math.fu", """
let add x y =
  x + y
"""
        "main.fu", """
let run =
  Math.add 1 2
"""
    ]
    match result with
    | Ok _ -> ()
    | Error msg -> Assert.Fail($"Should compile: {msg}")

[<Fact>]
let ``Open brings module symbols into scope by short name`` () =
    let result = compileSource [
        "math.fu", """
mod Math

let add x y =
  x + y
"""
        "main.fu", """
open Math

let run =
  add 1 2
"""
    ]
    match result with
    | Ok _ -> ()
    | Error msg -> Assert.Fail($"Should compile: {msg}")

[<Fact>]
let ``Qualified access works without open`` () =
    let result = compileSource [
        "math.fu", """
mod Math

let add x y =
  x + y
"""
        "main.fu", """
let run =
  Math.add 1 2
"""
    ]
    match result with
    | Ok _ -> ()
    | Error msg -> Assert.Fail($"Should compile: {msg}")

[<Fact>]
let ``Additive mod merging — two files contribute to same mod`` () =
    let result = compileSource [
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

let run =
  add 1 (sub 3 2)
"""
    ]
    match result with
    | Ok _ -> ()
    | Error msg -> Assert.Fail($"Should compile: {msg}")

[<Fact>]
let ``Same-module functions accessible by short name`` () =
    let result = compileSource [
        "app.fu", """
let helper x =
  x + 1

let run =
  helper 5
"""
    ]
    match result with
    | Ok _ -> ()
    | Error msg -> Assert.Fail($"Should compile: {msg}")
