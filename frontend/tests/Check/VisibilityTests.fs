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
    let result = Compiler.compileFiles None paths []
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
let ``Unqualified name without open fails`` () =
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
let ``Open is shallow — does not bring sub-module symbols`` () =
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

let private compileWithManifest (manifestLines: string array) (files: (string * string) list) =
    let tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString())
    System.IO.Directory.CreateDirectory(tmpDir) |> ignore
    let srcDir = System.IO.Path.Combine(tmpDir, "src")
    System.IO.Directory.CreateDirectory(srcDir) |> ignore
    let manifestPath = System.IO.Path.Combine(tmpDir, "libdep.fsi")
    System.IO.File.WriteAllLines(manifestPath, manifestLines)
    let paths =
        files |> List.map (fun (name, source) ->
            let path = System.IO.Path.Combine(srcDir, name)
            System.IO.File.WriteAllText(path, source)
            path)
    let result = Compiler.compileFiles None paths [manifestPath]
    try System.IO.Directory.Delete(tmpDir, true) with _ -> ()
    result

[<Fact>]
let ``Manifest symbols are resolvable via open`` () =
    let result = compileWithManifest
                    [| "Dep.Helpers.greet 1" |]
                    [ "main.fu", "open Dep.Helpers\n\nlet run =\n  greet 42\n" ]
    match result with
    | Ok _ -> ()
    | Error msg -> Assert.Fail($"Should compile with manifest: {msg}")

[<Fact>]
let ``Manifest symbols are resolvable via qualified name`` () =
    let result = compileWithManifest
                    [| "Dep.Helpers.greet 1" |]
                    [ "main.fu", "let run =\n  Dep.Helpers.greet 42\n" ]
    match result with
    | Ok _ -> ()
    | Error msg -> Assert.Fail($"Should compile with qualified manifest access: {msg}")
