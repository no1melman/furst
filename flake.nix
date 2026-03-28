{
  description = "Furst programming language — frontend (F#/dotnet) and backend (C++/LLVM)";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = { self, nixpkgs, flake-utils }:
    flake-utils.lib.eachDefaultSystem (system:
      let
        pkgs = import nixpkgs { inherit system; };

        # Pinned package versions
        dotnet    = pkgs.dotnet-sdk_10;
        clang     = pkgs.clang_20;
        llvm      = pkgs.llvmPackages_20.llvm;
        libclang  = pkgs.llvmPackages_20.libclang;

        # Shared package sets
        frontendPkgs = [
          dotnet
          pkgs.protobuf
        ];
        backendPkgs = [
          clang
          llvm
          libclang
          dotnet
        ] ++ (with pkgs; [
          cmake
          ninja
          protobuf
          abseil-cpp
          gtest
          clang-tools
          ccache
        ]);
      in
      {
        devShells = {
          frontend = pkgs.mkShell {
            name = "furst-frontend";
            packages = frontendPkgs;
          };

          backend = pkgs.mkShell {
            name = "furst-backend";
            packages = backendPkgs;

            shellHook = ''
              export CC=clang
              export CXX=clang++

              configure() {
                case "''${1:-}" in
                  debug)   cmake -B build -G Ninja -DCMAKE_BUILD_TYPE=Debug "''${@:2}" ;;
                  release) cmake -B build -G Ninja -DCMAKE_BUILD_TYPE=Release "''${@:2}" ;;
                  asan)    cmake -B build -G Ninja -DCMAKE_BUILD_TYPE=Debug -DCMAKE_CXX_FLAGS="-fsanitize=address,undefined" "''${@:2}" ;;
                  *)       cmake -B build -G Ninja "$@" ;;
                esac
              }

              build() { cmake --build build "$@"; }
              test()  { ctest --test-dir build --output-on-failure "$@"; }
              fmt()   { cmake --build build --target format; }
              lint()  { cmake --build build --target lint; }
              clean() { rm -rf build; }
              rebuild() { clean && configure "$@" && build; }
              cycle() { fmt && build && test; }
              gen-fixtures() {
                local root
                root="$(git rev-parse --show-toplevel)"
                dotnet test "''${root}/frontend" --filter 'GenerateFixtures' --verbosity quiet
              }
              preflight() { gen-fixtures && cycle; }

              echo ""
              echo "furst-backend commands:"
              echo "  configure [mode]   cmake configure"
              echo "    configure              default (no build type)"
              echo "    configure debug        -DCMAKE_BUILD_TYPE=Debug"
              echo "    configure release      -DCMAKE_BUILD_TYPE=Release"
              echo "    configure asan         Debug + AddressSanitizer + UBSan"
              echo "  build [args]       build all targets       (build --target furst_backend)"
              echo "  test [args]        run tests               (test -R Smoke)"
              echo "  fmt                clang-format all src"
              echo "  lint               clang-tidy all src"
              echo "  clean              rm -rf build"
              echo "  rebuild [mode]     clean + configure + build"
              echo "  cycle              fmt + build + test"
              echo "  gen-fixtures       regenerate .fso test fixtures from frontend"
              echo "  preflight          gen-fixtures + fmt + build + test"
              echo ""
            '';
          };

          # Default shell has everything
          default = pkgs.mkShell {
            name = "furst";
            packages = frontendPkgs ++ backendPkgs;

            shellHook = ''
              export CC=clang
              export CXX=clang++

              publish() {
                local root
                root="$(git rev-parse --show-toplevel)"
                rm -rf "''${root}/build"
                mkdir -p "''${root}/build"

                echo "==> Building backend (furstc)..."
                cd "''${root}/backend"
                cmake -B build -G Ninja && cmake --build build
                cp build/furstc "''${root}/build/furstc"
                cd "''${root}"

                echo "==> Publishing furstp..."
                dotnet publish "''${root}/frontend/src/furstp/Furst.Furstp.fsproj" \
                  -c Release -r linux-x64 -o "''${root}/build" \
                  -p:DebugType=none -p:DebugSymbols=false --nologo -v quiet

                echo "==> Publishing furst..."
                dotnet publish "''${root}/frontend/src/furst/Furst.Cli.fsproj" \
                  -c Release -r linux-x64 -o "''${root}/build" \
                  -p:DebugType=none -p:DebugSymbols=false --nologo -v quiet

                echo ""
                echo "published to build/:"
                echo "  build/furst    — furst CLI (build, run, new, ...)"
                echo "  build/furstp   — frontend compiler (.fu -> .fso)"
                echo "  build/furstc   — backend compiler (.fso -> native)"
                echo ""
                echo "usage: ./build/furst build"
              }

              echo ""
              echo "furst commands:"
              echo "  publish    build frontend + backend into build/"
              echo ""
            '';
          };
        };
      });
}
