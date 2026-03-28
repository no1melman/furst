module ExpressionParser

open Types
open Ast
open TokenCombinators

/// Atom: number literal, identifier, qualified name, or parenthesized expression
let rec pAtom : TParser<Expression> =
    fun tokens state ->
        match tokens with
        | t :: rest ->
            match t.Token with
            | NumberLiteral (IntValue i) -> POk (LiteralExpression (IntLiteral i), rest, state)
            | NumberLiteral (FloatValue f) -> POk (LiteralExpression (FloatLiteral f), rest, state)
            | Name (Word n) -> POk (IdentifierExpression n, rest, state)
            | QualifiedName parts -> POk (IdentifierExpression (System.String.Join(".", parts)), rest, state)
            | Parameter p -> POk (IdentifierExpression p, rest, state)
            | OpenParen ->
                // parenthesized expression: consume tokens until matching ClosedParen
                let rec findClose depth acc remaining =
                    match remaining with
                    | [] -> None
                    | t :: rest ->
                        match t.Token with
                        | ClosedParen when depth = 0 -> Some (List.rev acc, rest)
                        | ClosedParen -> findClose (depth - 1) (t :: acc) rest
                        | OpenParen -> findClose (depth + 1) (t :: acc) rest
                        | _ -> findClose depth (t :: acc) rest
                match findClose 0 [] rest with
                | Some (inner, afterClose) ->
                    match pExpr inner state with
                    | POk (expr, _, state') -> POk (expr, afterClose, state')
                    | PError e -> PError e
                | None ->
                    PError { Message = "Unmatched parenthesis"; Line = t.Line; Column = t.Column; Length = t.Length }
            | _ -> PError { Message = $"Expected expression, got {t.Token}"; Line = t.Line; Column = t.Column; Length = t.Length }
        | [] -> PError (CompileError.Empty "Expected expression, got end of input")

/// Function application: atom followed by more atoms (tightest binding)
and pApp : TParser<Expression> =
    fun tokens state ->
        match pAtom tokens state with
        | PError e -> PError e
        | POk (first, rest, state') ->
            match first with
            | IdentifierExpression funcName ->
                // try to parse arguments greedily
                let rec collectArgs acc toks st =
                    match toks with
                    | [] -> (List.rev acc, toks, st)
                    | t :: _ ->
                        match t.Token with
                        | Tokens.Addition | Tokens.Subtraction | Tokens.Multiply | ClosedParen -> (List.rev acc, toks, st)
                        | _ ->
                            match pAtom toks st with
                            | POk (arg, rest', st') -> collectArgs (arg :: acc) rest' st'
                            | PError _ -> (List.rev acc, toks, st)
                match collectArgs [] rest state' with
                | [], toks, st -> POk (first, toks, st)
                | args, toks, st ->
                    POk (FunctionCallExpression { FunctionName = funcName; Arguments = args }, toks, st)
            | _ -> POk (first, rest, state')

/// Unary negation: folds into literal when possible, NegateExpression otherwise
and pUnary : TParser<Expression> =
    fun tokens state ->
        match tokens with
        | t :: rest when t.Token = Subtraction ->
            match pUnary rest state with
            | PError e -> PError e
            | POk (inner, rest', state') ->
                match inner with
                | LiteralExpression (IntLiteral i) -> POk (LiteralExpression (IntLiteral -i), rest', state')
                | LiteralExpression (FloatLiteral f) -> POk (LiteralExpression (FloatLiteral -f), rest', state')
                | _ -> POk (NegateExpression inner, rest', state')
        | _ -> pApp tokens state

/// Multiplication layer
and pMul : TParser<Expression> =
    let opMul : TParser<Expression -> Expression -> Expression> =
        fun tokens state ->
            match tokens with
            | t :: rest when t.Token = Tokens.Multiply ->
                POk ((fun l r -> OperatorExpression { Left = l; Operator = Operator.Multiply; Right = r }), rest, state)
            | _ -> PError (CompileError.Empty "not *")
    chainl1 pUnary opMul

/// Addition/subtraction layer
and pAdd : TParser<Expression> =
    let opAdd : TParser<Expression -> Expression -> Expression> =
        fun tokens state ->
            match tokens with
            | t :: rest ->
                match t.Token with
                | Addition ->
                    POk ((fun l r -> OperatorExpression { Left = l; Operator = Operator.Add; Right = r }), rest, state)
                | Subtraction ->
                    POk ((fun l r -> OperatorExpression { Left = l; Operator = Operator.Subtract; Right = r }), rest, state)
                | _ -> PError (CompileError.Empty "not +/-")
            | _ -> PError (CompileError.Empty "not +/-")
    chainl1 pMul opAdd

/// Top-level expression parser
and pExpr : TParser<Expression> = pAdd
