module Commands.Lex

open Spectre.Console

let run (file: string) =
    match Compiler.readFile file with
    | Result.Error error -> AnsiConsole.MarkupLine $"[red]{Markup.Escape error}[/]"; 2
    | Ok source ->
        match Lexer.tokenise file source with
        | Result.Error error -> AnsiConsole.MarkupLine $"[red]Parse error: {Markup.Escape error}[/]"; 1
        | Ok rows ->
            rows |> List.iter Lexer.printRow
            0
