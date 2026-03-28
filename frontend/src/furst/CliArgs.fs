module CliArgs

open Argu

type NewArgs =
    | [<Mandatory; AltCommandLine("-n")>] Name of string
    | [<AltCommandLine("-o")>] Output of string
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Name _ -> "project name"
            | Output _ -> "output directory (default: ./<name>)"

type BuildArgs =
    | [<MainCommand>] File of string
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | File _ -> ".fu file to compile (omit for project build)"

type RunArgs =
    | [<MainCommand>] File of string
    | [<Last>] Rest of string list
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | File _ -> ".fu file to run (omit for project run)"
            | Rest _ -> "arguments passed to the program"

type CheckArgs =
    | [<MainCommand; Mandatory>] File of string
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | File _ -> ".fu file to check"

type LexArgs =
    | [<MainCommand; Mandatory>] File of string
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | File _ -> ".fu file to lex"

type AstArgs =
    | [<MainCommand; Mandatory>] File of string
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | File _ -> ".fu file to parse"

[<CliPrefix(CliPrefix.None)>]
type CliArgs =
    | [<SubCommand>] New of ParseResults<NewArgs>
    | [<SubCommand>] Build of ParseResults<BuildArgs>
    | [<SubCommand>] Run of ParseResults<RunArgs>
    | [<SubCommand>] Check of ParseResults<CheckArgs>
    | [<SubCommand>] Lex of ParseResults<LexArgs>
    | [<SubCommand>] Ast of ParseResults<AstArgs>
    | [<AltCommandLine("-V")>] Version
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | New _ -> "create a new project"
            | Build _ -> "compile a file or project"
            | Run _ -> "build and run a file or project"
            | Check _ -> "parse and check for errors"
            | Lex _ -> "show lexer token output"
            | Ast _ -> "show parsed AST"
            | Version -> "show version"
