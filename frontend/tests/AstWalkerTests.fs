module Furst.Tests.AstWalkerTests

open BasicTypes

open LanguageExpressions
open Xunit

type TokenStream(tokens: Tokens list) =
    let mutable currentIndex = 0
    
    member this.Read (count: int) =
        let oldIndex = currentIndex
        currentIndex <- currentIndex + count
        tokens.[oldIndex..count]
        
    // This doesn't advance the stream
    member this.Peek (count: int) =
        tokens.[currentIndex..count]
        
    member this.ReadTo ( token : Tokens) =
        let rec loopThrough rest count =
            match rest with
            | head :: tail ->
                if head = token then
                    Some count
                else
                    loopThrough tail (count + 1)
            | [ ] -> None
        
        loopThrough tokens 1
                    
            
    
type AstParser<'Result> = TokenStream -> Result<'Result, string>

let (|TypedParameterExpressionMatch|ParameterExpressionMatch|Incorrect|) (tokens: Tokens list) =
  match tokens with
  | [ OpenParen; Name name; TypeIdentifier; TypeDefinition typeDefinition; ClosedParen ] ->
    let a = { Name = name; Type = typeDefinition }
    TypedParameterExpressionMatch a
  | [ Name name ] -> ParameterExpressionMatch { Name = name; Type = Inferred }
  | _ -> Incorrect
  
let astParameterExpression =
    fun (tokenStream: TokenStream) ->
        let firstToken = tokenStream.Peek(1)
        let maybeTokens =
            match firstToken with
            | [ OpenParen ] ->
                let maybeCount = tokenStream.ReadTo(ClosedParen)
                maybeCount |> Option.map (fun count ->
                    tokenStream.Read(count) )
            | [ Name name ] ->
                Some (tokenStream.Read(1))
            | [] -> None
            | _ -> None
        
        maybeTokens
        |> Option.bind (function
           | TypedParameterExpressionMatch expr -> Some expr
           | ParameterExpressionMatch expr -> Some expr
           | Incorrect -> None )
        |> function
           | Some expr -> Ok expr
           | None -> Error "Incorrect"
           
let astFunctionDefinition =
    fun (tokenStream: TokenStream) ->
        let firstToken = tokenStream.Peek(1)
        let maybeTokens =
            match firstToken with
            | [ Let; Name name; Assignment ] ->
                let maybeCount = tokenStream.ReadTo(SemiColonTerminator)
                maybeCount |> Option.map tokenStream.Read
            | _ -> None
        
        maybeTokens
        |> Option.bind (function
           | [ Let; Name name; Assignment ] -> Some name
           | _ -> None )
        |> function
           | Some name -> Ok name
           | None -> Error "Incorrect"

[<Fact>]
let ``Given a list of tokens it can reduce to a set of tokens`` () =
    let list = [ Let; Name (Word "myFunction"); OpenParen; Name (Word "paramA"); TypeIdentifier; (TypeDefinition I32); ClosedParen; Name (Word "paramB"); Assignment ]
    let stream = TokenStream(list)
    let result = astParameterExpression stream
    
    match result with
    | Ok expr ->
        let (Word name) = expr.Name
        Assert.Equal("myFunction", name)
        Assert.Equal(I32, expr.Type)
    | _ -> failwith "fucked"
    
    
    
    