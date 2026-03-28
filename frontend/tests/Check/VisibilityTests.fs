module Furst.Tests.VisibilityTests

open Xunit
open Types

/// Helper: write source to temp files, compile via Compiler.compileFiles
let private compileSource (files: (string * string) list) =
    let tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString())
    System.IO.Directory.CreateDirectory(tmpDir) |> ignore
    let srcDir = System.IO.Path.Combine(tmpDir, "src")
    System.IO.Directory.CreateDirectory(srcDir) |> ignore
    let paths =
        files |> List.map (fun (name, source) ->
            let path = System.IO.Path.Combine(srcDir, name)
            System.IO.File.WriteAllText(path, source)
            path)
    let result = Compiler.compileFiles None paths
    try System.IO.Directory.Delete(tmpDir, true) with _ -> ()
    result

[<Fact>]
let ``Public function is callable from another module via qualified name`` () =
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

[<Fact>]
let ``Private function in another module is not callable`` () =
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

[<Fact>]
let ``Default visibility is public`` () =
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
