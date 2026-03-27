module ExpressionTests

open Xunit
open Types
open Ast
open AstBuilder
open Lexer

[<Fact>]
let ``Subtract operator should parse to AST`` () =
    let source = "let x = a - b"

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
                    | IdentifierExpression "a" -> ()
                    | _ -> Assert.Fail("Expected left 'a'")
                    match binOp.Right with
                    | IdentifierExpression "b" -> ()
                    | _ -> Assert.Fail("Expected right 'b'")
                | _ -> Assert.Fail("Expected OperatorExpression")
            | _ -> Assert.Fail("Expected LetBindingExpression")

[<Fact>]
let ``Multiply operator should parse to AST`` () =
    let source = "let x = a * b"

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
                    Assert.Equal(Operator.Multiply, binOp.Operator)
                | _ -> Assert.Fail("Expected OperatorExpression")
            | _ -> Assert.Fail("Expected LetBindingExpression")

[<Fact>]
let ``Chained binary ops should be left-associative`` () =
    // a + b + c should parse as (a + b) + c
    let source = "let x = a + b + c"

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | LetBindingExpression binding ->
                match binding.Value with
                | OperatorExpression outer ->
                    Assert.Equal(Operator.Add, outer.Operator)
                    match outer.Right with
                    | IdentifierExpression "c" -> ()
                    | _ -> Assert.Fail("Expected right to be 'c'")
                    match outer.Left with
                    | OperatorExpression inner ->
                        Assert.Equal(Operator.Add, inner.Operator)
                        match inner.Left with
                        | IdentifierExpression "a" -> ()
                        | _ -> Assert.Fail("Expected inner left to be 'a'")
                        match inner.Right with
                        | IdentifierExpression "b" -> ()
                        | _ -> Assert.Fail("Expected inner right to be 'b'")
                    | _ -> Assert.Fail("Expected inner OperatorExpression for left side")
                | _ -> Assert.Fail("Expected OperatorExpression")
            | _ -> Assert.Fail("Expected LetBindingExpression")

[<Fact>]
let ``ExpressionNode should have SourceLocation`` () =
    let source = "let x = 5"

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok exprNode ->
            // Verify location exists
            let (Line startLine) = exprNode.Location.StartLine
            let (Column startCol) = exprNode.Location.StartCol
            Assert.Equal(1L, startLine)  // Line 1
            Assert.Equal(1L, startCol)   // Column 1 (FParsec uses 1-based indexing)

[<Fact>]
let ``Standalone function call should parse to AST`` () =
    let source = "f a b"

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | FunctionCallExpression call ->
                Assert.Equal("f", call.FunctionName)
                Assert.Equal(2, call.Arguments.Length)
                match call.Arguments.[0] with
                | IdentifierExpression "a" -> ()
                | _ -> Assert.Fail("Expected first arg 'a'")
                match call.Arguments.[1] with
                | IdentifierExpression "b" -> ()
                | _ -> Assert.Fail("Expected second arg 'b'")
            | _ -> Assert.Fail("Expected FunctionCallExpression")

[<Fact>]
let ``Function call with parenthesized expression argument`` () =
    let source = "f a (2 + 3)"

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | FunctionCallExpression call ->
                Assert.Equal("f", call.FunctionName)
                Assert.Equal(2, call.Arguments.Length)
                match call.Arguments.[0] with
                | IdentifierExpression "a" -> ()
                | _ -> Assert.Fail("Expected first arg 'a'")
                match call.Arguments.[1] with
                | OperatorExpression binOp ->
                    Assert.Equal(Operator.Add, binOp.Operator)
                    match binOp.Left with
                    | LiteralExpression (IntLiteral 2) -> ()
                    | _ -> Assert.Fail("Expected left to be 2")
                    match binOp.Right with
                    | LiteralExpression (IntLiteral 3) -> ()
                    | _ -> Assert.Fail("Expected right to be 3")
                | _ -> Assert.Fail("Expected OperatorExpression as second arg")
            | _ -> Assert.Fail("Expected FunctionCallExpression")

[<Fact>]
let ``Struct definition should return not-yet-implemented error`` () =
    let source = "struct Foo {}"

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error err ->
            Assert.Contains("not yet implemented", err.Message)
        | Ok _ -> Assert.Fail("Expected error for struct definition")

[<Fact>]
let ``Qualified function call should parse`` () =
    let source = "Math.add 1 2"

    match createAST "test" source with
    | Error error -> Assert.Fail($"Parse failed: {error}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error error -> Assert.Fail($"AST build failed: {error.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | FunctionCallExpression call ->
                Assert.Equal("Math.add", call.FunctionName)
                Assert.Equal(2, call.Arguments.Length)
            | _ -> Assert.Fail("Expected FunctionCallExpression")
