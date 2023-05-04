$ME="$((pwd).Path)"
$depsSrcDir = "$ME/deps"
$depsBuildDir = "$ME/deps_build"

$llvmVersion = "9fda8322243168cbfcb78c4cf80afa838473a573"
$iwyuVersion="435ad9d35ceee7759ea8f8fd658579e979ee5146"
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

function Check-Path() {
  param(
    [string]$Path
  )

  return Test-Path -Path $Path
}

function Make-Dir() {
  param(
    [string]$Path
  )

  if (!(Check-Path -Path $Path)) {
    Write-Host "  Creating: $Path"
    New-Item -ItemType Directory -Path $Path -Force
  }
}

function Clone-Repo() {
  param(
    [string]$Dest,
    [string]$Repo,
    [string]$Version
  )

  $itemCount = (Get-ChildItem -Path $dest -Recurse | Measure-Object).Count
  Write-Host "Not Cloning: $Repo... $itemCount"
  if ($itemCount -gt 0) {
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

function Get-LLVM() {
  Write-Host "Running: Get-LLVM"

  $repo = "https://github.com/llvm/llvm-project.git"

  Clone-Repo -Repo $repo -Dest $llvmSrc -Version $llvmVersion
 }

function Get-IWYU() {
  Write-Host "Running: Get-IWYU"

  $repo="https://github.com/include-what-you-use/include-what-you-use.git"

  Clone-Repo -Repo $repo -Dest $iwyuSrc -Version $iwyuVersion
}

function Get-BDWGC() {
  Write-Host "Running: Get-BDWGC"

  $repo="https://github.com/ivmai/bdwgc.git"

  Clone-Repo -Repo $repo -Dest $bdwgcSrc -Version $BDWGC_VERSION
}

function Build-Gen() {
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

function Build-BDWGC() {
 
  $bdwgcBuildDir = "$depsBuildDir/${bdwgcDir}_build.$BDWGC_VERSION"
  $bdwgcInstallDir = "$depsBuildDir/$bdwgcDir.$BDWGC_VERSION"

  Make-Dir -Path $bdwgcBuildDir
  Make-Dir -Path $bdwgcInstallDir
  
  Push-Location $bdwgcBuildDir
  cmake -G Ninja `
        -DCMAKE_INSTALL_PREFIX="$bdwgcInstallDir" `
        -DBUILD_SHARED_LIBS=OFF `
        -DCMAKE_BUILD_TYPE=RelWithDebInfo `
        -DCMAKE_POSITION_INDEPENDENT_CODE=ON `
        -Dbuild_cord=ON `
        -Denable_atomic_uncollectable=ON `
        -Denable_cplusplus=OFF `
        -Denable_disclaim=ON `
        -Denable_docs=ON `
        -Denable_dynamic_loading=ON `
        -Denable_gc_assertions=ON `
        -Denable_handle_fork=ON `
        -Denable_java_finalization=OFF `
        -Denable_munmap=ON `
        -Denable_parallel_mark=ON `
        -Denable_thread_local_alloc=ON `
        -Denable_threads=ON `
        -Denable_threads_discovery=ON `
        -Denable_throw_bad_alloc_library=ON `
        -Dinstall_headers=ON `
        -DCMAKE_C_COMPILER="$CC" `
        -DCMAKE_CXX_COMPILER="$CXX" `
        "$bdwgcSrc"

  cmake --build . --parallel
  cmake -DCMAKE_INSTALL_PREFIX="$bdwgcInstallDir" -P cmake_install.cmake

  Pop-Location
}

function Build-LLVM() {
  Write-Host "Running: Build-LLVM"
  $buildDir = "$depsSrcDir/$llvmDir.$llvmVersion"
  $installDir = "$depsSrcDir/$llvmDir_build.$llvmVersion"
  $TARGET_ARCHS="X86;AArch64;" # AMDGPU;ARM;RISCV;WebAssembly"
  $env:TARGET_ARCHS=$TARGET_ARCHS

  $llvmBuildDir = "$depsBuildDir/${llvmDir}_build.$llvmVersion"
  $llvmInstallDir = "$depsBuildDir/$llvmDir.$llvmVersion"

  Make-Dir -Path $llvmBuildDir
  Make-Dir -Path $llvmInstallDir
  
  Push-Location $llvmBuildDir

        # -DCMAKE_C_COMPILER="$CC" `
        # -DCMAKE_CXX_COMPILER="$CXX" `
  cmake -G Ninja `
        -DCMAKE_INSTALL_PREFIX="$llvmInstallDir" `
        -DLLVM_PARALLEL_COMPILE_JOBS=7 `
        -DLLVM_PARALLEL_LINK_JOBS=1 `
        -DTARGET_ARCHS="$TARGET_ARCHS" `
        -C "$ME/cmake/caches/llvm.cmake" `
        -S "$llvmSrc/llvm"
  cmake --build . --parallel
  cmake -DCMAKE_INSTALL_PREFIX="$llvmInstallDir" -P cmake_install.cmake

  Pop-Location
}

Get-LLVM
# Get-IWYU
# Get-BDWGC
# Build-BDWGC
Build-LLVM
# Build-Gen
