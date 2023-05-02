


$llvmVersion = "9fda8322243168cbfcb78c4cf80afa838473a573"
$llvmSrc = "llvm.$llvmVersion"

$dest = "deps/$llvmSrc"

git clone --depth=1 https://github.com/llvm/llvm-project.git $dest
Push-Location $dest 
git fetch --depth=1 --filter=tree:0 origin "$llvmVersion"
git reset --hard FETCH_HEAD
Pop-Location
