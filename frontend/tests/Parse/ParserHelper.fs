module ParserHelper

open System
open Parsers
open FParsec
open Types

// Active patterns for matching TokenWithMetadata in tests

// Match token at specific position
let (|TokenAt|_|) (expectedLine: int64, expectedCol: int64) (twm: TokenWithMetadata) =
    match twm.Line, twm.Column with
    | Line l, Column c when l = expectedLine && c = expectedCol ->
        Some twm.Token
    | _ -> None

// Match token, ignore position
let (|AnyToken|) (twm: TokenWithMetadata) = twm.Token

// Match token and extract all metadata
let (|WithMeta|) (twm: TokenWithMetadata) =
    (twm.Token, twm.Line, twm.Column, twm.Length)

let consumeEntireContent (parser: Parsers.Parser<'t>) =
  (spaces >>. parser .>> spaces .>> eof)

let testParser<'t> (parser: Parsers.Parser<'t>) document (successFn: 't -> unit) : unit =
  let result = runParserOnString (consumeEntireContent parser) BlockScopeParserState.Default "code" document

  match result with
  | Success (r, _, _) -> successFn r
  | Failure (e, _, _) -> raise (Exception(e))
  
let failParser<'t> (parser: Parsers.Parser<'t>) document (failFn: string -> unit) : unit =
  let result = runParserOnString (consumeEntireContent parser) BlockScopeParserState.Default "code" document

  match result with
  | Success (r, _, _) -> raise (Exception("Parser should have failed"))
  | Failure (e, _, _) -> failFn e

let pureTestParser<'t> (parser: Parsers.Parser<'t>) document (successFn: 't -> unit) : unit =
  let result = runParserOnString parser BlockScopeParserState.Default "code" document
  
  match result with
  | Success(r, _, _) -> successFn r
  | Failure(e, _, _) -> raise (Exception(e))


let pureFailParser<'t> (parser: Parsers.Parser<'t>) document (failFn: string -> unit) : unit =
  let result = runParserOnString parser BlockScopeParserState.Default "code" document

  match result with
  | Success (r, _, _) -> raise (Exception("Parser should have failed"))
  | Failure (e, _, _) -> failFn e

