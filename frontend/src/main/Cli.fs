module Cli

open System
open System.IO
open BasicTypes
open LanguageExpressions

type CliCommand =
    | Build of string
    | Check of string
    | Lex of string
    | Ast of string
    | Help
    | Error of string

let helpText = """furst - The Furst programming language compiler

Usage: furst <command> <file.fu>

Commands:
  build <file>   Compile to LLVM IR (.ll)
  check <file>   Parse and check for errors
  lex <file>     Show lexer token output
  ast <file>     Show parsed AST
  help           Show this help"""

let parseArgs (argv: string array) =
    match argv |> Array.toList with
    | [ "build"; file ] -> Build file
    | [ "check"; file ] -> Check file
    | [ "lex"; file ] -> Lex file
    | [ "ast"; file ] -> Ast file
    | [ "help" ] | [ "--help" ] | [ "-h" ] | [] -> Help
    | cmd :: _ -> Error $"Unknown command: {cmd}"

let readFile (path: string) =
    if File.Exists path then
        Ok (File.ReadAllText path)
    else
        Result.Error $"File not found: {path}"

let formatError (source: string) (err: CompileError) =
    let (Line line) = err.Line
    let (Column col) = err.Column
    let (TokenLength len) = err.Length
    let lines = source.Split('\n')
    let lineIdx = int line - 1
    eprintfn "Error: %s" err.Message
    eprintfn "  at line %d, column %d" line col
    if lineIdx >= 0 && lineIdx < lines.Length then
        let srcLine = lines.[lineIdx]
        eprintfn "  | %s" srcLine
        let carets = System.String(' ', int col) + System.String('^', max 1 len)
        eprintfn "  | %s" carets

let runLex (file: string) =
    match readFile file with
    | Result.Error e -> eprintfn "%s" e; 2
    | Ok source ->
        match TestTwoPhase.createAST file source with
        | Result.Error e -> eprintfn "Parse error: %s" e; 1
        | Ok rows ->
            rows |> List.iter TestTwoPhase.rowReader
            0

let rec printExpr (indent: int) (expr: Expression) =
    let pad = System.String(' ', indent)
    match expr with
    | LetBindingExpression lb ->
        printfn "%sLetBinding \"%s\" : %A" pad lb.Name lb.Type
        printExpr (indent + 2) lb.Value
    | FunctionExpression fd ->
        printfn "%sFunctionDef \"%s\" : %A" pad fd.Identifier fd.Type
        printfn "%s  Params" pad
        for p in fd.Parameters do
            let (Word w) = p.Name
            printfn "%s    Param \"%s\" : %A" pad w p.Type
        let (BodyExpression bodyExprs) = fd.Body
        printfn "%s  Body" pad
        for e in bodyExprs do
            printExpr (indent + 4) e
    | FunctionCallExpression fc ->
        printfn "%sFunctionCall \"%s\"" pad fc.FunctionName
        for a in fc.Arguments do
            printExpr (indent + 2) a
    | OperatorExpression op ->
        printfn "%sBinaryOp %A" pad op.Operator
        printExpr (indent + 2) op.Left
        printExpr (indent + 2) op.Right
    | IdentifierExpression name ->
        printfn "%sIdentifier \"%s\"" pad name
    | LiteralExpression lit ->
        match lit with
        | IntLiteral i -> printfn "%sIntLiteral %d" pad i
        | FloatLiteral f -> printfn "%sFloatLiteral %g" pad f
        | StringLiteral s -> printfn "%sStringLiteral \"%s\"" pad s
    | StructExpression sd ->
        printfn "%sStructDef \"%s\"" pad sd.Name
        for (name, typ) in sd.Fields do
            printfn "%s  Field \"%s\" : %A" pad name typ

let runAst (file: string) =
    match readFile file with
    | Result.Error e -> eprintfn "%s" e; 2
    | Ok source ->
        match TestTwoPhase.createAST file source with
        | Result.Error e -> eprintfn "Parse error: %s" e; 1
        | Ok rows ->
            let results = rows |> List.map rowToExpression
            let mutable hasError = false
            for r in results do
                match r with
                | Ok node -> printExpr 0 node.Expr
                | Result.Error err ->
                    formatError source err
                    hasError <- true
            if hasError then 1 else 0

let runCheck (file: string) =
    match readFile file with
    | Result.Error e -> eprintfn "%s" e; 2
    | Ok source ->
        match TestTwoPhase.createAST file source with
        | Result.Error e -> eprintfn "Parse error: %s" e; 1
        | Ok rows ->
            let results = rows |> List.map rowToExpression
            let errors = results |> List.choose (function Result.Error e -> Some e | _ -> None)
            match errors with
            | [] -> printfn "OK"; 0
            | errs ->
                for err in errs do
                    formatError source err
                1

let runBuild (_file: string) =
    eprintfn "codegen not yet wired up"
    2

let run (argv: string array) =
    match parseArgs argv with
    | Help -> printfn "%s" helpText; 0
    | Error msg -> eprintfn "%s" msg; eprintfn "Run 'furst help' for usage."; 2
    | Build file -> runBuild file
    | Check file -> runCheck file
    | Lex file -> runLex file
    | Ast file -> runAst file
