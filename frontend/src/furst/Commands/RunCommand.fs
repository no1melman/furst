module Commands.Run

open System
open System.IO
open Spectre.Console

let private executeProgram (exePath: string) (args: string list) =
    let argStr = args |> String.concat " "
    let psi = Diagnostics.ProcessStartInfo(exePath, argStr)
    psi.UseShellExecute <- false
    let proc = Diagnostics.Process.Start(psi)
    proc.WaitForExit()
    proc.ExitCode

let run (file: string) (args: string list) =
    let baseName = Path.GetFileNameWithoutExtension(file)
    let buildResult = Commands.Build.run [file] baseName None None []
    if buildResult <> 0 then buildResult
    else
        let isProject = File.Exists "furst.yaml"
        let binDir = if isProject then "bin" else Path.GetDirectoryName(file)
        let baseName = Path.GetFileNameWithoutExtension(file)
        let exePath = Path.Combine(binDir, baseName)
        executeProgram exePath args

let runProject (args: string list) =
    let buildResult = Commands.Build.runProject ()
    if buildResult <> 0 then buildResult
    else
        match ProjectConfig.load "furst.yaml" with
        | Result.Error error -> AnsiConsole.MarkupLine $"[red]{Markup.Escape error}[/]"; 2
        | Ok project ->
            let exePath = Path.Combine("bin", project.Name)
            executeProgram exePath args
