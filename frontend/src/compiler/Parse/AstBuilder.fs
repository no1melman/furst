module AstBuilder

open Types
open Ast

// Helper to extract token list from Row
let tokensFromRow (row: Row) : Tokens list =
    row.Expressions |> List.map _.Token

// Extract identifier name from Name token
let extractName (tokens: Tokens list) : string option =
    tokens
    |> List.tryPick (function Name (Word n) -> Some n | _ -> None)

// Helper to get first token position for errors
let getFirstTokenPos (row: Row) : Line * Column * TokenLength =
    match row.Expressions |> List.tryHead with
    | Some t -> (t.Line, t.Column, t.Length)
    | None -> (Line 0L, Column 0L, TokenLength 0)

// Create error from token
let tokenError (message: string) (token: TokenWithMetadata) : CompileError =
    {
        Message = message
        Line = token.Line
        Column = token.Column
        Length = token.Length
    }

// Calculate SourceLocation from a single token
let tokenLocation (token: TokenWithMetadata) : SourceLocation =
    let (Line startLine) = token.Line
    let (Column startCol) = token.Column
    let (TokenLength len) = token.Length
    {
        StartLine = token.Line
        StartCol = token.Column
        EndLine = token.Line  // assume single-line token
        EndCol = Column (startCol + int64 len)
    }

// Calculate SourceLocation spanning a list of tokens
let tokensLocation (tokens: TokenWithMetadata list) : SourceLocation =
    match tokens with
    | [] ->
        { StartLine = Line 0L; StartCol = Column 0L; EndLine = Line 0L; EndCol = Column 0L }
    | tokens ->
        let first = tokens.Head
        let last = tokens |> List.last
        let (Column lastCol) = last.Column
        let (TokenLength lastLen) = last.Length
        {
            StartLine = first.Line
            StartCol = first.Column
            EndLine = last.Line
            EndCol = Column (lastCol + int64 lastLen)
        }

// Calculate SourceLocation for an entire row (including body)
let rec rowLocation (row: Row) : SourceLocation =
    match row.Body with
    | [] ->
        // No body, just use tokens
        tokensLocation row.Expressions
    | bodyRows ->
        // Has body, span from first token to end of last body row
        let firstToken = row.Expressions |> List.tryHead
        let lastBodyRow = bodyRows |> List.last
        let endLoc = rowLocation lastBodyRow
        match firstToken with
        | Some first ->
            {
                StartLine = first.Line
                StartCol = first.Column
                EndLine = endLoc.EndLine
                EndCol = endLoc.EndCol
            }
        | None -> endLoc

// Split a flat token list into argument groups:
// single tokens become one group, (x + y) becomes one group (without parens)
let splitArgumentGroups (tokens: TokenWithMetadata list) : TokenWithMetadata list list =
    let rec loop acc currentGroup depth remaining =
        match remaining with
        | [] ->
            match currentGroup with
            | [] -> acc |> List.rev
            | g -> (g |> List.rev) :: acc |> List.rev
        | t :: rest ->
            match t.Token, depth with
            | OpenParen, 0 ->
                // start a paren group; flush any pending single token
                let acc' =
                    match currentGroup with
                    | [] -> acc
                    | g -> (g |> List.rev) :: acc
                loop acc' [] 1 rest
            | ClosedParen, 1 ->
                // close paren group — the inner tokens are one argument group
                loop ((currentGroup |> List.rev) :: acc) [] 0 rest
            | OpenParen, d ->
                loop acc (t :: currentGroup) (d + 1) rest
            | ClosedParen, d ->
                loop acc (t :: currentGroup) (d - 1) rest
            | _, 0 ->
                // top-level token — each is its own group
                loop ([ t ] :: acc) [] 0 rest
            | _ ->
                // inside parens — accumulate
                loop acc (t :: currentGroup) depth rest
    loop [] [] 0 tokens

// Main expression builder - converts Row to ExpressionNode
let rec rowToExpression (row: Row) : Result<ExpressionNode, CompileError> =
    match tokensFromRow row with
    // mod Foo or mod Foo.Bar (via qualified name)
    | Mod :: Name (Word name) :: _ ->
        let loc = rowLocation row
        Result.Ok { Expr = ModuleDeclaration [name]; Location = loc }
    | Mod :: QualifiedName parts :: _ ->
        let loc = rowLocation row
        Result.Ok { Expr = ModuleDeclaration parts; Location = loc }
    // open Foo or open Foo.Bar
    | Open :: Name (Word name) :: _ ->
        let loc = rowLocation row
        Result.Ok { Expr = OpenDeclaration [name]; Location = loc }
    | Open :: QualifiedName parts :: _ ->
        let loc = rowLocation row
        Result.Ok { Expr = OpenDeclaration parts; Location = loc }
    // private let x = value (no body = private variable binding)
    | Private :: Let :: Name (Word name) :: Assignment :: valueToks when row.Body.IsEmpty ->
        buildLetBinding name valueToks row
    // private let f params = body (private function)
    | Private :: Let :: Name (Word _) :: _ when isFunctionDefinition { row with Expressions = row.Expressions |> List.tail } ->
        buildFunctionDefinition row
    // let x = value (no body = variable binding)
    | Let :: Name (Word name) :: Assignment :: valueToks when row.Body.IsEmpty ->
        buildLetBinding name valueToks row
    // let f params = body (has body = function)
    | Let :: Name (Word _) :: _ when isFunctionDefinition row ->
        buildFunctionDefinition row
    // struct X { ... }
    | Struct :: Name (Word name) :: _ ->
        buildStructDefinition name row
    // expression (identifier, literal, call, binop)
    | _ ->
        parseExpression row.Expressions

and buildLetBinding (name: string) (valueToks: Tokens list) (row: Row) : Result<ExpressionNode, CompileError> =
    if valueToks.IsEmpty then
        // Find Assignment token for position
        let assignTok = row.Expressions |> List.find (fun t -> t.Token = Assignment)
        Result.Error (tokenError $"Expected value after '=' in let binding for '{name}'" assignTok)
    else
        // Parse value tokens into expression
        let valueTokensMeta = row.Expressions |> List.skipWhile (fun t -> t.Token <> Assignment) |> List.tail
        match parseExpression valueTokensMeta with
        | Ok valueExprNode ->
            let loc = rowLocation row
            Result.Ok {
                Expr = LetBindingExpression {
                    Name = name
                    Type = Inferred
                    Value = valueExprNode.Expr
                }
                Location = loc
            }
        | Error e -> Result.Error e

and parseExpression (tokens: TokenWithMetadata list) : Result<ExpressionNode, CompileError> =
    // Fold Subtraction + NumberLiteral into a negative literal when the minus
    // appears at the start of the expression or immediately after an operator
    let isOperator token =
        match token with
        | Tokens.Addition | Tokens.Subtraction | Tokens.Multiply -> true
        | _ -> false

    let rec foldNegativeLiterals acc remaining =
        match remaining with
        | [] -> List.rev acc
        | minus :: number :: rest
            when minus.Token = Subtraction
            && (match acc with [] -> true | prev :: _ -> isOperator prev.Token)  ->
            match number.Token with
            | NumberLiteral (IntValue i) ->
                let negated = { number with Token = NumberLiteral (IntValue -i) }
                foldNegativeLiterals (negated :: acc) rest
            | NumberLiteral (FloatValue f) ->
                let negated = { number with Token = NumberLiteral (FloatValue -f) }
                foldNegativeLiterals (negated :: acc) rest
            | _ ->
                foldNegativeLiterals (minus :: acc) (number :: rest)
        | token :: rest ->
            foldNegativeLiterals (token :: acc) rest

    let tokens = foldNegativeLiterals [] tokens
    let loc = tokensLocation tokens

    // Convert token to operand Expression
    let tryParseOperand = function
        | Name (Word n) -> Some (IdentifierExpression n)
        | QualifiedName parts -> Some (IdentifierExpression (System.String.Join(".", parts)))
        | Parameter p -> Some (IdentifierExpression p)
        | NumberLiteral (IntValue i)   -> Some (LiteralExpression (IntLiteral i))
        | NumberLiteral (FloatValue f) -> Some (LiteralExpression (FloatLiteral f))
        | _ -> None

    // Convert token to operator
    let tryParseOperator = function
        | Tokens.Addition -> Some Operator.Add
        | Tokens.Subtraction -> Some Operator.Subtract
        | Tokens.Multiply -> Some Operator.Multiply
        | _ -> None

    let tokenList = tokens |> List.map _.Token

    match tokenList with
    // Single operand
    | [ singleToken ] ->
        match tryParseOperand singleToken with
        | Some expr -> Result.Ok { Expr = expr; Location = loc }
        | None ->
            Result.Error (tokenError $"Invalid expression: {singleToken}" tokens.Head)

    // Binary operations (handles chains: a + b + c)
    // Only match top-level operators (not inside parens)
    | firstToken :: rest when
        rest |> List.fold (fun (depth, found) t ->
            match t, depth with
            | OpenParen, _ -> (depth + 1, found)
            | ClosedParen, _ -> (depth - 1, found)
            | op, 0 when tryParseOperator op |> Option.isSome -> (depth, true)
            | _ -> (depth, found)) (0, false) |> snd ->
        // Parse first operand
        match tryParseOperand firstToken with
        | None ->
            Result.Error (tokenError "Expression must start with valid operand" tokens.Head)
        | Some firstExpr ->
            // Fold over (operator, operand) pairs
            // Left-associative: a + b + c = (a + b) + c
            let rec foldOps acc remaining =
                match remaining with
                | opToken :: operandToken :: tail ->
                    match tryParseOperator opToken, tryParseOperand operandToken with
                    | Some op, Some operand ->
                        let newExpr = OperatorExpression {
                            Left = acc
                            Operator = op
                            Right = operand
                        }
                        foldOps newExpr tail
                    | _ ->
                        Result.Error (tokenError "Invalid operator or operand in expression" tokens.Head)
                | [] -> Result.Ok acc
                | _ ->
                    Result.Error (tokenError "Incomplete binary operation" tokens.Head)

            match foldOps firstExpr rest with
            | Ok expr -> Result.Ok { Expr = expr; Location = loc }
            | Error e -> Result.Error e

    // Function call: f a b, f a (2 + 3), etc.
    | funcToken :: _ when tryParseOperand funcToken |> Option.isSome ->
        match tryParseOperand funcToken with
        | Some (IdentifierExpression funcName) ->
            let argTokens = tokens |> List.tail
            let argGroups = splitArgumentGroups argTokens
            let argResults = argGroups |> List.map parseExpression
            let errors = argResults |> List.choose (function Result.Error e -> Some e | _ -> None)
            match errors with
            | e :: _ -> Result.Error e
            | [] ->
                let args = argResults |> List.choose (function Result.Ok n -> Some n.Expr | _ -> None)
                Result.Ok {
                    Expr = FunctionCallExpression {
                        FunctionName = funcName
                        Arguments = args
                    }
                    Location = loc
                }
        | _ ->
            Result.Error (tokenError "Function call requires identifier" tokens.Head)

    | [] ->
        Result.Error (CompileError.Empty "Empty expression")

    | _ ->
        Result.Error (tokenError "Unrecognized expression" tokens.Head)

and buildFunctionDefinition (row: Row) : Result<ExpressionNode, CompileError> =
    let tokens = tokensFromRow row
    // strip Private if present
    let visibility, tokens =
        match tokens with
        | Tokens.Private :: rest -> Visibility.Private, rest
        | _ -> Visibility.Public, tokens
    match tokens with
    | Let :: Name (Word funcName) :: rest ->
        let paramsAndAssignment = rest |> List.takeWhile ((<>) Assignment)
        let parameters = extractParameters paramsAndAssignment

        let bodyResults = row.Body |> List.map rowToExpression

        let errors = bodyResults |> List.choose (function Error e -> Some e | _ -> None)
        if not errors.IsEmpty then
            Result.Error errors.Head
        else
            let bodyExprNodes = bodyResults |> List.choose (function Ok e -> Some e | _ -> None)
            let bodyExprs = bodyExprNodes |> List.map _.Expr
            let loc = rowLocation row
            let details = {
                Identifier = funcName
                Type = Inferred
                Parameters = parameters
                Body = BodyExpression bodyExprs
                Visibility = visibility
            }
            Result.Ok {
                Expr = FunctionDefinitionExpression (FunctionDefinition details)
                Location = loc
            }
    | _ ->
        let line, col, len = getFirstTokenPos row
        Result.Error {
            Message = "Invalid function definition"
            Line = line
            Column = col
            Length = len
        }

and extractParameters (tokens: Tokens list) : ParameterExpression list =
    let rec loop acc remaining =
        match remaining with
        | [] -> acc |> List.rev
        | OpenParen :: Parameter paramName :: TypeIdentifier :: TypeDefinition typeDef :: ClosedParen :: rest ->
            loop ({ Name = Word paramName; Type = typeDef } :: acc) rest
        | OpenParen :: Name (Word paramName) :: TypeIdentifier :: TypeDefinition typeDef :: ClosedParen :: rest ->
            loop ({ Name = Word paramName; Type = typeDef } :: acc) rest
        | Name (Word paramName) :: rest ->
            loop ({ Name = Word paramName; Type = Inferred } :: acc) rest
        | Parameter paramName :: rest ->
            loop ({ Name = Word paramName; Type = Inferred } :: acc) rest
        | _ :: rest -> loop acc rest
    loop [] tokens

and buildStructDefinition (name: string) (row: Row) : Result<ExpressionNode, CompileError> =
    // TODO: parse fields from row.Body
    let line, col, len = getFirstTokenPos row
    Result.Error {
        Message = "Struct definitions not yet implemented"
        Line = line
        Column = col
        Length = len
    }
