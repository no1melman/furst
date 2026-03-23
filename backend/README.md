# Furst Backend

C++23 backend that reads `.fso` (lowered AST protobuf) files and emits LLVM IR.

## Design Principles

### 1. .fso is the contract
Backend reads `.fso` files only. No coupling to F# types or frontend internals.

### 2. C++23 with smart pointers
Use `std::unique_ptr` and `std::shared_ptr` — no raw `new`/`delete`. Use modern C++23 features: `std::expected`, `std::print`, structured bindings, concepts where they clarify intent.

### 3. Single entrypoint interface
One public function is the entire API surface:

```cpp
// backend.h
struct CompileResult {
    bool success;
    std::string output_path;   // on success: path to .o / .ll
    std::string error_message; // on failure
};

CompileResult compile(const std::string& fso_path, const CompileOptions& options);
```

This is the seam for future in-memory interop (swap file path for buffer).

### 4. LLVM C++ API
Use the native C++ API (`llvm/IR/`, `llvm/Support/`, etc.). We're writing C++23 — no reason to drop to the C bindings. Wrap LLVM types in RAII/smart pointers where the API allows.

### 5. Single-pass emit
Read .fso → walk lowered AST → emit IR. No intermediate representation unless forced.

### 6. Errors are fatal
Invalid .fso = abort with clear message. Frontend owns validation; backend trusts input.

### 7. Testable without the frontend
Hand-crafted or fixture `.fso` files for backend tests. No dotnet dependency in backend test suite.

### 8. Minimal dependencies
protobuf, LLVM, libc++. That's it.

### 9. Nix drives the environment
All deps come from `flake.nix`. No vcpkg, no conan. `nix develop` gives everything.

### 10. Build with CMake
Standard `find_package(Protobuf)` and `find_package(LLVM)`. CMake finds what Nix provides.

## Development Workflow

Strict TDD cycle:

```
Write Test → Write Code → Pass Test → Format → Build → Test
```

After **any** change: `format → build → test` before committing.

### Commands

```bash
# enter dev env
nix develop

# format all C++ code
cmake --build build --target format

# build
cmake --build build

# test
ctest --test-dir build --output-on-failure
```

## Formatter: clang-format

Concrete config (`.clang-format` in backend root):

```yaml
BasedOnStyle: LLVM
IndentWidth: 4
ColumnLimit: 100
AllowShortFunctionsOnASingleLine: Inline
AllowShortIfStatementsOnASingleLine: Never
BreakBeforeBraces: Attach
PointerAlignment: Left
SortIncludes: CaseInsensitive
InsertBraces: true
```

- Enforced via `clang-format` (provided by Nix)
- CMake `format` target runs it on all `.cpp`/`.h` files
- Compatible with neovim via `conform.nvim` or `null-ls` — just point at `clang-format`

## Linter: Options

### Concrete (use these)

| Tool | What it does | Nix package |
|------|-------------|-------------|
| **clang-tidy** | Static analysis, modernize checks, bug detection | `clang-tools` |
| **compiler warnings** | `-Wall -Wextra -Wpedantic -Werror` as baseline | built into clang |

### clang-tidy checks (`.clang-tidy` in backend root)

```yaml
Checks: >
  -*,
  bugprone-*,
  cert-*,
  cppcoreguidelines-*,
  misc-*,
  modernize-*,
  performance-*,
  readability-*,
  -modernize-use-trailing-return-type,
  -readability-identifier-length
WarningsAsErrors: '*'
HeaderFilterRegex: 'src/.*\.h$'
```

Key enforced rules:
- `cppcoreguidelines-owning-memory` — no raw owning pointers
- `modernize-use-nullptr` — no `NULL`
- `bugprone-use-after-move` — catch use-after-move
- `performance-unnecessary-copy-initialization` — avoid unnecessary copies
- `readability-braces-around-statements` — always use braces

### Optional (evaluate later)

| Tool | Tradeoff |
|------|----------|
| **cppcheck** | Deeper analysis, slower, some false positives. Worth adding once codebase grows. |
| **include-what-you-use (IWYU)** | Keeps includes minimal. Can be noisy with generated protobuf headers. |
| **sanitizers (ASan, UBSan)** | Runtime checks. Add to test builds: `-fsanitize=address,undefined`. |
| **Sonar** | Heavy. Overkill for this project size. |

## Project Structure

```
backend/
├── CMakeLists.txt
├── .clang-format
├── .clang-tidy
├── src/
│   ├── backend.h          # single public interface
│   ├── backend.cpp
│   ├── fso_reader.h       # .fso deserialization
│   ├── fso_reader.cpp
│   ├── emitter.h          # LLVM IR generation
│   └── emitter.cpp
├── tests/
│   ├── CMakeLists.txt
│   ├── fixtures/           # .fso test files
│   ├── test_fso_reader.cpp
│   └── test_emitter.cpp
└── README.md
```
