module Cli

open System.IO
open Argu
open Spectre.Console
open CliArgs

let run (argv: string array) =
    let parser = ArgumentParser.Create<CliArgs>(programName = "furst")
    try
        let results = parser.ParseCommandLine(inputs = argv, raiseOnUsage = true)
        match results.GetSubCommand() with
        | CliArgs.Version ->
            Commands.Version.run ()
        | CliArgs.New newArgs ->
            let name = newArgs.GetResult NewArgs.Name
            let outputDir = newArgs.TryGetResult NewArgs.Output |> Option.defaultValue ("./" + name)
            Commands.New.run { Name = name; OutputDir = outputDir }
        | CliArgs.Build buildArgs ->
            match buildArgs.TryGetResult BuildArgs.File with
            | Some file -> Commands.Build.run [file] (Path.GetFileNameWithoutExtension(file)) None None []
            | None -> Commands.Build.runProject ()
        | CliArgs.Run runArgs ->
            match runArgs.TryGetResult RunArgs.File with
            | Some file ->
                let args = runArgs.TryGetResult RunArgs.Rest |> Option.defaultValue []
                Commands.Run.run file args
            | None ->
                let args = runArgs.TryGetResult RunArgs.Rest |> Option.defaultValue []
                Commands.Run.runProject args
        | CliArgs.Check checkArgs ->
            Commands.Check.run (checkArgs.GetResult CheckArgs.File)
        | CliArgs.Lex lexArgs ->
            Commands.Lex.run (lexArgs.GetResult LexArgs.File)
        | CliArgs.Ast astArgs ->
            Commands.Ast.run (astArgs.GetResult AstArgs.File)
    with
    | :? ArguParseException as ex ->
        match ex.ErrorCode with
        | ErrorCode.HelpText ->
            printfn "%s" ex.Message
            0
        | ErrorCode.PostProcess ->
            let lines = ex.Message.Split('\n')
            let usage = lines |> Array.skipWhile (fun l -> not (l.StartsWith("USAGE:")))
            printfn "%s" (System.String.Join("\n", usage))
            0
        | _ ->
            AnsiConsole.MarkupLine $"[red]{Markup.Escape ex.Message}[/]"
            2
