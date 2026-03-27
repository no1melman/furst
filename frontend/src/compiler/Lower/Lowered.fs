module Lowered

open Types
open Ast

type LoweredParam = {
    Name: string
    Type: TypeDefinitions
}

type LoweredFunctionDef = {
    Name: string
    ReturnType: TypeDefinitions
    Parameters: LoweredParam list
    Body: Expression list
    Location: SourceLocation
    ModulePath: ModulePath
    Visibility: Visibility
}

type LoweredStructDef = {
    Name: string
    Fields: (string * TypeDefinitions) list
    Location: SourceLocation
    ModulePath: ModulePath
}

type TopLevelDef =
    | TopFunction of LoweredFunctionDef
    | TopStruct of LoweredStructDef

let funcDetails (functionDef: FunctionDefinition) =
    let (FunctionDefinition details) = functionDef
    details
