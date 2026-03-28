module Furst.Tests.GenerateFixtures

open System.IO
open Xunit
open Types
open Ast
open AstBuilder
open Lowered

let private fixtureDir =
    Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "backend", "tests", "fixtures")

let private lowerWithPath (modPath: string list) (source: string) =
    let fileName = "test.fu"
    match Lexer.createAST fileName source with
    | Error e -> failwith $"Parse error: {e}"
    | Ok rows ->
        let results = rows |> List.map rowToExpression
        let errors = results |> List.choose (function Error e -> Some e.Message | _ -> None)
        if not errors.IsEmpty then
            let msg = System.String.Join("; ", errors)
            failwith $"AST errors: {msg}"
        let nodes = results |> List.choose (function Ok n -> Some n | _ -> None)
        Pipeline.lower (Types.ModulePath modPath) nodes

let private lower (source: string) = lowerWithPath [] source

let private writeFixture (name: string) (source: string) =
    Directory.CreateDirectory(fixtureDir) |> ignore
    let outputPath = Path.Combine(fixtureDir, name + ".fso")
    let defs = lower source
    FsoWriter.writeFso outputPath (name + ".fu") defs

[<Fact>]
let ``Generate fixture: simple_add`` () =
    writeFixture "simple_add" """
let add x y =
  x + y
"""

[<Fact>]
let ``Generate fixture: literal_int`` () =
    writeFixture "literal_int" """
let x = 42
"""

[<Fact>]
let ``Generate fixture: nested_function`` () =
    writeFixture "nested_function" """
let outer x =
  let inner y =
    y
  inner 5
"""

[<Fact>]
let ``Generate fixture: captured_param`` () =
    writeFixture "captured_param" """
let outer x =
  let inner y =
    x + y
  inner 5
"""

[<Fact>]
let ``Generate fixture: multi_function`` () =
    writeFixture "multi_function" """
let foo a =
  a

let bar b =
  b
"""

[<Fact>]
let ``Generate fixture: chained_ops`` () =
    writeFixture "chained_ops" """
let f a b c =
  a + b + c
"""

[<Fact>]
let ``Generate fixture: let_binding`` () =
    writeFixture "let_binding" """
let f x =
  let y = x + 1
  y
"""

[<Fact>]
let ``Generate fixture: return_42`` () =
    writeFixture "return_42" """
let main =
  42
"""

[<Fact>]
let ``Generate fixture: add_and_return`` () =
    writeFixture "add_and_return" """
let add x y =
  x + y

let main =
  add 13 29
"""

[<Fact>]
let ``Generate fixture: big_script`` () =
    writeFixture "big_script" """
let compute a =
  let step1 x =
    x + 1
  let step2 y =
    y + 2
  step1 a

let main n =
  compute n
"""

let private writeFixtureWithPath (name: string) (modPath: string list) (source: string) =
    Directory.CreateDirectory(fixtureDir) |> ignore
    let outputPath = Path.Combine(fixtureDir, name + ".fso")
    let defs = lowerWithPath modPath source
    FsoWriter.writeFso outputPath (name + ".fu") defs

[<Fact>]
let ``Generate fixture: module_path`` () =
    writeFixtureWithPath "module_path" ["Math"] """
let add x y =
  x + y

private let helper x =
  x + 1
"""
