module Cli

open System
open System.IO
open BasicTypes
open LanguageExpressions

type NewOptions = { Name: string; OutputDir: string }

type CliCommand =
    | Build of string
    | BuildProject
    | Run of string list
    | RunProject of string list
    | Check of string
    | Lex of string
    | Ast of string
    | New of NewOptions
    | Help
    | Error of string

let helpText = """furst - The Furst programming language compiler

Usage: furst <command> [options]

Commands:
  new -n <name> -o <dir>   Create a new project
  build                    Build project (uses furst.yaml in current dir)
  build <file>             Compile a single .fu file
  run [-- args...]         Build and run project
  run <file> [-- args...]  Build and run a single .fu file
  check <file>             Parse and check for errors
  lex <file>               Show lexer token output
  ast <file>               Show parsed AST
  help                     Show this help"""

let rec parseNewArgs (args: string list) (opts: NewOptions) =
    match args with
    | "-n" :: name :: rest -> parseNewArgs rest { opts with Name = name }
    | "-o" :: dir :: rest -> parseNewArgs rest { opts with OutputDir = dir }
    | [] -> opts
    | unknown :: _ -> { opts with Name = ""; OutputDir = unknown }

let parseArgs (argv: string array) =
    match argv |> Array.toList with
    | "new" :: rest ->
        let opts = parseNewArgs rest { Name = ""; OutputDir = "" }
        if opts.Name = "" then Error "new requires -n <name>"
        elif opts.OutputDir = "" then New { opts with OutputDir = "./" + opts.Name }
        else New opts
    | [ "build"; file ] when file.EndsWith(".fu") -> Build file
    | [ "build" ] -> BuildProject
    | "run" :: file :: rest when file.EndsWith(".fu") ->
        let args = match rest with "--" :: a -> a | _ -> rest
        Run(file :: args)
    | "run" :: rest ->
        let args = match rest with "--" :: a -> a | _ -> rest
        RunProject args
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
    | FunctionDefinitionExpression funcDef ->
        let fd = match funcDef with InternalFuncDef d | ExportedFuncDef d -> d
        let vis = match funcDef with ExportedFuncDef _ -> "export " | InternalFuncDef _ -> ""
        printfn "%s%sFunctionDef \"%s\" : %A" pad vis fd.Identifier fd.Type
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

// -- Backend invocation helpers --

let private invokeBackend (fsoPath: string) (outputPath: string) (targetTriple: string option) (linkLibs: string list) (manifests: string list) =
    let targetArgs = match targetTriple with Some t -> $" --target {t}" | None -> ""
    let linkArgs = linkLibs |> List.map (fun l -> $" --link {l}") |> String.concat ""
    let manifestArgs = manifests |> List.map (fun m -> $" --manifest {m}") |> String.concat ""
    let psi = System.Diagnostics.ProcessStartInfo("furstc-backend", $"{fsoPath} {outputPath}{targetArgs}{linkArgs}{manifestArgs}")
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    try
        let proc = System.Diagnostics.Process.Start(psi)
        proc.WaitForExit()
        if proc.ExitCode <> 0 then
            let err = proc.StandardError.ReadToEnd()
            Result.Error $"backend error: {err}"
        else
            Result.Ok outputPath
    with
    | :? System.ComponentModel.Win32Exception ->
        Result.Error "furstc-backend not found in PATH"

let private invokeAr (objPaths: string list) (archivePath: string) =
    let args = objPaths |> String.concat " "
    let psi = System.Diagnostics.ProcessStartInfo("ar", $"rcs {archivePath} {args}")
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    let proc = System.Diagnostics.Process.Start(psi)
    proc.WaitForExit()
    if proc.ExitCode <> 0 then
        let err = proc.StandardError.ReadToEnd()
        Result.Error $"ar error: {err}"
    else
        Result.Ok archivePath

let private invokeLinker (objPaths: string list) (linkLibs: string list) (exePath: string) =
    let allInputs = (objPaths @ linkLibs) |> String.concat " "
    let psi = System.Diagnostics.ProcessStartInfo("cc", $"-o {exePath} {allInputs}")
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    let proc = System.Diagnostics.Process.Start(psi)
    proc.WaitForExit()
    if proc.ExitCode <> 0 then
        let err = proc.StandardError.ReadToEnd()
        Result.Error $"linker error: {err}"
    else
        Result.Ok exePath

// -- Build command --

let runBuild (files: string list) (projectName: string) (targetTriple: string option) (projectType: string option) (depPaths: (string * string) list) =
    let isProject = File.Exists "furst.yaml"
    let buildDir = if isProject then "build" else Path.GetDirectoryName(files.Head)
    let binDir = if isProject then "bin" else Path.GetDirectoryName(files.Head)
    Directory.CreateDirectory(buildDir) |> ignore
    Directory.CreateDirectory(binDir) |> ignore

    let manifestPaths = depPaths |> List.map snd

    // merge all source files in order → single compilation unit
    let mutable mergedSource = ""
    let mutable mergeError = false
    for file in files do
        match readFile file with
        | Result.Error e -> eprintfn "%s" e; mergeError <- true
        | Ok source ->
            printfn "  %s" file
            mergedSource <- mergedSource + "\n" + source

    if mergeError then 1
    else
        let entryFile = files.Head
        match TestTwoPhase.createAST entryFile mergedSource with
        | Result.Error e -> eprintfn "Parse error: %s" e; 1
        | Ok rows ->
            let results = rows |> List.map rowToExpression
            let errors = results |> List.choose (function Result.Error e -> Some e | _ -> None)
            match errors with
            | e :: _ -> formatError mergedSource e; 1
            | [] ->
                let nodes = results |> List.choose (function Ok n -> Some n | _ -> None)
                let lowered = Lowering.lower nodes

                let fsoPath = Path.Combine(buildDir, projectName + ".fso")
                FsoWriter.writeFso fsoPath entryFile lowered

                let isLibrary = projectType = Some "library"

                if isLibrary then
                    let objPath = Path.Combine(buildDir, projectName + ".o")
                    match invokeBackend fsoPath objPath targetTriple [] [] with
                    | Result.Error e -> eprintfn "%s" e; 1
                    | Ok _ ->
                        let archivePath = Path.Combine(binDir, $"lib{projectName}.a")
                        match invokeAr [objPath] archivePath with
                        | Result.Error e -> eprintfn "%s" e; 1
                        | Ok _ ->
                            let manifestPath = Path.Combine(binDir, $"lib{projectName}.fsi")
                            let exportedFns =
                                lowered |> List.choose (function
                                    | Lowering.TopExportedFunction fd ->
                                        Some $"{fd.Name} {fd.Parameters.Length}"
                                    | _ -> None)
                            File.WriteAllLines(manifestPath, exportedFns)
                            printfn "archived %s (%d exports)" archivePath exportedFns.Length
                            0
                else
                    let exePath = Path.Combine(binDir, projectName)
                    let linkLibs = depPaths |> List.map fst
                    match invokeBackend fsoPath exePath targetTriple linkLibs manifestPaths with
                    | Result.Error e -> eprintfn "%s" e; 1
                    | Ok path -> printfn "compiled %s" path; 0

// -- New command --

let runNew (opts: NewOptions) =
    let dir = opts.OutputDir
    let srcDir = Path.Combine(dir, "src")
    Directory.CreateDirectory(srcDir) |> ignore

    let yaml = $"""name: {opts.Name}
version: 0.1.0
type: executable

sources:
  - src/main.fu

targets:
  - arch: x86_64
    os: linux
"""
    File.WriteAllText(Path.Combine(dir, "furst.yaml"), yaml)

    let mainFu = """let main =
  0
"""
    File.WriteAllText(Path.Combine(srcDir, "main.fu"), mainFu)

    let gitignore = """bin/
build/
*.fso
*.o
*.ll
"""
    File.WriteAllText(Path.Combine(dir, ".gitignore"), gitignore)

    printfn "created project '%s' at %s" opts.Name dir
    printfn ""
    printfn "  cd %s" dir
    printfn "  furst build"
    0

// -- Build project --

let rec runBuildSingleProject (projectDir: string) =
    let prevDir = Directory.GetCurrentDirectory()
    Directory.SetCurrentDirectory(projectDir)
    let result =
        match ProjectConfig.load "furst.yaml" with
        | Result.Error e -> eprintfn "%s" e; 2
        | Ok project ->
            let missing = project.Sources |> List.filter (fun s -> not (File.Exists s))
            if not missing.IsEmpty then
                for m in missing do eprintfn "source file not found: %s" m
                2
            else
                let triple =
                    match project.Targets with
                    | [] -> None
                    | t :: _ -> Some (ProjectConfig.buildTriple t)
                let projType =
                    match project.Type with
                    | ProjectConfig.Library -> Some "library"
                    | ProjectConfig.Executable -> None

                let mutable depLibs = []
                let mutable depFailed = false
                for dep in project.Dependencies do
                    match dep with
                    | ProjectConfig.LocalDependency depPath ->
                        let depYaml = Path.Combine(depPath, "furst.yaml")
                        match ProjectConfig.load depYaml with
                        | Result.Error e -> eprintfn "dependency error: %s" e; depFailed <- true
                        | Ok depProject ->
                            printfn "building dependency %s..." depProject.Name
                            let depResult = runBuildSingleProject depPath
                            if depResult <> 0 then depFailed <- true
                            else
                                let libPath = Path.Combine(depPath, "bin", $"lib{depProject.Name}.a")
                                let manifestPath = Path.Combine(depPath, "bin", $"lib{depProject.Name}.fsi")
                                depLibs <- (libPath, manifestPath) :: depLibs
                    | ProjectConfig.RemoteDependency (name, _) ->
                        eprintfn "remote dependencies not yet supported: %s" name
                        depFailed <- true

                if depFailed then 1
                else
                    printfn "building %s (%s) for %s" project.Name project.Version (triple |> Option.defaultValue "host")
                    runBuild project.Sources project.Name triple projType (List.rev depLibs)
    Directory.SetCurrentDirectory(prevDir)
    result

and runBuildWorkspace () =
    match ProjectConfig.loadWorkspace "furst-workspace.yaml" with
    | Result.Error e -> eprintfn "%s" e; 2
    | Ok workspace ->
        let mutable failed = false
        for projectPath in workspace.Projects do
            if not failed then
                printfn "=== %s ===" projectPath
                let result = runBuildSingleProject projectPath
                if result <> 0 then failed <- true
        if failed then 1 else 0

and runBuildProject () =
    if File.Exists "furst-workspace.yaml" then
        runBuildWorkspace ()
    elif File.Exists "furst.yaml" then
        runBuildSingleProject "."
    else
        eprintfn "no furst.yaml or furst-workspace.yaml found"
        2

// -- Run commands --

let private executeProgram (exePath: string) (args: string list) =
    let argStr = args |> String.concat " "
    let psi = System.Diagnostics.ProcessStartInfo(exePath, argStr)
    psi.UseShellExecute <- false
    let proc = System.Diagnostics.Process.Start(psi)
    proc.WaitForExit()
    proc.ExitCode

let runRun (file: string) (args: string list) =
    let baseName = Path.GetFileNameWithoutExtension(file)
    let buildResult = runBuild [file] baseName None None []
    if buildResult <> 0 then buildResult
    else
        let isProject = File.Exists "furst.yaml"
        let binDir = if isProject then "bin" else Path.GetDirectoryName(file)
        let baseName = Path.GetFileNameWithoutExtension(file)
        let exePath = Path.Combine(binDir, baseName)
        executeProgram exePath args

let runRunProject (args: string list) =
    let buildResult = runBuildProject ()
    if buildResult <> 0 then buildResult
    else
        match ProjectConfig.load "furst.yaml" with
        | Result.Error e -> eprintfn "%s" e; 2
        | Ok project ->
            let exePath = Path.Combine("bin", project.Name)
            executeProgram exePath args

// -- Entry point --

let run (argv: string array) =
    match parseArgs argv with
    | Help -> printfn "%s" helpText; 0
    | Error msg -> eprintfn "%s" msg; eprintfn "Run 'furst help' for usage."; 2
    | New opts -> runNew opts
    | BuildProject -> runBuildProject ()
    | Build file -> runBuild [file] (Path.GetFileNameWithoutExtension(file)) None None []
    | Run (file :: args) -> runRun file args
    | Run [] -> runRunProject []
    | RunProject args -> runRunProject args
    | Check file -> runCheck file
    | Lex file -> runLex file
    | Ast file -> runAst file
