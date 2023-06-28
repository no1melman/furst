function EnsureDirectory {
    param (
        [Parameter(Mandatory=$true)]
        [string]$Path
    )

    $directoryName = Split-Path $Path -Leaf

    if (Test-Path -Path $Path -PathType Container) {
        Write-Host "The $directoryName directory already exists."
    }
    else {
        try {
            New-Item -Path $Path -ItemType Directory -ErrorAction Stop
            Write-Host "The $directoryName directory has been created."
        }
        catch {
            Write-Host "Failed to create the $directoryName directory: $($_.Exception.Message)"
        }
    }
}

function TryRemoveDirectory {
  param (
      [Parameter(Mandatory=$true)]
      [string]$Path,

      [switch]$Force,
      [switch]$Recurse
  )

  $directoryName = Split-Path $Path -Leaf

  if (Test-Path -Path $Path -PathType Container) {
      try {
          Remove-Item -Path $Path -Recurse:$Recurse -Force:$Force -ErrorAction Stop
          Write-Host "The $directoryName directory has been removed."
      }
      catch {
          Write-Host "Failed to remove the $directoryName directory: $($_.Exception.Message)"
      }
  }
  else {
      Write-Host "The $directoryName directory does not exist."
  }
}

function PromptYesNoQuestion {
    param (
        [Parameter(Mandatory=$true)]
        [string]$Question
    )

    $response = Read-Host -Prompt $Question

    if ([string]::IsNullOrWhiteSpace($response) -or $response -eq "Y" -or $response -eq "y") {
        Write-Host "$Question... yes"
        return $true
    }
    else {
        Write-Host "$Question... no"
        return $false
    }
}

$newline = [System.Environment]::NewLine

$freshRepoQuestion = "Do you want to check out a fresh repository? (Y/n)"
$shouldCheckout = PromptYesNoQuestion -Question $freshRepoQuestion 

if ($shouldCheckout) {
    # Code to execute if the user chooses to check out a fresh repository
    TryRemoveDirectory -Path "./llvm-project" -Recurse -Force
    git clone --depth 1 --config core.autocrlf=false https://github.com/llvm/llvm-project.git
    Write-Host "$newline=======$newline"
    Write-Host "Fresh repository checked out."
} else {
    # Code to execute if the user chooses not to check out a fresh repository
    Write-Host "Skipping fresh repository checkout."
}


Write-Host "$newlinePreparing LLVM for build$newline"

Set-Location llvm-project
TryRemoveDirectory -Recurse -Force -Path ./build
EnsureDirectory -Path "./build"
Set-Location build

$continuePrepareQuestion = "Do you want to prepare LLVM? (Y/n)"
$shouldPrepare = PromptYesNoQuestion -Question $continuePrepareQuestion

if ($shouldPrepare) {
    # definitely just a windows thing 
    # -Thost=x64 ` 
    
    # potentially just a windows thing
    # -DLLVM_TARGETS_TO_BUILD="host" ` 

    # potentially won't work on windows
    # -DLLVM_ENABLE_LLD=ON ` 
    
    # potentially just a windows thing
    # -DLLVM_ENABLE_ASSERTIONS=ON 


  cmake `
    ../llvm `
    -G "Visual Studio 17 2022" `
    -DCMAKE_BUILD_TYPE=Release `
    -Thost=x64 `
    -DLLVM_TARGETS_TO_BUILD="host" `
    -DLLVM_ENABLE_PROJECTS="clang;clang-tools-extra;lld;lldb;mlir" `
    -DLLVM_ENABLE_LLD=ON `
    -DLLVM_CCACHE_BUILD=ON `
    -DLLVM_ENABLE_ASSERTIONS=ON

} else {
    Write-Host "$newlineSkipping preparation...$newline"
}

$continueBuildQuestion = "Do you want to continue? (Y/n)"
$shouldBuild = PromptYesNoQuestion -Question $continueBuildQuestion

if ($shouldBuild) {
  # Code to execute if the user chooses to continue
  Write-Host "Continuing..."
  cmake --build . --target install --parallel
} else {
  # Code to execute if the user chooses not to continue
  Write-Host "Exiting..."
}

