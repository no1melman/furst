I'm an F# expert. Given the code below

```
type Item =
    | Indent of int
    | Let
    | Word of string
    | Assignment
    | Multiply

type ExpressionList = Item list

type Row =
    { Indent: int
      Expressions: ExpressionList
      Body: Row list }

let items =
    [
     { Indent = 2
       Expressions = [Word "i"; Multiply; Word "j"]
       Body = [] }
     { Indent = 0
       Expressions = [Let; Word "h"; Assignment]
       Body = [] }
     { Indent = 0
       Expressions = [Let; Word "f"; Assignment; Word "g"]
       Body = [] }
     { Indent = 2
       Expressions = [Word "e"]
       Body = [] }
     { Indent = 4
       Expressions = [Word "d"]
       Body = [] };
     { Indent = 2
       Expressions = [Let; Word "c"; Assignment]
       Body = [] }
     { Indent = 2
       Expressions = [Word "b"]
       Body = [] }
     { Indent = 0
       Expressions = [Let; Word "a"; Assignment]
       Body = [] }]

let output =
    [ { Indent = 0
        Expressions = [ Let; Word "a"; Assignment ]
        Body =
          [ { Indent = 2
              Expressions = [ Word "b" ]
              Body = [] }
            { Indent = 2
              Expressions = [ Let; Word "c"; Assignment ]
              Body =
                  [ { Indent = 4
                      Expressions = [ Word "d" ]
                      Body = [] } ] }
            { Indent = 2
              Expressions = [ Word "e" ]
              Body = [] }
          ] }
      { Indent = 0
        Expressions = [Let; Word "f"; Assignment; Word "g"]
        Body = [] }
      { Indent = 0
        Expressions = [Let; Word "h"; Assignment]
        Body = [
            { Indent = 2
              Expressions = [Word "i"; Multiply; Word "j"]
              Body = [] }] }
    ]

```

Write an f# function that takes items and produces the output by nesting using the row indent value using a stack.
The resulting array should only have indents of 0, all other rows should have been nested into the body of that row. If rows have identical indents then they should be nested in each other.

let nestItems (items: Row list) : Row list =
    let stack = System.Collections.Generic.Stack<Row>()
    let mutable currentIndent = -1
    let mutable currentRow = { Indent = -1; Expressions = []; Body = [] }
    let mutable nestedRows = []
    
    let rec processItem (item: Row) =
        match item.Indent with
        | indent when indent > currentIndent ->
            stack.Push(currentRow)
            currentRow <- { Indent = item.Indent; Expressions = item.Expressions; Body = [] }
            currentIndent <- item.Indent
        | indent when indent <= currentIndent ->
            let rec popStackUntilIndent (targetIndent: int) =
                if currentIndent > targetIndent then
                    let parentRow = stack.Pop()
                    nestedRows <- [parentRow] @ nestedRows
                    currentRow <- parentRow
                    currentIndent <- parentRow.Indent
                    popStackUntilIndent targetIndent
            
            popStackUntilIndent item.Indent
            currentRow <- { currentRow with Body = nestedRows }
            nestedRows <- []
            currentRow <- { Indent = item.Indent; Expressions = item.Expressions; Body = [] }
        
        nestedRows <- nestedRows @ [currentRow]
    
    items |> List.iter processItem
    
    currentRow <- { currentRow with Body = nestedRows }
    [currentRow]

// Usage:
let nestedItems = nestItems items

printfn "%A" nestedItems
