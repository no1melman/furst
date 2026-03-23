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
            | FunctionDefinitionExpression (InternalFuncDef func) ->
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
            | FunctionDefinitionExpression (InternalFuncDef outerFunc) ->
                Assert.Equal("outer", outerFunc.Identifier)
                Assert.Single(outerFunc.Parameters) |> ignore

                match outerFunc.Body with
                | BodyExpression bodyExprs ->
                    Assert.Equal(2, bodyExprs.Length)

                    // First expression: nested function definition
                    match bodyExprs.[0] with
                    | FunctionDefinitionExpression (InternalFuncDef innerFunc) ->
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
let ``Function with body expression referencing literal should parse to AST`` () =
    let source = """let x b =
  5 + b
"""

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | FunctionDefinitionExpression (InternalFuncDef func) ->
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
let ``Untyped parameter function should parse with inferred type`` () =
    let source = """let f a =
  a + 1
"""

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | FunctionDefinitionExpression (InternalFuncDef func) ->
                Assert.Equal("f", func.Identifier)
                Assert.Single(func.Parameters) |> ignore
                let param = func.Parameters.[0]
                Assert.Equal(Word "a", param.Name)
                Assert.Equal(Inferred, param.Type)
            | _ -> Assert.Fail("Expected FunctionDefinitionExpression")

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
let ``Function with multiple body expressions should parse`` () =
    let source = """let f a =
  let x = 1
  a + x
"""

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | FunctionDefinitionExpression (InternalFuncDef func) ->
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
let ``Typed parameter function should parse with explicit type`` () =
    let source = """let f (a: i32) =
  a + 1
"""

    match createAST "test" source with
    | Error e -> Assert.Fail($"Parse failed: {e}")
    | Ok rows ->
        match rowToExpression rows.Head with
        | Error e -> Assert.Fail($"AST build failed: {e.Message}")
        | Ok exprNode ->
            match exprNode.Expr with
            | FunctionDefinitionExpression (InternalFuncDef func) ->
                Assert.Equal("f", func.Identifier)
                Assert.Single(func.Parameters) |> ignore
                let param = func.Parameters.[0]
                Assert.Equal(Word "a", param.Name)
                Assert.Equal(I32, param.Type)
            | _ -> Assert.Fail("Expected FunctionDefinitionExpression")

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
