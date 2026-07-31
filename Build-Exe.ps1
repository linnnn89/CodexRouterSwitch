[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$sourceRoot = Join-Path $PSScriptRoot "src"
$sources = @(
  (Join-Path $sourceRoot "CodexRouterSwitch.cs"),
  (Join-Path $sourceRoot "EnhancedMainForm.cs")
)
$manifest = Join-Path $sourceRoot "app.manifest"
$dist = Join-Path $PSScriptRoot "dist"
$output = Join-Path $dist "CodexRouterSwitch.exe"

$compilerCandidates = @(
  (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
  (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
)
$compiler = $compilerCandidates |
  Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
  Select-Object -First 1
if (-not $compiler) {
  throw "The Windows .NET Framework C# compiler was not found."
}

foreach ($source in $sources) {
  if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
    throw "Required C# source file was not found: $source"
  }
}

New-Item -ItemType Directory -Path $dist -Force | Out-Null

$references = @(
  "System.dll",
  "System.Core.dll",
  "System.Drawing.dll",
  "System.Windows.Forms.dll",
  "System.Web.Extensions.dll",
  "System.Management.dll"
)

$arguments = @(
  "/nologo",
  "/target:winexe",
  "/main:CodexRouterSwitch.EnhancedProgram",
  "/platform:anycpu",
  "/optimize+",
  "/debug-",
  "/warn:4",
  "/utf8output",
  "/win32manifest:$manifest",
  "/out:$output"
)
$arguments += $references | ForEach-Object { "/reference:$_" }
$arguments += $sources

& $compiler @arguments
if ($LASTEXITCODE -ne 0) {
  throw "C# compilation failed with exit code $LASTEXITCODE."
}
if (-not (Test-Path -LiteralPath $output -PathType Leaf)) {
  throw "The compiler did not create $output."
}

$item = Get-Item -LiteralPath $output
$hash = Get-FileHash -LiteralPath $output -Algorithm SHA256
[pscustomobject]@{
  Ok = $true
  Compiler = $compiler
  EntryPoint = "CodexRouterSwitch.EnhancedProgram"
  Sources = $sources
  Output = $item.FullName
  Bytes = $item.Length
  SHA256 = $hash.Hash
} | ConvertTo-Json
