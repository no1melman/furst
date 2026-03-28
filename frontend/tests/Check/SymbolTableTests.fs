module Furst.Tests.SymbolTableTests

open Xunit
open Types
open Ast
open Lowered

[<Fact>]
let ``addSymbol and resolveSymbol works for simple name`` () =
    let tableResult =
        SymbolTable.empty
        |> SymbolTable.addSymbol ["Math"; "add"] Visibility.Public 2
    match tableResult with
    | Error error -> Assert.Fail(error)
    | Ok table ->
        let result = SymbolTable.resolveSymbol "Math.add" table
        Assert.True(result.IsSome)
        Assert.Equal(2, result.Value.ParamCount)

[<Fact>]
let ``resolveSymbol via open works`` () =
    let tableResult =
        SymbolTable.empty
        |> SymbolTable.addSymbol ["Math"; "add"] Visibility.Public 2
    match tableResult with
    | Error error -> Assert.Fail(error)
    | Ok table ->
        let tableWithOpen = SymbolTable.addOpen ["Math"] table
        let result = SymbolTable.resolveSymbol "add" tableWithOpen
        Assert.True(result.IsSome)
        Assert.Equal<string list>(["Math"; "add"], result.Value.FullPath)

[<Fact>]
let ``duplicate symbol returns error`` () =
    let result =
        SymbolTable.empty
        |> SymbolTable.addSymbol ["Foo"; "bar"] Visibility.Public 0
        |> Result.bind (SymbolTable.addSymbol ["Foo"; "bar"] Visibility.Public 0)
    match result with
    | Ok _ -> Assert.Fail("Expected duplicate error")
    | Error message -> Assert.Contains("duplicate", message)

let private dummyLoc : SourceLocation = {
    StartLine = Line 1L; StartCol = Column 1L
    EndLine = Line 1L; EndCol = Column 10L
}

let private mkFn name modPath paramNames bodyExprs : TopLevelDef =
    TopFunction {
        Name = name
        ReturnType = Inferred
        Parameters = paramNames |> List.map (fun n -> { Name = n; Type = I32 })
        Body = bodyExprs
        Location = dummyLoc
        ModulePath = ModulePath modPath
        Visibility = Public
    }

[<Fact>]
let ``Forward reference is rejected`` () =
    // bar calls foo, but foo is declared after bar
    let defs = [
        mkFn "bar" ["Test"] [] [FunctionCallExpression { FunctionName = "foo"; Arguments = [] }]
        mkFn "foo" ["Test"] [] [LiteralExpression (IntLiteral 1)]
    ]
    match Pipeline.checkForwardReferences SymbolTable.empty defs with
    | Ok _ -> Assert.Fail("Expected forward reference error")
    | Error msg -> Assert.Contains("forward reference", msg)

[<Fact>]
let ``Valid declaration order succeeds`` () =
    // foo declared first, bar calls foo — valid
    let defs = [
        mkFn "foo" ["Test"] [] [LiteralExpression (IntLiteral 1)]
        mkFn "bar" ["Test"] [] [FunctionCallExpression { FunctionName = "foo"; Arguments = [] }]
    ]
    match Pipeline.checkForwardReferences SymbolTable.empty defs with
    | Ok table -> Assert.True(table.Symbols.Count = 2)
    | Error msg -> Assert.Fail($"Unexpected error: {msg}")

[<Fact>]
let ``Function can reference its own parameters`` () =
    let defs = [
        mkFn "add" ["Test"] ["x"; "y"] [
            OperatorExpression { Left = IdentifierExpression "x"; Operator = Add; Right = IdentifierExpression "y" }
        ]
    ]
    match Pipeline.checkForwardReferences SymbolTable.empty defs with
    | Ok _ -> ()
    | Error msg -> Assert.Fail($"Unexpected error: {msg}")

[<Fact>]
let ``Duplicate symbol is rejected`` () =
    let defs = [
        mkFn "foo" ["Test"] [] [LiteralExpression (IntLiteral 1)]
        mkFn "foo" ["Test"] [] [LiteralExpression (IntLiteral 2)]
    ]
    match Pipeline.checkForwardReferences SymbolTable.empty defs with
    | Ok _ -> Assert.Fail("Expected duplicate error")
    | Error msg -> Assert.Contains("duplicate", msg)

[<Fact>]
let ``Private function is accessible from same module`` () =
    let defs = [
        mkFn "helper" ["App"] [] [LiteralExpression (IntLiteral 1)]
        |> fun d -> match d with TopFunction fn -> TopFunction { fn with Visibility = Visibility.Private } | x -> x
        mkFn "main" ["App"] [] [FunctionCallExpression { FunctionName = "helper"; Arguments = [] }]
    ]
    match Pipeline.checkForwardReferences SymbolTable.empty defs with
    | Ok _ -> ()
    | Error msg -> Assert.Fail($"Unexpected error: {msg}")

[<Fact>]
let ``Private function is not accessible from different module`` () =
    let defs = [
        mkFn "secret" ["Internal"] [] [LiteralExpression (IntLiteral 1)]
        |> fun d -> match d with TopFunction fn -> TopFunction { fn with Visibility = Visibility.Private } | x -> x
        mkFn "caller" ["App"] [] [FunctionCallExpression { FunctionName = "Internal.secret"; Arguments = [] }]
    ]
    match Pipeline.checkForwardReferences SymbolTable.empty defs with
    | Ok _ -> Assert.Fail("Expected error for private access")
    | Error msg -> Assert.Contains("forward reference", msg)
