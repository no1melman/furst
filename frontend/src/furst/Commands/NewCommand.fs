module Commands.New

open System.IO
open Spectre.Console

type Options = { Name: string; OutputDir: string }

let run (opts: Options) =
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

    let mainFu = """let main args =
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

    AnsiConsole.MarkupLine $"[green]created project '{Markup.Escape opts.Name}' at {Markup.Escape dir}[/]"
    printfn ""
    printfn "  cd %s" dir
    printfn "  furst build"
    0
