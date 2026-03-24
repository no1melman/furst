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

## What the Rust borrow checker solves

The borrow checker exists to prevent three categories of memory bugs **at compile time**, with zero runtime cost:

### 1. Use-after-free

```cpp
// C++
Item* item = new Item("foo", 5);
delete item;
std::cout << item->name;  // undefined behaviour — reads freed memory
```

The borrow checker prevents this by enforcing that **no reference can outlive the thing it points to**. Every reference has a lifetime, and the compiler proves that the referent is alive for the entire duration the reference exists.

### 2. Double-free

```cpp
// C++
Item* a = new Item("foo", 5);
Item* b = a;       // two pointers to same memory
delete a;
delete b;          // double free — heap corruption
```

Rust solves this with **ownership**: exactly one variable owns each allocation. When you assign `let b = a`, ownership *moves* — `a` is no longer valid. There's always exactly one owner, so there's exactly one free.

### 3. Data races (aliasing + mutation)

```cpp
// C++
std::vector<int> v = {1, 2, 3};
int& ref = v[0];         // reference into the vector's buffer
v.push_back(4);          // may reallocate — ref is now dangling
std::cout << ref;        // undefined behaviour
```

This is the subtle one. The borrow checker enforces the **aliasing XOR mutability** rule: at any point in time, you can have either:
- One mutable reference (`&mut T`), OR
- Any number of immutable references (`&T`)

Never both. This means if someone is reading data, nobody can be writing it. If someone is writing, nobody else can see it. This eliminates an entire class of iterator invalidation, concurrent mutation, and aliasing bugs.

### What it costs you

The borrow checker is not free in terms of developer experience:

- **Learning curve**: lifetimes are one of the steepest parts of learning Rust. `'a` annotations, lifetime elision rules, `where T: 'a` bounds — it's a lot of machinery.
- **Fighting the borrow checker**: some perfectly valid patterns don't typecheck. Self-referential structs, graph structures, doubly-linked lists — these require `unsafe`, `Rc<RefCell<T>>`, or arena allocators to work around the ownership model.
- **Infectious annotations**: once one function needs a lifetime parameter, callers often need them too. It propagates up the call stack.
- **Refactoring friction**: moving code around can break borrow relationships in non-obvious ways.

### The spectrum of options for Furst

Given what we've already discussed (RAII recommended, move semantics, `Ptr<T>`), here's how I see the design space:

| Approach | Use-after-free | Double-free | Data races | Ergonomics |
|----------|---------------|-------------|------------|------------|
| **Manual** (C) | no protection | no protection | no protection | easy to write, hard to debug |
| **RAII + move** (C++ simplified) | partial — dangling refs possible | solved by single ownership | not solved | good, no annotations |
| **RAII + move + borrow rules** (Rust-lite) | solved | solved | solved | moderate — some annotation needed |
| **Full borrow checker** (Rust) | solved | solved | solved | steep — lifetimes everywhere |
| **Reference counting** (Swift/Koka) | solved at runtime | solved | partial (runtime checks) | excellent — no annotations |

### Unresolved
- Where on this spectrum does Furst want to be?
- Is use-after-free a compile-time concern for Furst, or is RAII + move "good enough" with runtime checks as a safety net?
- Does Furst care about data race safety at the type level, or is that handled by the concurrency model (green threads, message passing)?

## Compiler-driven ownership with user-controlled placement

What you're describing is a system where:

1. **The user decides *where*** — stack or heap, via the presence or absence of `new`
2. **The compiler decides *when to free*** — by analysing ownership flow, no manual `free`, no GC pause
3. **The compiler decides *when to copy vs mutate*** — reuse analysis, invisible to the user

This is actually a distinct point in the design space that none of the mainstream languages occupy cleanly:

- **Rust** gives you control over where *and* when, but you have to annotate lifetimes to prove it
- **Swift** gives you good ergonomics but everything is refcounted at runtime — you don't control placement, and ARC has overhead
- **Koka/Lean4** do reuse analysis but are fully GC'd — you don't control placement at all
- **C++** gives you full control but zero compiler help on ownership

What you want is something like: **Koka's compiler intelligence with C++'s placement control and no GC**.

### How this could work in practice

The compiler would need to perform **ownership analysis** as a compilation pass — after type inference but before LLVM emission. For each `Ptr<T>` (heap allocation), it determines:

**Case 1: Single owner, linear path**
```fsharp
let item = new Item { name = "foo", count = 5 }
process item
// item never used again → compiler inserts free after process()
```
No annotation needed. The compiler sees `item` is created, passed to `process`, and never touched again. It inserts the `free` call. This is what Rust does, but without requiring you to think about it.

**Case 2: Returned to caller — ownership transfer**
```fsharp
let makeItem () : Ptr<Item> =
    let item = new Item { name = "foo", count = 5 }
    item    // ownership moves to caller, no free here
```
Compiler sees the value escapes via return. Caller now owns it. Same as Rust's move semantics, but inferred rather than annotated.

**Case 3: Shared read access — the interesting one**
```fsharp
let item = new Item { name = "foo", count = 5 }
let name = item.name    // borrows item for read
log name
log item.count          // item still alive here
// item dies here → free
```
The compiler needs to know that `item` is still needed after `name` is extracted. This is where Rust would require a lifetime annotation. But if Furst's compiler can do **liveness analysis** (which LLVM already does for registers), it can figure this out without any annotation — `item` is live until its last use on the next line.

**Case 4: The hard one — aliasing**
```fsharp
let item = new Item { name = "foo", count = 5 }
let alias = item        // two names for the same heap allocation
log alias.name
log item.count          // is item still valid?
```
This is where you have to make a choice:
- **Move semantics** (Rust): `let alias = item` invalidates `item`. Compile error on `item.count`. Safe but sometimes annoying.
- **Reference counting** (Swift/Koka): `let alias = item` bumps a refcount. Both valid. Freed when count hits zero. Small runtime cost.
- **Copy on alias** (value semantics): `let alias = item` deep-copies. Both independent. Can be expensive.

### Where I think Furst should land

Given your goals — compiler does the work, no GC, good ergonomics — I'd suggest:

1. **Move by default** for heap values. `let alias = item` moves ownership. If you use `item` after, compile error. This is zero-cost and catches bugs.
2. **Explicit copy when needed**: `let alias = copy item` makes a deep copy. User opts in to the cost.
3. **Implicit borrowing for function args**: when you pass a `Ptr<T>` to a function that only reads it, the compiler passes a borrow — no move, no copy, no refcount. The compiler proves the callee doesn't store or free it.
4. **Reuse analysis** on top: when the compiler can prove a value is uniquely owned and about to die, it mutates in place instead of copying. This is the Koka optimisation and it composes with move semantics perfectly.

This gives you:
- No lifetime annotations (compiler infers from liveness)
- No GC (ownership is always clear)
- No refcounting overhead (moves are free, borrows are free)
- User controls placement (`new` or not)
- Escape hatch: `copy` when you genuinely need two owners

The main trade-off is that users will occasionally hit "value moved, can't use it here" errors. But that's a much smaller surface than Rust's full borrow checker — no lifetime parameters, no `'a`, no `where T: 'a`. Just "this thing moved, you can't use the old name."

### Unresolved
- Does implicit borrowing for function args need any annotation on the function signature, or is it fully inferred?
- What about storing a `Ptr<T>` in a struct field — does the struct now own it? Does the struct need to be movable?
- Closures that capture heap values — move into closure or borrow?
- Does `copy` do a shallow or deep copy? What if the struct has nested `Ptr<T>` fields?

## Resolving ownership details

### 1. Does implicit borrowing for function args need annotation, or is it fully inferred?

**Fully inferred, with an opt-in annotation when you want to take ownership.**

The compiler analyses the function body. If the function only reads from the argument and doesn't store it, return it, or pass it to something that takes ownership — it's a borrow. No annotation needed.

```fsharp
// compiler infers: borrows item, doesn't consume it
let getName (item: Ptr<Item>) : String =
    item.name       // read-only access, this is a borrow

// compiler infers: takes ownership, because it returns the value
let passThrough (item: Ptr<Item>) : Ptr<Item> =
    item            // returned — ownership transfers to caller

// compiler infers: takes ownership, because it stores it
let cache (item: Ptr<Item>) (store: Ptr<Store>) : unit =
    store.cached = item     // stored — ownership consumed
```

The caller side is where this matters. When you call `getName item`, the compiler knows `getName` borrows, so `item` is still valid after the call. When you call `cache item store`, the compiler knows `item` is consumed — using it after is a compile error.

The one case where you might want an explicit annotation is **interfaces/traits** — when the compiler can't see the function body (because it's a trait method or FFI). In that case, you'd need to declare intent:

```fsharp
trait Nameable
    getName (self) : String          // borrows self (default)
    consume (own self) : unit        // takes ownership of self
```

`own` as a parameter modifier — only needed at abstraction boundaries where the body isn't visible. Everywhere else, inferred.

### 2. Storing a `Ptr<T>` in a struct field — ownership and movability

The struct should **own** the `Ptr<T>` in its field. And yes, this makes the struct itself subject to move semantics.

```fsharp
struct Team
    name: String            // value type, copied freely
    leader: Ptr<Person>     // owned heap reference

let team = new Team { name = "Alpha", leader = new Person { name = "Alice" } }

let other = team            // moves the whole Team, including the Ptr<Person>
// team is now invalid — both the Team allocation and the Person it owns moved

log other.leader.name       // fine — other owns everything
log team.leader.name        // compile error: team was moved
```

When a struct is freed (goes out of scope or CE cleanup), it **recursively frees** its owned `Ptr<T>` fields. This is standard RAII — the destructor walks the fields.

What about **non-owning references** in struct fields? This is where Rust needs lifetime annotations. The initial design should **not allow them**. If a struct has a `Ptr<T>` field, it owns it. If you need to *refer* to data owned elsewhere, use an index, an ID, or restructure so ownership is clear. This sidesteps the hardest part of Rust's lifetime system entirely.

```fsharp
// NOT allowed (at least initially):
struct View
    item: &Item     // borrowed reference in a struct — lifetime hell

// Instead, restructure:
struct View
    itemId: i64     // refer by ID, look up when needed
```

This is a real constraint — it means no self-referential structs, no borrowed views stored in fields. But it's the same constraint that makes the "no lifetime annotations" promise possible. You can revisit this later if it proves too limiting.

### 3. Closures that capture heap values — move or borrow?

This should follow the same rule as function arguments: **the compiler infers based on usage**.

```fsharp
let item = new Item { name = "foo", count = 5 }

// borrows — closure only reads item, doesn't outlive current scope
let getName = fun () -> item.name
log (getName ())
log item.count          // fine — item was borrowed, not moved

// moves — closure is returned, outlives current scope
let makeGetter (item: Ptr<Item>) =
    fun () -> item.name     // item must move into closure, it outlives the function
// item is consumed — the closure owns it now
```

The key heuristic: **if the closure escapes the scope where the captured value lives, it must move. Otherwise, it borrows.**

The compiler can determine this:
- Closure returned from function → escapes → move
- Closure passed to `List.map` in the same scope → doesn't escape → borrow
- Closure stored in a struct field → escapes → move

For the case where the user wants to force a move (e.g., spawning a green thread):

```fsharp
let item = new Item { name = "foo", count = 5 }

// explicit move into a closure that will outlive this scope
spawn (fun () ->
    log item.name       // compiler detects: spawn's closure escapes → item moves in
)
// item invalid here — moved into the spawned task
```

No `move` keyword needed on the closure itself — the compiler sees that `spawn` takes a closure that escapes (because `spawn`'s signature says so via the ownership analysis of its body, or via an `own` annotation on the trait). The inference handles it.

### 4. Does `copy` do a shallow or deep copy?

**Deep copy.** Always. A shallow copy of a `Ptr<T>` would create two owners of the same heap allocation — exactly the aliasing problem we're trying to avoid.

```fsharp
struct Team
    name: String
    leader: Ptr<Person>

let a = new Team { name = "Alpha", leader = new Person { name = "Alice" } }
let b = copy a

// b is a completely independent clone:
// - new Team allocation
// - new String for name
// - new Person allocation for leader
// a and b share nothing
```

For nested `Ptr<T>` fields, `copy` recurses — it copies the Team, then copies the Person that `leader` points to, and so on. This is like Rust's `Clone` trait.

The compiler can **auto-generate** the deep copy implementation for any struct where all fields are copyable. If a field is a type that can't be copied (e.g., a file handle or mutex), `copy` on the containing struct is a compile error.

```fsharp
struct SafeToCopy
    name: String
    count: i32
    data: Ptr<Buffer>       // Ptr<T> is copyable if T is copyable
// copy works — compiler generates recursive deep copy

struct NotCopyable
    name: String
    handle: FileHandle       // FileHandle is not copyable
// copy is a compile error — "FileHandle does not support copy"
```

This could be a trait:

```fsharp
trait Copyable
    copy (self) : Self       // auto-derived for structs with all-copyable fields
```

### Summary of resolutions

| Question | Resolution |
|----------|-----------|
| Borrowing annotation | Fully inferred from function body. `own` keyword only at abstraction boundaries (traits, FFI) |
| Ptr in struct fields | Struct owns it. Recursive free on drop. No borrowed references in struct fields (initially) |
| Closures | Inferred — borrows if closure stays in scope, moves if it escapes |
| Copy semantics | Always deep. Auto-derived for structs with all-copyable fields. Compile error if a field isn't copyable |

### Unresolved
- Should `own` be the keyword, or something else? (`take`, `consume`, `move`?)
- Can the "no borrowed references in struct fields" constraint be relaxed later without breaking existing code?
- Should there be a `Copyable` trait, or is copy-ability a built-in compiler concept?
- Value types (stack) — are they always implicitly copyable, or do large structs also move by default?

## Immutability-first ownership: refcounting + reuse analysis

Fundamental insight: Furst is immutable. The user never sees mutation. This changes the ownership story — **sharing is always safe** because aliasing + mutation can't happen. The only question is lifetime management.

### Default behaviour: refcounted sharing

```fsharp
let people = req.deserialiseBody<Person list>   // heap list, ownership moved out
let active = people                              // refcount bumps to 2, same heap pointer
let archived = people                            // refcount bumps to 3

// user writes pure immutable transformations:
let filtered = List.filter (fun p -> p.isActive) active
let sorted = List.sortBy (fun p -> p.name) archived
```

To the user, `filtered` and `sorted` are new lists. But under the hood:

- When `filtered` is computed, the compiler checks: is `active`'s refcount 1? **No, it's shared.** So it allocates a new list and filters into it.
- But if the code was instead:

```fsharp
let people = req.deserialiseBody<Person list>
let filtered = List.filter (fun p -> p.isActive) people
// people is never used again — refcount is 1
// compiler MUTATES IN PLACE — removes non-active items from the existing allocation
```

**The refcount IS the reuse analysis mechanism.** Refcount 1 means "I'm the only owner, safe to mutate." Refcount > 1 means "someone else is looking at this, I need to copy."

The user never thinks about this. They write pure immutable code. The compiler and refcount conspire to make it fast.

### Explicit control with `copy`

`copy` is the escape hatch for performance predictability — when the user knows upfront they're going to diverge:

```fsharp
let people = req.deserialiseBody<Person list>

// user knows these will diverge, clones upfront for predictable perf
let active = copy people        // deep copy, refcount 1, will get reuse optimisation
let archived = copy people      // deep copy, refcount 1, same
// people still valid, refcount 1 (the copies are independent)

let filtered = List.filter (fun p -> p.isActive) active     // mutates in place
let sorted = List.sortBy (fun p -> p.name) archived         // mutates in place
```

vs the default:

```fsharp
let active = people             // shared, refcount 2
let archived = people           // shared, refcount 3

let filtered = List.filter (fun p -> p.isActive) active     // must copy — refcount > 1
let sorted = List.sortBy (fun p -> p.name) archived         // must copy — refcount > 1
```

Same result either way. But `copy` upfront gives the user control over *when* the allocation happens.

### The full picture

Furst's memory model:

1. **Everything is immutable** to the user
2. **Stack values copy freely** — they're small, it's cheap
3. **Heap references are refcounted** — sharing is the default, safe because immutable
4. **Reuse analysis via refcount** — if refcount is 1 at a transformation site, mutate in place. If > 1, copy-then-mutate. User never sees this.
5. **`copy` keyword** — explicit deep clone for when the user wants to force independent ownership upfront
6. **`new` keyword** — user controls stack vs heap placement

This is Koka's reuse analysis married to explicit placement control, with no GC — just refcounting. And because everything is immutable, refcounting has no cycles problem (cycles require mutation to create).

### Unresolved
- Refcounting overhead — every heap value gets a refcount field. Is atomic refcounting needed (for green threads), or can the compiler prove thread-locality in most cases?
- Should the compiler warn when it detects a "refcount > 1 at transformation site" that could be avoided with `copy`? Like a performance lint?
- Does `copy` on a list copy the spine only or the elements too? (If elements are value types, spine-only is a deep copy. If elements are `Ptr<T>`, need to recurse.)
- The "no cycles" claim — is this actually guaranteed? Can you construct a cycle with immutable data? (I believe not, but worth proving.)

## Green threads as ownership boundaries

Clean rule: green threads are **ownership boundaries**. Everything goes in as a move, everything comes out as a return. No shared state, no refcounting across threads.

```fsharp
let people = req.deserialiseBody<Person list>

// everything moved in — people is invalid after this
let handle = spawn (fun () ->
    let filtered = List.filter (fun p -> p.isActive) people
    filtered    // return moves ownership back out
)

// people invalid here — moved into the thread
let result = await handle   // moves the return value out to this scope
```

### Refcounting never needs to be atomic

If heap values can never be shared across threads, refcount operations are always thread-local. No `atomic_increment`/`atomic_decrement` — just plain integer ops. Swift pays a real cost for atomic ARC because any reference could potentially cross threads. Furst doesn't, because the ownership boundary at `spawn` makes it impossible.

### Inter-thread communication: channels

For talking between threads, a channel is the natural primitive. It's an explicit move across the boundary — you put something in, you no longer own it. The other side takes it out, they now own it.

```fsharp
let (tx, rx) = Channel.create<Item> ()

spawn (fun () ->
    // tx moved into this thread
    let item = produceItem ()
    Channel.send tx item        // item moved into channel, invalid here
    Channel.send tx (produceItem ())
    Channel.close tx
)

// rx stays in this thread
for item in Channel.receive rx      // item moved out of channel to us
    log item.name
```

This is essentially Go's channels or Rust's `mpsc`, but the ownership semantics fall out naturally from the move rules. Sending on a channel is a move. Receiving is a move. No copying, no sharing.

### Broadcast / multiple consumers

If you need one producer sending to many consumers:

- **Broadcast always deep copies** — simple, consistent, maybe wasteful
- **Broadcast uses `Shared<T>`** — immutable, atomically refcounted, can cross thread boundaries. The one exception to "no atomic refcounting."

Lean toward: broadcast deep copies by default, introduce `Shared<T>` later as an optimisation if profiling shows it's a bottleneck.

### The low-level abstraction

For cases where channels are too high-level — shared memory regions:

```fsharp
// low-level: explicit shared memory with lock
let shared = SharedMem.create<Buffer> (Buffer.alloc 4096)

spawn (fun () ->
    SharedMem.withLock shared (fun buf ->
        Buffer.write buf "hello"
    )
)

spawn (fun () ->
    SharedMem.withLock shared (fun buf ->
        let data = Buffer.read buf
        log data
    )
)
```

`SharedMem<T>` would be:
- Atomically refcounted (the one exception)
- Access only through `withLock` — no way to get a raw reference out
- The closure inside `withLock` borrows the data, can't move it out
- Explicitly marked as the "you're opting into shared mutable state" escape hatch

### Hierarchy of thread communication

1. **Default**: move everything, no sharing — zero-cost, safe
2. **Channels**: move values between threads — explicit, predictable
3. **SharedMem**: actual shared mutable state — explicit, locked, you know what you're getting into

### Unresolved
- Should `spawn` take explicit parameters rather than capturing? e.g. `spawn (people) (fun people -> ...)` to make the move visually obvious?
- Channel buffering — bounded or unbounded by default?
- Can `SharedMem.withLock` nest? Deadlock potential if two shared regions locked in different order
- Is `SharedMem` too low-level for v1? Could defer it and just ship channels

## Stack size limits

**The compiler doesn't auto-promote to heap.** If you declare something on the stack, it goes on the stack. Stack overflow from large values is a "you know what you're doing" situation and is rare enough that it doesn't need a language-level solution.

**Recursion is the real risk**, and `rec` already flags it. The compiler can do two things with `rec` functions:

```fsharp
// marked recursive — compiler knows this could blow the stack
let rec fibonacci (n: i32) : i32 =
    if n <= 1 then n
    else fibonacci (n - 1) + fibonacci (n - 2)

// tail-recursive — compiler can optimise to a loop, no stack growth
let rec factorial (n: i32) (acc: i32) : i32 =
    if n <= 1 then acc
    else factorial (n - 1) (n * acc)    // tail position → becomes a goto
```

For `rec` functions:
1. **Tail calls → loop optimisation.** LLVM already supports this with `musttail` or the compiler can lower it to a loop directly. Zero stack growth.
2. **Non-tail recursion → no special treatment.** The user wrote it, they accept the stack depth. Standard behaviour, same as C++.

### Recursive data types

Recursive types connect here:

```fsharp
type Tree =
    | Leaf of i32
    | Node of Tree * Tree       // recursive — must be heap, compiler can't size it
```

Initial thought was this always requires heap because the type is unsized. But that's wrong — or at least too broad.

A fixed-shape recursive structure known at compile time can live on the stack:

```fsharp
// stack — compiler knows the full shape at the declaration site
let tree = Node (Leaf 1, Node (Leaf 2, Leaf 3))
// sizeof(Node) + sizeof(Node) + 3 * sizeof(Leaf) — all known, one contiguous stack alloc
```

The problem is only when size is **not known at compile time**:

```fsharp
// how deep? depends on runtime data — must be heap
let rec buildTree (items: List<i32>) : Tree =
    match items with
    | [x] -> Leaf x
    | xs ->
        let (left, right) = List.splitAt (List.length xs / 2) xs
        Node (buildTree left, buildTree right)
```

So recursive types follow the same rule as everything else:

| Known at compile time | Placement |
|---|---|
| `let arr = [1, 2, 3]` | Stack — size known |
| `let arr = List.ofInput data` | Heap — size unknown |
| `Node (Leaf 1, Leaf 2)` | Stack — shape known |
| `buildTree runtimeData` | Heap — shape unknown |

No special rule needed. **If the compiler can size it, stack. If it can't, heap.** The user controls placement the same way as everything else.

```fsharp
// stack — compiler knows the full shape
let tree = Node (Leaf 1, Node (Leaf 2, Leaf 3))

// heap — user explicitly says so because they know it'll be runtime-sized
let tree = new (buildTree items)
```

### Can the compiler promote from stack to heap?

The fundamental problem with promotion is **existing references**. If something points at stack address `0x7fff1230` and you move data to heap address `0x55a0beef`, that pointer dangles.

But in Furst's model, this might be solvable:
1. Everything is immutable — no one is writing through those references
2. The compiler controls all access — no raw user-held pointers
3. Refcounting means the compiler knows exactly who's pointing at what

The compiler can use an indirection layer and rewrite reference paths at the promotion point. This is what Go does with goroutine stacks — they start small, grow by copying, rewrite all internal pointers.

### Small buffer optimisation pattern

The practical approach — **start on stack, overflow to heap during construction**:

```fsharp
// user writes:
let items = List.filter (fun p -> p.isActive) people

// compiler generates:
// 1. start with a stack buffer (heuristic size)
// 2. if result fits → stays on stack, zero heap allocation
// 3. if result overflows → malloc, copy to heap, continue there
```

Key insight: **promotion only happens during construction**. Once fully built, the value is either stack or heap and stays there. References are only handed out after construction, so no dangling pointers.

### User mental model

```fsharp
// don't care where it lives — compiler handles it
let items = List.filter (fun p -> p.isActive) people

// I know this will be huge — put it on the heap from the start
let items = new List.filter (fun p -> p.isActive) people

// I know exactly how big this is — always stack
let items: i32[64] = ...
```

Compiler behaviour:
1. **Known size at compile time** → stack, exact allocation
2. **Unknown size, no `new`** → start on stack, promote to heap if it overflows during construction
3. **Explicit `new`** → heap from the start

### The stack: per-function or whole chain?

One contiguous block per thread, shared across the whole call chain. Each function call pushes a stack frame. `alloca` allocates within the current function's frame — when the function returns, that memory is gone. Deep recursion blows the stack because frames accumulate.

### Tagged bindings for explicit placement (future exploration)

Potential syntax for forcing placement policy:

```fsharp
// compiler decides — known size goes stack, unknown may promote
let items = List.filter (fun p -> p.isActive) people

// explicit heap — no stack phase, heap from the start
let items: <heap> Item list = List.filter (fun p -> p.isActive) people

// explicit stack — compile error if compiler can't prove it fits
let items: <stack> Item list = List.filter (fun p -> p.isActive) people
```

This separates placement policy from construction — `new` was doing two jobs. Tags handle placement, construction is just construction. **Side note: revisit this idea when the default analysis (known vs unknown size) proves insufficient. For now, stick with compiler-analysed placement as the default.**

### Unresolved
- Should the compiler warn on non-tail `rec` functions? Or is that too noisy?
- What's the default small buffer size? Per-type heuristic or global threshold?
- Does promotion count as a heap allocation for refcounting purposes? (It should)
- Can the compiler use escape analysis to skip the stack phase when it knows the result will be large?
- For stack-allocated recursive types, flat layout or nested structs?
- Stack size configuration per-thread / per-green-thread?
- Does the `<heap>`/`<stack>` tag syntax conflict with generic type parameters?

## Allocator strategy

### The default: just work

For most users, the allocator should be invisible. The compiler picks a good general-purpose allocator — probably `mimalloc` or `jemalloc` rather than system `malloc`, since they're better for the small, short-lived allocation patterns functional languages produce (lots of copies from reuse analysis).

```fsharp
// user doesn't think about allocators
let item = Item { name = "foo", count = 5 }
let items = List.map transform people
```

### Scoped allocators via CEs

The `heap { }` CE concept extends naturally — the CE controls which allocator backs `let!` allocations:

```fsharp
// arena allocator — bulk free at end, no individual frees
arena {
    let! items = List.map transform people
    let! filtered = List.filter predicate items
    let! sorted = List.sortBy key filtered
    return sorted    // moved out to caller's allocator
}   // everything except sorted freed in one shot

// pool allocator — preallocated fixed-size blocks
pool<Item> 1000 {
    let! items = List.map transform people
    return items
}
```

### What LLVM sees

```llvm
; default: malloc/free
%item = call ptr @malloc(i64 16)
call void @free(ptr %item)

; arena: bump pointer, bulk free
%item = call ptr @arena_alloc(ptr %arena, i64 16)
call void @arena_destroy(ptr %arena)

; pool: grab from preallocated slab
%item = call ptr @pool_alloc(ptr %pool)
call void @pool_free(ptr %pool, ptr %item)
```

### Interaction with refcounting

Arena-allocated values can't be individually freed. Solution: **refcount tracks lifetime, but deallocation is delegated to the allocator.** Arena's free is a no-op. Pool's free returns to pool. Refcount decrement calls `allocator.free(ptr)` instead of `free(ptr)`.

Allocation metadata stored alongside the value:

```
┌──────────┬──────────┬────────────────┐
│ refcount │ alloc_id │ actual data... │
└──────────┴──────────┴────────────────┘
```

### The escape problem

When a value allocated in an arena is returned out of the CE, `return` copies to the outer allocator before the arena is destroyed. That's the price of bulk deallocation.

### v1 scope

The default allocator covers web services and most application code. Arena and pool are domain-specific optimisations. The design should **accommodate** custom allocators from the start (refcount/free indirection built in), but CE syntax and arena/pool implementations can come later.

### Unresolved
- Should allocator metadata always be present, or only when custom allocators are in use?
- Can allocators compose? Arena inside a pool inside the default?
- Thread-local allocators — does each green thread get its own instance?

## Allocator as ambient context

Users should never write their own allocator. Don't want Zig-style allocator threading. The allocator is an **ambient context** set at function boundaries via attributes, not a parameter.

### The attribute and DU

```fsharp
type AllocatorType =
    | Default       // mimalloc/jemalloc
    | Arena         // bump allocator, bulk free on scope exit
    | Pool of size: i32  // fixed-size block pool

[<Allocator(Arena)>]
let handleRequest (req: Request) : Response =
    // everything heap-allocated in here uses the arena
    let body = req.deserialiseBody<Order list>
    let validated = List.filter Order.isValid body
    let response = buildResponse validated
    response    // copied out of arena to caller's allocator on return
    // arena destroyed, all intermediate allocations gone in one shot
```

`AllocatorType` is a closed DU — users can't add variants. Compiler controls allocation strategy.

### Ambient inheritance — no parameter threading

```fsharp
[<Allocator(Arena)>]
let handleRequest (req: Request) : Response =
    let items = buildResponse validated     // uses arena, doesn't know or care
    let valid = validate order              // uses arena, doesn't know or care
```

Functions called within an `[<Allocator>]` scope inherit the allocator. No parameter, no attribute needed on callees.

### `<heap>` is abstract placement

`<heap>` means "heap-allocate using whatever allocator is active." It's about placement policy (stack vs heap), not which heap. The allocator attribute controls which heap. They're orthogonal.

### Green threads and request lifecycle

```fsharp
server.onRequest (fun req ->
    spawn (fun () ->
        let response = handleRequest req    // arena-backed
        req.respond response                // response copied out
    )   // green thread dies, arena destroyed, all memory reclaimed instantly
)
```

Framework sets the allocator policy. App developer writes normal code. Arena cleanup on green thread exit — one bulk deallocation per request. No GC pauses, predictable latency.

### Compiler implementation

`[<Allocator>]` compiles to a thread-local allocator swap in the function prologue/epilogue:

```llvm
define ptr @handleRequest(ptr %req) {
    %arena = call ptr @arena_create(i64 65536)
    call void @set_thread_allocator(ptr %arena)
    ; ... function body, all allocs route through arena ...
    %result = call ptr @copy_to_parent_allocator(ptr %response)
    call void @arena_destroy(ptr %arena)
    call void @restore_thread_allocator()
    ret ptr %result
}
```

### Unresolved
- Thread-local allocator swap cost — acceptable or should the compiler inline it away?

## Key decisions: attributes, arenas, and green threads

**Attributes are compiler intrinsics** — not user-extensible AOP. Keep it close-knit for now.

**Arenas don't nest.** An arena is for a domain boundary — one per request, one per file processing job, one per connection. Sub-arenas add complexity for no benefit. Refcount handles cleanup of individual values within the arena; the arena itself bulk-frees at the end.

**`green` keyword for green threads:**

```fsharp
// clear: handleRequest is a function, req is moved in
green handleRequest req

// no closure — compiler trivially lints that only explicit args cross the boundary
// no accidental captures possible
```

### Arena and refcount interaction

In an arena, refcount-free is a no-op — memory sits until the arena dies.

**Why arenas can't free individual items:** An arena is a big block with a bump pointer. Allocation is a single pointer increment — no bookkeeping. But if item B dies between items A and C, that space is trapped:

```
┌────────────────────────────────────────────────┐
│ [item A] [  dead B  ] [item C] [ ...free... ]  │
└────────────────────────────────────────────────┘
                                  ↑ bump pointer
// B is dead but can't be reclaimed — A and C surround it
```

You can only free the **whole arena at once** — reset the bump pointer to the start.

A hybrid arena with a free list would lose the simplicity and speed that makes arenas attractive — you'd reinvent a general-purpose allocator with extra steps.

**Resolution:** Arenas are for short-lived scopes. Long-lived work uses the default allocator where refcount free is real. Don't try to make arenas do everything.

```fsharp
// HTTP request — arena, short-lived, bounded
[<Allocator(Arena)>]
let handleRequest (req: Request) : Response = ...

// WebSocket — default allocator, long-lived, needs real refcount free
let handleConnection (ws: WebSocket) : unit =
    for msg in ws.messages
        let response = process msg
        ws.send response
```

### Unresolved
- Should `green` always imply Arena, or is that a separate decision?
- Can `green` take multiple arguments? `green handleRequest req ctx`
- Should there be a warning/lint if `[<Allocator(Arena)>]` is applied to a function that could run indefinitely?

**Decision:** Arenas are for short-lived, bounded scopes — bulk allocate, bulk free. Default allocator handles long-lived work with refcount cleanup at natural boundaries. Periodic arena reset (checkpoint + copy) is an interesting future optimisation but not v1. Framework developers choose the right allocator for their domain.

## String representation

### Why Rust has six string types

| Type | Owned? | Heap? | Why it exists |
|------|--------|-------|---------------|
| `String` | yes | yes | The "normal" owned growable string |
| `&str` | no | maybe | Borrowed view into someone else's string |
| `&'static str` | no | no | String literals baked into the binary |
| `Box<str>` | yes | yes | Owned fixed-size heap string |
| `CString`/`&CStr` | yes/no | yes/no | Null-terminated C FFI strings |
| `OsString`/`&OsStr` | yes/no | yes/no | OS-native strings (Windows paths aren't UTF-8) |

Root causes: ownership must be explicit in the type system (so owned vs borrowed = separate types), and FFI boundaries don't speak UTF-8. Furst's refcounting eliminates the owned/borrowed split. FFI is deferred.

### Furst: one type, optimisations under the hood

1. **One type: `String`.** No `&str` vs `String` distinction. Refcounting handles sharing.
2. **SSO built in.** Short strings (≤23 bytes) stored inline, no heap. Long strings heap-allocate. Invisible to the user.

```
SSO string (small, ≤23 bytes):
┌──────────────────────────────┐
│ h e l l o \0 ... [len] [flag]│  → inline, no heap
└──────────────────────────────┘

Heap string (large):
┌──────────┬────────┬──────────┐
│ ptr      │ length │ capacity │  → points to heap buffer
└──────────┴────────┴──────────┘
```

3. **String literals are special.** Baked into the binary, zero allocation, pointer to static data. But still type `String` — no separate type.
4. **Interning is a library concern.** Frameworks provide `Intern.get "name"` if they need deduplication. Language doesn't build it in.

```fsharp
let name = "hello"                      // static data in binary, no allocation
let greeting = $"hello {name}"          // computed, SSO if small, heap if large
let body = req.readBody ()              // heap, too large for SSO
let key = Intern.get "name"             // library-level interning
```

One type, one mental model, optimisations under the hood.

### Decisions
- **UTF-8 always.** UTF-16 available as a standard library if needed.
- **String interpolation is a language feature:** `"Hello ${name}, you have ${count:toString} items, born ${dob:toIso}"`. `:` pipes to a formatter for non-string types.
- **Interning and reuse optimisation deferred** — build a memory analyser first, optimise based on real data.

### Unresolved
- Is 23 bytes the right SSO threshold?
- Separate `Bytes` type for binary data, or is that a standard library concern?

## Array and slice types

### 1. Fixed-size arrays — stack, compile-time known

```fsharp
let rgb = [255, 128, 0]              // i32[3] — stack, value type, copies on assignment
let matrix = [[1, 0], [0, 1]]        // i32[2][2]
```

Size is part of the type — `i32[3]` ≠ `i32[4]`. Same as C arrays / Rust's `[T; N]`. LLVM: contiguous `alloca`.

### 2. Dynamic lists — runtime-sized, refcounted

```fsharp
let items = List.map transform people    // runtime-determined size
```

Growable buffer (pointer + length + capacity). Follows all existing memory rules — refcounted, stack-first with promotion, reuse analysis applies.

### 3. Slices — views via refcount

A slice is a view into an existing array or list. No copy, no borrowed references — **shares via refcount to the parent**:

```
Original list:                          Slice:
┌──────────┬────────────────────┐      ┌──────────────┬────────┬────────┐
│ refcount │ [1, 2, 3, 4, 5]   │ ◄──  │ ref to parent │ offset │ length │
│    2     │                    │      │              │   1    │   3    │
└──────────┴────────────────────┘      └──────────────┴────────┴────────┘
```

```fsharp
let items = [1, 2, 3, 4, 5]
let middle = items[1..3]        // refcount on items bumped, no copy, sees [2,3,4]
let first = items[0..1]         // refcount bumped again

process middle                  // middle dies, refcount decrements
process first                   // first dies, refcount decrements
// items refcount back to 1 — reuse analysis can mutate in place again
```

Consistent with the whole model — no new concepts, no lifetimes. Refcount naturally prevents reuse-optimisation of the parent while slices exist.

### Decisions

- **`Slice<T>` is its own type** — not interchangeable with `List<T>`, inspired by .NET `Span<T>`
- **Slice of a slice refcounts the original parent**, not the intermediate — just tightens offset/length on the same parent reference
- **Slice to list is always an explicit copy**: `List.ofSlice slice`
- **No implicit coercion anywhere** — program correctness is paramount

```fsharp
let items = [1, 2, 3, 4, 5]
let middle = items[1..3]            // Slice<i32>, refcounts items
let inner = middle[0..1]            // Slice<i32>, still refcounts items (not middle)

let owned = List.ofSlice inner      // explicit copy, new List<i32>
let bad: List<i32> = items[1..3]    // compile error — Slice<i32> is not List<i32>
```

No implicit type coercion extends to the whole language:

```fsharp
let a: String = 2                   // compile error
let b: i32 = "1"                    // compile error
let a: String = String.ofInt 2      // explicit — "2"
let b: i32 = Int.parse "1"          // explicit — 1
```

Types are what they say they are. `Ptr<T>` is not `T`, `Slice<T>` is not `List<T>`. If you want to convert, you say so explicitly. This is one of F#'s best qualities — Furst should be even stricter.

### Unresolved
- Can you slice a fixed-size array? Would it refcount the stack frame?
- Should `List.map` / `List.filter` etc. accept `Slice<T>` via a shared trait, or require conversion?

## Destructors / drop behaviour

Resources beyond memory (file handles, sockets, connections) need cleanup logic, not just freeing bytes.

### Drop trait + `use` bindings

Two mechanisms that compose:

**`Drop` trait** defines **how** to clean up:

```fsharp
trait Drop =
    drop (self) : unit

impl Drop for FileHandle =
    drop (self) = OS.close self.fd
```

Compiler auto-generates `drop` for structs — calls `drop` on each field that implements `Drop`, then frees memory. User only writes `drop` for types holding external resources.

**`use` binding** controls **when** — deterministic cleanup at scope exit:

```fsharp
use file = File.open "data.txt"
let contents = File.readAll file
// file dropped HERE at scope exit, guaranteed — handle closed
```

Without `use`, drop happens when refcount hits zero — timing is less predictable if references are shared. With `use`, cleanup is at scope exit **regardless**. Important for scarce resources (DB connections, file handles).

### `use` bindings are linear

To prevent use-after-close, `use` bindings cannot be shared:

```fsharp
use file = File.open "data.txt"
let alias = file                    // compile error: use bindings cannot be shared
let contents = File.readAll file    // fine — borrows, doesn't store
```

Narrow restriction — only applies to `use` bindings. Regular bindings share freely via refcount.

### Summary

| Mechanism | When cleanup happens |
|-----------|---------------------|
| Refcount → 0 | When last reference dies (timing depends on sharing) |
| `use` binding | Scope exit, guaranteed, linear ownership enforced |
| `Drop` trait | Defines custom cleanup logic, called by either mechanism |

### Loan pattern over handle passing

Why pass handles around? The handle is an implementation detail. What you want is access to the *operation*. The resource owner controls lifecycle, you pass in what to do:

```fsharp
// loan pattern — handle never exposed to user code
let contents = File.withReader "data.txt" (fun reader ->
    reader.readAll ()
)

let users = Db.withConnection connString (fun db ->
    db.query<User> "SELECT * FROM users"
)
```

The `with*` function owns the resource, creates it, passes it to the callback, then closes it. The handle never becomes a first-class value. Misuse is structurally impossible.

Works for sequential operations too:

```fsharp
let report = Db.withConnection connString (fun db ->
    let users = db.query<User> "SELECT * FROM users"
    let orders = db.query<Order> "SELECT * FROM orders"
    let report = buildReport users orders
    db.execute "INSERT INTO reports ..." report
    report
)
```

### Idiomatic pattern: loan first, `use` as escape hatch

1. Standard library APIs expose `with*` functions — you never get a raw handle
2. `with*` manages creation and cleanup
3. Callback receives a handle that can't escape — borrowed, not owned
4. `use` exists for edge cases where loan nesting is impractical
5. `Drop` is internal machinery — used by `with*` implementations, not user-facing

### `use` is opt-in, not enforced

No compiler-forced cleanup. The .NET `IDisposable` warning hell (e.g., `HttpContent` that's disposable but not yours to dispose) shows why forcing it is a bad idea. The user decides.

```fsharp
// bind normally — your responsibility
let handle = File.open "hello.txt"
// ... do stuff ...
use handle          // standalone: trigger drop at end of this scope

// or bind with use from the start
use handle = File.open "hello.txt"
// dropped at scope exit

// or just don't clean up — that's on you
let handle = File.open "hello.txt"
// handle leaks if you forget. OS reclaims on process exit.
```

`use handle` as a standalone statement = "start dropping this at end of scope." Deferred cleanup you can attach to any existing binding.

`with*` loan pattern is still idiomatic for library APIs:

```fsharp
let withReader (path: String) (f: Reader -> 'a) : 'a =
    use reader = Reader.create path
    f reader
    // reader dropped here
```

**Decision:** No warnings, no forced `use`. `Drop` defines how. `use` controls when. Can tighten later if needed.

### Unresolved
- Should the callback handle be a special "non-escaping" type, or is borrow inference enough?
- Does this interact with green threads? Connection closed when thread exits?
- Error handling during drop — if `OS.close` fails: ignore, log, or panic?
- Does `use` compose with arena? (Handle closed immediately, memory freed with arena)

## Escape analysis

Escape analysis asks: can the compiler prove a heap allocation doesn't escape, and demote it to stack? JVMs do this aggressively.

### Does Furst need it?

Not really for placement — the stack-first-promote-to-heap default already captures most of what escape analysis gives you:

1. **User said `<heap>` explicitly** — respect their choice
2. **Runtime-sized data** — promotion model already handles it
3. **Stack is already the default** — nothing to demote

### Where it is valuable: refcount elision

If the compiler proves a heap value is local to a function, it can **skip refcounting entirely** and free at scope exit:

```fsharp
let process (items: List<Item>) : i32 =
    let filtered = List.filter predicate items   // heap, but doesn't escape
    let count = List.length filtered
    count
    // filtered never escapes — skip refcount, free directly at function exit
```

One less refcount operation per allocation. Not about placement, about reducing overhead.

### Unresolved
- Is refcount elision via escape analysis worth the compiler complexity?
- Later optimisation pass, not v1?

## Memory layout

### Padding and alignment

CPUs are fastest with naturally aligned data. Compiler inserts padding between fields:

```fsharp
struct Example
    a: i8       // 1 byte
    b: i64      // 8 bytes
    c: i8       // 1 byte
```

Naive (declaration order): 24 bytes (14 bytes wasted as padding)
Reordered (largest first): 16 bytes — saved 8 bytes

```
Reordered:
┌────────────────────────────────────┬────┬────┬──────────┐
│ b (8 bytes)                        │ a  │ c  │ 6 bytes  │
└────────────────────────────────────┴────┴────┴──────────┘
```

### Compiler reorders by default

Furst is immutable, users access fields by name, never by position. No pointer arithmetic, no `offsetof`. The compiler is **free to reorder fields** for optimal packing. User never thinks about it.

LLVM doesn't care about field order — frontend maintains a name-to-index mapping:

```llvm
%Example = type { i64, i8, i8 }     ; reordered: b, a, c
%a_ptr = getelementptr %Example, ptr %ex, i32 0, i32 1   ; "a" maps to index 1
```

### Layout control via `Align` attribute

```fsharp
type AlignMode =
    | Packed        // no padding — binary protocols, network formats
    | SIMD          // 16/32-byte alignment for vectorised operations
    | Preserve      // declaration order — FFI, matching C layouts
    // default (no attribute): compiler reorders for optimal packing

[<Align(Packed)>]
struct NetworkHeader
    version: i8
    flags: i8
    length: i16

[<Align(SIMD)>]
struct Vec4
    x: Float
    y: Float
    z: Float
    w: Float

[<Align(Preserve)>]
struct CInterop
    a: i8
    b: i64
    c: i8
```

Closed DU, compiler intrinsic — same pattern as `AllocatorType`.

### Unresolved
- Hot/cold splitting (PGO-guided field placement) — future optimisation?

## Weak references (future)

**Deferred.** In a refcounted immutable world, cycles can't form, so weak refs are less critical. Revisit if caching patterns need them.

## FFI memory (future)

**Deferred.** C/C++ bindings not needed right now. Key questions for when we get there: who owns C-allocated memory, how to wrap raw pointers into Furst's ownership model, how to pass Furst memory to C without metadata confusion, whether an `unsafe` block concept is needed.

### Intrinsic vs standard library (for AllocatorType)

**Standard library**: defined in Furst code that ships with the language. The compiler has no special knowledge of it — it's a regular type in a regular file.

**Compiler intrinsic**: hardcoded into the compiler binary. The compiler *knows* what it is without looking it up, like `i32`. The user never sees the definition.

`AllocatorType` should be a **compiler intrinsic** because the compiler needs to emit fundamentally different LLVM IR for each variant — bump pointer for Arena, slab alloc for Pool, malloc for Default. It's a code generation concern, not something user code can abstract over.

### Unresolved
- Is the allocator set permanently fixed, or could a plugin/extension system allow new allocator types in the future?

---

## Finalisation

### ADRs generated
- **ADR-0004: Memory Ownership Model** — refcounting, move semantics, immutability-first, reuse analysis, green thread ownership boundaries
- **ADR-0005: Resource Management** — Drop trait, `use` binding (opt-in), loan pattern
- **ADR-0006: Allocation Strategy** — stack-first promotion, arena allocators, Allocator attribute, Align attribute
- **ADR-0007: String and Collection Types** — String with SSO, List<T>, Slice<T>, fixed-size arrays, no implicit coercion

### Epics added to tasks.md
- **Epic 8: Memory Ownership & Refcounting** — Ptr<T>, refcount, move semantics, ownership analysis, copy, reuse analysis
- **Epic 9: Resource Management** — Drop trait, use binding, loan pattern APIs
- **Epic 10: String & Collection Types** — String, interpolation, arrays, List<T>, Slice<T>, Align
- **Epic 11: Green Thread Ownership** — green keyword, non-atomic refcount, channels, arena allocator

### Dependency chain
Epic 6 (type system) → Epic 7 (traits) → Epic 8 (ownership) → Epic 9 (Drop/use) → Epic 10 (collections) → Epic 11 (threads + allocators)

### Consistency notes
- ADR-0003 (green threads) proposed `spawn` keyword — now superseded by `green fn arg` syntax from this discussion. ADR-0003 should be updated.
- ADR-0002 (type system) lists "Type classes/traits" as "could have" — Epic 7 promotes this to required (needed for Drop, Copyable).
- Earlier discussion sections proposed `new` for heap allocation — later replaced by compiler-analysed placement with future `<heap>`/`<stack>` tags. The `new` keyword may be repurposed or dropped.
- `Ptr<T>` auto-deref was proposed early — still consistent with later refcounting decisions. Auto-deref is sugar, ownership is tracked by the compiler regardless.
