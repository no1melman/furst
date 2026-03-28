module Furst.Tests.CompileHelper

open Types

let compileSource (files: (string * string) list) =
    let tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString())
    System.IO.Directory.CreateDirectory(tmpDir) |> ignore
    let srcDir = System.IO.Path.Combine(tmpDir, "src")
    System.IO.Directory.CreateDirectory(srcDir) |> ignore
    let paths =
        files |> List.map (fun (name, source) ->
            let path = System.IO.Path.Combine(srcDir, name)
            System.IO.File.WriteAllText(path, source)
            path)
    let result = Compiler.compileFiles None paths []
    try System.IO.Directory.Delete(tmpDir, true) with _ -> ()
    result

let compileSourceAsLibrary (libRoot: string) (files: (string * string) list) =
    let tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString())
    System.IO.Directory.CreateDirectory(tmpDir) |> ignore
    let srcDir = System.IO.Path.Combine(tmpDir, "src")
    System.IO.Directory.CreateDirectory(srcDir) |> ignore
    let paths =
        files |> List.map (fun (name, source) ->
            let path = System.IO.Path.Combine(srcDir, name)
            System.IO.File.WriteAllText(path, source)
            path)
    let result = Compiler.compileFiles (Some libRoot) paths []
    try System.IO.Directory.Delete(tmpDir, true) with _ -> ()
    result

let compileWithManifest (manifestLines: string array) (files: (string * string) list) =
    let tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString())
    System.IO.Directory.CreateDirectory(tmpDir) |> ignore
    let srcDir = System.IO.Path.Combine(tmpDir, "src")
    System.IO.Directory.CreateDirectory(srcDir) |> ignore
    let manifestPath = System.IO.Path.Combine(tmpDir, "libdep.fsi")
    System.IO.File.WriteAllLines(manifestPath, manifestLines)
    let paths =
        files |> List.map (fun (name, source) ->
            let path = System.IO.Path.Combine(srcDir, name)
            System.IO.File.WriteAllText(path, source)
            path)
    let result = Compiler.compileFiles None paths [manifestPath]
    try System.IO.Directory.Delete(tmpDir, true) with _ -> ()
    result
