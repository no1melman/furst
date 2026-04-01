module Furst.Tests.Compile.EntryPointTests

open Xunit
open Types
open Lowered
open Furst.Tests.CompileHelper

[<Fact>]
let ``Entry point main has empty module path in executable`` () =
    let result = compileSource [
        "main.fu", """
let main args =
  42
"""
    ]
    match result with
    | Ok defs ->
        let mainFn =
            defs |> List.tryPick (function
                | TopFunction fn when fn.Name = "main" -> Some fn
                | _ -> None)
        Assert.True(mainFn.IsSome, "Expected main function")
        let (ModulePath parts) = mainFn.Value.ModulePath
        Assert.Empty(parts)
    | Error msg -> Assert.Fail($"Should compile: {msg}")

[<Fact>]
let ``Non-main functions keep module path in executable`` () =
    let result = compileSource [
        "main.fu", """
let helper =
  1

let main args =
  helper
"""
    ]
    match result with
    | Ok defs ->
        let helperFn =
            defs |> List.tryPick (function
                | TopFunction fn when fn.Name = "helper" -> Some fn
                | _ -> None)
        Assert.True(helperFn.IsSome, "Expected helper function")
        let (ModulePath parts) = helperFn.Value.ModulePath
        Assert.NotEmpty(parts)
    | Error msg -> Assert.Fail($"Should compile: {msg}")

[<Fact>]
let ``Library project does not strip main module path`` () =
    let result = compileSourceAsLibrary "MyLib" [
        "main.fu", """
let main args =
  42
"""
    ]
    match result with
    | Ok defs ->
        let mainFn =
            defs |> List.tryPick (function
                | TopFunction fn when fn.Name = "main" -> Some fn
                | _ -> None)
        Assert.True(mainFn.IsSome, "Expected main function")
        let (ModulePath parts) = mainFn.Value.ModulePath
        Assert.NotEmpty(parts)
    | Error msg -> Assert.Fail($"Should compile: {msg}")
