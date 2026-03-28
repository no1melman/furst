module ProjectConfig

open System.IO
open YamlDotNet.Serialization
open YamlDotNet.Serialization.NamingConventions

// -- YAML DTOs (raw deserialization targets, nullable strings) --

[<CLIMutable>]
type YamlTarget = {
    Arch: string
    Os: string
}

[<CLIMutable>]
type YamlDependency = {
    Path: string
    Name: string
    Version: string
}

[<CLIMutable>]
type YamlLibrary = {
    Name: string
}

[<CLIMutable>]
type YamlProject = {
    Name: string
    Version: string
    Type: string
    Library: YamlLibrary
    Sources: string array
    Targets: YamlTarget array
    Dependencies: YamlDependency array
}

// -- Domain types (clean, used by the rest of the compiler) --

type Target = {
    Arch: string
    Os: string
}

type Dependency =
    | LocalDependency of path: string
    | RemoteDependency of name: string * version: string option

type ProjectType = Executable | Library

type Project = {
    Name: string
    Version: string
    Type: ProjectType
    Library: string option
    Sources: string list
    Targets: Target list
    Dependencies: Dependency list
}

// -- Mapping --

let private mapTarget (y: YamlTarget) : Target =
    { Arch = y.Arch; Os = y.Os }

let private mapDependency (y: YamlDependency) : Result<Dependency, string> =
    match Option.ofObj y.Path, Option.ofObj y.Name with
    | Some path, _ -> Result.Ok (LocalDependency path)
    | _, Some name -> Result.Ok (RemoteDependency (name, Option.ofObj y.Version))
    | None, None -> Result.Error "dependency must have either 'path' or 'name'"

let private mapProjectType (t: string) =
    match t with
    | "library" -> Library
    | _ -> Executable

let private mapProject (y: YamlProject) : Result<Project, string> =
    let deps =
        match y.Dependencies with
        | null -> Result.Ok []
        | arr ->
            arr
            |> Array.toList
            |> List.map mapDependency
            |> List.fold (fun acc r ->
                match acc, r with
                | Result.Ok ds, Result.Ok d -> Result.Ok (ds @ [d])
                | Result.Error e, _ -> Result.Error e
                | _, Result.Error e -> Result.Error e
            ) (Result.Ok [])
    match deps with
    | Result.Error e -> Result.Error e
    | Ok resolvedDeps ->
        let sources =
            match y.Sources with
            | null | [||] -> ["src/main.fu"]
            | arr -> arr |> Array.toList
        Result.Ok {
            Name = y.Name
            Version = y.Version
            Type = mapProjectType y.Type
            Library =
                match box y.Library with
                | null -> None
                | _ -> Option.ofObj y.Library.Name
            Sources = sources
            Targets =
                match y.Targets with
                | null -> []
                | arr -> arr |> Array.toList |> List.map mapTarget
            Dependencies = resolvedDeps
        }

let buildTriple (target: Target) =
    let vendor = "unknown"
    let env =
        match target.Os with
        | "linux" -> "gnu"
        | "darwin" -> ""
        | "windows" -> "msvc"
        | _ -> ""
    match env with
    | "" -> $"{target.Arch}-{vendor}-{target.Os}"
    | e -> $"{target.Arch}-{vendor}-{target.Os}-{e}"

let load (path: string) : Result<Project, string> =
    if not (File.Exists path) then
        Result.Error $"project file not found: {path}"
    else
        try
            let yaml = File.ReadAllText(path)
            let deserializer =
                DeserializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .Build()
            let yamlProject = deserializer.Deserialize<YamlProject>(yaml)
            mapProject yamlProject
        with
        | ex -> Result.Error $"failed to parse {path}: {ex.Message}"

// -- Workspace --

[<CLIMutable>]
type YamlWorkspace = {
    Projects: string array
}

type Workspace = {
    Projects: string list
}

let loadWorkspace (path: string) : Result<Workspace, string> =
    if not (File.Exists path) then
        Result.Error $"workspace file not found: {path}"
    else
        try
            let yaml = File.ReadAllText(path)
            let deserializer =
                DeserializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .Build()
            let ws = deserializer.Deserialize<YamlWorkspace>(yaml)
            let projects =
                match ws.Projects with
                | null -> []
                | arr -> arr |> Array.toList
            Result.Ok { Projects = projects }
        with
        | ex -> Result.Error $"failed to parse {path}: {ex.Message}"
