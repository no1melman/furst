module Furst.Integration.TestHelper

open System
open System.IO
open System.Diagnostics

let private projectRoot =
    let mutable dir = DirectoryInfo(__SOURCE_DIRECTORY__)
    while dir <> null && not (File.Exists(Path.Combine(dir.FullName, "flake.nix"))) do
        dir <- dir.Parent
    dir.FullName

let private furstBin = Path.Combine(projectRoot, "build", "furst")

type RunResult = {
    ExitCode: int
    Stdout: string
    Stderr: string
}

let private runProcess (exe: string) (args: string) (workDir: string) =
    let psi = ProcessStartInfo(exe, args)
    psi.WorkingDirectory <- workDir
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    let proc = Process.Start(psi)
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    proc.WaitForExit()
    { ExitCode = proc.ExitCode; Stdout = stdout; Stderr = stderr }

/// Scaffold a temporary Furst project, build it, and optionally run it.
/// Returns (buildResult, runResult option).
type ProjectFixture = {
    Dir: string
    Name: string
}

let createProject (name: string) (projectType: string) (sources: (string * string) list) =
    let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
    Directory.CreateDirectory(tmpDir) |> ignore
    let srcDir = Path.Combine(tmpDir, "src")
    Directory.CreateDirectory(srcDir) |> ignore

    let sourceEntries =
        sources
        |> List.map (fun (fileName, _) -> $"  - src/{fileName}")
        |> String.concat "\n"

    let yaml = $"""name: {name}
version: 0.1.0
type: {projectType}
sources:
{sourceEntries}
targets:
  - arch: x86_64
    os: linux
"""
    File.WriteAllText(Path.Combine(tmpDir, "furst.yaml"), yaml)

    for (fileName, content) in sources do
        File.WriteAllText(Path.Combine(srcDir, fileName), content)

    { Dir = tmpDir; Name = name }

let createLibraryProject (name: string) (libName: string) (sources: (string * string) list) =
    let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
    Directory.CreateDirectory(tmpDir) |> ignore
    let srcDir = Path.Combine(tmpDir, "src")
    Directory.CreateDirectory(srcDir) |> ignore

    let sourceEntries =
        sources
        |> List.map (fun (fileName, _) -> $"  - src/{fileName}")
        |> String.concat "\n"

    let yaml = $"""name: {name}
version: 0.1.0
type: library
library:
  name: {libName}
sources:
{sourceEntries}
targets:
  - arch: x86_64
    os: linux
"""
    File.WriteAllText(Path.Combine(tmpDir, "furst.yaml"), yaml)

    for (fileName, content) in sources do
        File.WriteAllText(Path.Combine(srcDir, fileName), content)

    { Dir = tmpDir; Name = name }

let buildProject (fixture: ProjectFixture) =
    // Put furst on PATH
    let env = Environment.GetEnvironmentVariable("PATH")
    let buildDir = Path.GetDirectoryName(furstBin)
    Environment.SetEnvironmentVariable("PATH", $"{buildDir}:{env}")
    let result = runProcess furstBin "build" fixture.Dir
    Environment.SetEnvironmentVariable("PATH", env)
    result

let runProject (fixture: ProjectFixture) =
    let env = Environment.GetEnvironmentVariable("PATH")
    let buildDir = Path.GetDirectoryName(furstBin)
    Environment.SetEnvironmentVariable("PATH", $"{buildDir}:{env}")
    let result = runProcess furstBin "run" fixture.Dir
    Environment.SetEnvironmentVariable("PATH", env)
    result

let cleanup (fixture: ProjectFixture) =
    try Directory.Delete(fixture.Dir, true) with _ -> ()
