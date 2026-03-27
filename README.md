# Furst

Functional first memory safe language based on LLVM.

## Getting Started

### Nix (recommended)

Requires [Nix with flakes enabled](https://nixos.org/download).

```bash
# full environment (frontend + backend)
nix develop

# frontend only (F#/dotnet + protobuf)
nix develop .#frontend

# backend only (clang, cmake, LLVM, etc.)
nix develop .#backend
```

The **default** shell provides a `publish` command that builds everything into `build/`.

The **backend** shell provides helper commands: `configure`, `build`, `test`, `fmt`, `lint`, `clean`, `rebuild`, `cycle`, `gen-fixtures`, `preflight`. Run any shell to see usage.

### Legacy

```pwsh
./build.ps1
```

Pulls LLVM into `deps/` and runs cmake.

### Tooling

  - CMake
  - CCache
  - Ninja
  - Clang & Clangd
  - C++ 20
