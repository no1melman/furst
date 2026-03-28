module Furst.Tests.Compile.DependencyTests

open Xunit
open Furst.Tests.CompileHelper

[<Fact>]
let ``Manifest symbols are resolvable via open`` () =
    let result = compileWithManifest
                    [| "Dep.Helpers.greet 1" |]
                    [
                        "main.fu", """
open Dep.Helpers

let run =
  greet 42
"""
                    ]
    match result with
    | Ok _ -> ()
    | Error msg -> Assert.Fail($"Should compile with manifest: {msg}")

[<Fact>]
let ``Manifest symbols are resolvable via qualified name`` () =
    let result = compileWithManifest
                    [| "Dep.Helpers.greet 1" |]
                    [
                        "main.fu", """
let run =
  Dep.Helpers.greet 42
"""
                    ]
    match result with
    | Ok _ -> ()
    | Error msg -> Assert.Fail($"Should compile with qualified manifest access: {msg}")
