# CLAUDE.md

This project is creating a new programming language heavily based on F#. The frontend is currently implemented in F#, and the backend uses LLVM LIR (LLVM Intermediate Representation) for code generation.

in all interactions be extremely concise and sacrifice grammer for the sake of concision

## Versioning

the minor version in `VERSION` tracks checked-off epic tasks in `EPICS.md`. after each task is marked `[x]`, increment the minor version in `VERSION` to match the total count of checked tasks. e.g. 35 checked tasks = `0.35.0`

## Build & Test

all commands must run inside nix devshells to ensure correct toolchain versions. use `nix develop .#<shell> -c bash -c '...'`.

- **frontend** (`nix develop .#frontend`): `dotnet test`, `dotnet build`, `dotnet reportgenerator` etc from the frontend dir
- **backend** (`nix develop .#backend`): shell hooks provide `configure [mode]`, `build`, `test`, `fmt`, `lint`, `clean`, `rebuild`, `cycle`, `gen-fixtures`, `preflight`
- **default** (`nix develop .#default`): `publish` (builds both frontend + backend)

## Plan

at the end of each plan, give a list of unresolved questions to answer, if any. make the questions extremely concise. sacrifice grammer for the sake of concision
