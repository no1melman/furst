module FsoWriter

open System
open System.IO
open BasicTypes
open LanguageExpressions
open Lowering

// -- Type mapping --

let private makeBuiltin (kind: Furst.BuiltinType.Types.Kind) =
    let b = Furst.BuiltinType()
    b.Kind <- kind
    b

let private mapType (t: TypeDefinitions) : Furst.TypeRef =
    let tr = Furst.TypeRef()
    match t with
    | I32 -> tr.Builtin <- makeBuiltin Furst.BuiltinType.Types.Kind.I32
    | I64 -> tr.Builtin <- makeBuiltin Furst.BuiltinType.Types.Kind.I64
    | Float -> tr.Builtin <- makeBuiltin Furst.BuiltinType.Types.Kind.Float
    | Double -> tr.Builtin <- makeBuiltin Furst.BuiltinType.Types.Kind.Double
    | String -> tr.Builtin <- makeBuiltin Furst.BuiltinType.Types.Kind.String
    | Inferred -> tr.Builtin <- makeBuiltin Furst.BuiltinType.Types.Kind.Void
    | UserDefined name -> tr.UserDefined <- name
    tr

let private mapSourceLoc (loc: LanguageExpressions.SourceLocation) : Furst.SourceLocation =
    let (Line sl) = loc.StartLine
    let (Column sc) = loc.StartCol
    let (Line el) = loc.EndLine
    let (Column ec) = loc.EndCol
    let s = Furst.SourceLocation()
    s.StartLine <- sl
    s.StartCol <- sc
    s.EndLine <- el
    s.EndCol <- ec
    s

// -- Expression mapping --

let private mapOperator (op: LanguageExpressions.Operator) : Furst.Operator =
    match op with
    | Add -> Furst.Operator.OpAdd
    | Subtract -> Furst.Operator.OpSubtract
    | Multiply -> Furst.Operator.OpMultiply

let rec private mapExpr (expr: Expression) : Furst.Expression =
    let e = Furst.Expression()
    match expr with
    | LetBindingExpression lb ->
        let plb = Furst.LetBinding()
        plb.Name <- lb.Name
        plb.Type <- mapType lb.Type
        plb.Value <- mapExpr lb.Value
        e.LetBinding <- plb
        e.ResolvedType <- mapType lb.Type

    | FunctionCallExpression fc ->
        let pfc = Furst.FunctionCall()
        pfc.Name <- fc.FunctionName
        for arg in fc.Arguments do
            pfc.Arguments.Add(mapExpr arg)
        e.FunctionCall <- pfc
        e.ResolvedType <- mapType Inferred

    | OperatorExpression op ->
        let pop = Furst.Operation()
        pop.Left <- mapExpr op.Left
        pop.Op <- mapOperator op.Operator
        pop.Right <- mapExpr op.Right
        e.Operation <- pop
        e.ResolvedType <- mapType Inferred

    | IdentifierExpression name ->
        e.Identifier <- name
        e.ResolvedType <- mapType Inferred

    | LiteralExpression lit ->
        let plv = Furst.LiteralValue()
        match lit with
        | IntLiteral i ->
            plv.IntLiteral <- i
            e.ResolvedType <- mapType I32
        | FloatLiteral f ->
            plv.FloatLiteral <- f
            e.ResolvedType <- mapType Double
        | StringLiteral s ->
            plv.StringLiteral <- s
            e.ResolvedType <- mapType String
        e.Literal <- plv

    | FunctionDefinitionExpression _ ->
        e.Identifier <- "<error:unlowered-function>"
        e.ResolvedType <- mapType Inferred

    | StructExpression _ ->
        e.Identifier <- "<error:unlowered-struct>"
        e.ResolvedType <- mapType Inferred
    e

// -- Top-level mapping --

let private mapFunctionDef (fd: LoweredFunctionDef) : Furst.FunctionDef =
    let pfd = Furst.FunctionDef()
    pfd.Name <- fd.Name
    pfd.ReturnType <- mapType fd.ReturnType
    for p in fd.Parameters do
        let pp = Furst.Parameter()
        pp.Name <- p.Name
        pp.Type <- mapType p.Type
        pfd.Parameters.Add(pp)
    for bodyExpr in fd.Body do
        pfd.Body.Add(mapExpr bodyExpr)
    pfd.Location <- mapSourceLoc fd.Location
    pfd

let private mapStructDef (sd: LoweredStructDef) : Furst.StructDef =
    let psd = Furst.StructDef()
    psd.Name <- sd.Name
    for (fieldName, fieldType) in sd.Fields do
        let pp = Furst.Parameter()
        pp.Name <- fieldName
        pp.Type <- mapType fieldType
        psd.Fields.Add(pp)
    psd.Location <- mapSourceLoc sd.Location
    psd

// -- FSO file writing --

let private fsoMagic = [| byte 'F'; byte 'S'; byte 'O'; 0uy |]
let private fsoVersion = BitConverter.GetBytes(1us)
let private fsoReserved = [| 0uy; 0uy |]

let writeFso (outputPath: string) (sourceFile: string) (defs: TopLevelDef list) : unit =
    let m = Furst.FurstModule()
    m.SourceFile <- sourceFile

    for d in defs do
        let tl = Furst.TopLevel()
        match d with
        | TopFunction fd | TopExportedFunction fd -> tl.Function <- mapFunctionDef fd
        | TopStruct sd -> tl.StructDef <- mapStructDef sd
        m.Definitions.Add(tl)

    use fs = File.Create(outputPath)
    fs.Write(fsoMagic, 0, 4)
    fs.Write(fsoVersion, 0, 2)
    fs.Write(fsoReserved, 0, 2)
    use cos = new Google.Protobuf.CodedOutputStream(fs, true)
    m.WriteTo(cos)
