module ParserHelper

open System
open FParsec

let consumeEntireContent (parser: CommonParsers.Parser<'t>) =
  (spaces >>. parser .>> spaces .>> eof)

let testParser<'t> (parser: CommonParsers.Parser<'t>) document (successFn: 't -> unit) : unit =
  let result = runParserOnString (consumeEntireContent parser) () "code" document

  match result with
  | Success (r, _, _) -> successFn r
  | Failure (e, _, _) -> raise (Exception(e))
  
let failParser<'t> (parser: CommonParsers.Parser<'t>) document (failFn: string -> unit) : unit =
  let result = runParserOnString (consumeEntireContent parser) () "code" document

  match result with
  | Success (r, _, _) -> raise (Exception("Parser should have failed"))
  | Failure (e, _, _) -> failFn e
