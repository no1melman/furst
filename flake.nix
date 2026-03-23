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
      in
      {
        devShells = {
          frontend = pkgs.mkShell {
            name = "furst-frontend";
            packages = with pkgs; [
              dotnet-sdk_9
              protobuf
            ];
          };

          backend = pkgs.mkShell {
            name = "furst-backend";
            packages = with pkgs; [
              clang_18
              cmake
              ninja
              protobuf
              abseil-cpp
              gtest
              llvmPackages_18.llvm
              llvmPackages_18.libclang
              clang-tools
              ccache
              dotnet-sdk_9
            ];

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
            packages = with pkgs; [
              # Frontend
              dotnet-sdk_9
              # Backend
              clang_18
              cmake
              ninja
              protobuf
              abseil-cpp
              gtest
              llvmPackages_18.llvm
              llvmPackages_18.libclang
              clang-tools
              ccache
            ];

            shellHook = ''
              export CC=clang
              export CXX=clang++

              publish() {
                local root
                root="$(git rev-parse --show-toplevel)"
                mkdir -p "''${root}/build"

                echo "==> Building backend..."
                cd "''${root}/backend"
                cmake -B build -G Ninja && cmake --build build
                cd "''${root}"

                echo "==> Publishing frontend..."
                dotnet publish "''${root}/frontend/src/main/Furst.Frontend.fsproj" \
                  -c Release -o "''${root}/build/furstc" --nologo -v quiet

                # Create wrapper script
                cat > "''${root}/build/furstc" << 'WRAPPER'
              #!/usr/bin/env bash
              SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
              export PATH="''${SCRIPT_DIR}:''${PATH}"
              dotnet "''${SCRIPT_DIR}/furstc/Furst.Frontend.dll" "$@"
              WRAPPER
                chmod +x "''${root}/build/furstc"

                echo ""
                echo "published to build/:"
                echo "  build/furstc           — furst compiler"
                echo "  build/furstc-backend   — backend (called by furstc)"
                echo ""
                echo "usage: ./build/furstc build examples/hello.fu"
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
