$ME="$((pwd).Path)"
$depsSrcDir = "$ME/deps"
$depsBuildDir = "C:/env"

$llvmVersion = "28bdff19e3ad981ef83e372e8c301f1407b82135"
$iwyuVersion="14e9b208914a84fcdf49bf9f5d08897a4b3dc4b8"
$BDWGC_VERSION="release-8_2"

$llvmDir = "llvm"
$bdwgcDir = "bdwgc"
$iwyuDir = "iwyu"
$llvmSrc="$depsSrcDir/$llvmDir.$llvmVersion"
$iwyuSrc="$depsSrcDir/$iwyuDir.$iwyuVersion"
$bdwgcSrc = "$depsSrcDir/$bdwgcDir.$BDWGC_VERSION"

$MUSL_VERSION="v1.2.3"

# $winLibDir="C:/ProgramData/chocolatey/lib/winlibs/tools/mingw64/bin"
# $CC="$winLibDir/clang.exe"
# $CXX="$winLibDir/clang++.exe"

$TARGET="x86_64-pc-linux-musl"
$COMPILER_ARGS="-fno-sanitize=all"

$env:TARGET=$TARGET
$env:COMPILER_ARGS=$COMPILER_ARGS


$env:CC="C:/ProgramData/chocolatey/lib/winlibs/tools/mingw64/bin/gcc.exe"
$env:CXX="C:/ProgramData/chocolatey/lib/winlibs/tools/mingw64/bin/g++.exe"

$linkerPath="C:/ProgramData/chocolatey/lib/winlibs/tools/mingw64/bin"


function Check-Path {
  param(
    [string]$Path
  )

  return Test-Path -Path $Path
}

function Make-Dir {
  param(
    [string]$Path
  )

  if (!(Check-Path -Path $Path)) {
    Write-Host "  Creating: $Path"
    New-Item -ItemType Directory -Path $Path -Force 2>&1 | Out-Null
  }
}

function Clone-Repo {
  param(
    [string]$Dest,
    [string]$Repo,
    [string]$Version
  )

  $itemCount = (Get-ChildItem -Path $Dest -Recurse -Depth 1 | Measure-Object).Count
  if ($itemCount -gt 0) {
    Write-Host "Not Cloning: $Repo... $itemCount"
    return
  }
  Write-Host "Cloning: $Repo"
  Write-Host ""
  git clone --depth=1 $Repo $Dest
  Push-Location $Dest 
  git fetch --depth=1 --filter=tree:0 origin "$Version"
  git reset --hard FETCH_HEAD
  Pop-Location
}

function Get-LLVM {
  Write-Host "Running: Get-LLVM"

  $repo = "https://github.com/llvm/llvm-project.git"

  Clone-Repo -Repo $repo -Dest $llvmSrc -Version $llvmVersion
 }

function Get-IWYU {
  Write-Host "Running: Get-IWYU"

  $repo="https://github.com/include-what-you-use/include-what-you-use.git"

  Clone-Repo -Repo $repo -Dest $iwyuSrc -Version $iwyuVersion
}

function Get-BDWGC {
  Write-Host "Running: Get-BDWGC"

  $repo="https://github.com/ivmai/bdwgc.git"

  Clone-Repo -Repo $repo -Dest $bdwgcSrc -Version $BDWGC_VERSION
}

function Build-Gen {
  Write-Host "Running: Build-Gen"
  Push-Location ./build

  Write-Host "Cleaning build dir..."
  Get-ChildItem * | ForEach-Object { Remove-Item -path $_.FullName -Recurse }
  
  Write-Host "Running CMake..."

  cmake -G Ninja `
        -DLLVM_VERSION="$llvmVersion" `
        ..

  Pop-Location
}

function Build-BDWGC {
 
  $bdwgcBuildDir = "$depsBuildDir/${bdwgcDir}_build.$BDWGC_VERSION"
  $bdwgcInstallDir = "$depsBuildDir/$bdwgcDir.$BDWGC_VERSION"

  Remove-Item -Path $bdwgcBuildDir -Force -Recurse
  Write-Host "  - Deleted: $bdwgcBuildDir"

  Make-Dir -Path $bdwgcBuildDir
  Make-Dir -Path $bdwgcInstallDir
  
  Push-Location $bdwgcBuildDir
  cmake -G Ninja `
        -DCMAKE_INSTALL_PREFIX="$bdwgcInstallDir" `
        -DBUILD_SHARED_LIBS=OFF `
        -DCMAKE_BUILD_TYPE=RelWithDebInfo `
        -DCMAKE_POSITION_INDEPENDENT_CODE=ON `
        -DBUILD_CORD=ON `
        -DENABLE_ATOMIC_UNCOLLECTABLE=ON `
        -DENABLE_CPLUSPLUS=OFF `
        -DENABLE_DISCLAIM=ON `
        -DENABLE_DOCS=ON `
        -DENABLE_DYNAMIC_LOADING=ON `
        -DENABLE_GC_ASSERTIONS=ON `
        -DENABLE_HANDLE_FORK=ON `
        -DENABLE_JAVA_FINALIZATION=OFF `
        -DENABLE_MUNMAP=ON `
        -DENABLE_PARALLEL_MARK=ON `
        -DENABLE_THREAD_LOCAL_ALLOC=ON `
        -DENABLE_THREADS=ON `
        -DENABLE_THREADS_DISCOVERY=ON `
        -DENABLE_THROW_BAD_ALLOC_LIBRARY=ON `
        -DINSTALL_HEADERS=ON `
        -DLLVM_PARALLEL_COMPILE_JOBS=12 `
        -DLLVM_PARALLEL_LINK_JOBS=1 `
        "$bdwgcSrc"

  cmake --build . --parallel
  cmake -DCMAKE_INSTALL_PREFIX="$bdwgcInstallDir" -P cmake_install.cmake

  Pop-Location
}

function Build-LLVM {
  Write-Host "Running: Build-LLVM"
  $buildDir = "$depsSrcDir/$llvmDir.$llvmVersion"
  $installDir = "$depsSrcDir/$llvmDir_build.$llvmVersion"

  $llvmBuildDir = "$depsBuildDir/${llvmDir}_build.$llvmVersion"
  $llvmInstallDir = "$depsBuildDir/$llvmDir.$llvmVersion"

  Remove-Item -Path $llvmBuildDir -Force -Recurse
  Write-Host "  - Deleted: $llvmBuildDir"

  Make-Dir -Path $llvmBuildDir
  Make-Dir -Path $llvmInstallDir
  
  Push-Location $llvmBuildDir

        # -DCMAKE_C_COMPILER="$CC" `
        # -DCMAKE_CXX_COMPILER="$CXX" `

  cmake -G Ninja `
        -DCMAKE_INSTALL_PREFIX="$llvmInstallDir" `
        -DLLVM_PARALLEL_COMPILE_JOBS=12 `
        -DLLVM_PARALLEL_LINK_JOBS=1 `
        -DCMAKE_BUILD_TYPE=Release `
        -DLLVM_BUILD_EXAMPLES=OFF `
        -DLLVM_ENABLE_ASSERTIONS=ON `
        -DLLVM_CCACHE_BUILD=ON `
        -DCMAKE_EXPORT_COMPILE_COMMANDS=ON `
        -DLLVM_ENABLE_PROJECTS='clang;lldb;lld;mlir;clang-tools-extra' `
        -DLLVM_ENABLE_RUNTIMES='compiler-rt;libcxx;libcxxabi;libunwind' `
        -DCMAKE_C_COMPILER="$CC" `
        -DCMAKE_CXX_COMPILER="$CXX" `
        -DCMAKE_CXX_FLAGS="-Wl,--stack,32000000" `
        -DLLVM_ENABLE_LLD=ON `
        "$llvmSrc/llvm"
  cmake --build . --parallel
  cmake -DCMAKE_INSTALL_PREFIX="$llvmInstallDir" -P cmake_install.cmake

  Write-Host ""
  Write-Host "  LLVM Install Dir: $llvmInstallDir"
  Write-Host ""

  Pop-Location
}

# Get-LLVM
# Get-IWYU
# Get-BDWGC
# Build-BDWGC
# Build-LLVM
# Build-Gen

Write-Host "Setting up Env"
Write-Host "  - LLVM_SRC_ROOT :: $llvmInstallDir"
$env:LLVM_SRC_ROOT=$llvmInstallDir

# fixing oldnames.lib https://social.msdn.microsoft.com/Forums/vstudio/en-US/7cf77b51-0868-4a05-92fa-6999c2317ad2/error-lnk1104-cannot-open-file-oldnameslib?forum=vseditor
# fixing msvcrtd.lib https://stackoverflow.com/questions/72356405/vs2022-lld-link-error-could-not-open-oldnames-lib-no-such-file-or-direct
