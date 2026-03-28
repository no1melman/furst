module Compiler

open System
open System.IO
open System.Collections.Concurrent
open System.Threading.Tasks
open Types
open Ast
open TokenCombinators

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

let deriveModulePath (libRoot: string option) (filePath: string) : ModulePath =
    let normalized = filePath.Replace('\\', '/')
    let stripped =
        match normalized.IndexOf("src/") with
        | i when i >= 0 -> normalized.Substring(i + 4)
        | _ -> normalized
    let noExt = Path.GetFileNameWithoutExtension(stripped) |> fun name ->
        let dir = Path.GetDirectoryName(stripped)
        if String.IsNullOrEmpty(dir) then name
        else dir.Replace('\\', '/') + "/" + name
    let fileParts =
        noExt.Split('/')
        |> Array.filter (fun s -> s <> "")
        |> Array.map (fun s -> string (Char.ToUpper s.[0]) + s.[1..])
        |> Array.toList
    let allParts =
        match libRoot with
        | Some root ->
            let libParts = root.Split('.') |> Array.toList
            libParts @ fileParts
        | None -> fileParts
    ModulePath allParts

let parseFile (filePath: string) : Result<ExpressionNode list * string, string> =
    match readFile filePath with
    | Result.Error error -> Result.Error error
    | Ok source ->
        match Lexer.tokenise filePath source with
        | Result.Error error -> Result.Error $"Parse error in {filePath}: {error}"
        | Ok rows ->
            match RowParser.parseFile rows emptyState with
            | Result.Error error ->
                formatError source error
                Result.Error $"AST error in {filePath}: {error.Message}"
            | Ok (nodes, _state) ->
                Ok (nodes, source)

let lowerFileNodes (baseModulePath: ModulePath) (nodes: ExpressionNode list) : Result<Lowered.TopLevelDef list, string> =
    let mutable currentPath = baseModulePath
    let mutable currentGroup : ExpressionNode list = []
    let mutable allLowered : Lowered.TopLevelDef list = []
    let mutable error : string option = None

    for node in nodes do
        if error.IsNone then
            match node.Expr with
            | LibDeclaration parts ->
                if not currentGroup.IsEmpty then
                    match Pipeline.lower currentPath (List.rev currentGroup) with
                    | Ok lowered -> allLowered <- allLowered @ lowered
                    | Error e -> error <- Some e
                    currentGroup <- []
                let (ModulePath currentParts) = baseModulePath
                let modPart = currentParts |> List.tryLast |> Option.toList
                currentPath <- ModulePath (parts @ modPart)
            | ModuleDeclaration (parts, []) ->
                if not currentGroup.IsEmpty then
                    match Pipeline.lower currentPath (List.rev currentGroup) with
                    | Ok lowered -> allLowered <- allLowered @ lowered
                    | Error e -> error <- Some e
                    currentGroup <- []
                currentPath <- ModulePath parts
            | ModuleDeclaration (parts, body) ->
                if not currentGroup.IsEmpty then
                    match Pipeline.lower currentPath (List.rev currentGroup) with
                    | Ok lowered -> allLowered <- allLowered @ lowered
                    | Error e -> error <- Some e
                    currentGroup <- []
                if error.IsNone then
                    let bodyNodes = body |> List.map (fun expr -> { Expr = expr; Location = node.Location })
                    match Pipeline.lower (ModulePath parts) bodyNodes with
                    | Ok lowered -> allLowered <- allLowered @ lowered
                    | Error e -> error <- Some e
            | OpenDeclaration _ ->
                currentGroup <- node :: currentGroup
            | _ ->
                currentGroup <- node :: currentGroup

    if error.IsNone && not currentGroup.IsEmpty then
        match Pipeline.lower currentPath (List.rev currentGroup) with
        | Ok lowered -> allLowered <- allLowered @ lowered
        | Error e -> error <- Some e

    match error with
    | Some e -> Result.Error e
    | None -> Ok allLowered

/// Load a .fsi manifest and register its symbols in the given table.
/// Format: "qualified.path paramCount" per line.
let loadManifest (path: string) (table: SymbolTable.SymbolTable) : Result<SymbolTable.SymbolTable, string> =
    if not (File.Exists path) then
        Result.Error $"manifest not found: {path}"
    else
        let lines = File.ReadAllLines(path)
        lines |> Array.fold (fun tableResult line ->
            match tableResult with
            | Result.Error _ -> tableResult
            | Ok tbl ->
                let parts = line.Split(' ')
                if parts.Length < 2 then Ok tbl
                else
                    let qualifiedName = parts.[0]
                    let paramCount = int parts.[1]
                    let symbolPath = qualifiedName.Split('.') |> Array.toList
                    SymbolTable.addSymbol symbolPath Visibility.Public paramCount tbl
        ) (Ok table)

let compileFiles (libRoot: string option) (files: string list) (manifests: string list) : Result<Lowered.TopLevelDef list, string> =
    let indexMap = files |> List.mapi (fun i f -> (f, i)) |> Map.ofList
    let results = ConcurrentDictionary<int, Lowered.TopLevelDef list>()
    let errors = ConcurrentBag<string>()

    Parallel.ForEach(files, fun file ->
        match parseFile file with
        | Result.Error error -> errors.Add(error)
        | Ok (nodes, _source) ->
            let modulePath = deriveModulePath libRoot file
            match lowerFileNodes modulePath nodes with
            | Ok lowered -> results.[indexMap.[file]] <- lowered
            | Error error -> errors.Add($"Type error in {file}: {error}")
    ) |> ignore

    if not (errors.IsEmpty) then
        Result.Error (errors |> Seq.head)
    else
        let ordered =
            [0 .. files.Length - 1]
            |> List.collect (fun i -> results.[i])
        // seed symbol table with dependency symbols from manifests
        let seedResult =
            manifests |> List.fold (fun acc path ->
                match acc with
                | Result.Error _ -> acc
                | Ok tbl -> loadManifest path tbl
            ) (Ok SymbolTable.empty)
        match seedResult with
        | Result.Error error -> Result.Error error
        | Ok seedTable ->
            match Pipeline.checkForwardReferences seedTable ordered with
            | Result.Error error -> Result.Error error
            | Ok symTable ->
                let resolved = Pipeline.resolveNames symTable ordered
                // For executables (no libRoot): strip module path from `main` so backend emits bare entry point
                let resolved =
                    if libRoot.IsNone then
                        resolved |> List.map (function
                            | Lowered.TopFunction fn when fn.Name = "main" ->
                                Lowered.TopFunction { fn with ModulePath = ModulePath [] }
                            | def -> def)
                    else resolved
                Result.Ok resolved
