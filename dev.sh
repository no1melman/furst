#!/usr/bin/env bash
set -euo pipefail

ROOT="$(git rev-parse --show-toplevel)"
cd "${ROOT}/backend"

export CC=clang
export CXX=clang++

configure() {
    case "${1:-}" in
        debug)   cmake -B build -G Ninja -DCMAKE_BUILD_TYPE=Debug "${@:2}" ;;
        release) cmake -B build -G Ninja -DCMAKE_BUILD_TYPE=Release "${@:2}" ;;
        asan)    cmake -B build -G Ninja -DCMAKE_BUILD_TYPE=Debug -DCMAKE_CXX_FLAGS="-fsanitize=address,undefined" "${@:2}" ;;
        *)       cmake -B build -G Ninja "$@" ;;
    esac
}

build()   { cmake --build build "$@"; }
test()    { ctest --test-dir build --output-on-failure "$@"; }
fmt()     { cmake --build build --target format; }
lint()    { cmake --build build --target lint; }
clean()   { rm -rf build; }
rebuild() { clean && configure "$@" && build; }
cycle()   { fmt && build && test; }

gen-fixtures() {
    dotnet test "${ROOT}/frontend" --filter 'GenerateFixtures' --verbosity quiet
}

preflight() { gen-fixtures && cycle; }

publish() {
    rm -rf "${ROOT}/build"
    mkdir -p "${ROOT}/build"

    echo "==> Building backend (furstc)..."
    cmake -B build -G Ninja && cmake --build build
    cp build/furstc "${ROOT}/build/furstc"

    echo "==> Publishing furstp..."
    dotnet publish "${ROOT}/frontend/src/furstp/Furst.Furstp.fsproj" \
        -c Release -r linux-x64 -o "${ROOT}/build" \
        -p:DebugType=none -p:DebugSymbols=false --nologo -v quiet

    echo "==> Publishing furst..."
    dotnet publish "${ROOT}/frontend/src/furst/Furst.Cli.fsproj" \
        -c Release -r linux-x64 -o "${ROOT}/build" \
        -p:DebugType=none -p:DebugSymbols=false --nologo -v quiet

    echo ""
    echo "published to build/:"
    echo "  build/furst    — furst CLI (build, run, new, ...)"
    echo "  build/furstp   — frontend compiler (.fu -> .fso)"
    echo "  build/furstc   — backend compiler (.fso -> native)"
}

# Frontend commands
fe-test()  { cd "${ROOT}/frontend" && dotnet test "$@"; }
fe-build() { cd "${ROOT}/frontend" && dotnet build "$@"; }

cmd="${1:-help}"
shift || true

case "$cmd" in
    configure|build|test|fmt|lint|clean|rebuild|cycle|gen-fixtures|preflight|publish|fe-test|fe-build)
        "$cmd" "$@"
        ;;
    help|*)
        echo "usage: dev.sh <command> [args...]"
        echo ""
        echo "backend:"
        echo "  configure [mode]   cmake configure (debug|release|asan)"
        echo "  build [args]       build all targets"
        echo "  test [args]        run tests"
        echo "  fmt                clang-format"
        echo "  lint               clang-tidy"
        echo "  clean              rm -rf build"
        echo "  rebuild [mode]     clean + configure + build"
        echo "  cycle              fmt + build + test"
        echo "  gen-fixtures       regenerate .fso test fixtures"
        echo "  preflight          gen-fixtures + cycle"
        echo ""
        echo "frontend:"
        echo "  fe-test [args]     dotnet test"
        echo "  fe-build [args]    dotnet build"
        echo ""
        echo "full:"
        echo "  publish            build everything into build/"
        ;;
esac
