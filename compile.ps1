function Ensure-BuildDirectory {
    param (
        [Parameter(Mandatory=$true)]
        [string]$Path
    )

    if (Test-Path -Path $Path -PathType Container) {
        Write-Host "The build directory already exists."
    }
    else {
        try {
            New-Item -Path $Path -ItemType Directory -ErrorAction Stop
            Write-Host "The build directory has been created."
        }
        catch {
            Write-Host "Failed to create the build directory: $($_.Exception.Message)"
        }
    }
}

Remove-Item -Recurse -Force ./build
Ensure-BuildDirectory -Path ./build
try {
  # cmake ../ -G "Visual Studio 17 2022" -DCMAKE_BUILD_TYPE=Release -Thost=x64

  cd build 
  cmake ../ -G "Ninja" `
    -DCMAKE_BUILD_TYPE=Debug `
    -DCMAKE_CXX_COMPILER=clang++ `
    -DCMAKE_C_COMPILER=clang
  cmake --build . --parallel
} finally {
  cd ..
}
