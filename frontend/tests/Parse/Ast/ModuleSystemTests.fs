module ModuleSystemTests

open Xunit
open Types
open Ast
open AstBuilder
open Lexer

[<Fact>]
let ``Header-style mod has empty body`` () =
    let source = """
mod Foo
"""

    match createAST "test" source with
    | Error error -> Assert.Fail($"Parse failed: {error}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error error -> Assert.Fail($"AST build failed: {error.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | ModuleDeclaration (parts, body) ->
                Assert.Equal<string list>(["Foo"], parts)
                Assert.Empty(body)
            | _ -> Assert.Fail("Expected ModuleDeclaration")

[<Fact>]
let ``Open declaration should parse to OpenDeclaration`` () =
    let source = """
open Foo.Bar
"""

    match createAST "test" source with
    | Error error -> Assert.Fail($"Parse failed: {error}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error error -> Assert.Fail($"AST build failed: {error.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | OpenDeclaration parts ->
                Assert.Equal<string list>(["Foo"; "Bar"], parts)
            | _ -> Assert.Fail("Expected OpenDeclaration")

[<Fact>]
let ``Mod with scoped body parses let binding`` () =
    let source = """
mod Foo
  let x = 1
"""

    match createAST "test" source with
    | Error error -> Assert.Fail($"Parse failed: {error}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error error -> Assert.Fail($"AST build failed: {error.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | ModuleDeclaration (parts, body) ->
                Assert.Equal<string list>(["Foo"], parts)
                Assert.Single(body) |> ignore
                match body.Head with
                | LetBindingExpression lb -> Assert.Equal("x", lb.Name)
                | _ -> Assert.Fail("Expected LetBindingExpression in body")
            | _ -> Assert.Fail("Expected ModuleDeclaration")

[<Fact>]
let ``Dotted mod with body`` () =
    let source = """
mod Api.Types
  let y = 2
"""

    match createAST "test" source with
    | Error error -> Assert.Fail($"Parse failed: {error}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error error -> Assert.Fail($"AST build failed: {error.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | ModuleDeclaration (parts, body) ->
                Assert.Equal<string list>(["Api"; "Types"], parts)
                Assert.Single(body) |> ignore
            | _ -> Assert.Fail("Expected ModuleDeclaration")

[<Fact>]
let ``Mod with function body`` () =
    let source = """
mod Math
  let add x y =
    x + y
"""

    match createAST "test" source with
    | Error error -> Assert.Fail($"Parse failed: {error}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error error -> Assert.Fail($"AST build failed: {error.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | ModuleDeclaration (parts, body) ->
                Assert.Equal<string list>(["Math"], parts)
                Assert.Single(body) |> ignore
                match body.Head with
                | FunctionDefinitionExpression _ -> ()
                | _ -> Assert.Fail("Expected FunctionDefinitionExpression in body")
            | _ -> Assert.Fail("Expected ModuleDeclaration")

[<Fact>]
let ``Sibling mods each have scoped bodies`` () =
    let source = """
mod Foo
  let x = 1
mod Bar
  let y = 2
"""

    match createAST "test" source with
    | Error error -> Assert.Fail($"Parse failed: {error}")
    | Ok rows ->
        Assert.Equal(2, rows.Length)
        let results = rows |> List.map rowToExpression
        for r in results do
            match r with
            | Error e -> Assert.Fail($"AST build failed: {e.Message}")
            | Ok node ->
                match node.Expr with
                | ModuleDeclaration (_, body) -> Assert.Single(body) |> ignore
                | _ -> Assert.Fail("Expected ModuleDeclaration")

[<Fact>]
let ``Lib declaration parses single name`` () =
    let source = """
lib Furst
"""
    match createAST "test" source with
    | Error error -> Assert.Fail($"Parse failed: {error}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error error -> Assert.Fail($"AST build failed: {error.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | LibDeclaration parts ->
                Assert.Equal<string list>(["Furst"], parts)
            | _ -> Assert.Fail("Expected LibDeclaration")

[<Fact>]
let ``Lib declaration parses dotted name`` () =
    let source = """
lib Furst.Collections
"""
    match createAST "test" source with
    | Error error -> Assert.Fail($"Parse failed: {error}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error error -> Assert.Fail($"AST build failed: {error.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | LibDeclaration parts ->
                Assert.Equal<string list>(["Furst"; "Collections"], parts)
            | _ -> Assert.Fail("Expected LibDeclaration")

[<Fact>]
let ``deriveModulePath without lib root`` () =
    let (ModulePath parts) = Compiler.deriveModulePath None "src/collections/list.fu"
    Assert.Equal<string list>(["Collections"; "List"], parts)

[<Fact>]
let ``deriveModulePath with lib root`` () =
    let (ModulePath parts) = Compiler.deriveModulePath (Some "Furst.Collections") "src/list.fu"
    Assert.Equal<string list>(["Furst"; "Collections"; "List"], parts)

[<Fact>]
let ``deriveModulePath with lib root and nested path`` () =
    let (ModulePath parts) = Compiler.deriveModulePath (Some "Furst") "src/collections/list.fu"
    Assert.Equal<string list>(["Furst"; "Collections"; "List"], parts)
