[CmdletBinding()]
param(
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src\FractalMcp\FractalMcp.csproj'

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts\fractal-mcp-win-x64'
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

& dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --property:PublishSingleFile=true `
    --property:IncludeNativeLibrariesForSelfExtract=true `
    --property:DebugType=None `
    --property:DebugSymbols=false `
    --output $OutputDirectory

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$executablePath = Join-Path $OutputDirectory 'fractal-mcp.exe'
$executable = Get-Item -LiteralPath $executablePath
$hash = Get-FileHash -LiteralPath $executablePath -Algorithm SHA256

[pscustomobject]@{
    Path = $executable.FullName
    Bytes = $executable.Length
    SHA256 = $hash.Hash
}
