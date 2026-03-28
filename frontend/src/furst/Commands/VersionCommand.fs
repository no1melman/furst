module Commands.Version

open Spectre.Console

let private getVersion () =
    let assembly = System.Reflection.Assembly.GetEntryAssembly()
    let attrs = assembly.GetCustomAttributes(typeof<System.Reflection.AssemblyInformationalVersionAttribute>, false)
    match attrs |> Array.tryHead with
    | Some attr -> (attr :?> System.Reflection.AssemblyInformationalVersionAttribute).InformationalVersion
    | None -> "unknown"

let run () =
    AnsiConsole.MarkupLine $"[bold]furst[/] {Markup.Escape (getVersion ())}"
    0
