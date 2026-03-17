# Session Handoff

## What was worked on
Added CLI (`Cli.fs`) with `help`, `lex`, `ast`, `check`, `build` (stub) commands. Rewrote `Program.fs` as entrypoint. Added proper indented tree printer for `ast` command. Removed `<!>` debug operator noise from parser output.

## Current state
CLI fully working via `dotnet exec` (NixOS dynamic linking prevents `dotnet run` directly). 31/33 tests pass, 2 pre-existing failures unrelated to CLI work.

## Next step
Design the frontend→backend interchange format. CLI should handle the full pipeline (`build` calls frontend then invokes backend). Need a binary serialization format (protobuf was discussed previously) to pass AST from F# frontend to C++ LLVM backend. Also need to generate test fixture files so the backend can be developed/tested independently.

## Key decisions
- No project flake.nix — home-manager already provides dotnet + rider + neovim
- `<!>` debug operator neutered to identity function (kept operator so call sites don't break)
- CLI owns the full compile pipeline — not just outputting AST for external consumption
- Interchange format TBD: protobuf likely, binary preferred over JSON for size
