module Commands.Check

open RowParser
open TokenCombinators
open Spectre.Console

let run (file: string) =
    match Compiler.readFile file with
    | Result.Error error -> AnsiConsole.MarkupLine $"[red]{Markup.Escape error}[/]"; 2
    | Ok source ->
        match Lexer.tokenise file source with
        | Result.Error error -> AnsiConsole.MarkupLine $"[red]Parse error: {Markup.Escape error}[/]"; 1
        | Ok rows ->
            match RowParser.parseFile rows emptyState with
            | Ok _ -> AnsiConsole.MarkupLine "[green]OK[/]"; 0
            | Error error ->
                Compiler.formatError source error
                1
