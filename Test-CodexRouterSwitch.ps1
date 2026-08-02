[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

function Get-IcoSizes {
  param([Parameter(Mandatory = $true)][string]$Path)

  $stream = [IO.File]::OpenRead($Path)
  $reader = New-Object IO.BinaryReader($stream)
  try {
    if ($reader.ReadUInt16() -ne 0 -or $reader.ReadUInt16() -ne 1) {
      throw "The icon header is invalid."
    }
    $count = $reader.ReadUInt16()
    $sizes = @()
    for ($index = 0; $index -lt $count; $index++) {
      $width = [int]$reader.ReadByte()
      $height = [int]$reader.ReadByte()
      if ($width -eq 0) {
        $width = 256
      }
      if ($height -eq 0) {
        $height = 256
      }
      [void]$reader.ReadBytes(14)
      $sizes += "$width`x$height"
    }
    return $sizes
  } finally {
    $reader.Dispose()
    $stream.Dispose()
  }
}

$switchScript = Join-Path $PSScriptRoot "CodexRouterSwitch.ps1"
$buildScript = Join-Path $PSScriptRoot "Build-Exe.ps1"
$iconPath = Join-Path $PSScriptRoot "assets\icon\CodexRouterSwitch.ico"
$expectedIconSizes = @(
  "16x16",
  "20x20",
  "24x24",
  "32x32",
  "40x40",
  "48x48",
  "64x64",
  "128x128",
  "256x256"
)
if (-not (Test-Path -LiteralPath $iconPath -PathType Leaf)) {
  throw "The multi-size application icon was not found."
}
$actualIconSizes = @(Get-IcoSizes -Path $iconPath)
if (
  ($actualIconSizes -join ",") -ne
  ($expectedIconSizes -join ",")
) {
  throw "Unexpected ICO sizes: $($actualIconSizes -join ', ')"
}
$routerRoot = $env:CODEX_ROUTER_SWITCH_ROUTER_ROOT
if ([String]::IsNullOrWhiteSpace($routerRoot)) {
  $routerRoot = Join-Path $env:LOCALAPPDATA "codex-router"
}
$routerRoot = [IO.Path]::GetFullPath($routerRoot)
$nodePath = Join-Path $env:LOCALAPPDATA "hermes\node\node.exe"
if (-not (Test-Path -LiteralPath $nodePath -PathType Leaf)) {
  $nodePath = (Get-Command node.exe -ErrorAction Stop).Source
}

$parseErrors = $null
$tokens = $null
[void][System.Management.Automation.Language.Parser]::ParseFile(
  $switchScript,
  [ref]$tokens,
  [ref]$parseErrors
)
if ($parseErrors.Count -gt 0) {
  throw ($parseErrors | Out-String)
}

$selfTestRaw = & powershell.exe `
  -NoLogo `
  -NoProfile `
  -ExecutionPolicy Bypass `
  -File $switchScript `
  -Mode SelfTest
if ($LASTEXITCODE -ne 0) {
  throw "SelfTest failed: $selfTestRaw"
}
$selfTest = $selfTestRaw | ConvertFrom-Json
if (-not $selfTest.Ok -or $selfTest.MutationsPerformed) {
  throw "SelfTest returned an unexpected result."
}

$guiTestRaw = & powershell.exe `
  -NoLogo `
  -NoProfile `
  -ExecutionPolicy Bypass `
  -STA `
  -File $switchScript `
  -Mode GuiSelfTest
if ($LASTEXITCODE -ne 0) {
  throw "GuiSelfTest failed: $guiTestRaw"
}
$guiTest = $guiTestRaw | ConvertFrom-Json
if (-not $guiTest.Ok -or $guiTest.WindowDisplayed) {
  throw "GuiSelfTest returned an unexpected result."
}

$committedExe = Join-Path $PSScriptRoot "dist\CodexRouterSwitch.exe"
if (-not (Test-Path -LiteralPath $committedExe -PathType Leaf)) {
  throw "The committed CodexRouterSwitch.exe was not found."
}
$distributionTestRoot = Join-Path (
  Join-Path $PSScriptRoot "work\test_outputs"
) ("committed-exe-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $distributionTestRoot -Force | Out-Null
$committedGuiResult = Join-Path $distributionTestRoot "gui-self-test.json"
$committedGuiProcess = Start-Process `
  -FilePath $committedExe `
  -ArgumentList @("--gui-self-test-file", $committedGuiResult) `
  -Wait `
  -PassThru
if ($committedGuiProcess.ExitCode -ne 0) {
  throw "Committed EXE GUI self-test failed with exit code $($committedGuiProcess.ExitCode)."
}
$committedGuiTest = Get-Content -LiteralPath $committedGuiResult -Raw |
  ConvertFrom-Json
if (
  -not $committedGuiTest.ok -or
  -not $committedGuiTest.enhancedUi -or
  -not $committedGuiTest.modernUi -or
  -not $committedGuiTest.pureChineseUi -or
  -not $committedGuiTest.layoutSafe -or
  $committedGuiTest.uiLanguage -ne "zh-CN" -or
  $committedGuiTest.version -ne "1.2.3" -or
  -not $committedGuiTest.refreshMutationGuard -or
  -not $committedGuiTest.readOnlyArgsRestricted -or
  $committedGuiTest.windowDisplayed -or
  $committedGuiTest.mutationsPerformed
) {
  throw "The committed EXE is stale or returned an unexpected result."
}

$buildRaw = & powershell.exe `
  -NoLogo `
  -NoProfile `
  -ExecutionPolicy Bypass `
  -File $buildScript
if ($LASTEXITCODE -ne 0) {
  throw "EXE build failed: $buildRaw"
}
$build = $buildRaw | ConvertFrom-Json
if (-not $build.Ok -or -not (Test-Path -LiteralPath $build.Output -PathType Leaf)) {
  throw "Build-Exe returned an unexpected result."
}
if ($build.EntryPoint -ne "CodexRouterSwitch.EnhancedProgram") {
  throw "Build-Exe did not select the enhanced UI entry point."
}
if (
  -not (Test-Path -LiteralPath $iconPath -PathType Leaf) -or
  $build.Icon -ne $iconPath
) {
  throw "Build-Exe did not use the expected application icon."
}
$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName(
  $build.Output
).Version.ToString()
if ($assemblyVersion -ne "1.2.3.0") {
  throw "Unexpected EXE assembly version: $assemblyVersion"
}

Add-Type -AssemblyName System.Drawing
$embeddedIcon = [Drawing.Icon]::ExtractAssociatedIcon($build.Output)
if ($null -eq $embeddedIcon) {
  throw "The rebuilt EXE does not expose an associated application icon."
}
$embeddedIcon.Dispose()

$exeTestRoot = Join-Path (
  Join-Path $PSScriptRoot "work\test_outputs"
) ("enhanced-exe-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $exeTestRoot -Force | Out-Null

$exeGuiResult = Join-Path $exeTestRoot "gui-self-test.json"
$exeGuiProcess = Start-Process `
  -FilePath $build.Output `
  -ArgumentList @("--gui-self-test-file", $exeGuiResult) `
  -Wait `
  -PassThru
if ($exeGuiProcess.ExitCode -ne 0) {
  throw "Enhanced EXE GUI self-test failed with exit code $($exeGuiProcess.ExitCode)."
}
$exeGuiTest = Get-Content -LiteralPath $exeGuiResult -Raw | ConvertFrom-Json
if (
  -not $exeGuiTest.ok -or
  -not $exeGuiTest.enhancedUi -or
  -not $exeGuiTest.modernUi -or
  -not $exeGuiTest.pureChineseUi -or
  -not $exeGuiTest.layoutSafe -or
  $exeGuiTest.uiLanguage -ne "zh-CN" -or
  $exeGuiTest.version -ne "1.2.3" -or
  -not $exeGuiTest.refreshMutationGuard -or
  -not $exeGuiTest.readOnlyArgsRestricted -or
  $exeGuiTest.windowDisplayed -or
  $exeGuiTest.mutationsPerformed
) {
  throw "Enhanced EXE GUI self-test returned an unexpected result."
}

$exeSelfResult = Join-Path $exeTestRoot "controller-self-test.json"
$exeSelfProcess = Start-Process `
  -FilePath $build.Output `
  -ArgumentList @("--self-test-file", $exeSelfResult) `
  -Wait `
  -PassThru
if ($exeSelfProcess.ExitCode -ne 0) {
  throw "Enhanced EXE controller self-test failed with exit code $($exeSelfProcess.ExitCode)."
}
$exeSelfTest = Get-Content -LiteralPath $exeSelfResult -Raw | ConvertFrom-Json
if (-not $exeSelfTest.ok -or $exeSelfTest.mutationsPerformed) {
  throw "Enhanced EXE controller self-test returned an unexpected result."
}

$testRoot = Join-Path (
  Join-Path $PSScriptRoot "work\test_outputs"
) ("router-switch-" + [Guid]::NewGuid().ToString("N"))
$fakeCodexHome = Join-Path $testRoot "codex"
$fakeState = Join-Path $fakeCodexHome "codex-router"
New-Item -ItemType Directory -Path $fakeState -Force | Out-Null

$originalConfig = @'
model = "gpt-preserved-test"
model_provider = "openai"
model_reasoning_effort = "high"

[profiles.research]
model = "gpt-profile-test"
approval_policy = "on-request"
'@

$configPath = Join-Path $fakeCodexHome "config.toml"
$callerSecretPath = Join-Path $fakeState "caller-secret"
[System.IO.File]::WriteAllText(
  $configPath,
  $originalConfig,
  (New-Object System.Text.UTF8Encoding($false))
)
[System.IO.File]::WriteAllText(
  $callerSecretPath,
  "router-switch-test-caller-secret-0123456789",
  (New-Object System.Text.UTF8Encoding($false))
)

$savedEnvironment = [ordered]@{
  MODEL_ROUTER_TARGET = $env:MODEL_ROUTER_TARGET
  CODEX_HOME = $env:CODEX_HOME
  MODEL_ROUTER_STATE_DIR = $env:MODEL_ROUTER_STATE_DIR
  CODEX_ROUTER_STATE_DIR = $env:CODEX_ROUTER_STATE_DIR
}

try {
  $env:MODEL_ROUTER_TARGET = "codex"
  $env:CODEX_HOME = $fakeCodexHome
  $env:MODEL_ROUTER_STATE_DIR = $fakeState
  $env:CODEX_ROUTER_STATE_DIR = $fakeState

  & $nodePath (Join-Path $routerRoot "src\config-manager.mjs") enable | Out-Null
  if ($LASTEXITCODE -ne 0) {
    throw "Isolated config enable failed."
  }
  $enabled = Get-Content -LiteralPath $configPath -Raw
  if ($enabled -notmatch "# BEGIN codex-router-managed") {
    throw "Managed Router block was not added in the isolated test."
  }
  if ($enabled -notmatch '\[profiles\.research\]') {
    throw "The isolated enable operation did not preserve the profile."
  }

  & $nodePath (Join-Path $routerRoot "src\config-manager.mjs") disable | Out-Null
  if ($LASTEXITCODE -ne 0) {
    throw "Isolated config disable failed."
  }
  $disabled = Get-Content -LiteralPath $configPath -Raw
  if ($disabled -match "# BEGIN codex-router-managed") {
    throw "Managed Router block was not removed in the isolated test."
  }
  if ($disabled -notmatch 'model = "gpt-preserved-test"') {
    throw "The isolated disable operation did not preserve the native model."
  }
  if ($disabled -notmatch 'model_provider = "openai"') {
    throw "The isolated disable operation did not preserve the native provider."
  }
  if ($disabled -notmatch '\[profiles\.research\]') {
    throw "The isolated disable operation did not preserve the profile."
  }
} finally {
  $env:MODEL_ROUTER_TARGET = $savedEnvironment.MODEL_ROUTER_TARGET
  $env:CODEX_HOME = $savedEnvironment.CODEX_HOME
  $env:MODEL_ROUTER_STATE_DIR = $savedEnvironment.MODEL_ROUTER_STATE_DIR
  $env:CODEX_ROUTER_STATE_DIR = $savedEnvironment.CODEX_ROUTER_STATE_DIR
}

[pscustomobject]@{
  Ok = $true
  Syntax = "pass"
  ReadOnlySelfTest = "pass"
  ScriptGuiSelfTest = "pass"
  CommittedEnhancedExe = "pass"
  EnhancedExeBuild = "pass"
  EnhancedGuiSelfTest = "pass"
  ModernChineseUi = "pass"
  LayoutSelfTest = "pass"
  MultiSizeApplicationIcon = "pass"
  AssemblyVersion = $assemblyVersion
  EnhancedControllerSelfTest = "pass"
  IsolatedEnableDisable = "pass"
  RealCodexConfigChanged = $false
  TestOutput = $testRoot
  DistributionTestOutput = $distributionTestRoot
  ExeTestOutput = $exeTestRoot
  ExeSHA256 = $build.SHA256
} | ConvertTo-Json
