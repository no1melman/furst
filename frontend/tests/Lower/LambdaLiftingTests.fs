module Furst.Tests.LambdaLiftingTests

open Xunit
open Types
open Ast
open RowParser
open TokenCombinators
open Lowered

let lower (source: string) =
    match Lexer.tokenise "test.fu" source with
    | Error e -> failwith $"Parse error: {e}"
    | Ok rows ->
        let nodes =
            match RowParser.parseFile rows emptyState with
            | Error e -> failwith $"AST error: {e.Message}"
            | Ok (nodes, _) -> nodes
        Pipeline.lower (Types.ModulePath []) nodes

[<Fact>]
let ``Simple function with multiline body lowers to single top-level def`` () =
    let defs = lower """
let add x y =
  x + y
"""
    Assert.Equal(1, defs.Length)
    match defs.[0] with
    | TopFunction functionDef ->
        Assert.Equal("add", functionDef.Name)
        Assert.Equal(2, functionDef.Parameters.Length)
        Assert.Equal("x", functionDef.Parameters.[0].Name)
        Assert.Equal("y", functionDef.Parameters.[1].Name)
        Assert.NotEmpty(functionDef.Body)
    | _ -> failwith "expected function"

[<Fact>]
let ``Nested function gets lambda-lifted with mangled name`` () =
    let defs = lower """
let outer x =
  let inner y =
    y
  inner 5
"""
    let names = defs |> List.map (function TopFunction functionDef -> functionDef.Name | TopStruct structDef -> structDef.Name | TopOpen _ -> "<open>")
    Assert.Contains("outer$inner", names)
    Assert.Contains("outer", names)

[<Fact>]
let ``Lambda-lifted function gets own params`` () =
    let defs = lower """
let outer x =
  let inner y =
    y
  inner 5
"""
    let inner = defs |> List.pick (function TopFunction functionDef when functionDef.Name = "outer$inner" -> Some functionDef | _ -> None)
    let paramNames = inner.Parameters |> List.map (fun param -> param.Name)
    Assert.Contains("y", paramNames)

[<Fact>]
let ``Lambda-lifted function captures outer params when used in body`` () =
    let defs = lower """
let outer x =
  let inner y =
    x + y
  inner 5
"""
    let inner = defs |> List.pick (function TopFunction functionDef when functionDef.Name = "outer$inner" -> Some functionDef | _ -> None)
    let paramNames = inner.Parameters |> List.map (fun param -> param.Name)
    // inner should have captured 'x' + own param 'y'
    Assert.Contains("x", paramNames)
    Assert.Contains("y", paramNames)
    Assert.Equal(2, inner.Parameters.Length)

[<Fact>]
let ``Multiple top-level functions stay flat`` () =
    let defs = lower """
let foo a =
  a

let bar b =
  b
"""
    let names = defs |> List.map (function TopFunction functionDef -> functionDef.Name | TopStruct structDef -> structDef.Name | TopOpen _ -> "<open>")
    Assert.Contains("foo", names)
    Assert.Contains("bar", names)

[<Fact>]
let ``Let binding at top level becomes zero-param function`` () =
    let defs = lower "let x = 42"
    Assert.Equal(1, defs.Length)
    match defs.[0] with
    | TopFunction functionDef ->
        Assert.Equal("x", functionDef.Name)
        Assert.Empty(functionDef.Parameters)
    | _ -> failwith "expected function"

[<Fact>]
let ``Multiple nested functions all get hoisted`` () =
    let defs = lower """
let compute a =
  let step1 x =
    x
  let step2 y =
    y
  step1 a
"""
    let names = defs |> List.map (function TopFunction functionDef -> functionDef.Name | TopStruct structDef -> structDef.Name | TopOpen _ -> "<open>")
    Assert.Contains("compute$step1", names)
    Assert.Contains("compute$step2", names)
    Assert.Contains("compute", names)
    Assert.Equal(3, defs.Length)
