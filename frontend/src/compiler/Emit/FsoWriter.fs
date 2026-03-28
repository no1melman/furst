module FsoWriter

open System
open System.IO
open Types
open Ast
open Lowered

// -- Type mapping --

let private makeBuiltin (kind: Furst.BuiltinType.Types.Kind) =
    let builtin = Furst.BuiltinType()
    builtin.Kind <- kind
    builtin

let private mapType (typeDef: TypeDefinitions) : Furst.TypeRef =
    let typeRef = Furst.TypeRef()
    match typeDef with
    | I32 -> typeRef.Builtin <- makeBuiltin Furst.BuiltinType.Types.Kind.I32
    | I64 -> typeRef.Builtin <- makeBuiltin Furst.BuiltinType.Types.Kind.I64
    | Float -> typeRef.Builtin <- makeBuiltin Furst.BuiltinType.Types.Kind.Float
    | Double -> typeRef.Builtin <- makeBuiltin Furst.BuiltinType.Types.Kind.Double
    | String -> typeRef.Builtin <- makeBuiltin Furst.BuiltinType.Types.Kind.String
    | Inferred -> typeRef.Builtin <- makeBuiltin Furst.BuiltinType.Types.Kind.Void
    | UserDefined name -> typeRef.UserDefined <- name
    typeRef

let private mapSourceLoc (loc: Ast.SourceLocation) : Furst.SourceLocation =
    let (Line startLine) = loc.StartLine
    let (Column startCol) = loc.StartCol
    let (Line endLine) = loc.EndLine
    let (Column endCol) = loc.EndCol
    let sourceLoc = Furst.SourceLocation()
    sourceLoc.StartLine <- startLine
    sourceLoc.StartCol <- startCol
    sourceLoc.EndLine <- endLine
    sourceLoc.EndCol <- endCol
    sourceLoc

// -- Expression mapping --

let private mapOperator (operator: Ast.Operator) : Furst.Operator =
    match operator with
    | Add -> Furst.Operator.OpAdd
    | Subtract -> Furst.Operator.OpSubtract
    | Multiply -> Furst.Operator.OpMultiply

let rec private mapExpr (expr: Expression) : Furst.Expression =
    let protoExpr = Furst.Expression()
    match expr with
    | LetBindingExpression letBinding ->
        let protoLetBinding = Furst.LetBinding()
        protoLetBinding.Name <- letBinding.Name
        protoLetBinding.Type <- mapType letBinding.Type
        protoLetBinding.Value <- mapExpr letBinding.Value
        protoExpr.LetBinding <- protoLetBinding
        protoExpr.ResolvedType <- mapType letBinding.Type

    | FunctionCallExpression functionCall ->
        let protoFunctionCall = Furst.FunctionCall()
        protoFunctionCall.Name <- functionCall.FunctionName
        for arg in functionCall.Arguments do
            protoFunctionCall.Arguments.Add(mapExpr arg)
        protoExpr.FunctionCall <- protoFunctionCall
        protoExpr.ResolvedType <- mapType Inferred

    | OperatorExpression operation ->
        let protoOperation = Furst.Operation()
        protoOperation.Left <- mapExpr operation.Left
        protoOperation.Op <- mapOperator operation.Operator
        protoOperation.Right <- mapExpr operation.Right
        protoExpr.Operation <- protoOperation
        protoExpr.ResolvedType <- mapType Inferred

    | IdentifierExpression name ->
        protoExpr.Identifier <- name
        protoExpr.ResolvedType <- mapType Inferred

    | LiteralExpression lit ->
        let protoLiteral = Furst.LiteralValue()
        match lit with
        | IntLiteral i ->
            protoLiteral.IntLiteral <- i
            protoExpr.ResolvedType <- mapType I32
        | FloatLiteral f ->
            protoLiteral.FloatLiteral <- f
            protoExpr.ResolvedType <- mapType Double
        | StringLiteral s ->
            protoLiteral.StringLiteral <- s
            protoExpr.ResolvedType <- mapType String
        protoExpr.Literal <- protoLiteral

    | FunctionDefinitionExpression _ ->
        protoExpr.Identifier <- "<error:unlowered-function>"
        protoExpr.ResolvedType <- mapType Inferred

    | StructExpression _ ->
        protoExpr.Identifier <- "<error:unlowered-struct>"
        protoExpr.ResolvedType <- mapType Inferred

    | ModuleDeclaration _ ->
        protoExpr.Identifier <- "<error:unlowered-mod>"
        protoExpr.ResolvedType <- mapType Inferred

    | LibDeclaration _ ->
        protoExpr.Identifier <- "<error:unlowered-lib>"
        protoExpr.ResolvedType <- mapType Inferred

    | OpenDeclaration _ ->
        protoExpr.Identifier <- "<error:unlowered-open>"
        protoExpr.ResolvedType <- mapType Inferred
    protoExpr

// -- Top-level mapping --

let private mapFunctionDef (functionDef: LoweredFunctionDef) : Furst.FunctionDef =
    let protoFuncDef = Furst.FunctionDef()
    protoFuncDef.Name <- functionDef.Name
    protoFuncDef.ReturnType <- mapType functionDef.ReturnType
    for param in functionDef.Parameters do
        let protoParam = Furst.Parameter()
        protoParam.Name <- param.Name
        protoParam.Type <- mapType param.Type
        protoFuncDef.Parameters.Add(protoParam)
    for bodyExpr in functionDef.Body do
        protoFuncDef.Body.Add(mapExpr bodyExpr)
    protoFuncDef.Location <- mapSourceLoc functionDef.Location
    let (ModulePath parts) = functionDef.ModulePath
    for part in parts do
        protoFuncDef.ModulePath.Add(part)
    protoFuncDef.IsPrivate <- (functionDef.Visibility = Visibility.Private)
    protoFuncDef

let private mapStructDef (structDef: LoweredStructDef) : Furst.StructDef =
    let protoStructDef = Furst.StructDef()
    protoStructDef.Name <- structDef.Name
    for (fieldName, fieldType) in structDef.Fields do
        let protoParam = Furst.Parameter()
        protoParam.Name <- fieldName
        protoParam.Type <- mapType fieldType
        protoStructDef.Fields.Add(protoParam)
    protoStructDef.Location <- mapSourceLoc structDef.Location
    let (ModulePath parts) = structDef.ModulePath
    for part in parts do
        protoStructDef.ModulePath.Add(part)
    protoStructDef

// -- FSO file writing --

let private fsoMagic = [| byte 'F'; byte 'S'; byte 'O'; 0uy |]
let private fsoVersion = BitConverter.GetBytes(1us)
let private fsoReserved = [| 0uy; 0uy |]

let writeFso (outputPath: string) (sourceFile: string) (defs: TopLevelDef list) : unit =
    let furstModule = Furst.FurstModule()
    furstModule.SourceFile <- sourceFile

    for def in defs do
        match def with
        | TopOpen _ -> () // opens are compile-time only, not emitted
        | _ ->
            let topLevel = Furst.TopLevel()
            match def with
            | TopFunction functionDef -> topLevel.Function <- mapFunctionDef functionDef
            | TopStruct structDef -> topLevel.StructDef <- mapStructDef structDef
            | TopOpen _ -> ()
            furstModule.Definitions.Add(topLevel)

    use fileStream = File.Create(outputPath)
    fileStream.Write(fsoMagic, 0, 4)
    fileStream.Write(fsoVersion, 0, 2)
    fileStream.Write(fsoReserved, 0, 2)
    use codedOutput = new Google.Protobuf.CodedOutputStream(fileStream, true)
    furstModule.WriteTo(codedOutput)
