module UserDefinedOperatorTests

open Xunit
open Types
open Ast
open RowParser
open TokenCombinators
open Lexer

/// Helper: parse multi-line source through full pipeline (tokenise + parseFile)
let private parseSource (source: string) =
    match tokenise "test" source with
    | Error e -> Error $"Tokenise failed: {e}"
    | Ok rows ->
        match parseFile rows emptyState with
        | Error e -> Error $"Parse failed: {e.Message}"
        | Ok (nodes, state) -> Ok (nodes, state)

[<Fact>]
let ``Operator definition should parse as function definition`` () =
    let source = """
let (|>) a f =
  f a
"""
    match parseSource source with
    | Error e -> Assert.Fail(e)
    | Ok (nodes, _) ->
        Assert.Single(nodes) |> ignore
        match nodes.Head.Expr with
        | FunctionDefinitionExpression (FunctionDefinition func) ->
            Assert.Equal("op_pipe_gt", func.Identifier)
            Assert.Equal(2, func.Parameters.Length)
        | other -> Assert.Fail($"Expected FunctionDefinitionExpression, got {other}")

[<Fact>]
let ``Operator with three params should parse`` () =
    let source = """
let (>>) f g x =
  g (f x)
"""
    match parseSource source with
    | Error e -> Assert.Fail(e)
    | Ok (nodes, _) ->
        match nodes.Head.Expr with
        | FunctionDefinitionExpression (FunctionDefinition func) ->
            Assert.Equal("op_gt_gt", func.Identifier)
            Assert.Equal(3, func.Parameters.Length)
        | other -> Assert.Fail($"Expected FunctionDefinitionExpression, got {other}")

[<Fact>]
let ``Operator should produce OperatorName token in lexer`` () =
    let source = """
let (|>) a f =
  f a
"""
    match tokenise "test" source with
    | Error e -> Assert.Fail($"Tokenise failed: {e}")
    | Ok rows ->
        let tokens = rows.Head.Expressions |> List.map _.Token
        // should have: Let, OperatorName "op_pipe_gt", Parameter "a", Parameter "f", Assignment
        Assert.Contains(OperatorName "op_pipe_gt", tokens)

[<Fact>]
let ``Infix operator usage should desugar to function call`` () =
    let source = """
let (|>) a f =
  f a

let x = 2 |> sum
"""
    match parseSource source with
    | Error e -> Assert.Fail(e)
    | Ok (nodes, _) ->
        Assert.Equal(2, nodes.Length)
        match nodes.[1].Expr with
        | LetBindingExpression binding ->
            match binding.Value with
            | FunctionCallExpression call ->
                Assert.Equal("op_pipe_gt", call.FunctionName)
                Assert.Equal(2, call.Arguments.Length)
                match call.Arguments.[0] with
                | LiteralExpression (IntLiteral 2) -> ()
                | other -> Assert.Fail($"Expected literal 2, got {other}")
                match call.Arguments.[1] with
                | IdentifierExpression "sum" -> ()
                | other -> Assert.Fail($"Expected identifier sum, got {other}")
            | other -> Assert.Fail($"Expected FunctionCallExpression, got {other}")
        | other -> Assert.Fail($"Expected LetBindingExpression, got {other}")

[<Fact>]
let ``User op has lower precedence than arithmetic`` () =
    // 1 + 2 |> f  should parse as  (1 + 2) |> f
    let source = """
let (|>) a f =
  f a

let x = 1 + 2 |> f
"""
    match parseSource source with
    | Error e -> Assert.Fail(e)
    | Ok (nodes, _) ->
        match nodes.[1].Expr with
        | LetBindingExpression binding ->
            match binding.Value with
            | FunctionCallExpression call ->
                Assert.Equal("op_pipe_gt", call.FunctionName)
                match call.Arguments.[0] with
                | OperatorExpression op ->
                    Assert.Equal(Operator.Add, op.Operator)
                | other -> Assert.Fail($"Expected OperatorExpression as left, got {other}")
            | other -> Assert.Fail($"Expected FunctionCallExpression, got {other}")
        | other -> Assert.Fail($"Expected LetBindingExpression, got {other}")

[<Fact>]
let ``Chained user ops should be left-associative`` () =
    // a |> f |> g  should parse as  (a |> f) |> g
    let source = """
let (|>) a f =
  f a

let x = a |> f |> g
"""
    match parseSource source with
    | Error e -> Assert.Fail(e)
    | Ok (nodes, _) ->
        match nodes.[1].Expr with
        | LetBindingExpression binding ->
            match binding.Value with
            | FunctionCallExpression outerCall ->
                Assert.Equal("op_pipe_gt", outerCall.FunctionName)
                match outerCall.Arguments.[1] with
                | IdentifierExpression "g" -> ()
                | other -> Assert.Fail($"Expected g, got {other}")
                match outerCall.Arguments.[0] with
                | FunctionCallExpression innerCall ->
                    Assert.Equal("op_pipe_gt", innerCall.FunctionName)
                | other -> Assert.Fail($"Expected inner FunctionCallExpression, got {other}")
            | other -> Assert.Fail($"Expected FunctionCallExpression, got {other}")
        | other -> Assert.Fail($"Expected LetBindingExpression, got {other}")

[<Fact>]
let ``Multiple user operators can coexist`` () =
    let source = """
let (|>) a f =
  f a

let (>>) f g x =
  g (f x)

let x = a |> f
let y = f >> g
"""
    match parseSource source with
    | Error e -> Assert.Fail(e)
    | Ok (nodes, _) ->
        Assert.Equal(4, nodes.Length)
        // x = a |> f
        match nodes.[2].Expr with
        | LetBindingExpression binding ->
            match binding.Value with
            | FunctionCallExpression call -> Assert.Equal("op_pipe_gt", call.FunctionName)
            | other -> Assert.Fail($"Expected (|>) call, got {other}")
        | other -> Assert.Fail($"Expected LetBindingExpression, got {other}")
        // y = f >> g
        match nodes.[3].Expr with
        | LetBindingExpression binding ->
            match binding.Value with
            | FunctionCallExpression call -> Assert.Equal("op_gt_gt", call.FunctionName)
            | other -> Assert.Fail($"Expected (>>) call, got {other}")
        | other -> Assert.Fail($"Expected LetBindingExpression, got {other}")

[<Fact>]
let ``Function application stops at user operator`` () =
    // sum 10 |> f  should parse as  (sum 10) |> f, not sum(10, |>, f)
    let source = """
let (|>) a f =
  f a

let x = sum 10 |> f
"""
    match parseSource source with
    | Error e -> Assert.Fail(e)
    | Ok (nodes, _) ->
        match nodes.[1].Expr with
        | LetBindingExpression binding ->
            match binding.Value with
            | FunctionCallExpression call ->
                Assert.Equal("op_pipe_gt", call.FunctionName)
                match call.Arguments.[0] with
                | FunctionCallExpression innerCall ->
                    Assert.Equal("sum", innerCall.FunctionName)
                    Assert.Equal(1, innerCall.Arguments.Length)
                | other -> Assert.Fail($"Expected sum call as left, got {other}")
            | other -> Assert.Fail($"Expected FunctionCallExpression, got {other}")
        | other -> Assert.Fail($"Expected LetBindingExpression, got {other}")
