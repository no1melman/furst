module ModuleSystemTests

open Xunit
open Ast
open AstBuilder
open Lexer

[<Fact>]
let ``Mod declaration should parse to ModuleDeclaration`` () =
    let source = "mod Foo"
    match createAST "test" source with
    | Error error -> Assert.Fail($"Parse failed: {error}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error error -> Assert.Fail($"AST build failed: {error.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | ModuleDeclaration parts ->
                Assert.Equal<string list>(["Foo"], parts)
            | _ -> Assert.Fail("Expected ModuleDeclaration")

[<Fact>]
let ``Open declaration should parse to OpenDeclaration`` () =
    let source = "open Foo.Bar"
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
