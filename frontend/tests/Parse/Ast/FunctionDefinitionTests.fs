module FunctionDefinitionTests

open Xunit
open Types
open Ast
open RowParser
open TokenCombinators
open Lexer

[<Fact>]
let ``Function definition with simple body should parse to AST`` () =
    let source = """
let add a b =
  a + b
"""

    match tokenise "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        Assert.Single(rows) |> ignore
        match parseRow rows.Head emptyState with
        | Error e -> Assert.Fail($"AST build failed: {e.Message} at {e.Line}:{e.Column}")
        | Ok (exprNode, _) ->
            match exprNode.Expr with
            | FunctionDefinitionExpression (FunctionDefinition func) ->
                Assert.Equal("add", func.Identifier)
                Assert.Equal(2, func.Parameters.Length)

                match func.Body with
                | BodyExpression exprs ->
                    Assert.Single(exprs) |> ignore
                    match exprs.Head with
                    | OperatorExpression binOp ->
                        Assert.Equal(Operator.Add, binOp.Operator)
                    | _ -> Assert.Fail("Expected OperatorExpression in body")
            | _ -> Assert.Fail("Expected FunctionDefinitionExpression")

[<Fact>]
let ``Function with nested function and call should parse`` () =
    let source = """
let outer a =
  let inner b =
    b + 1
  inner a
"""

    match tokenise "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        Assert.Single(rows) |> ignore

        match parseRow rows.Head emptyState with
        | Error e -> Assert.Fail($"AST build failed: {e.Message} at {e.Line}:{e.Column}")
        | Ok (exprNode, _) ->
            match exprNode.Expr with
            | FunctionDefinitionExpression (FunctionDefinition outerFunc) ->
                Assert.Equal("outer", outerFunc.Identifier)
                Assert.Single(outerFunc.Parameters) |> ignore

                match outerFunc.Body with
                | BodyExpression bodyExprs ->
                    Assert.Equal(2, bodyExprs.Length)

                    // First expression: nested function definition
                    match bodyExprs.[0] with
                    | FunctionDefinitionExpression (FunctionDefinition innerFunc) ->
                        Assert.Equal("inner", innerFunc.Identifier)
                        Assert.Single(innerFunc.Parameters) |> ignore

                        match innerFunc.Body with
                        | BodyExpression innerBody ->
                            Assert.Single(innerBody) |> ignore
                            match innerBody.[0] with
                            | OperatorExpression binOp ->
                                Assert.Equal(Operator.Add, binOp.Operator)
                            | _ -> Assert.Fail("Expected OperatorExpression in inner body")
                    | _ -> Assert.Fail("Expected nested FunctionDefinitionExpression")

                    // Second expression: function call
                    match bodyExprs.[1] with
                    | FunctionCallExpression call ->
                        Assert.Equal("inner", call.FunctionName)
                        Assert.Single(call.Arguments) |> ignore
                        match call.Arguments.[0] with
                        | IdentifierExpression "a" -> ()
                        | _ -> Assert.Fail("Expected identifier 'a' as argument")
                    | _ -> Assert.Fail("Expected FunctionCallExpression")
            | _ -> Assert.Fail("Expected FunctionDefinitionExpression")

[<Fact>]
let ``Function with body expression referencing literal should parse to AST`` () =
    let source = """
let x b =
  5 + b
"""

    match tokenise "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match parseRow rows.Head emptyState with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok (exprNode, _) ->
            match exprNode.Expr with
            | FunctionDefinitionExpression (FunctionDefinition func) ->
                Assert.Equal("x", func.Identifier)
                Assert.Single(func.Parameters) |> ignore
                match func.Body with
                | BodyExpression exprs ->
                    Assert.Single(exprs) |> ignore
                    match exprs.[0] with
                    | OperatorExpression binOp ->
                        Assert.Equal(Operator.Add, binOp.Operator)
                        match binOp.Left with
                        | LiteralExpression (IntLiteral 5) -> ()
                        | _ -> Assert.Fail("Expected left to be IntLiteral 5")
                        match binOp.Right with
                        | IdentifierExpression "b" -> ()
                        | _ -> Assert.Fail("Expected right to be 'b'")
                    | _ -> Assert.Fail("Expected OperatorExpression in body")
            | _ -> Assert.Fail("Expected FunctionDefinitionExpression")

[<Fact>]
let ``Untyped parameter function should parse with inferred type`` () =
    let source = """
let f a =
  a + 1
"""

    match tokenise "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match parseRow rows.Head emptyState with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok (exprNode, _) ->
            match exprNode.Expr with
            | FunctionDefinitionExpression (FunctionDefinition func) ->
                Assert.Equal("f", func.Identifier)
                Assert.Single(func.Parameters) |> ignore
                let param = func.Parameters.[0]
                Assert.Equal(Word "a", param.Name)
                Assert.Equal(Inferred, param.Type)
            | _ -> Assert.Fail("Expected FunctionDefinitionExpression")

[<Fact>]
let ``Typed parameter function should parse with explicit type`` () =
    let source = """
let f (a: i32) =
  a + 1
"""

    match tokenise "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match parseRow rows.Head emptyState with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok (exprNode, _) ->
            match exprNode.Expr with
            | FunctionDefinitionExpression (FunctionDefinition func) ->
                Assert.Equal("f", func.Identifier)
                Assert.Single(func.Parameters) |> ignore
                let param = func.Parameters.[0]
                Assert.Equal(Word "a", param.Name)
                Assert.Equal(I32, param.Type)
            | _ -> Assert.Fail("Expected FunctionDefinitionExpression")

[<Fact>]
let ``Both params typed should parse with explicit types`` () =
    let source = """
let add (a: i32) (b: i32) =
  a + b
"""

    match tokenise "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match parseRow rows.Head emptyState with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok (exprNode, _) ->
            match exprNode.Expr with
            | FunctionDefinitionExpression (FunctionDefinition func) ->
                Assert.Equal("add", func.Identifier)
                Assert.Equal(2, func.Parameters.Length)
                Assert.Equal(Word "a", func.Parameters.[0].Name)
                Assert.Equal(I32, func.Parameters.[0].Type)
                Assert.Equal(Word "b", func.Parameters.[1].Name)
                Assert.Equal(I32, func.Parameters.[1].Type)
            | _ -> Assert.Fail("Expected FunctionDefinitionExpression")

[<Fact>]
let ``Mixed params untyped then typed should parse`` () =
    let source = """
let add a (b: i32) =
  a + b
"""

    match tokenise "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match parseRow rows.Head emptyState with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok (exprNode, _) ->
            match exprNode.Expr with
            | FunctionDefinitionExpression (FunctionDefinition func) ->
                Assert.Equal(2, func.Parameters.Length)
                Assert.Equal(Word "a", func.Parameters.[0].Name)
                Assert.Equal(Inferred, func.Parameters.[0].Type)
                Assert.Equal(Word "b", func.Parameters.[1].Name)
                Assert.Equal(I32, func.Parameters.[1].Type)
            | _ -> Assert.Fail("Expected FunctionDefinitionExpression")

[<Fact>]
let ``Mixed params typed then untyped should parse`` () =
    let source = """
let add (a: i32) b =
  a + b
"""

    match tokenise "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match parseRow rows.Head emptyState with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok (exprNode, _) ->
            match exprNode.Expr with
            | FunctionDefinitionExpression (FunctionDefinition func) ->
                Assert.Equal(2, func.Parameters.Length)
                Assert.Equal(Word "a", func.Parameters.[0].Name)
                Assert.Equal(I32, func.Parameters.[0].Type)
                Assert.Equal(Word "b", func.Parameters.[1].Name)
                Assert.Equal(Inferred, func.Parameters.[1].Type)
            | _ -> Assert.Fail("Expected FunctionDefinitionExpression")

[<Fact(Skip = "Return type annotations not yet supported")>]
let ``Function with return type annotation should parse`` () =
    let source = """
let add (a: i32) (b: i32) : i32 =
  a + b
"""

    match tokenise "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match parseRow rows.Head emptyState with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok (exprNode, _) ->
            match exprNode.Expr with
            | FunctionDefinitionExpression (FunctionDefinition func) ->
                Assert.Equal("add", func.Identifier)
                Assert.Equal(I32, func.Type)
                Assert.Equal(2, func.Parameters.Length)
            | _ -> Assert.Fail("Expected FunctionDefinitionExpression")

[<Fact>]
let ``Function with multiple body expressions should parse`` () =
    let source = """
let f a =
  let x = 1
  a + x
"""

    match tokenise "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match parseRow rows.Head emptyState with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok (exprNode, _) ->
            match exprNode.Expr with
            | FunctionDefinitionExpression (FunctionDefinition func) ->
                match func.Body with
                | BodyExpression exprs ->
                    Assert.Equal(2, exprs.Length)
                    match exprs.[0] with
                    | LetBindingExpression binding -> Assert.Equal("x", binding.Name)
                    | _ -> Assert.Fail("Expected LetBindingExpression as first body expr")
                    match exprs.[1] with
                    | OperatorExpression _ -> ()
                    | _ -> Assert.Fail("Expected OperatorExpression as second body expr")
            | _ -> Assert.Fail("Expected FunctionDefinitionExpression")

[<Fact>]
let ``Private function should parse with Private visibility`` () =
    let source = """
private let helper x =
  x + 1
"""
    match tokenise "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match parseRow rows.Head emptyState with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok (exprNode, _) ->
            match exprNode.Expr with
            | FunctionDefinitionExpression (FunctionDefinition func) ->
                Assert.Equal("helper", func.Identifier)
                Assert.Equal(Types.Visibility.Private, func.Visibility)
            | _ -> Assert.Fail("Expected FunctionDefinitionExpression")

[<Fact>]
let ``Public function should default to Public visibility`` () =
    let source = """
let add a b =
  a + b
"""
    match tokenise "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match parseRow rows.Head emptyState with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok (exprNode, _) ->
            match exprNode.Expr with
            | FunctionDefinitionExpression (FunctionDefinition func) ->
                Assert.Equal(Types.Visibility.Public, func.Visibility)
            | _ -> Assert.Fail("Expected FunctionDefinitionExpression")

