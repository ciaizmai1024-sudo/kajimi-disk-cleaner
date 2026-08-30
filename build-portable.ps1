$ErrorActionPreference = 'Stop'

$root = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$output = [IO.Path]::GetFullPath((Join-Path $root 'dist'))
if (-not $output.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Output directory must remain inside the repository: $output"
}
if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}
New-Item -ItemType Directory -Path $output | Out-Null

dotnet publish (Join-Path $root 'KajimiDiskCleaner.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $output `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$files = @(Get-ChildItem -LiteralPath $output -File)
if ($files.Count -ne 1 -or $files[0].Extension -ne '.exe') {
    throw "The portable build must contain exactly one EXE. Found: $($files.Name -join ', ')"
}

Write-Host "Built: $($files[0].FullName)"
Write-Host "Size:  $([math]::Round($files[0].Length / 1MB, 2)) MB"

