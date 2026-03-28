module Furst.Tests.RoundtripTests

open System.IO
open Xunit
open Ast
open AstBuilder
open Lowered

let lower (source: string) =
    match Lexer.createAST "test.fu" source with
    | Error e -> failwith $"Parse error: {e}"
    | Ok rows ->
        let results = rows |> List.map rowToExpression
        let errors = results |> List.choose (function Error e -> Some e.Message | _ -> None)
        if not errors.IsEmpty then
            let msg = System.String.Join("; ", errors)
            failwith $"AST errors: {msg}"
        let nodes = results |> List.choose (function Ok n -> Some n | _ -> None)
        Pipeline.lower (Types.ModulePath []) nodes

let roundtrip (source: string) =
    let defs = lower source
    let tmp = Path.GetTempFileName() + ".fso"
    try
        FsoWriter.writeFso tmp "test.fu" defs
        let bytes = File.ReadAllBytes(tmp)
        // verify header
        Assert.Equal(byte 'F', bytes.[0])
        Assert.Equal(byte 'S', bytes.[1])
        Assert.Equal(byte 'O', bytes.[2])
        Assert.Equal(0uy, bytes.[3])
        Assert.Equal(1uy, bytes.[4]) // version low byte
        Assert.Equal(0uy, bytes.[5]) // version high byte
        // parse protobuf from offset 8
        let protoBytes = bytes.[8..]
        Furst.FurstModule.Parser.ParseFrom(protoBytes)
    finally
        if File.Exists(tmp) then File.Delete(tmp)

[<Fact>]
let ``Roundtrip simple function with body`` () =
    let m = roundtrip """
let add x y =
  x + y
"""
    Assert.Equal("test.fu", m.SourceFile)
    Assert.Equal(1, m.Definitions.Count)
    let fn = m.Definitions.[0].Function
    Assert.Equal("add", fn.Name)
    Assert.Equal(2, fn.Parameters.Count)
    Assert.Equal("x", fn.Parameters.[0].Name)
    Assert.Equal("y", fn.Parameters.[1].Name)
    // body should have the operation
    Assert.True(fn.Body.Count > 0, "body should not be empty")
    Assert.Equal(Furst.Expression.KindOneofCase.Operation, fn.Body.[0].KindCase)

[<Fact>]
let ``Roundtrip nested function produces hoisted def`` () =
    let m = roundtrip """
let outer x =
  let inner y =
    y
  inner 5
"""
    let names = [ for d in m.Definitions -> d.Function.Name ]
    Assert.Contains("outer$inner", names)
    Assert.Contains("outer", names)

[<Fact>]
let ``Roundtrip preserves function call in body`` () =
    let m = roundtrip """
let outer x =
  let inner y =
    y
  inner 5
"""
    let outer = m.Definitions |> Seq.find (fun d -> d.Function.Name = "outer")
    let hasCall =
        outer.Function.Body |> Seq.exists (fun e ->
            e.KindCase = Furst.Expression.KindOneofCase.FunctionCall
            && e.FunctionCall.Name = "outer$inner")
    Assert.True(hasCall, "outer body should call outer$inner (mangled name)")

[<Fact>]
let ``Roundtrip multiple functions`` () =
    let m = roundtrip """
let foo a =
  a

let bar b =
  b

let baz c =
  c
"""
    Assert.Equal(3, m.Definitions.Count)
    let names = [ for d in m.Definitions -> d.Function.Name ]
    Assert.Contains("foo", names)
    Assert.Contains("bar", names)
    Assert.Contains("baz", names)

[<Fact>]
let ``Roundtrip literal values preserved`` () =
    let m = roundtrip "let x = 42"
    let fn = m.Definitions.[0].Function
    Assert.Equal("x", fn.Name)
    let lit = fn.Body.[0]
    Assert.Equal(Furst.Expression.KindOneofCase.Literal, lit.KindCase)
    Assert.Equal(42, lit.Literal.IntLiteral)

[<Fact>]
let ``Roundtrip binary op in multiline body`` () =
    let m = roundtrip """
let f a b =
  a + b
"""
    let fn = m.Definitions.[0].Function
    Assert.Equal(2, fn.Parameters.Count)
    let body = fn.Body.[0]
    Assert.Equal(Furst.Expression.KindOneofCase.Operation, body.KindCase)
    Assert.Equal(Furst.Operator.OpAdd, body.Operation.Op)

[<Fact>]
let ``Roundtrip chain of binary ops in multiline body`` () =
    let m = roundtrip """
let f a b c =
  a + b + c
"""
    let fn = m.Definitions.[0].Function
    Assert.Equal(3, fn.Parameters.Count)
    // body is a nested operation: (a + b) + c
    let body = fn.Body.[0]
    Assert.Equal(Furst.Expression.KindOneofCase.Operation, body.KindCase)
    // left side should also be an operation (left-associative)
    Assert.Equal(Furst.Expression.KindOneofCase.Operation, body.Operation.Left.KindCase)

[<Fact>]
let ``Roundtrip big script with nested functions and calls`` () =
    let m = roundtrip """
let compute a =
  let step1 x =
    x + 1
  let step2 y =
    y + 2
  step1 a

let main n =
  compute n
"""
    let names = [ for d in m.Definitions -> d.Function.Name ]
    Assert.Contains("compute$step1", names)
    Assert.Contains("compute$step2", names)
    Assert.Contains("compute", names)
    Assert.Contains("main", names)

    // step1 body should have an operation (x + 1)
    let step1 = m.Definitions |> Seq.find (fun d -> d.Function.Name = "compute$step1")
    Assert.True(
        step1.Function.Body |> Seq.exists (fun e ->
            e.KindCase = Furst.Expression.KindOneofCase.Operation),
        "step1 body should contain x + 1 operation")

    // main body should call compute
    let main = m.Definitions |> Seq.find (fun d -> d.Function.Name = "main")
    Assert.True(
        main.Function.Body |> Seq.exists (fun e ->
            e.KindCase = Furst.Expression.KindOneofCase.FunctionCall
            && e.FunctionCall.Name = "compute"),
        "main should call compute")

[<Fact>]
let ``Roundtrip FSO header is exactly 8 bytes`` () =
    let defs = lower "let x = 1"
    let tmp = Path.GetTempFileName() + ".fso"
    try
        FsoWriter.writeFso tmp "test.fu" defs
        let bytes = File.ReadAllBytes(tmp)
        // must be at least 8 bytes
        Assert.True(bytes.Length >= 8, "FSO file must be at least 8 bytes")
        // magic
        Assert.Equal<byte>(byte 'F', bytes.[0])
        Assert.Equal<byte>(byte 'S', bytes.[1])
        Assert.Equal<byte>(byte 'O', bytes.[2])
        Assert.Equal<byte>(0uy, bytes.[3])
        // version 1
        Assert.Equal<byte>(1uy, bytes.[4])
        Assert.Equal<byte>(0uy, bytes.[5])
        // reserved
        Assert.Equal<byte>(0uy, bytes.[6])
        Assert.Equal<byte>(0uy, bytes.[7])
    finally
        if File.Exists(tmp) then File.Delete(tmp)

[<Fact>]
let ``Roundtrip private function sets is_private`` () =
    let source = """
private let helper x =
  x + 1
"""
    let defs =
        match Lexer.createAST "test.fu" source with
        | Error e -> failwith $"Parse error: {e}"
        | Ok rows ->
            let results = rows |> List.map rowToExpression
            let nodes = results |> List.choose (function Ok n -> Some n | _ -> None)
            Pipeline.lower (Types.ModulePath []) nodes
    let tmp = Path.GetTempFileName() + ".fso"
    try
        FsoWriter.writeFso tmp "test.fu" defs
        let bytes = File.ReadAllBytes(tmp)
        let protoBytes = bytes.[8..]
        let m = Furst.FurstModule.Parser.ParseFrom(protoBytes)
        let fn = m.Definitions.[0].Function
        Assert.True(fn.IsPrivate, "private function should have is_private=true")
    finally
        if File.Exists(tmp) then File.Delete(tmp)

[<Fact>]
let ``Roundtrip preserves module_path`` () =
    let source = """
let add x y =
  x + y
"""
    let defs =
        match Lexer.createAST "test.fu" source with
        | Error e -> failwith $"Parse error: {e}"
        | Ok rows ->
            let results = rows |> List.map rowToExpression
            let nodes = results |> List.choose (function Ok n -> Some n | _ -> None)
            Pipeline.lower (Types.ModulePath ["Math"; "Utils"]) nodes
    let tmp = Path.GetTempFileName() + ".fso"
    try
        FsoWriter.writeFso tmp "test.fu" defs
        let bytes = File.ReadAllBytes(tmp)
        let protoBytes = bytes.[8..]
        let m = Furst.FurstModule.Parser.ParseFrom(protoBytes)
        let fn = m.Definitions.[0].Function
        Assert.Equal(2, fn.ModulePath.Count)
        Assert.Equal("Math", fn.ModulePath.[0])
        Assert.Equal("Utils", fn.ModulePath.[1])
    finally
        if File.Exists(tmp) then File.Delete(tmp)
