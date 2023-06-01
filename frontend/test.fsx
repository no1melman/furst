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

let goDeep =
  updateUserState (fun bsp -> { bsp with Depth = bsp.Depth + 1 })

let backOut =
  updateUserState (fun bsp -> { bsp with Depth = bsp.Depth - 1 })

let depthFromState (pfn: int -> Parser<_,BlockScopeParserState>) =
  (fun (stream: CharStream<BlockScopeParserState>) ->
    let { Depth = depth } = stream.UserState
    (pfn depth) stream
    )

let onlyDepthSpaces =
  depthFromState (fun depth -> onlyNSpaces (depth * indentSize))

let upgradeToFatal (p: Parser<_,_>) =
  fun stream ->
    let reply = p stream
    if reply.Status = Error then
      Reply(FatalError, reply.Error)
    else
      reply

mainParserRef.Value <-
  goDeep >>.
  letWord >>.
  allSpaces >>.
  word >>.
  allSpaces >>.
  assignmentSymbol >>.
  newline >>.
  (sepEndBy1 (onlyDepthSpaces >>. (attempt (word |>> fun a -> [a]) <!> "attempting word" <|> mainParser)) ((upgradeToFatal newline) <!> "sep new line")
    |>> List.collect id) .>>
  backOut

runParserOnString (mainParser) BlockScopeParserState.Default "code" document
|> printfn "%A"
