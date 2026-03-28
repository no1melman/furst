# Modules, Namespaces, and the BCL

## Starting Point

Here's where I see the current state and the problem space:

**What you have now:** Libraries work — you can mark functions with `export`, compile to `.a` archives with `.fsi` manifests, and link projects together via `dependencies:` in `furst.yaml`. Multi-file compilation merges sources into a single flat namespace.

**What's missing for a BCL:** Right now every exported function lives in a single flat namespace. If you start building a standard library with, say, `List.map`, `String.length`, `IO.println`, there's no way to scope those names. You'd end up with name collisions, no discoverability, and no way for users to selectively import what they need.

There are a few design axes to explore:

### 1. Module vs Namespace — what's the unit?

F# has both `namespace` (grouping, no values) and `module` (can contain values, functions, types). Some languages merge these into one concept. For Furst, the key question is whether you want:

- **File-is-module** (like OCaml, Rust, Go) — each `.fu` file implicitly defines a module named after the file
- **Explicit module declarations** (like F#, Haskell) — `module List` at the top of a file, potentially multiple modules per file or a module spanning multiple files
- **Namespace-only** (like C#) — just dotted name grouping, no module-as-value semantics

For a BCL, you'll want at minimum `Furst.Collections.List`, `Furst.IO`, `Furst.String` style paths.

### 2. How do users reference things?

Once things are in modules, how do you get at them?

- **Qualified access:** `List.map f xs` — always prefix with module name
- **Open/import:** `open Furst.Collections` then just `List.map`
- **Selective import:** `from Furst.Collections import List` or `open Furst.Collections (List, Map)`
- **Auto-open:** some BCL modules (like `Prelude` or core operators) are always in scope

### 3. How does this interact with the existing library system?

Currently a library project produces `lib{name}.a` + `lib{name}.fsi`. The manifest is just `functionName paramCount`. For modules to work, the manifest format needs to grow — it needs to carry module paths, and eventually types. The question is whether module structure maps 1:1 to project structure (each library is a module) or whether a single library can export multiple modules.

### 4. Proto/.fso implications

The protobuf format currently has `FurstModule` with a flat list of `TopLevel` definitions. Export status is actually *lost* during serialization right now — both `TopFunction` and `TopExportedFunction` map to the same `FunctionDef`. This needs fixing regardless, but modules would add a `module_path` or similar field.

### Unresolved
- File-is-module vs explicit `module` declaration vs both?
- Should the BCL ship as one library or many (one per domain)?
- Does the module system need to exist before Epic 6 (type system) or can they be parallel?
- What's the minimum viable module system that unblocks BCL work?

What's your instinct on the file-is-module vs explicit declaration question? And do you have a feel for what the BCL's first modules would be?

## lib + mod: Two-Level Scoping

> **User:** I like the idea of being able to do `mod List ... impl... mod Map ... impl...` in a single file. That gives a lot of flexibility if devs want to add additional behaviour to a List module that will appear in the intellisense, and keep files to a minimum if the mods are really small. I don't think we need namespaces at all... but can we have maybe `lib` and `mod`? So if we want `lib Furst` and then `mod List`, that gives us scoped modules.

So the mental model would be:

```furst
lib Furst.Collections

mod List
    export let map f xs = ...
    export let filter f xs = ...

mod Map
    export let empty = ...
    export let add key value m = ...
```

And consumers would write:

```furst
open Furst.Collections

let result = List.map f xs
let m = Map.empty
```

Or fully qualified: `Furst.Collections.List.map`

This is appealing for several reasons:

**1. `lib` maps naturally to your existing project system.** A `lib` declaration is essentially what the project already *is* — a `furst.yaml` with `type: library`. The `lib` keyword just gives it a name inside the source code that matches the package identity. You could even enforce that the `lib` declaration matches the project name in `furst.yaml`, or derive it automatically.

**2. `mod` is the unit of organisation within a library.** Multiple mods per file means small related modules (like `List` and `List.Unsafe` or `Map` and `SortedMap`) can live together. But a mod can also span multiple files in the `sources:` list — if two files both say `mod List` under the same `lib`, their definitions merge.

**3. No namespace concept needed.** The dotted path in `lib Furst.Collections` gives you hierarchical grouping without a separate `namespace` keyword. The `lib` *is* the namespace.

### Questions this raises

**Should `lib` be declared in source or derived from `furst.yaml`?** There's a tension:
- If it's in source: every `.fu` file needs a `lib` line at the top (boilerplate, but explicit)
- If it's from yaml: cleaner files, but the source alone doesn't tell you its identity
- Hybrid: yaml is authoritative, `lib` declaration in source is optional but validated against yaml if present

**What about executables?** An executable project (`type: executable`) presumably doesn't need `lib`. Do its functions live in an implicit anonymous module? Can an executable have `mod` blocks for internal organisation, or is everything top-level?

**Mod merging across files — is that desirable?** If `src/list.fu` and `src/list_extras.fu` both declare `mod List`, do they merge? F# does this with `namespace` (multiple files can contribute to the same namespace). It's powerful for BCL organisation but adds complexity to the compiler — you need to collect all mod declarations across files before building the symbol table.

**Does `mod` nest?** Could you write:

```furst
mod Collections
    mod List
        export let map f xs = ...
```

Or is nesting only via the `lib` dotted path? I'd suggest keeping `mod` flat (no nesting) and using `lib` dotted paths for hierarchy. Simpler to implement, and nested modules add a lot of complexity for marginal benefit.

### Unresolved
- `lib` in source vs derived from yaml vs hybrid?
- Can executables use `mod`?
- Should two files be able to contribute to the same `mod`?
- Are mods flat (no nesting) or can they nest?

What's your thinking on where `lib` lives — in the source file or purely in the yaml?

## Lib Hierarchy and the YAML Root

> **User:** I think libs should roll up, so if you had `lib Furst.Collections.Generic` and you put stuff there, that's all you would get. But if you defined `lib Furst.Collections` later and put some mods there, that would then mean that both those namespaces had mods. But also, if you defined lib at the top like `lib Collections` (the most top level lib name would come from `library: name: Furst version: 0.1.0` in the yaml) then any further libs in that file would be nested under it maybe... or you could define `lib Collections.Generic`. It depends if Collections wants to define some modules...

So the yaml gives you the root identity:

```yaml
library:
  name: Furst
  version: 0.1.0
```

And then in source files, `lib` declarations are relative to that root. So `lib Collections` actually means `Furst.Collections` in the fully qualified world. This is nice — it means your source files don't repeat "Furst" everywhere, and if you ever rename the package, you change it in one place.

Then you've got two possible approaches for how libs work within a file:

### Approach A: All `lib` paths are relative to yaml root, independently

```furst
// file: src/collections.fu
// yaml root: Furst

lib Collections
mod List
    export let map f xs = ...

lib Collections.Generic
mod List
    export let map f xs = ...  // generic version
```

This produces `Furst.Collections.List.map` and `Furst.Collections.Generic.List.map`. Each `lib` statement is independent and always relative to the yaml root. Simple, predictable.

### Approach B: First `lib` sets scope, subsequent libs nest under it

```furst
// file: src/collections.fu
// yaml root: Furst

lib Collections           // → Furst.Collections

mod List                  // → Furst.Collections.List
    export let map f xs = ...

lib Generic               // → Furst.Collections.Generic (nested under Collections)
mod List                  // → Furst.Collections.Generic.List
    export let map f xs = ...
```

This is more compact but introduces a scoping rule that's implicit and file-dependent. The meaning of `lib Generic` changes based on what came before it in the file. That feels fragile — reordering code or splitting files changes semantics.

### My instinct: Approach A is safer

Approach B saves a few characters but creates a class of bugs where moving code between files changes its meaning. With Approach A, every `lib` line is self-describing. You can grep the codebase for `lib Collections.Generic` and find everything that contributes to that path. With B, you'd need to read the file from the top to know what `lib Generic` actually means.

### The roll-up question

The roll-up behaviour you described is interesting and I think it's the right call. `lib Collections.Generic` existing doesn't require `lib Collections` to exist — they're independent scoping paths that happen to share a prefix. But when a consumer writes `open Furst.Collections`, they'd see the mods defined directly in `Collections`, and `Generic` would appear as a sub-path they could drill into.

This means `lib` isn't really a container you instantiate — it's more like a path you attach modules to. Multiple files, even across different projects, could contribute to the same lib path. That's powerful for the BCL: the core `Furst` package defines `Furst.Collections.List`, and a community package could define `Furst.Collections.PriorityQueue` by using the same lib path.

But wait — should third-party packages be able to contribute to the `Furst.*` path? That could lead to confusion ("is this BCL or community?"). Maybe the yaml root name acts as a boundary: only the package named `Furst` can define `lib` paths, and other packages use their own root. So a community package with `library: name: FurstExtras` would produce `FurstExtras.Collections.PriorityQueue`.

### Unresolved
- Approach A (all libs relative to yaml root) vs Approach B (first lib scopes subsequent ones)?
- Can different packages contribute mods to the same lib path, or is the yaml root a hard boundary?
- If `lib Collections` has no mods of its own (it's just a parent for `Collections.Generic`), does it need to be declared anywhere, or does it exist implicitly?
- Does `open Furst.Collections` pull in sub-paths like `Generic` as accessible names, or only direct mods?

What's your feel on the third-party boundary question? And are you leaning A or B?

## Module Extensions (Cross-Package Augmentation)

> **User:** I think people should be able to attach to mods, so if you have `List.map` in Furst, then someone can attach a `List.mean` and the user can import their library. So `import CallumsListExtensions` would then mean in that file all `List` mod gets it because the extensions file would have done something... I think F# has this functionality in some way.

Yes — F# has exactly this with **type extensions**. There are two kinds:

- **Intrinsic extensions** — defined in the same file/namespace as the type, compiled as real members
- **Optional extensions** — defined elsewhere, even in different assemblies. They only become visible when you `open` the namespace that defines them. They show up in intellisense once opened.

What you're describing is the module-level equivalent: **module extensions** or **module augmentation**. The mechanic would be something like:

```furst
// CallumsListExtensions library
// yaml root: CallumsListExtensions

extend mod Furst.Collections.List
    export let mean xs = ...
    export let median xs = ...
```

Then in consumer code:

```furst
open Furst.Collections       // gets List.map, List.filter etc.
open CallumsListExtensions    // now List.mean and List.median also appear

let avg = List.mean myList    // works because the extension is in scope
```

This is powerful and gives you a very natural extension story. A few things to think through:

### How does resolution work?

When the compiler sees `List.mean`, it needs to check:
1. The original `Furst.Collections.List` module — not there
2. Any `extend mod` declarations that are in scope via `open` — found in `CallumsListExtensions`

This means the compiler maintains a **layered view** of each module: the base definition plus any extensions brought in by `open` statements. The extensions don't modify the original module — they're overlays that only exist in the current compilation unit's view.

### What `extend mod` means for the manifest

The `.fsi` manifest for `CallumsListExtensions` would need to express "these functions attach to `Furst.Collections.List`" rather than defining a new module. Something like:

```
extend Furst.Collections.List mean 1
extend Furst.Collections.List median 1
```

The consuming compiler reads this and knows: when this library is `open`ed, add these symbols to the `List` module's visible surface.

### Conflict resolution

What happens if two extension libraries both define `List.mean`? Options:
- **Last `open` wins** — fragile, order-dependent
- **Compiler error** — ambiguous symbol, user must qualify: `CallumsListExtensions.List.mean` vs `OtherLib.List.mean`
- **Explicit disambiguation required** — if a conflict exists, you must use the full path

The compiler error approach is the safest — it's what F# does with ambiguous optional extensions, and it prevents silent surprises.

### Can extensions add to mods in the same package?

Within the same library, if two files both say `mod List`, we said earlier they'd merge. That's basically an intrinsic extension — same package, definitions merge. The `extend mod` keyword would only be needed for cross-package augmentation. This keeps the distinction clean:

- **Same package:** `mod List` — merges naturally
- **Different package:** `extend mod Furst.Collections.List` — explicit augmentation

### What you CAN'T do with extensions

Worth deciding early: can extensions see the private/non-exported internals of the module they're extending? Almost certainly not — they should only be able to build on the public API. This keeps the module's encapsulation intact and means extensions are purely additive.

### Unresolved
- `extend mod` as the keyword, or something else? (`augment`, `patch`, `add to`?)
- Can extensions override/shadow existing functions, or strictly additive only?
- Should extensions be able to add types/values to a mod, or only functions?
- Do extensions need to declare which package they're extending as a dependency, or is the fully qualified path enough?

This is a great feature for ecosystem growth — it means the BCL can ship lean and community packages can enrich it without forking. What's your take on the conflict resolution approach?

## Decisions and Visibility

> **User:** I'm torn between ergonomics and explicitness. I fall in the camp of probably throw if there is ambiguous, but that would cause a problem if you were using some functions from 2 libraries that had nothing to do with extending List but both these libraries had overrides that you may not even want to use... I dunno if this is a real concern. But I think at this point we should put these questions onto the back burner. Let's go with lib and mod, no nesting libs in files, it's all flat. Mods probably don't need nesting (that's what extends is for I guess). Open should only bring in what is under the lib specified, nothing deeper. I'm not sure now where to go — export or have private as a keyword... everything public by default?

### Decisions Locked In

So we've landed on:

1. **`lib` + `mod`, two keywords, two levels.** `lib` is the package path (relative to yaml root), `mod` is the organisational unit within it.
2. **No nesting of `lib` within files** — all `lib` declarations are flat, relative to the yaml root (Approach A).
3. **Mods are flat** — no nested mods. Cross-package augmentation via `extend mod` (details deferred).
4. **`open Furst.Collections`** brings in only direct mods under that lib path — not sub-paths like `Collections.Generic`. You'd need `open Furst.Collections.Generic` separately.
5. **Libs roll up** — `lib Collections.Generic` doesn't require `lib Collections` to exist. They're independent paths.

### The Visibility Question: export vs private

This is a genuinely interesting design fork. Let's lay out both sides properly.

#### Option 1: Private by default, `export` to expose (current approach)

```furst
mod List
    let internal_helper xs = ...       // private — only visible within this mod
    export let map f xs = ...          // public — visible to consumers
    export let filter f xs = ...
```

**Pros:**
- Safe by default — you can't accidentally leak implementation details
- Forces authors to think about their public API surface
- Familiar from Rust (`pub`), OCaml (`.mli` files), and many other systems

**Cons:**
- Verbose for libraries where most things *are* public (like a BCL)
- The BCL will be almost entirely `export let` — lots of noise for little signal

#### Option 2: Public by default, `private` to hide

```furst
mod List
    private let internal_helper xs = ...   // hidden
    let map f xs = ...                     // public
    let filter f xs = ...
```

**Pros:**
- Cleaner for libraries — most definitions are API surface, you only annotate the exceptions
- Less ceremony for the common case in a BCL
- Matches F# modules (where `let` bindings in a module are public unless marked `internal` or `private`)

**Cons:**
- Easy to accidentally expose things — a quick helper function is immediately public API
- Refactoring risk: adding a function you intended as temporary is now part of your contract

#### Option 3: Visibility depends on context

```furst
// In a mod block inside a lib: public by default
lib Collections
mod List
    let map f xs = ...              // public (you're writing a library API)
    private let helper xs = ...     // explicitly hidden

// In an executable or outside a mod: private by default
let main = ...                      // not exported, it's an app
```

This is pragmatic — library modules are mostly API surface, so default to public there. Application code is mostly internal, so default to private there. But it creates a rule that's context-dependent, which can surprise people.

#### My lean: Option 2 (public by default) with a twist

Given that `mod` blocks exist specifically to define an API surface — that's their purpose — it makes sense that things inside a mod are public by default. You're putting something in a `mod` *because* you want it accessible. If you wanted it private, you'd define it outside the mod or mark it explicitly.

This also means you could have file-level private helpers that sit outside any `mod` block:

```furst
lib Collections

let shared_helper xs = ...    // not in a mod — file-private, available to mods in this file

mod List
    let map f xs = shared_helper (transform xs)   // public
    private let sort_impl xs = ...                 // explicitly private within mod
```

### Unresolved
- If public-by-default in mods, what about top-level definitions outside any mod — are they file-private? Package-private? Invisible?
- Does `private` mean "private to the mod" or "private to the file"?
- Should there be a `internal` level (visible within the package but not to consumers)?

What's your gut on this? The F# alignment (public by default in modules) feels natural for Furst, but the "accidentally public" risk is real.

## Scoping, Types, and Implicit Mods

> **User:** `lib` should only be for creating libraries in my opinion, your API doesn't need it and I think something like `mod Api.Types` could be a better way to achieve. I also don't know if types should be scoped to just a mod, maybe they can be scoped to libs but declaration order matters, same with file order just like F# in my opinion... I think that if you don't specify a mod then it should take it from the file name and directory structure from root. Everything public by default, everything can live in a mod, except types maybe, they can live everywhere (but with auto mod creation, is that an issue). Mods should also be flat and maybe later we can have something like `=` to dictate a nested mod (talking about whitespace here). I think you can have private scoped to a mod, so if you want a bunch of functions that don't leak out a library then it's all good.

### Decisions from this round

#### 1. `lib` is library-only

`lib` only appears in library projects (`type: library` in yaml). Executables never use it. This makes `lib` a clear signal: "I'm defining a reusable package API." If you're writing an app, you're writing an app — you don't need package semantics.

#### 2. `mod` is universal — both libs and executables can use it

An API project can do `mod Api.Types`, `mod Api.Handlers` for internal organisation. This is nice — it means `mod` is the one organisational primitive, and `lib` is just the publication layer on top.

Wait — you said mods are flat but then `mod Api.Types` has a dot. Let me clarify: are you thinking dotted mod names are allowed as a shorthand for what would otherwise be nesting? So `mod Api.Types` is a single flat mod whose name happens to contain a dot, not a `Types` mod nested inside an `Api` mod? That's how I'd read it — the dot is just part of the name, like a convention, not structural nesting. The compiler treats `Api.Types` as an atomic module name.

#### 3. Implicit mod from file/directory structure

This is a big one. If a file doesn't declare a `mod`, the compiler infers one from the path:

```
src/
├── main.fu          → no mod (top-level, executable entry)
├── handlers.fu      → mod Handlers (implicit)
├── models/
│   ├── user.fu      → mod Models.User (implicit)
│   └── order.fu     → mod Models.Order (implicit)
```

This gives you a Go/Rust-like convention where project structure *is* module structure, but you can override it with an explicit `mod` declaration. Files that declare `mod` explicitly ignore the path-based default.

For libraries, the yaml root prefixes everything:
```
// yaml root: Furst
src/
├── collections/
│   ├── list.fu      → Furst.Collections.List (implicit)
│   └── map.fu       → Furst.Collections.Map (implicit)
```

So you could build the entire BCL without ever writing a `mod` line — just organise your files.

#### 4. Types live everywhere, not scoped to mods

This is the most provocative decision. If types are at the lib/file level rather than mod-scoped:

```furst
lib Collections

// Type lives at the lib level, visible to all mods in this lib
type Option<'a> =
    | Some of 'a
    | None

mod Option
    let map f opt = ...       // operates on the type above
    let bind f opt = ...

mod List
    let tryHead xs = ...      // returns Option<'a>, can reference the type
```

This means types are like shared vocabulary for the library — they're declared once and available across all mods within that lib. Mods then provide the *functions* that operate on those types. That's actually a very clean separation: types define *what things are*, mods define *what you can do with them*.

**Declaration order matters** (F# style) — a type must be declared before it's used. File order in `sources:` matters too. This is a strong constraint but it prevents circular dependencies and makes the compiler simpler. F# developers are used to this and it forces good architecture.

**But here's a question:** if types live at the lib level, what happens when a consumer does `open Furst.Collections`? Do they get the types automatically? They'd need to — otherwise `Option.map` works but you can't construct a `Some 42`. So `open` on a lib path brings in both:
- All mods under that path (as qualified names: `List.map`, `Option.bind`)
- All types declared at that path level (`Option`, etc.)

#### 5. Public by default, `private` scoped to mod

```furst
mod List
    let map f xs = ...              // public
    let filter f xs = ...           // public
    private let partition_impl = ... // private to this mod

// Outside any mod — if implicit mod from filename, still public
// If you want truly file-private helpers:
private let file_helper x = ...     // private, not visible outside this file
```

`private` means "doesn't leak out of this mod." For library projects, that means it's not in the public API. For executables, it means other mods in the project can't see it.

#### 6. The `=` idea for nested mods (future)

You mentioned maybe using `=` for nesting later. I think you mean something like:

```furst
mod Collections =
    mod List
        let map f xs = ...
```

Where the `=` and indentation creates a nested scope? This is interesting as a future extension but I agree it should wait. Flat mods + dotted names (`mod Collections.List`) cover the same ground with less complexity for now.

### One tension to flag

You said types can live everywhere and mods are auto-created from filenames. What happens here:

```
src/list.fu    → implicit mod List
```

```furst
// src/list.fu — no explicit mod declaration

type ListNode<'a> = ...    // where does this type live?

let map f xs = ...         // this goes in mod List (from filename)
```

Is the type in the implicit `List` mod? Or at the lib level above it? If everything in the file goes into the implicit mod, then types *are* scoped to mods in this case. If types are always lifted out to the lib scope, the compiler needs a rule like "type declarations are hoisted out of their containing mod to the lib scope."

I think the cleanest answer is: **if a file has an implicit mod, types declared in it belong to that mod** — but they're re-exported at the lib level too, so consumers can access them either way. But this might be over-engineering it for now.

### Unresolved
- Are dotted mod names (`mod Api.Types`) a flat name with dots, or structural nesting?
- When a file has an implicit mod (from filename), do types in that file belong to the mod or get hoisted to the lib level?
- Should `private` also work at the lib level (package-private, visible to all mods in the lib but not to consumers)?
- How does `main.fu` work — does it get an implicit mod, or is it special-cased as the entry point?

What's your thinking on where types land when you've got implicit mods from filenames?

## Types in Mods

> **User:** Types can live in mods as well.

That simplifies things a lot. So types are just another thing that can live in a mod — same as functions, same visibility rules. No special hoisting, no lib-level type scope.

```furst
mod List
    type Node<'a> =           // public, lives in List mod
        | Cons of 'a * Node<'a>
        | Nil

    let map f xs = ...         // public
    private let helper = ...   // private to mod
```

Consumer writes `List.Node` to reference the type, `List.map` to call the function. `open` brings both in.

With implicit mods from filenames, it just works — `src/list.fu` becomes `mod List`, and everything in the file (types and functions) belongs to that mod. No ambiguity about where types live.

This also means the tension flagged earlier goes away entirely. Types follow the same rules as everything else: they live where they're declared, they're public by default, and `private` keeps them mod-internal.

The only remaining question from the previous batch is really about `main.fu` — does it get an implicit mod or is it special-cased? My instinct is it's special-cased: the entry file defined in `furst.yaml` (`entry: src/main.fu`) has no implicit mod. Its top-level definitions are just the program. You *can* declare mods in it if you want internal organisation, but there's no wrapping `mod Main` around everything.

### Unresolved
- Is `main.fu` (the entry file) special-cased with no implicit mod, or does it get `mod Main`?

## Entry Point and Consistency

> **User:** Let's be consistent, everything can get automatically wrapped, and I think for entry point project types, there should always be one and only one `main` function declared at the root level at the bottom of the ordering. Because it should be the last thing out, right.

Clean rule. So:

- **Every file gets an implicit mod from its path** — no exceptions, including `main.fu` → `mod Main`
- **Entry point projects must have exactly one `main` function** declared at the root level (outside any mod), at the bottom of the file ordering
- **`main` is the only thing that lives outside a mod** in the entire language — it's the program's entry point, and it must be last because declaration order matters

```furst
// src/helpers.fu → mod Helpers (implicit)
let greet name = printfn "Hello, %s" name

// src/main.fu → mod Main (implicit, but main itself is root-level)
mod Main
    let setup = ...

let main = Main.setup; Helpers.greet "world"   // root-level, must be last
```

Actually wait — there's a tension. If `main.fu` gets an implicit `mod Main`, then is `main` the function *inside* that mod or *outside* it? You said "at the root level" which I read as: `main` is the one special function that sits outside all mods, at the root. The file still gets its implicit mod for any other definitions, but `main` itself is hoisted out.

Or simpler: maybe the rule is just that the `main` function is always at the root scope regardless of what file it's in, and the compiler enforces there's exactly one across all files. The file's implicit mod wraps everything *except* `main`.

That feels slightly magical though. An alternative: `main` lives inside its mod like everything else (`Main.main`), and the compiler/linker just knows to look for a function literally called `main` in the last file's implicit mod as the entry point. No hoisting, no special scoping — just a naming convention plus ordering.

### Unresolved
- Is `main` a special root-level function outside all mods, or a regular function the compiler knows to look for by name?

## Entry Point Clarification

> **User:** What I meant with root level was project physical level. `main` functions can be defined anywhere and anyhow, but it will be looked for in the last file at the root of the project scope.

So `main` isn't special syntactically at all — it's just a function called `main`. The compiler knows to look for it in the last file in the source ordering, at the top level of that file (which means inside that file's implicit mod).

So if your `sources:` list ends with `src/main.fu`:

```furst
// src/main.fu → implicit mod Main

let setup = ...
let main = setup; 0    // this is the entry point, just because it's called "main" in the last file
```

The compiler finds `Main.main` and uses it as the entry point. No special keyword, no root-level hoisting, no magic. Just a convention: **last file, function named `main`**.

This means:
- `main` is inside `mod Main` like everything else — fully consistent
- The compiler enforces: in an executable project, the last file in `sources:` must contain a function called `main`
- If someone puts a `main` function in a different file, it's just a regular function — no conflict, no entry point semantics
- Libraries don't need this check at all

## Closing Out Remaining Questions

> **User:** BCL can be many, just like dotnet. I think the module system should probably exist now. The root name for a library comes from yaml, I guess we could make the file system create the lib name, unless you use `lib` to override it.

1. **BCL ships as many libraries** — `Furst.Collections`, `Furst.IO`, `Furst.Text`, etc. Just like .NET's System.* packages. Each is its own project with its own `furst.yaml`.

2. **Module system is next** — before or alongside Epic 6 (type system). Makes sense, you need scoping before you can build a meaningful type system on top.

3. **Lib name comes from yaml + file system, with `lib` override:**
   - The yaml `library: name:` gives the root (e.g., `Furst`)
   - Directory structure under `src/` builds the lib path automatically — so `src/collections/list.fu` in a package named `Furst` gives you `Furst.Collections.List`
   - `lib` keyword in a source file overrides the filesystem-derived path for that file

So the BCL could literally be just files in folders with no `lib` declarations at all:

```
furst-collections/
├── furst.yaml          # library: name: Furst.Collections
├── src/
│   ├── list.fu         → Furst.Collections.List (from filesystem)
│   └── map.fu          → Furst.Collections.Map (from filesystem)
```

But if you wanted to put multiple lib paths in one file, you'd use explicit `lib`:

```furst
// src/misc.fu
// filesystem would give: Furst.Collections.Misc

lib Collections          // override: this file contributes to Furst.Collections
mod SortedSet
    let empty = ...

lib Collections.Concurrent   // also contributes here
mod ConcurrentMap
    let empty = ...
```

## Final Decisions: Additivity and Shadowing

> **User:** Behind the scenes dotted should build up a hierarchy if necessary. `mod Furst.Collections` will be additive to any existing scopes called `Furst.Collections`, even if it's across compiled lib files (aka different projects). Everything should be additive and shadowing should be strictly disallowed for everything in the language. Clashes should probably result in compile time error. Two files can contribute to mod, even two files can contribute to lib as long as lib is defined in the second one as the convention would take over giving it the wrong name.

Final decisions:

1. **Dotted names are structural** — `mod Furst.Collections` creates/extends a hierarchy, not a flat name with dots. The compiler builds a tree behind the scenes.

2. **Everything is additive** — multiple files, even across different compiled projects, can contribute to the same mod or lib path. Definitions merge.

3. **No shadowing, ever** — if two things define the same symbol at the same path, it's a compile-time error. No implicit overriding, no last-definition-wins. This is a language-wide rule.

4. **Two files can contribute to the same mod** — but if the second file relies on filesystem convention for its mod name, it would get the wrong name (from its filename). So the second file must use an explicit `mod` declaration to target the right mod. First file can use convention, second must be explicit.

## Finalisation

### Produced
- **ADR-0008:** Module System (lib + mod) — `docs/adr/ADR-0008-module-system.md`
- **Epic 5b:** Module System — added to `EPICS.md` (15 tasks)
- EPICS.md moved from `backend/` to project root

### Deferred to future discussions
- `extend mod` cross-package augmentation (keyword, conflict resolution, visibility)
- Nested mods via `=` syntax
- `internal` visibility level (package-private)
- Prelude / auto-open modules for BCL
