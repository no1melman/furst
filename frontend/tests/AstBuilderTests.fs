module AstBuilderTests

open Xunit
open BasicTypes
open LanguageExpressions
open TestTwoPhase

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
let ``Function definition with simple body should parse to AST`` () =
    let source = """
let add a b =
  a + b
"""

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        Assert.Single(rows) |> ignore
        match rowToExpression rows.Head with
        | Error e -> Assert.Fail($"AST build failed: {e.Message} at {e.Line}:{e.Column}")
        | Ok exprNode ->
            match exprNode.Expr with
            | FunctionExpression func ->
                Assert.Equal("add", func.Identifier)
                Assert.Equal(2, func.Parameters.Length)

                match func.Body with
                | BodyExpression exprs ->
                    Assert.Single(exprs) |> ignore
                    match exprs.Head with
                    | OperatorExpression binOp ->
                        Assert.Equal(Operator.Add, binOp.Operator)
                    | _ -> Assert.Fail("Expected OperatorExpression in body")
            | _ -> Assert.Fail("Expected FunctionExpression")

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
let ``Function with nested function and call should parse`` () =
    let source = """
let outer a =
  let inner b =
    b + 1
  inner a
"""

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        Assert.Single(rows) |> ignore

        match rowToExpression rows.Head with
        | Error e -> Assert.Fail($"AST build failed: {e.Message} at {e.Line}:{e.Column}")
        | Ok exprNode ->
            match exprNode.Expr with
            | FunctionExpression outerFunc ->
                Assert.Equal("outer", outerFunc.Identifier)
                Assert.Single(outerFunc.Parameters) |> ignore

                match outerFunc.Body with
                | BodyExpression bodyExprs ->
                    Assert.Equal(2, bodyExprs.Length)

                    // First expression: nested function definition
                    match bodyExprs.[0] with
                    | FunctionExpression innerFunc ->
                        Assert.Equal("inner", innerFunc.Identifier)
                        Assert.Single(innerFunc.Parameters) |> ignore

                        match innerFunc.Body with
                        | BodyExpression innerBody ->
                            Assert.Single(innerBody) |> ignore
                            match innerBody.[0] with
                            | OperatorExpression binOp ->
                                Assert.Equal(Operator.Add, binOp.Operator)
                            | _ -> Assert.Fail("Expected OperatorExpression in inner body")
                    | _ -> Assert.Fail("Expected nested FunctionExpression")

                    // Second expression: function call
                    match bodyExprs.[1] with
                    | FunctionCallExpression call ->
                        Assert.Equal("inner", call.FunctionName)
                        Assert.Single(call.Arguments) |> ignore
                        match call.Arguments.[0] with
                        | IdentifierExpression "a" -> ()
                        | _ -> Assert.Fail("Expected identifier 'a' as argument")
                    | _ -> Assert.Fail("Expected FunctionCallExpression")
            | _ -> Assert.Fail("Expected FunctionExpression")

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
