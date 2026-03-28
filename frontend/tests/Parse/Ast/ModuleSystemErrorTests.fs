module ModuleSystemErrorTests

open Xunit
open Ast
open AstBuilder
open Lexer

[<Fact>]
let ``Nested mod declarations are rejected`` () =
    let source = """
mod Foo
  mod Bar
"""

    match createAST "test" source with
    | Error error -> Assert.Fail($"Parse failed: {error}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error error ->
            Assert.Contains("Nested mod declarations are not allowed", error.Message)
        | Ok _ ->
            Assert.Fail("Expected error for nested mod declaration")

[<Fact>]
let ``Lib inside mod is rejected`` () =
    let source = """
mod Foo
  lib Bar
"""

    match createAST "test" source with
    | Error error -> Assert.Fail($"Parse failed: {error}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error error ->
            Assert.Contains("lib declarations are not allowed inside mod", error.Message)
        | Ok _ ->
            Assert.Fail("Expected error for lib inside mod")
