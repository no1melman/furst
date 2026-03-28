---
name: cpp-backend
description: C++23 backend coding standards and workflow for Furst compiler
allowed-tools: Read, Edit, Grep, Bash, Write
---

Read backend/README.md for full design principles, then apply these rules:

## Teaching Mode

The user is learning C++ from a C#/F# background. After writing any C++ code:
- Walk through ~20 lines at a time, then stop and wait for the user to type "next" for the next chunk
- Frame explanations in terms of C#/F# equivalents (e.g. "this is like a record type in F#")
- Repeat explanations for features not yet marked as "learnt" — even if they appear multiple times
- If the user says they've learnt a feature (e.g. "I know pragma once now"), update the learnt list in `backend/CPP_LEARNT.md` and stop explaining that feature in future
- Check `backend/CPP_LEARNT.md` before explaining — skip anything already listed there

## Language

- C++23 standard (`-std=c++23`)
- Smart pointers only — no raw `new`/`delete`
- Use `std::unique_ptr` for ownership, `std::shared_ptr` only when shared ownership is genuinely needed
- Use ``Result<T, E>` (from `result.h`)` for fallible internal operations
- Prefer `const&` parameters, move semantics for transfers
- Use concepts to constrain templates

## Design Principles

- **Composition over inheritance** — use structs with value members, not class hierarchies. Exception: error types may use `std::variant` or a base class where it genuinely simplifies dispatch
- **Convention over configuration** — sensible defaults everywhere. No options/flags unless there's a real second use case
- **No premature abstraction** — write the concrete thing first. Extract only when duplication is proven
- **Value types by default** — prefer structs passed by value or `const&`. Heap allocate (smart pointers) only when ownership transfer or polymorphism requires it
- **Free functions over methods** — unless state is genuinely encapsulated, use free functions in a namespace. Like F# module functions
- **Explicit over clever** — no operator overloading, no implicit conversions, no SFINAE. Use concepts if you need compile-time dispatch
- **Pure functions over side effects** — return new values, don't mutate inputs. If a function takes a non-const reference for performance, comment why
- **Headers are interfaces** — `.h` files show the public contract, nothing else. Implementation details stay in `.cpp`

## Single Entrypoint

All public API goes through `backend.h` → `compile()`. Internal modules (fso_reader, emitter) are implementation details, never exposed.

## Workflow — every change

1. **Write test** for the behavior
2. **Write code** to pass it
3. **Run**: `cmake --build build --target format && cmake --build build && ctest --test-dir build --output-on-failure`
4. Never skip format or test steps

## Style

- `.clang-format` and `.clang-tidy` in backend root are authoritative
- `-Wall -Wextra -Wpedantic -Werror` — no warnings allowed
- LLVM C++ API (`llvm/IR/`, `llvm/Support/`, etc.) — wrap in RAII where possible
- Braces always, even for single-line if/else
- 4-space indent, 100 col limit

## Errors

- Invalid `.fso` input = abort with clear message to stderr
- Backend trusts frontend validation — no redundant type checking
- Use ``Result<T, E>` (from `result.h`)` for internal error propagation, `std::abort` / `std::exit` at boundaries

## Testing

- Test framework: Google Test (from Nix)
- Tests use fixture `.fso` files in `backend/tests/fixtures/`
- No dependency on dotnet or the frontend to run backend tests
- **Test through the entrypoint only** — tests call `compile()`, not internal modules. This keeps tests decoupled from implementation and lets code coverage reveal missing scenarios
- Exception: complex internal logic that genuinely needs isolated testing (rare — justify it)
- No testing internal functions just because they exist

## Naming

- `snake_case`: functions, variables, namespaces
- `PascalCase`: types, structs, classes
- `UPPER_SNAKE`: constants and macros
- Files: `snake_case.cpp`, `snake_case.h`
