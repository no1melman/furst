# Memory Allocation & Ownership

## Stack vs Heap in Furst

**C++ model you like**: no `new` = stack, `new` = heap. Simple, explicit.

For Furst with LLVM, the key mapping:
- **Stack** → LLVM `alloca` (automatic, scoped lifetime)
- **Heap** → call to `malloc`/custom allocator (manual or ownership-managed lifetime)

### Proposal: Value-first, explicit heap

```fsharp
// stack by default - just declare it
let point = { x = 1, y = 2 }        // alloca, lives on stack frame

// heap via `new` keyword - returns a pointer/ref
let point = new { x = 1, y = 2 }    // malloc, lives on heap

// same for user types
struct Point
    x: i32
    y: i32

let a = Point { x = 1, y = 2 }      // stack
let b = new Point { x = 1, y = 2 }   // heap
```

### Design questions to resolve

1. **Ownership model** - three options:
   - **Manual** (C-like): caller responsible for `free`/`delete`. Simplest to implement, most footgun-prone
   - **RAII/scope-based** (C++-like): heap allocs freed when owner goes out of scope. Need move semantics
   - **Borrow checker** (Rust-like): compile-time lifetime tracking. Most complex, safest

2. **Pointer types in the type system** - do you want:
   - Implicit: `new Point` returns `Point` but it's secretly a pointer (like C# reference types)
   - Explicit: `new Point` returns `Ptr<Point>` or `&Point` - different type, must dereference
   - I'd strongly recommend explicit - it aligns with your "no C# nuance" goal

3. **Pass-by semantics** - when calling functions:
   - Stack values: copy by default? Or pass by reference?
   - Heap values: always pass the pointer?
   - C++ does copy-by-default for values, which is consistent but can be expensive for large structs

4. **Arrays/strings** - these almost always need heap. Do you:
   - Always heap-allocate them (like most languages)
   - Allow stack arrays with fixed size (`let arr: i32[4]` → stack, `let arr = new i32[n]` → heap)

### What this means for LLVM IR

Stack:
```llvm
%point = alloca %Point
store i32 1, ptr %point            ; x field
%y_ptr = getelementptr %Point, ptr %point, i32 0, i32 1
store i32 2, ptr %y_ptr            ; y field
; automatically freed when function returns
```

Heap:
```llvm
%point = call ptr @malloc(i64 8)   ; sizeof(Point)
store i32 1, ptr %point
%y_ptr = getelementptr %Point, ptr %point, i32 0, i32 1
store i32 2, ptr %y_ptr
; must call @free(%point) at some point
```

### My recommendation for Furst

Start with:
- **Stack by default**, `new` for heap (matches your C++ preference)
- **Explicit pointer types** (`Ptr<T>`) so stack vs heap is visible in the type system
- **RAII** for heap cleanup - simpler than borrow checker, safer than manual. Can add borrow checking later
- **Copy semantics** for small value types, require explicit `&` for pass-by-reference

This gives you C++-level control without C++'s 40 years of baggage.

### Unresolved
- `new` keyword or different syntax? (`box`, `heap`, `alloc`?)
- support raw pointers for FFI/unsafe blocks?
- GC for the async green thread runtime (ADR-0003) or RAII there too?
- allow `new` in struct fields? (heap inside stack-allocated struct = common C++ pattern)
- how does this interact with your planned algebraic types (ADR-0002)? sum types on stack need to be sized

## Ptr<T> as monadic, auto-deref, and CEs

The thing is that Ptr<T> (seems like it could be monadic) could cause users confusion, if we know that it is Ptr<T> we don't need to make anything else difficult for it, like getting data, should still be accessed the same way. Or we could leverage some monadic stuff to access - because we could bring in some new things like CEs in F#...

**Auto-deref (Rust's approach)**: compiler knows `Ptr<T>` so it inserts deref automatically on field access. No `->` vs `.` distinction like C++:

```fsharp
let a = Point { x = 1, y = 2 }        // Point, stack
let b = new Point { x = 1, y = 2 }    // Ptr<Point>, heap

a.x   // direct field access
b.x   // compiler auto-derefs, same syntax
```

Type system tracks it, user doesn't care at the access site. The `Ptr<T>` only matters for:
- function signatures (so caller knows ownership implications)
- assignment/move semantics
- lifetime reasoning

**The monadic angle** is where it gets interesting for Furst's identity. `Ptr<T>` is essentially `Option`-like if you allow null pointers, or a resource monad if you think about allocation/deallocation. CEs could give you:

```fsharp
// CE for scoped heap usage - automatic cleanup at end
heap {
    let! p = Point { x = 1, y = 2 }    // heap alloc
    let! q = Point { x = p.x, y = 3 }  // heap alloc, auto-derefs p
    return p.x + q.y
}   // both freed here, RAII-style

// or for nullable pointer chains (like Option CE)
ptr {
    let! node = maybeNull
    let! child = node.left
    return child.value
}   // short-circuits on null, no explicit null checks
```

This gives you:
- **`heap { }`** - scoped allocation CE, builder manages `malloc`/`free`. Monadic bind = alloc + register for cleanup
- **`ptr { }`** - safe nullable traversal CE, like `option { }` in F# but for pointers

### What this means architecturally

The CE approach maps cleanly to LLVM because the builder desugars to known patterns:

```
heap { let! x = ... }
  ↓ desugar
Bind(alloc(sizeof T), fun x -> ... finally free(x))
  ↓ LLVM
call @malloc → use → call @free
```

You'd need in your type system:
- `Ptr<T>` as a generic type (already fits your ADR-0002 parametric polymorphism plan)
- Auto-deref rule: if `expr : Ptr<T>` and you access `.field`, unify on `T`
- CE builders as special struct types with `Bind`/`Return`/`Zero` methods

### Where I'd land

Combine both:
1. **Auto-deref everywhere** - `Ptr<T>` is transparent at access sites
2. **`heap { }` CE** for scoped RAII allocation - the idiomatic way to do heap work
3. **`new` keyword** still available outside CEs for when you want manual control
4. **`ptr { }` CE** for safe null-pointer chaining (add later)

This makes Furst distinct from both C++ (which has no monadic allocation) and Rust (which has no CE-style sugar for resource management).

### Unresolved
- can `heap { }` nest? inner scope frees before outer?
- `let!` vs `let` inside CE - or infer from type? (F# requires `let!`, adds friction)
- does `new` outside a CE require explicit `free`, or implicit RAII via scope?
- how does this interact with green threads - does a task CE compose with heap CE?

## Concrete scenario: API body parsing

If we take an idiomatic way to accept an incoming list of things from an API, C# and C++ would allocate some heap memory list, get all the bytes for the body, stream through a json parser which allocates on the heap then transfers ownership to that heap memory.

### C++ version (for reference)
```cpp
std::vector<Item> getItems(HttpRequest& req) {
    auto body = req.readBody();           // heap: string buffer
    auto items = json::parse<vector<Item>>(body);  // heap: parsed objects
    return items;                          // move semantics, no copy
}
```

### What Furst could look like

**Option A: explicit heap, auto-deref makes it clean**
```fsharp
export let getItems (req: HttpRequest) : List<Item> =
    let body = req.readBody ()          // returns Ptr<Buffer>, heap allocated
    let items = Json.parse<Item> body   // returns Ptr<List<Item>>, heap allocated
    items                               // caller owns it now, move semantics
```

Looks identical to any high-level language. `Ptr<T>` is in the types but invisible at usage. The compiler tracks ownership - `body` gets freed when `items` takes over the parsed data, `items` moves to caller.

**Option B: heap CE for when you want scoped cleanup**
```fsharp
export let processItems (req: HttpRequest) : i32 =
    heap {
        let! body = req.readBody ()
        let! items = Json.parse<Item> body
        let count = List.length items       // no let!, it's a stack i32
        return count
    }   // body and items freed here, only count escapes
```

The CE version shines when you **don't** want to return the heap data - you process it and return a value type. The builder knows everything bound with `let!` gets freed at scope end.

**Option C: streaming with composition**
```fsharp
export let processItems (req: HttpRequest) : List<Item> =
    heap {
        let! stream = req.bodyStream ()
        let! items = Json.parseStream<Item> stream  // streaming parse, low peak memory
        return! items   // return! = move out of CE, caller owns it, not freed
    }   // stream freed, items moved to caller
```

`return!` vs `return` distinction: `return!` moves the `Ptr<T>` out to caller (transfers ownership). `return` copies/extracts the value and frees everything.

### The key insight

Three patterns emerge:
1. **Pass-through** (Option A): alloc → return to caller. No CE needed, just move semantics
2. **Scoped processing** (Option B): alloc → use → free, return value type. CE handles cleanup
3. **Selective escape** (Option C): alloc multiple things → free some, move others out. `return!` signals "don't free this one"

### What LLVM sees for Option C
```llvm
define ptr @processItems(ptr %req) {
    %stream = call ptr @bodyStream(ptr %req)      ; malloc inside
    %items = call ptr @jsonParseStream(ptr %stream) ; malloc inside
    call void @free(ptr %stream)                    ; CE cleanup
    ; %items NOT freed - moved to caller via return
    ret ptr %items
}
```

### Unresolved
- `return` vs `return!` clear enough? or different keyword for "move out"?
- should `List<T>` always be heap? or support stack-allocated fixed-size arrays separately?
- streaming/lazy iteration - separate concern or part of heap CE?
- does the JSON parser example imply Furst needs a reflection/serialization story?

## Immutability by default, mutable optimizations under the hood

F# really suffers because it needs CLIMutable which breaks the story. Want immutability by default, with mutability optimizations under the hood for memory management.

### Reuse analysis

```fsharp
struct Item
    name: String
    count: i32

// user sees immutable transformations
let updated = { item with count = item.count + 1 }
```

Under the hood, compiler decides:

```
// if `item` is never used after this line → MUTATE IN PLACE
// if `item` is used later → COPY THEN MUTATE
```

This is **reuse analysis** (Koka and Lean4 do this). LLVM IR for the reuse case:

```llvm
; item has refcount 1 and is dead after this → reuse
%count_ptr = getelementptr %Item, ptr %item, i32 0, i32 1
store i32 %new_count, ptr %count_ptr    ; mutate in place, no alloc
```

vs the copy case:

```llvm
; item still live → copy
%new = call ptr @malloc(i64 12)
call void @llvm.memcpy(ptr %new, ptr %item, i64 12)
%count_ptr = getelementptr %Item, ptr %new, i32 0, i32 1
store i32 %new_count, ptr %count_ptr
```

### Serialization without CLIMutable

The problem: C#/F# deserializers want to call `obj.Name = value` per field. That forces mutability into your type definition. Gross.

Furst approach: **compile-time constructor-based deserialization**. No reflection, no mutation.

```fsharp
// attribute or derive-style marker
[<Serialize>]
struct Item
    name: String
    count: i32
```

Compiler generates at compile time (not runtime reflection):

```fsharp
// generated - never seen by user
let __deserialize_Item (reader: JsonReader) : Item =
    let name = reader.readField<String> "name"
    let count = reader.readField<i32> "count"
    Item { name = name, count = count }   // single constructor call, immutable

let __serialize_Item (writer: JsonWriter) (item: Item) : unit =
    writer.writeField "name" item.name
    writer.writeField "count" item.count
```

No mutation. Construct the whole thing at once via the constructor. Serializer writes fields by reading them (immutable access is fine).

### How this connects to stack/heap

The serialization story composes with your allocation model:

```fsharp
// stack - small response, known size
let item = Json.parse<Item> body          // constructed on stack

// heap - list of unknown size
let items = Json.parseList<Item> body     // Ptr<List<Item>>, heap

// heap CE - scoped processing
heap {
    let! items = Json.parseList<Item> body
    let total = List.sumBy (fun i -> i.count) items   // stack i32
    return total
}   // items freed, total returned as value
```

### The reuse optimization in practice

```fsharp
// processing a list of API items
let transform (items: List<Item>) : List<Item> =
    List.map (fun i -> { i with count = i.count * 2 }) items
```

If `items` is uniquely owned (refcount 1, or moved in), the compiler can **mutate the list nodes and structs in place**. Zero allocations for what looks like a pure functional transformation.

This is where Furst gets genuinely different: **looks like F#, performs like C++**.

### Unresolved
- `[<Serialize>]` attribute or keyword like `derive` (Rust) or just automatic for all structs?
- reference counting for reuse analysis or ownership-based (move semantics only)?
- custom serialization formats - trait/typeclass for `Serializable<T>`?
- nested immutable types - does reuse analysis work recursively? (Koka does, it's complex)

## Comptime reflection

Don't always know what the structure will look like. Don't want compiler-generated deserializers. But if you're looping through a reader calling next token and reading it, you can match on it and set the appropriate property. Maybe we could have some reflection which helps the compiler under the hood write out code you don't want to.

What if reflection is compile-time only, like Zig's `comptime`?

```fsharp
// compiler knows all fields of Item at compile time
// this is a compile-time loop, not runtime reflection
let parseItem (reader: JsonReader) : Item =
    partial item: Item

    while reader.next ()
        let field = reader.fieldName ()
        // comptime: unrolls to match on each field
        comptime for f in fields<Item>
            if field == f.name
                item.[f] = reader.read<f.type> ()

    item
```

Compiler unrolls that to:

```fsharp
// what actually gets emitted
while reader.next ()
    let field = reader.fieldName ()
    if field == "name"
        item.name = reader.readString ()
    elif field == "count"
        item.count = reader.readInt ()
```

Zero runtime cost. No reflection metadata in the binary. But when you need custom logic you just... don't use `comptime` and write the match yourself.

### Mix both - custom overrides with comptime fallback

```fsharp
let parseItem (reader: JsonReader) : Item =
    partial item: Item

    while reader.next ()
        match reader.fieldName ()
            // custom handling for weird API field name
            "item_count" -> item.count = reader.readInt ()
            // everything else: comptime handles it
            name -> comptime for f in fields<Item>
                if name == f.name
                    item.[f] = reader.read<f.type> ()

    item
```

### What LLVM sees

`partial` is just an `alloca` with stores. The freeze is the compiler stopping emitting stores:

```llvm
%item = alloca %Item          ; partial item: Item
; ... loop with stores ...
store ptr %name_str, ptr %item           ; item.name = ...
%count_ptr = getelementptr %Item, ptr %item, i32 0, i32 1
store i32 %count_val, ptr %count_ptr     ; item.count = ...
; freeze point: return
ret ptr %item   ; or memcpy to caller's stack
```

`comptime for` becomes nothing - it's fully expanded before LLVM IR emission. The lowering pass unrolls it using the struct definition it already has.

### How this fits the stack/heap story

```fsharp
partial item: Item              // stack, mutable until frozen
partial item: new Item          // heap, mutable until frozen, returns Ptr<Item>
```

Same rules. `new` just means the `alloca` becomes `malloc`. Auto-deref means access syntax is identical.

### Unresolved
- freeze point detection: return only? or any use as argument?
- what if a field is never set on some code path? (branch where match misses a field) - compile error or runtime error?
- `comptime` scope: just `for` over fields, or more general compile-time execution?
- `item.[f]` syntax for comptime field access - or something else?

## Partial object construction (pinned)

`partial` declares a mutable-until-frozen binding. No scoped block — just a declaration then set fields:

```fsharp
let parseItem (reader: JsonReader) : Item =
    partial item: Item

    while reader.next ()
        match reader.fieldName ()
            "name" -> item.name = reader.readString ()
            "count" -> item.count = reader.readInt ()
            _ -> reader.skip ()

    item    // freeze point — compiler checks all fields set
```

- `partial item: new Item` for heap-allocated partial
- Freezes on first use as immutable (return, pass as arg)
- Compile-time completeness check at freeze point

**Status: pinned for later.**

### Unresolved (when revisited)
- freeze point detection: return only or any use as argument?
- unset field on some code path: compile error or runtime error?
- `comptime for f in fields<T>` for compile-time field iteration
- `item.[f]` syntax for comptime field access
