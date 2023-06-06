#r "nuget:FParsec"
#load "./main/BasicTypes.fs" "./main/CommonParsers.fs" 

open FParsec
open CommonParsers

let document = """let a =
  b
  let c =
  d
"""

let (<!>) (p: Parser<_,_>) label : Parser<_,_> =
    fun stream ->
        printfn "%A: Entering %s" stream.Position label
        let reply = p stream
        printfn "%A: Leaving %s (%A)" stream.Position label reply.Status
        reply


let indentSize = 2

let mainParser, mainParserRef = createParserForwardedToRef<string list, BlockScopeParserState>()

let goDeepMut =
  updateUserState (fun bsp -> { bsp with Depth = bsp.Depth + 1 })

let backOutMut =
  updateUserState (fun bsp -> { bsp with Depth = bsp.Depth - 1 })

let depthFromState (pfn: int -> Parser<_,BlockScopeParserState>) =
  (fun (stream: CharStream<BlockScopeParserState>) ->
    let { Depth = depth } = stream.UserState
    (pfn depth) stream
    )

let onlyDepthSpacesMut =
  fun (stream: CharStream<BlockScopeParserState>) ->
    let { Depth = depth; Capture = capture } = stream.UserState
    match capture with
    | JustJumpedIn ->
      // if we've just jumped into the method ignore any space capture
      // then update the user state
      stream.UserState <- { Depth = depth; Capture = NoStatus }
      Reply(()) 
    | NoStatus
    | WeveLeftTheMethod -> (onlyNSpaces (depth * indentSize)) stream
    

let analyseIndent =
  fun (stream: CharStream<BlockScopeParserState>) ->
    let { Depth = depth } = stream.UserState

    let spaceReply = allSpaces stream
    if spaceReply.Status = Ok then
      let consumed = spaceReply.Result
      if depth < consumed then Reply(Error, expected "")
      else Reply(Error, expected "")
    else spaceReply

let upgradeToFatal (p: Parser<_,_>) =
  fun stream ->
    let reply = p stream
    if reply.Status = Error then
      Reply(FatalError, reply.Error)
    else
      reply

let pGetToTheNextCharMut : Parser<_,_> =
    fun (stream: CharStream<BlockScopeParserState>) ->
        let indentation = stream.SkipNewlineThenWhitespace(2, true)
        let state = stream.UserState
        printfn "Indent %A :: Depth %i :: x %A" indentation state.Depth (state.Depth * 2)
        match (state.Depth * 2), indentation with
        | expected, actual when expected = actual -> 
          // same line
          stream.UserState <- { state with Capture = JustJumpedIn }
          Reply(())
        | expected, actual ->
          Reply(FatalError, FParsec.Error.expected $"Indentation of %i{expected} spaces but got %i{actual}")

let pWhitespace =
  anyOf (seq { ' '; '\t'; '\f' })

let pTryLetOrOtherExpresion =
  ((attempt mainParser) <!> "attempting main parser") <|> (sepEndBy1 word pWhitespace)

let pLetBody =
  sepEndBy1
    ((onlyDepthSpacesMut <!> "only depth") >>. pTryLetOrOtherExpresion)
    (newline <!> "sep new line")
  |>> List.collect id

mainParserRef.Value <-
  (goDeepMut <!> "going deep") >>.
  (letWord <!> "attemping let") >>.
  allSpaces >>.
  word >>.
  allSpaces >>.
  assignmentSymbol >>.
  (pGetToTheNextCharMut <!> "attempting next char") >>. // if we've just declared a let scope,
  // then we should always be indented by 2...
  pLetBody .>>
  backOutMut

runParserOnString mainParser BlockScopeParserState.Default "code" document
|> printfn "%A"
