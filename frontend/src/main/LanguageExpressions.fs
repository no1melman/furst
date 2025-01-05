module LanguageExpressions

open BasicTypes

type ParameterExpression = { Name: WordToken; Type: TypeDefinitions }

type Expressions =
  | ParameterExpression

let (|TypedParameterExpressionMatch|ParameterExpressionMatch|Incorrect|) (tokens: Tokens list) =
  match tokens with
  | [ OpenParen; Name name; TypeIdentifier; TypeDefinition typeDefinition; ClosedParen ] ->
    TypedParameterExpressionMatch { Name = name; Type = typeDefinition }
  | [ Name name ] -> ParameterExpressionMatch { Name = name; Type = Inferred }
  | _ -> Incorrect

type BodyExpression = BodyExpression of Expression list
  and FunctionDefinition =
    {
      Identifier: string
      Type: TypeDefinitions 
      Parameters: ParameterExpression list
      Body: BodyExpression
    }
    static member IsFunctionDefinition (row: Row) =
      match row.Expressions with
      | [ Let; Name _; Assignment ] -> true
      | Let :: rest ->
         match rest with
         | Name _ :: possibleParameterList ->
            FunctionDefinition.IsParameterListExpression possibleParameterList
         | _ -> false
      | _ -> false
    static member IsParameterListExpression (tokens: Tokens list) =
      let isParameterExpression tokens =
        match tokens with
        | TypedParameterExpression expr -> Some expr
        | ParameterExpression expr -> Some expr
        | Incorrect -> None
      
      let rec walkList list =
        match list with
        | OpenParen :: _ ->
          let maybeExpr = isParameterExpression list.[0..4]
          maybeExpr |> Option.bind (fun expr ->
              let remaining = list.[5..]
              match remaining with
              | [] -> Some [ expr ]
              | _ -> walkList list.[5..]
            )
        | Name _ :: tail -> walkList tail
        // once we're empty we can just leave
        | [] -> true
        | _ -> false
       
      match tokens with
      | [] -> false
      | _ -> walkList tokens
      
            
          
  and Expression =
    | FunctionExpression of FunctionDefinition
    | ReturnExpression of Tokens
