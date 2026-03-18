module Furst.Tests.LoweringIntegrationTests

open System
open System.IO
open Xunit
open BasicTypes
open LanguageExpressions
open Lowering

// Helper: source string → lowered TopLevelDef list
let lower (source: string) =
    let fileName = "test.fu"
    match TestTwoPhase.createAST fileName source with
    | Error e -> failwith $"Parse error: {e}"
    | Ok rows ->
        let results = rows |> List.map rowToExpression
        let errors = results |> List.choose (function Error e -> Some e.Message | _ -> None)
        if not errors.IsEmpty then
            let msg = String.Join("; ", errors)
            failwith $"AST errors: {msg}"
        let nodes = results |> List.choose (function Ok n -> Some n | _ -> None)
        Lowering.lower nodes

// Helper: source string → write .fso → read back FurstModule
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

// ── Lowering tests ──

[<Fact>]
let ``Simple function with multiline body lowers to single top-level def`` () =
    let source = "let add x y =\n  x + y"
    let defs = lower source
    Assert.Equal(1, defs.Length)
    match defs.[0] with
    | TopFunction fd ->
        Assert.Equal("add", fd.Name)
        Assert.Equal(2, fd.Parameters.Length)
        Assert.Equal("x", fd.Parameters.[0].Name)
        Assert.Equal("y", fd.Parameters.[1].Name)
        Assert.NotEmpty(fd.Body)
    | _ -> failwith "expected function"

[<Fact>]
let ``Nested function gets lambda-lifted with mangled name`` () =
    let source = "let outer x =\n  let inner y =\n    y\n  inner 5"
    let defs = lower source
    let names = defs |> List.map (function TopFunction fd -> fd.Name | TopStruct sd -> sd.Name)
    Assert.Contains("outer$inner", names)
    Assert.Contains("outer", names)

[<Fact>]
let ``Lambda-lifted function gets own params`` () =
    let source = "let outer x =\n  let inner y =\n    y\n  inner 5"
    let defs = lower source
    let inner = defs |> List.pick (function TopFunction fd when fd.Name = "outer$inner" -> Some fd | _ -> None)
    let paramNames = inner.Parameters |> List.map (fun p -> p.Name)
    Assert.Contains("y", paramNames)

[<Fact>]
let ``Lambda-lifted function captures outer params when used in body`` () =
    let source = "let outer x =\n  let inner y =\n    x + y\n  inner 5"
    let defs = lower source
    let inner = defs |> List.pick (function TopFunction fd when fd.Name = "outer$inner" -> Some fd | _ -> None)
    let paramNames = inner.Parameters |> List.map (fun p -> p.Name)
    // inner should have captured 'x' + own param 'y'
    Assert.Contains("x", paramNames)
    Assert.Contains("y", paramNames)
    Assert.Equal(2, inner.Parameters.Length)

[<Fact>]
let ``Multiple top-level functions stay flat`` () =
    let source = "let foo a =\n  a\nlet bar b =\n  b"
    let defs = lower source
    let names = defs |> List.map (function TopFunction fd -> fd.Name | TopStruct sd -> sd.Name)
    Assert.Contains("foo", names)
    Assert.Contains("bar", names)

[<Fact>]
let ``Let binding at top level becomes zero-param function`` () =
    let defs = lower "let x = 42"
    Assert.Equal(1, defs.Length)
    match defs.[0] with
    | TopFunction fd ->
        Assert.Equal("x", fd.Name)
        Assert.Empty(fd.Parameters)
    | _ -> failwith "expected function"

[<Fact>]
let ``Multiple nested functions all get hoisted`` () =
    let source = "let compute a =\n  let step1 x =\n    x\n  let step2 y =\n    y\n  step1 a"
    let defs = lower source
    let names = defs |> List.map (function TopFunction fd -> fd.Name | TopStruct sd -> sd.Name)
    Assert.Contains("compute$step1", names)
    Assert.Contains("compute$step2", names)
    Assert.Contains("compute", names)
    Assert.Equal(3, defs.Length)

// ── Roundtrip (parse → lower → .fso → deserialize) tests ──

[<Fact>]
let ``Roundtrip simple function with body`` () =
    let source = "let add x y =\n  x + y"
    let m = roundtrip source
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
    let source = "let outer x =\n  let inner y =\n    y\n  inner 5"
    let m = roundtrip source
    let names = [ for d in m.Definitions -> d.Function.Name ]
    Assert.Contains("outer$inner", names)
    Assert.Contains("outer", names)

[<Fact>]
let ``Roundtrip preserves function call in body`` () =
    let source = "let outer x =\n  let inner y =\n    y\n  inner 5"
    let m = roundtrip source
    let outer = m.Definitions |> Seq.find (fun d -> d.Function.Name = "outer")
    let hasCall =
        outer.Function.Body |> Seq.exists (fun e ->
            e.KindCase = Furst.Expression.KindOneofCase.FunctionCall
            && e.FunctionCall.Name = "inner")
    Assert.True(hasCall, "outer body should call inner")

[<Fact>]
let ``Roundtrip multiple functions`` () =
    let source = "let foo a =\n  a\nlet bar b =\n  b\nlet baz c =\n  c"
    let m = roundtrip source
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
    let source = "let f a b =\n  a + b"
    let m = roundtrip source
    let fn = m.Definitions.[0].Function
    Assert.Equal(2, fn.Parameters.Count)
    let body = fn.Body.[0]
    Assert.Equal(Furst.Expression.KindOneofCase.Operation, body.KindCase)
    Assert.Equal(Furst.Operator.OpAdd, body.Operation.Op)

[<Fact>]
let ``Roundtrip chain of binary ops in multiline body`` () =
    let source = "let f a b c =\n  a + b + c"
    let m = roundtrip source
    let fn = m.Definitions.[0].Function
    Assert.Equal(3, fn.Parameters.Count)
    // body is a nested operation: (a + b) + c
    let body = fn.Body.[0]
    Assert.Equal(Furst.Expression.KindOneofCase.Operation, body.KindCase)
    // left side should also be an operation (left-associative)
    Assert.Equal(Furst.Expression.KindOneofCase.Operation, body.Operation.Left.KindCase)

[<Fact>]
let ``Roundtrip big script with nested functions and calls`` () =
    let source = "let compute a =\n  let step1 x =\n    x + 1\n  let step2 y =\n    y + 2\n  step1 a\n\nlet main n =\n  compute n"
    let m = roundtrip source
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
