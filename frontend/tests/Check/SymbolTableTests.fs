module Furst.Tests.SymbolTableTests

open Xunit
open Types

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
