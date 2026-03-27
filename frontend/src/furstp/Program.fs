module Furstp.Main

open System
open System.IO
open Types
open Lowered

let private usage () =
    eprintfn "furstp — Furst frontend compiler"
    eprintfn ""
    eprintfn "Usage: furstp -o <output.fso> <file1.fu> [file2.fu ...]"
    2

[<EntryPoint>]
let main argv =
    let args = argv |> Array.toList
    match args with
    | "-o" :: outputPath :: files when not files.IsEmpty ->
        let mutable allLowered : TopLevelDef list = []
        let mutable failed = false

        for file in files do
            match Compiler.parseFile file with
            | Result.Error error ->
                eprintfn "%s" error
                failed <- true
            | Ok (nodes, _source) ->
                let modulePath = Compiler.deriveModulePath file
                let lowered = Compiler.lowerFileNodes modulePath nodes
                allLowered <- allLowered @ lowered

        if failed then 1
        else
            let sourceFile = files.Head
            FsoWriter.writeFso outputPath sourceFile allLowered
            0
    | _ -> usage ()
