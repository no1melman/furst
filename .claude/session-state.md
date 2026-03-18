# Session Handoff

## What was worked on
Implemented the full lowered AST interchange pipeline: proto schema, Lowering.fs (lambda lifting + flatten), FsoWriter.fs (protobuf serialization with 8-byte FSO header), wired `build` command. Created Furst.Proto C# class lib as workaround for Grpc.Tools not running on NixOS. Upgraded all projects to net9.0. Added 16 integration tests covering lowering and .fso roundtrip.

## Current state
All changes are unstaged/uncommitted on `feature/claude-time`. 47/49 tests pass (2 pre-existing failures). The `build` command works end-to-end: .fu → parse → lower → .fso.

## Next step
C++ backend integration: read .fso files, emit LLVM IR. Start with `find_package(Protobuf)` in CMakeLists.txt, fso_reader, then a basic expression emitter.

## Key decisions
- Furst.Proto is a separate C# class library because Grpc.Tools protoc binary can't run on NixOS (dynamic linking). Proto code is pre-generated via `nix-shell -p protobuf --run "protoc --csharp_out=..."`.
- Lowering produces intermediate F# types (LoweredFunctionDef etc.) rather than going straight to protobuf types — cleaner separation.
- `Inferred` types map to `VOID` in proto since type inference doesn't exist yet.
- Lambda lifting mangles names as `outer$inner` and captures used outer params as extra parameters.
