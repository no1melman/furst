module Commands.Ast

open System
open Ast
open AstBuilder
open Spectre.Console

let rec printExpr (indent: int) (expr: Expression) =
    let pad = String(' ', indent)
    match expr with
    | LetBindingExpression letBinding ->
        printfn "%sLetBinding \"%s\" : %A" pad letBinding.Name letBinding.Type
        printExpr (indent + 2) letBinding.Value
    | FunctionDefinitionExpression (FunctionDefinition details) ->
        printfn "%sFunctionDef \"%s\" : %A" pad details.Identifier details.Type
        printfn "%s  Params" pad
        for param in details.Parameters do
            let (Types.Word word) = param.Name
            printfn "%s    Param \"%s\" : %A" pad word param.Type
        let (BodyExpression bodyExprs) = details.Body
        printfn "%s  Body" pad
        for bodyExpr in bodyExprs do
            printExpr (indent + 4) bodyExpr
    | FunctionCallExpression functionCall ->
        printfn "%sFunctionCall \"%s\"" pad functionCall.FunctionName
        for arg in functionCall.Arguments do
            printExpr (indent + 2) arg
    | OperatorExpression operation ->
        printfn "%sBinaryOp %A" pad operation.Operator
        printExpr (indent + 2) operation.Left
        printExpr (indent + 2) operation.Right
    | IdentifierExpression name ->
        printfn "%sIdentifier \"%s\"" pad name
    | LiteralExpression lit ->
        match lit with
        | IntLiteral i -> printfn "%sIntLiteral %d" pad i
        | FloatLiteral f -> printfn "%sFloatLiteral %g" pad f
        | StringLiteral s -> printfn "%sStringLiteral \"%s\"" pad s
    | StructExpression structDef ->
        printfn "%sStructDef \"%s\"" pad structDef.Name
        for (name, typ) in structDef.Fields do
            printfn "%s  Field \"%s\" : %A" pad name typ
    | ModuleDeclaration (parts, body) ->
        printfn "%smod %s" pad (String.Join(".", parts))
        for bodyExpr in body do
            printExpr (indent + 2) bodyExpr
    | LibDeclaration parts ->
        printfn "%slib %s" pad (String.Join(".", parts))
    | OpenDeclaration parts ->
        printfn "%sopen %s" pad (String.Join(".", parts))

let run (file: string) =
    match Compiler.readFile file with
    | Result.Error error -> AnsiConsole.MarkupLine $"[red]{Markup.Escape error}[/]"; 2
    | Ok source ->
        match Lexer.createAST file source with
        | Result.Error error -> AnsiConsole.MarkupLine $"[red]Parse error: {Markup.Escape error}[/]"; 1
        | Ok rows ->
            let results = rows |> List.map rowToExpression
            let mutable hasError = false
            for result in results do
                match result with
                | Ok node -> printExpr 0 node.Expr
                | Result.Error error ->
                    Compiler.formatError source error
                    hasError <- true
            if hasError then 1 else 0
