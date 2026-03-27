module Compiler

open System
open System.IO
open Types
open Ast
open AstBuilder

let readFile (path: string) =
    if File.Exists path then
        Ok (File.ReadAllText path)
    else
        Result.Error $"File not found: {path}"

let formatError (source: string) (error: CompileError) =
    let (Line line) = error.Line
    let (Column col) = error.Column
    let (TokenLength len) = error.Length
    let lines = source.Split('\n')
    let lineIdx = int line - 1
    eprintfn "Error: %s" error.Message
    eprintfn "  at line %d, column %d" line col
    if lineIdx >= 0 && lineIdx < lines.Length then
        let srcLine = lines.[lineIdx]
        eprintfn "  | %s" srcLine
        let carets = System.String(' ', int col) + System.String('^', max 1 len)
        eprintfn "  | %s" carets

let deriveModulePath (filePath: string) : ModulePath =
    let normalized = filePath.Replace('\\', '/')
    let stripped =
        match normalized.IndexOf("src/") with
        | i when i >= 0 -> normalized.Substring(i + 4)
        | _ -> normalized
    let noExt = Path.GetFileNameWithoutExtension(stripped) |> fun name ->
        let dir = Path.GetDirectoryName(stripped)
        if String.IsNullOrEmpty(dir) then name
        else dir.Replace('\\', '/') + "/" + name
    let parts =
        noExt.Split('/')
        |> Array.filter (fun s -> s <> "")
        |> Array.map (fun s -> string (Char.ToUpper s.[0]) + s.[1..])
        |> Array.toList
    ModulePath parts

let parseFile (filePath: string) : Result<ExpressionNode list * string, string> =
    match readFile filePath with
    | Result.Error error -> Result.Error error
    | Ok source ->
        match Lexer.createAST filePath source with
        | Result.Error error -> Result.Error $"Parse error in {filePath}: {error}"
        | Ok rows ->
            let results = rows |> List.map rowToExpression
            let errors = results |> List.choose (function Result.Error error -> Some error | _ -> None)
            match errors with
            | error :: _ ->
                formatError source error
                Result.Error $"AST error in {filePath}: {error.Message}"
            | [] ->
                let nodes = results |> List.choose (function Ok node -> Some node | _ -> None)
                Ok (nodes, source)

let lowerFileNodes (baseModulePath: ModulePath) (nodes: ExpressionNode list) : Lowered.TopLevelDef list =
    let mutable currentPath = baseModulePath
    let mutable currentGroup : ExpressionNode list = []
    let mutable allLowered : Lowered.TopLevelDef list = []

    for node in nodes do
        match node.Expr with
        | ModuleDeclaration parts ->
            if not currentGroup.IsEmpty then
                let lowered = Pipeline.lower currentPath (List.rev currentGroup)
                allLowered <- allLowered @ lowered
                currentGroup <- []
            currentPath <- ModulePath parts
        | OpenDeclaration _ ->
            currentGroup <- node :: currentGroup
        | _ ->
            currentGroup <- node :: currentGroup

    if not currentGroup.IsEmpty then
        let lowered = Pipeline.lower currentPath (List.rev currentGroup)
        allLowered <- allLowered @ lowered

    allLowered
