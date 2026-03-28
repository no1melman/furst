module Commands.Check

open AstBuilder
open Spectre.Console

let run (file: string) =
    match Compiler.readFile file with
    | Result.Error error -> AnsiConsole.MarkupLine $"[red]{Markup.Escape error}[/]"; 2
    | Ok source ->
        match Lexer.createAST file source with
        | Result.Error error -> AnsiConsole.MarkupLine $"[red]Parse error: {Markup.Escape error}[/]"; 1
        | Ok rows ->
            let results = rows |> List.map rowToExpression
            let errors = results |> List.choose (function Result.Error error -> Some error | _ -> None)
            match errors with
            | [] -> AnsiConsole.MarkupLine "[green]OK[/]"; 0
            | errors ->
                for error in errors do
                    Compiler.formatError source error
                1
