#r "nuget:FParsec"
#load "./main/BasicTypes.fs" "./main/CommonParsers.fs" 

open FParsec
open CommonParsers
open System


type Expression =
  | ReturnExpr of string
  | LetExpr of string * Expression list

let upgradeToFatal (p: Parser<_,_>) =
  fun stream ->
    let reply = p stream
    if reply.Status = Error then
      Reply(FatalError, reply.Error)
    else
      reply

let (<!>) (p: Parser<_,_>) label : Parser<_,_> =
    fun stream ->
        printfn "%A: Entering %s" stream.Position label
        let reply = p stream
        printfn "%A: Leaving %s (%A)" stream.Position label reply.Status
        reply

let (<!!!>) (p: Parser<_,_>) label : Parser<_,_> =
    fun stream ->
        printfn "%A: Entering %s" stream.Position label
        printfn "%A: State Before\r   %A" stream.Position stream.UserState
        let reply = p stream
        printfn "%A: State After\r   %A" stream.Position stream.UserState
        printfn "%A: Leaving %s (%A)" stream.Position label reply.Status
        reply

let indentSize = 2

let mainParser, mainParserRef = createParserForwardedToRef<Expression, BlockScopeParserState>()

let goDeepMut =
  updateUserState (fun bsp -> { bsp with Depth = bsp.Depth + 1 })

let backOutMut =
  updateUserState (fun bsp -> { bsp with Depth = bsp.Depth - 1 })

let depthFromState (pfn: int -> Parser<_,BlockScopeParserState>) =
  fun (stream: CharStream<BlockScopeParserState>) ->
    let { Depth = depth } = stream.UserState
    (pfn depth) stream

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

let pGetToTheNextCharMut : Parser<_,_> =
    fun (stream: CharStream<BlockScopeParserState>) ->
        let indentation = stream.SkipNewlineThenWhitespace(2, true)
        let state = stream.UserState
        printfn "Indent %A :: Depth %i :: %A spaces" indentation state.Depth (state.Depth * 2)
        match (state.Depth * 2), indentation with
        | expected, actual when expected = actual -> 
          // same line
          stream.UserState <- { state with Capture = JustJumpedIn }
          Reply(())
        | expected, actual ->
          Reply(FatalError, FParsec.Error.expected $"Indentation of %i{expected} spaces but got %i{actual}")

let pWhitespace =
  skipAnyOf (seq { ' '; '\t'; '\f' })
let pManyWhitespace1 =
  skipMany1 pWhitespace
let pManyWhitespace =
  skipMany pWhitespace

let pLetDeclaration =
  letWord >>.
  pManyWhitespace1 >>.
  word .>>
  pManyWhitespace1 .>>
  assignmentSymbol .>>
  pManyWhitespace
  |>> fun w -> LetExpr (w, [])


let pExpr =
  sepEndBy1 word pWhitespace
  |>> function
      | [ w ] -> ReturnExpr w
      | head :: tail ->
        if tail.Length > 0 then invalidOp "Can't handle that"
        else ReturnExpr head
      | _ -> invalidOp "Can't handle that"
  <!> "pexpr"
let pTryLetOrOtherExpression : Parser<Expression, BlockScopeParserState> =
  pBranch
    (letWord >>. pManyWhitespace1) // check it's a let declaration
    mainParser // do the main parsing and fully break if it doesn't work out
    pExpr // otherwise just understand the expression

let pLetBody =
  sepEndBy
    ((onlyDepthSpacesMut) >>. pTryLetOrOtherExpression)
    newline
  .>>. opt ((onlyDepthSpacesMut >>. pExpr) <!> "final expr" <?> "Let functions should end with expression")
  |>> fun (a,b) ->
        match b with
        | Some e ->
          let finalList = a @ [e]
          match finalList |> List.last with
          | ReturnExpr _ -> finalList
          | _ -> invalidOp "Need to end on a expression"
        | _ -> a

mainParserRef.Value <-
  //depthFromState (fun depth -> onlyNSpaces (depth * indentSize)) >>. // try to eat any indentation
  goDeepMut >>.
  pLetDeclaration .>>.
  (pGetToTheNextCharMut >>. // if we've just declared a let scope,
  // then we should always be indented by 2...
  pLetBody) 
  |>> (fun (l, b) ->
        match l with
        | LetExpr (name, expressions) -> LetExpr (name, b @ expressions)
        | _ -> invalidOp "needs to be let expr"
     ) .>>
  backOutMut

let document = """let a =
  b
  let c = d
"""

runParserOnString mainParser BlockScopeParserState.Default "code" document
|> printfn "%A"
