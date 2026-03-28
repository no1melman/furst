module Commands.Build

open System
open System.IO
open Types
open Spectre.Console

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

let run (files: string list) (projectName: string) (targetTriple: string option) (projectType: string option) (libRoot: string option) (depPaths: (string * string) list) =
    let isProject = File.Exists "furst.yaml"
    let buildDir = if isProject then "build" else Path.GetDirectoryName(files.Head)
    let binDir = if isProject then "bin" else Path.GetDirectoryName(files.Head)
    Directory.CreateDirectory(buildDir) |> ignore
    Directory.CreateDirectory(binDir) |> ignore

    let manifestPaths = depPaths |> List.map snd

    for file in files do printfn "  %s" file

    match Compiler.compileFiles libRoot files manifestPaths with
    | Result.Error error -> AnsiConsole.MarkupLine $"[red]{Markup.Escape error}[/]"; 1
    | Ok lowered ->
        let lowered = lowered
        let entryFile = files.Head
        let fsoPath = Path.Combine(buildDir, projectName + ".fso")
        FsoWriter.writeFso fsoPath entryFile lowered

        let isLibrary = projectType = Some "library"

        if isLibrary then
            let objPath = Path.Combine(buildDir, projectName + ".o")
            match invokeBackend fsoPath objPath targetTriple [] [] with
            | Result.Error error -> AnsiConsole.MarkupLine $"[red]{Markup.Escape error}[/]"; 1
            | Ok _ ->
                let archivePath = Path.Combine(binDir, $"lib{projectName}.a")
                match invokeAr [objPath] archivePath with
                | Result.Error error -> AnsiConsole.MarkupLine $"[red]{Markup.Escape error}[/]"; 1
                | Ok _ ->
                    let manifestPath = Path.Combine(binDir, $"lib{projectName}.fsi")
                    let exportedFns =
                        lowered |> List.choose (function
                            | Lowered.TopFunction functionDef when functionDef.Visibility = Public ->
                                let (Types.ModulePath parts) = functionDef.ModulePath
                                let qualifiedName = System.String.Join(".", parts @ [functionDef.Name])
                                Some $"{qualifiedName} {functionDef.Parameters.Length}"
                            | _ -> None)
                    File.WriteAllLines(manifestPath, exportedFns)
                    AnsiConsole.MarkupLine $"[green]archived {Markup.Escape archivePath} ({exportedFns.Length} exports)[/]"
                    0
        else
            let exePath = Path.Combine(binDir, projectName)
            let linkLibs = depPaths |> List.map fst
            match invokeBackend fsoPath exePath targetTriple linkLibs manifestPaths with
            | Result.Error error -> AnsiConsole.MarkupLine $"[red]{Markup.Escape error}[/]"; 1
            | Ok path -> AnsiConsole.MarkupLine $"[green]compiled {Markup.Escape path}[/]"; 0

let rec runSingleProject (projectDir: string) =
    let prevDir = Directory.GetCurrentDirectory()
    Directory.SetCurrentDirectory(projectDir)
    let result =
        match ProjectConfig.load "furst.yaml" with
        | Result.Error error -> AnsiConsole.MarkupLine $"[red]{Markup.Escape error}[/]"; 2
        | Ok project ->
            let missing = project.Sources |> List.filter (fun source -> not (File.Exists source))
            if not missing.IsEmpty then
                for missingFile in missing do AnsiConsole.MarkupLine $"[red]source file not found: {Markup.Escape missingFile}[/]"
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
                let libRoot =
                    match project.Type with
                    | ProjectConfig.Library -> project.Library
                    | ProjectConfig.Executable -> None

                let mutable depLibs = []
                let mutable depFailed = false
                for dep in project.Dependencies do
                    match dep with
                    | ProjectConfig.LocalDependency depPath ->
                        let depYaml = Path.Combine(depPath, "furst.yaml")
                        match ProjectConfig.load depYaml with
                        | Result.Error error -> AnsiConsole.MarkupLine $"[red]dependency error: {Markup.Escape error}[/]"; depFailed <- true
                        | Ok depProject ->
                            printfn "building dependency %s..." depProject.Name
                            let depResult = runSingleProject depPath
                            if depResult <> 0 then depFailed <- true
                            else
                                let libPath = Path.Combine(depPath, "bin", $"lib{depProject.Name}.a")
                                let manifestPath = Path.Combine(depPath, "bin", $"lib{depProject.Name}.fsi")
                                depLibs <- (libPath, manifestPath) :: depLibs
                    | ProjectConfig.RemoteDependency (name, _) ->
                        AnsiConsole.MarkupLine $"[red]remote dependencies not yet supported: {Markup.Escape name}[/]"
                        depFailed <- true

                if depFailed then 1
                else
                    printfn "building %s (%s) for %s" project.Name project.Version (triple |> Option.defaultValue "host")
                    run project.Sources project.Name triple projType libRoot (List.rev depLibs)
    Directory.SetCurrentDirectory(prevDir)
    result

and runWorkspace () =
    match ProjectConfig.loadWorkspace "furst-workspace.yaml" with
    | Result.Error error -> AnsiConsole.MarkupLine $"[red]{Markup.Escape error}[/]"; 2
    | Ok workspace ->
        let mutable failed = false
        for projectPath in workspace.Projects do
            if not failed then
                printfn "=== %s ===" projectPath
                let result = runSingleProject projectPath
                if result <> 0 then failed <- true
        if failed then 1 else 0

and runProject () =
    if File.Exists "furst-workspace.yaml" then
        runWorkspace ()
    elif File.Exists "furst.yaml" then
        runSingleProject "."
    else
        AnsiConsole.MarkupLine "[red]no furst.yaml or furst-workspace.yaml found[/]"
        2
