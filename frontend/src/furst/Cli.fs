module Cli

open System
open System.IO
open Types
open Ast
open AstBuilder

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
    | Version
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
    | [ "--version" ] | [ "-V" ] -> Version
    | [ "help" ] | [ "--help" ] | [ "-h" ] | [] -> Help
    | cmd :: _ -> Error $"Unknown command: {cmd}"

let runLex (file: string) =
    match Compiler.readFile file with
    | Result.Error error -> eprintfn "%s" error; 2
    | Ok source ->
        match Lexer.createAST file source with
        | Result.Error error -> eprintfn "Parse error: %s" error; 1
        | Ok rows ->
            rows |> List.iter Lexer.rowReader
            0

let rec printExpr (indent: int) (expr: Expression) =
    let pad = System.String(' ', indent)
    match expr with
    | LetBindingExpression letBinding ->
        printfn "%sLetBinding \"%s\" : %A" pad letBinding.Name letBinding.Type
        printExpr (indent + 2) letBinding.Value
    | FunctionDefinitionExpression (FunctionDefinition details) ->
        printfn "%sFunctionDef \"%s\" : %A" pad details.Identifier details.Type
        printfn "%s  Params" pad
        for param in details.Parameters do
            let (Word word) = param.Name
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
    | ModuleDeclaration parts ->
        printfn "%smod %s" pad (String.Join(".", parts))
    | OpenDeclaration parts ->
        printfn "%sopen %s" pad (String.Join(".", parts))

let runAst (file: string) =
    match Compiler.readFile file with
    | Result.Error error -> eprintfn "%s" error; 2
    | Ok source ->
        match Lexer.createAST file source with
        | Result.Error error -> eprintfn "Parse error: %s" error; 1
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

let runCheck (file: string) =
    match Compiler.readFile file with
    | Result.Error error -> eprintfn "%s" error; 2
    | Ok source ->
        match Lexer.createAST file source with
        | Result.Error error -> eprintfn "Parse error: %s" error; 1
        | Ok rows ->
            let results = rows |> List.map rowToExpression
            let errors = results |> List.choose (function Result.Error error -> Some error | _ -> None)
            match errors with
            | [] -> printfn "OK"; 0
            | errors ->
                for error in errors do
                    Compiler.formatError source error
                1

// -- Backend invocation helpers --

let private invokeBackend (fsoPath: string) (outputPath: string) (targetTriple: string option) (linkLibs: string list) (manifests: string list) =
    let targetArgs = match targetTriple with Some triple -> $" --target {triple}" | None -> ""
    let linkArgs = linkLibs |> List.map (fun lib -> $" --link {lib}") |> String.concat ""
    let manifestArgs = manifests |> List.map (fun manifest -> $" --manifest {manifest}") |> String.concat ""
    let psi = Diagnostics.ProcessStartInfo("furstc", $"{fsoPath} {outputPath}{targetArgs}{linkArgs}{manifestArgs}")
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    try
        let proc = Diagnostics.Process.Start(psi)
        proc.WaitForExit()
        if proc.ExitCode <> 0 then
            let error = proc.StandardError.ReadToEnd()
            Result.Error $"backend error: {error}"
        else
            Result.Ok outputPath
    with
    | :? ComponentModel.Win32Exception ->
        Result.Error "furstc not found in PATH"

let private invokeAr (objPaths: string list) (archivePath: string) =
    let args = objPaths |> String.concat " "
    let psi = Diagnostics.ProcessStartInfo("ar", $"rcs {archivePath} {args}")
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    let proc = Diagnostics.Process.Start(psi)
    proc.WaitForExit()
    if proc.ExitCode <> 0 then
        let error = proc.StandardError.ReadToEnd()
        Result.Error $"ar error: {error}"
    else
        Result.Ok archivePath

// -- Build command --

let runBuild (files: string list) (projectName: string) (targetTriple: string option) (projectType: string option) (depPaths: (string * string) list) =
    let isProject = File.Exists "furst.yaml"
    let buildDir = if isProject then "build" else Path.GetDirectoryName(files.Head)
    let binDir = if isProject then "bin" else Path.GetDirectoryName(files.Head)
    Directory.CreateDirectory(buildDir) |> ignore
    Directory.CreateDirectory(binDir) |> ignore

    let manifestPaths = depPaths |> List.map snd

    let mutable allLowered : Lowered.TopLevelDef list = []
    let mutable parseError = false
    for file in files do
        printfn "  %s" file
        match Compiler.parseFile file with
        | Result.Error error -> eprintfn "%s" error; parseError <- true
        | Ok (nodes, _source) ->
            let modulePath = Compiler.deriveModulePath file
            let lowered = Compiler.lowerFileNodes modulePath nodes
            allLowered <- allLowered @ lowered

    if parseError then 1
    else
        let lowered = allLowered
        let entryFile = files.Head
        let fsoPath = Path.Combine(buildDir, projectName + ".fso")
        FsoWriter.writeFso fsoPath entryFile lowered

        let isLibrary = projectType = Some "library"

        if isLibrary then
            let objPath = Path.Combine(buildDir, projectName + ".o")
            match invokeBackend fsoPath objPath targetTriple [] [] with
            | Result.Error error -> eprintfn "%s" error; 1
            | Ok _ ->
                let archivePath = Path.Combine(binDir, $"lib{projectName}.a")
                match invokeAr [objPath] archivePath with
                | Result.Error error -> eprintfn "%s" error; 1
                | Ok _ ->
                    let manifestPath = Path.Combine(binDir, $"lib{projectName}.fsi")
                    let exportedFns =
                        lowered |> List.choose (function
                            | Lowered.TopFunction functionDef when functionDef.Visibility = Public ->
                                Some $"{functionDef.Name} {functionDef.Parameters.Length}"
                            | _ -> None)
                    File.WriteAllLines(manifestPath, exportedFns)
                    printfn "archived %s (%d exports)" archivePath exportedFns.Length
                    0
        else
            let exePath = Path.Combine(binDir, projectName)
            let linkLibs = depPaths |> List.map fst
            match invokeBackend fsoPath exePath targetTriple linkLibs manifestPaths with
            | Result.Error error -> eprintfn "%s" error; 1
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
        | Result.Error error -> eprintfn "%s" error; 2
        | Ok project ->
            let missing = project.Sources |> List.filter (fun source -> not (File.Exists source))
            if not missing.IsEmpty then
                for missingFile in missing do eprintfn "source file not found: %s" missingFile
                2
            else
                let triple =
                    match project.Targets with
                    | [] -> None
                    | target :: _ -> Some (ProjectConfig.buildTriple target)
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
                        | Result.Error error -> eprintfn "dependency error: %s" error; depFailed <- true
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
    | Result.Error error -> eprintfn "%s" error; 2
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
    let psi = Diagnostics.ProcessStartInfo(exePath, argStr)
    psi.UseShellExecute <- false
    let proc = Diagnostics.Process.Start(psi)
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
        | Result.Error error -> eprintfn "%s" error; 2
        | Ok project ->
            let exePath = Path.Combine("bin", project.Name)
            executeProgram exePath args

// -- Entry point --

let private getVersion () =
    let assembly = System.Reflection.Assembly.GetEntryAssembly()
    let attrs = assembly.GetCustomAttributes(typeof<System.Reflection.AssemblyInformationalVersionAttribute>, false)
    match attrs |> Array.tryHead with
    | Some attr -> (attr :?> System.Reflection.AssemblyInformationalVersionAttribute).InformationalVersion
    | None -> "unknown"

let run (argv: string array) =
    match parseArgs argv with
    | Version -> printfn "furst %s" (getVersion ()); 0
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
