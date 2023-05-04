$llvmVersion = "9fda8322243168cbfcb78c4cf80afa838473a573"
$llvmSrc = "llvm.$llvmVersion"

function Get-LLVM() {
  Write-Host "Running: Get-LLVM"
  $dest = "deps/$llvmSrc"
  $itemCount = (Get-ChildItem -Path $dest -Recurse | Measure-Object).Count
  Write-Host "There is an llvm... $itemCount"
  if ($itemCount -gt 0) {
    return
  }

  Write-Host "Cloning I guess..."
  Write-Host ""
  git clone --depth=1 https://github.com/llvm/llvm-project.git $dest
  Push-Location $dest 
  git fetch --depth=1 --filter=tree:0 origin "$llvmVersion"
  git reset --hard FETCH_HEAD
  Pop-Location
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

Get-LLVM
Build-Gen
