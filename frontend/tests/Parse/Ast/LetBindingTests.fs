module LetBindingTests

open Xunit
open Types
open Ast
open AstBuilder
open Lexer

[<Fact>]
let ``Simple variable binding should parse to AST`` () =
    let source = "let x = 5"

    let astResult = createAST "test" source

    match astResult with
    | Error e -> Assert.Fail($"Failed to parse: {e}")
    | Ok rows ->
        Assert.Single(rows) |> ignore

        let row = rows.Head
        match rowToExpression row with
        | Error e ->
            Assert.Fail($"Failed to build AST: {e.Message} at {e.Line}:{e.Column}")
        | Ok exprNode ->
            match exprNode.Expr with
            | LetBindingExpression binding ->
                Assert.Equal("x", binding.Name)
                Assert.Equal(Inferred, binding.Type)
                match binding.Value with
                | LiteralExpression (IntLiteral 5) -> ()
                | _ -> Assert.Fail("Expected IntLiteral(5)")
            | _ -> Assert.Fail("Expected LetBindingExpression")

[<Fact>]
let ``Variable binding with identifier should parse to AST`` () =
    let source = "let x = y"

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | LetBindingExpression binding ->
                Assert.Equal("x", binding.Name)
                match binding.Value with
                | IdentifierExpression "y" -> ()
                | _ -> Assert.Fail("Expected IdentifierExpression 'y'")
            | _ -> Assert.Fail("Expected LetBindingExpression")

[<Fact>]
let ``Variable binding with binary op should parse to AST`` () =
    let source = "let result = a + b"

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | LetBindingExpression binding ->
                Assert.Equal("result", binding.Name)
                match binding.Value with
                | OperatorExpression binOp ->
                    Assert.Equal(Operator.Add, binOp.Operator)
                    match binOp.Left with
                    | IdentifierExpression "a" -> ()
                    | _ -> Assert.Fail("Expected left to be 'a'")
                    match binOp.Right with
                    | IdentifierExpression "b" -> ()
                    | _ -> Assert.Fail("Expected right to be 'b'")
                | _ -> Assert.Fail("Expected OperatorExpression")
            | _ -> Assert.Fail("Expected LetBindingExpression")

[<Fact>]
let ``Empty let binding should return error`` () =
    let source = "let x ="

    match createAST "test" source with
    | Error _ -> () // Parser might fail, that's ok
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error err ->
            Assert.Contains("Expected value after '='", err.Message)
        | Ok _ -> Assert.Fail("Should have failed with empty binding")

[<Fact>]
let ``Float literal should parse to AST`` () =
    let source = "let x = 3.14"

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | LetBindingExpression binding ->
                match binding.Value with
                | LiteralExpression (FloatLiteral f) ->
                    Assert.Equal(3.14, f, 3)
                | _ -> Assert.Fail("Expected FloatLiteral")
            | _ -> Assert.Fail("Expected LetBindingExpression")

[<Fact>]
let ``Binding with function call value should parse`` () =
    let source = "let x = f a"

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | LetBindingExpression binding ->
                Assert.Equal("x", binding.Name)
                match binding.Value with
                | FunctionCallExpression call ->
                    Assert.Equal("f", call.FunctionName)
                    Assert.Single(call.Arguments) |> ignore
                    match call.Arguments.[0] with
                    | IdentifierExpression "a" -> ()
                    | _ -> Assert.Fail("Expected argument 'a'")
                | _ -> Assert.Fail("Expected FunctionCallExpression")
            | _ -> Assert.Fail("Expected LetBindingExpression")

[<Fact>]
let ``Negative integer literal should parse`` () =
    let source = "let x = -42"

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | LetBindingExpression binding ->
                match binding.Value with
                | LiteralExpression (IntLiteral n) ->
                    Assert.Equal(-42, n)
                | _ -> Assert.Fail("Expected IntLiteral(-42)")
            | _ -> Assert.Fail("Expected LetBindingExpression")

[<Fact>]
let ``Negative integer in binary op should parse`` () =
    let source = "let x = a + -1"

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | LetBindingExpression binding ->
                match binding.Value with
                | OperatorExpression binOp ->
                    Assert.Equal(Operator.Add, binOp.Operator)
                    match binOp.Right with
                    | LiteralExpression (IntLiteral n) -> Assert.Equal(-1, n)
                    | _ -> Assert.Fail("Expected IntLiteral(-1)")
                | _ -> Assert.Fail("Expected OperatorExpression")
            | _ -> Assert.Fail("Expected LetBindingExpression")

[<Fact>]
let ``f -1 parses as subtraction not function call (same as F#)`` () =
    let source = "let x = f - 1"

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | LetBindingExpression binding ->
                match binding.Value with
                | OperatorExpression binOp ->
                    Assert.Equal(Operator.Subtract, binOp.Operator)
                    match binOp.Left with
                    | IdentifierExpression "f" -> ()
                    | _ -> Assert.Fail("Expected left 'f'")
                    match binOp.Right with
                    | LiteralExpression (IntLiteral 1) -> ()
                    | _ -> Assert.Fail("Expected right IntLiteral(1)")
                | _ -> Assert.Fail("Expected OperatorExpression")
            | _ -> Assert.Fail("Expected LetBindingExpression")

[<Fact>]
let ``Negative literal as function arg requires parens`` () =
    let source = "let x = f (-1)"

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | LetBindingExpression binding ->
                match binding.Value with
                | FunctionCallExpression call ->
                    Assert.Equal("f", call.FunctionName)
                    Assert.Single(call.Arguments) |> ignore
                    match call.Arguments.[0] with
                    | LiteralExpression (IntLiteral n) -> Assert.Equal(-1, n)
                    | _ -> Assert.Fail("Expected IntLiteral(-1)")
                | _ -> Assert.Fail("Expected FunctionCallExpression")
            | _ -> Assert.Fail("Expected LetBindingExpression")

[<Fact>]
let ``Negative float literal should parse`` () =
    let source = "let x = -3.14"

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | LetBindingExpression binding ->
                match binding.Value with
                | LiteralExpression (FloatLiteral f) ->
                    Assert.Equal(-3.14, f, 3)
                | _ -> Assert.Fail("Expected FloatLiteral(-3.14)")
            | _ -> Assert.Fail("Expected LetBindingExpression")

[<Fact>]
let ``Multiple let bindings in sequence should parse`` () =
    let source = "let x = 1\nlet y = 2"

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        Assert.Equal(2, rows.Length)
        let results = rows |> List.map rowToExpression
        let exprs = results |> List.choose (function Ok n -> Some n | _ -> None)
        Assert.Equal(2, exprs.Length)
        match exprs.[0].Expr with
        | LetBindingExpression binding -> Assert.Equal("x", binding.Name)
        | _ -> Assert.Fail("Expected first LetBindingExpression")
        match exprs.[1].Expr with
        | LetBindingExpression binding -> Assert.Equal("y", binding.Name)
        | _ -> Assert.Fail("Expected second LetBindingExpression")

[<Fact>]
let ``Binding type is always Inferred (no typed let bindings yet)`` () =
    let source = "let x = 5"

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | LetBindingExpression binding ->
                Assert.Equal(Inferred, binding.Type)
            | _ -> Assert.Fail("Expected LetBindingExpression")

[<Fact(Skip = "Typed let bindings not yet supported in AST builder")>]
let ``Typed let binding should parse with explicit type`` () =
    let source = "let x: i32 = 5"

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | LetBindingExpression binding ->
                Assert.Equal("x", binding.Name)
                Assert.Equal(I32, binding.Type)
            | _ -> Assert.Fail("Expected LetBindingExpression")
