[CmdletBinding()]
param(
  [ValidateSet("UI", "On", "Off", "Status", "SelfTest", "GuiSelfTest")]
  [string]$Mode = "UI"
)

$ErrorActionPreference = "Stop"

function Resolve-RouterPort {
  param([string]$Value)

  if ([String]::IsNullOrWhiteSpace($Value)) {
    return 4102
  }

  $port = 0
  if (-not [int]::TryParse(
      $Value.Trim(),
      [Globalization.NumberStyles]::None,
      [Globalization.CultureInfo]::InvariantCulture,
      [ref]$port
    ) -or $port -lt 1 -or $port -gt 65535) {
    throw "CODEX_ROUTER_SWITCH_ROUTER_PORT must be an integer from 1 to 65535."
  }
  return $port
}

$RouterPort = Resolve-RouterPort -Value $env:CODEX_ROUTER_SWITCH_ROUTER_PORT
$RouterHealthUrl = "http://127.0.0.1:$RouterPort/health"

$RouterRoot = $env:CODEX_ROUTER_SWITCH_ROUTER_ROOT
if ([String]::IsNullOrWhiteSpace($RouterRoot)) {
  $RouterRoot = Join-Path $env:LOCALAPPDATA "codex-router"
}
$RouterRoot = [IO.Path]::GetFullPath($RouterRoot)

$CodexHome = $env:CODEX_ROUTER_SWITCH_CODEX_HOME
if ([String]::IsNullOrWhiteSpace($CodexHome)) {
  $CodexHome = Join-Path $env:USERPROFILE ".codex"
}
$CodexHome = [IO.Path]::GetFullPath($CodexHome)
$RouterStateRoot = Join-Path $CodexHome "codex-router"
$RouterStartScript = Join-Path $RouterStateRoot "start-codex-router.cmd"
$VisibleLauncher = Join-Path $PSScriptRoot "Run-Visible-Codex-Router.cmd"
$ConsoleStatePath = Join-Path $RouterStateRoot "router-switch-console.json"
$ConfigManagerScript = Join-Path $RouterRoot "src\config-manager.mjs"
$CatalogScript = Join-Path $RouterRoot "src\catalog.mjs"
$ServiceScript = Join-Path $RouterRoot "src\service.mjs"
$WindowsServiceScript = Join-Path $RouterRoot "src\service-windows.mjs"

function ConvertTo-CommandLineArgument {
  param([Parameter(Mandatory = $true)][string]$Value)

  if ($Value -notmatch '[\s"]') {
    return $Value
  }

  $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
  $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
  return '"' + $escaped + '"'
}

function Resolve-NodePath {
  $candidates = @(
    (Join-Path $env:LOCALAPPDATA "hermes\node\node.exe"),
    (Join-Path $env:LOCALAPPDATA "Programs\nodejs\node.exe")
  )

  foreach ($candidate in $candidates) {
    if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
      return $candidate
    }
  }

  $node = Get-Command node.exe -ErrorAction SilentlyContinue
  if ($node) {
    return $node.Source
  }

  throw "Node.js was not found. Codex Router cannot be controlled."
}

function Assert-RouterFiles {
  $requiredFiles = @(
    $ConfigManagerScript,
    $CatalogScript,
    $ServiceScript,
    $WindowsServiceScript,
    $VisibleLauncher
  )

  foreach ($path in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
      throw "Required file is missing: $path"
    }
  }

  [void](Resolve-NodePath)
}

function Invoke-ExternalProcess {
  param(
    [Parameter(Mandatory = $true)][string]$FilePath,
    [string[]]$Arguments = @(),
    [int]$TimeoutMilliseconds = 300000
  )

  $startInfo = New-Object System.Diagnostics.ProcessStartInfo
  $startInfo.FileName = $FilePath
  $startInfo.Arguments = (($Arguments | ForEach-Object {
    ConvertTo-CommandLineArgument -Value ([string]$_)
  }) -join " ")
  $startInfo.WorkingDirectory = $RouterRoot
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true

  $process = New-Object System.Diagnostics.Process
  $process.StartInfo = $startInfo
  $childEnvironment = [ordered]@{
    MODEL_ROUTER_TARGET = "codex"
    CODEX_HOME = $CodexHome
    MODEL_ROUTER_STATE_DIR = $RouterStateRoot
    CODEX_ROUTER_STATE_DIR = $RouterStateRoot
  }
  $previousEnvironment = @{}
  try {
    foreach ($entry in $childEnvironment.GetEnumerator()) {
      $previousEnvironment[$entry.Key] = [Environment]::GetEnvironmentVariable(
        $entry.Key,
        [EnvironmentVariableTarget]::Process
      )
      [Environment]::SetEnvironmentVariable(
        $entry.Key,
        [string]$entry.Value,
        [EnvironmentVariableTarget]::Process
      )
    }
    if (-not $process.Start()) {
      throw "Could not start: $FilePath"
    }
  } finally {
    foreach ($entry in $childEnvironment.GetEnumerator()) {
      [Environment]::SetEnvironmentVariable(
        $entry.Key,
        $previousEnvironment[$entry.Key],
        [EnvironmentVariableTarget]::Process
      )
    }
  }

  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()

  if (-not $process.WaitForExit($TimeoutMilliseconds)) {
    try {
      $process.Kill()
    } catch {
      # The process may already have exited.
    }
    throw "Command timed out: $FilePath $($startInfo.Arguments)"
  }

  $stdout = $stdoutTask.Result
  $stderr = $stderrTask.Result
  $exitCode = $process.ExitCode
  $process.Dispose()

  if ($exitCode -ne 0) {
    $detail = $stderr.Trim()
    if (-not $detail) {
      $detail = $stdout.Trim()
    }
    if (-not $detail) {
      $detail = "exit code $exitCode"
    }
    throw "Command failed: $detail"
  }

  return [pscustomobject]@{
    ExitCode = $exitCode
    StdOut = $stdout
    StdErr = $stderr
  }
}

function Invoke-RouterNode {
  param(
    [Parameter(Mandatory = $true)][string]$ScriptPath,
    [string[]]$Arguments = @(),
    [int]$TimeoutMilliseconds = 300000
  )

  $nodePath = Resolve-NodePath
  $allArguments = @($ScriptPath) + @($Arguments)
  return Invoke-ExternalProcess `
    -FilePath $nodePath `
    -Arguments $allArguments `
    -TimeoutMilliseconds $TimeoutMilliseconds
}

function Get-ConfigStatus {
  $result = Invoke-RouterNode `
    -ScriptPath $ConfigManagerScript `
    -Arguments @("status") `
    -TimeoutMilliseconds 15000
  return ($result.StdOut | ConvertFrom-Json)
}

function Test-RouterHealth {
  param([int]$TimeoutMilliseconds = 1500)

  $request = $null
  $response = $null
  try {
    $request = [System.Net.HttpWebRequest]::Create($RouterHealthUrl)
    $request.Method = "GET"
    $request.Timeout = $TimeoutMilliseconds
    $request.ReadWriteTimeout = $TimeoutMilliseconds
    $request.KeepAlive = $false
    $response = $request.GetResponse()
    $reader = New-Object System.IO.StreamReader($response.GetResponseStream())
    try {
      $payload = $reader.ReadToEnd() | ConvertFrom-Json
      return ($payload.service -eq "codex-router")
    } finally {
      $reader.Dispose()
    }
  } catch {
    return $false
  } finally {
    if ($response) {
      $response.Dispose()
    }
  }
}

function Wait-RouterHealth {
  param(
    [Parameter(Mandatory = $true)][bool]$Expected,
    [int]$TimeoutSeconds = 300
  )

  $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
  do {
    if ((Test-RouterHealth) -eq $Expected) {
      return $true
    }
    Start-Sleep -Milliseconds 300
  } while ([DateTime]::UtcNow -lt $deadline)

  return $false
}

function Get-SwitchStatus {
  $config = Get-ConfigStatus
  $healthy = Test-RouterHealth
  $configOn = ($config.mode -eq "router")

  if ($configOn -and $healthy) {
    $state = "On"
    $message = "Router is enabled and healthy."
  } elseif ($configOn) {
    $state = "Degraded"
    $message = "Router configuration is enabled, but the visible router process is not healthy."
  } elseif ($healthy) {
    $state = "Orphaned"
    $message = "Native Codex is active, but an untracked router process is still running."
  } else {
    $state = "Off"
    $message = "Native Codex is active. Router settings are preserved."
  }

  return [pscustomobject]@{
    State = $state
    ConfigOn = $configOn
    Healthy = $healthy
    Model = $config.model
    ModelProvider = $config.model_provider
    RouterPort = $RouterPort
    Message = $message
  }
}

function Stop-TrackedRouterConsole {
  if (-not (Test-Path -LiteralPath $ConsoleStatePath -PathType Leaf)) {
    return $false
  }

  try {
    $saved = Get-Content -LiteralPath $ConsoleStatePath -Raw | ConvertFrom-Json
    $pidValue = [int]$saved.pid
    $process = Get-Process -Id $pidValue -ErrorAction SilentlyContinue
    if (-not $process) {
      return $false
    }

    if ($process.ProcessName -notin @("cmd", "conhost")) {
      throw "Refusing to stop PID $pidValue because it is not the recorded command console."
    }

    if ($saved.startTimeUtc) {
      $savedStart = [DateTime]::Parse(
        [string]$saved.startTimeUtc,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind
      )
      $actualStart = $process.StartTime.ToUniversalTime()
      if ([Math]::Abs(($actualStart - $savedStart.ToUniversalTime()).TotalSeconds) -gt 3) {
        throw "Refusing to stop PID $pidValue because the PID has been reused."
      }
    }

    $taskkill = Join-Path $env:SystemRoot "System32\taskkill.exe"
    [void](Invoke-ExternalProcess `
      -FilePath $taskkill `
      -Arguments @("/PID", [string]$pidValue, "/T", "/F") `
      -TimeoutMilliseconds 30000)
    return $true
  } finally {
    Remove-Item -LiteralPath $ConsoleStatePath -Force -ErrorAction SilentlyContinue
  }
}

function Write-OfficialStartScript {
  $rendered = Invoke-RouterNode `
    -ScriptPath $WindowsServiceScript `
    -Arguments @("render") `
    -TimeoutMilliseconds 15000

  if (-not $rendered.StdOut.Contains("src\start.mjs")) {
    throw "The repository did not render a recognized Windows start script."
  }

  [System.IO.Directory]::CreateDirectory($RouterStateRoot) | Out-Null
  $utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
  [System.IO.File]::WriteAllText(
    $RouterStartScript,
    $rendered.StdOut,
    $utf8WithoutBom
  )
}

function Start-VisibleRouterConsole {
  if (-not (Test-Path -LiteralPath $RouterStartScript -PathType Leaf)) {
    throw "Router start script was not created: $RouterStartScript"
  }

  $startInfo = New-Object System.Diagnostics.ProcessStartInfo
  $startInfo.FileName = $env:ComSpec
  $startInfo.Arguments = '/D /S /C ""' + $VisibleLauncher + '""'
  $startInfo.WorkingDirectory = $RouterRoot
  $startInfo.UseShellExecute = $true
  $startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Normal

  $process = [System.Diagnostics.Process]::Start($startInfo)
  if (-not $process) {
    throw "The visible Router console could not be started."
  }

  $startTimeUtc = $process.StartTime.ToUniversalTime().ToString("o")
  $record = [ordered]@{
    version = 1
    pid = $process.Id
    startTimeUtc = $startTimeUtc
    launcher = $VisibleLauncher
    routerStartScript = $RouterStartScript
  }
  $json = $record | ConvertTo-Json
  [System.IO.File]::WriteAllText(
    $ConsoleStatePath,
    $json,
    (New-Object System.Text.UTF8Encoding($false))
  )
}

function Enable-RouterVisible {
  Assert-RouterFiles
  $initialConfig = Get-ConfigStatus

  try {
    [void](Stop-TrackedRouterConsole)

    [void](Invoke-RouterNode `
      -ScriptPath $ServiceScript `
      -Arguments @("uninstall") `
      -TimeoutMilliseconds 30000)

    if (-not (Wait-RouterHealth -Expected $false -TimeoutSeconds 20)) {
      throw "A router process that was not started by this switch still owns the configured Router port."
    }

    [void](Invoke-RouterNode `
      -ScriptPath $CatalogScript `
      -TimeoutMilliseconds 60000)

    Write-OfficialStartScript

    [void](Invoke-RouterNode `
      -ScriptPath $ConfigManagerScript `
      -Arguments @("enable") `
      -TimeoutMilliseconds 15000)

    Start-VisibleRouterConsole

    if (-not (Wait-RouterHealth -Expected $true -TimeoutSeconds 300)) {
      throw "The Router did not become healthy within 300 seconds. Check router.log."
    }

    return [pscustomobject]@{
      Ok = $true
      State = "On"
      Message = "Router is ON in a visible console. Restart Codex manually."
    }
  } catch {
    try {
      [void](Stop-TrackedRouterConsole)
    } catch {
      # Preserve the original failure.
    }

    if ($initialConfig.mode -ne "router") {
      try {
        [void](Invoke-RouterNode `
          -ScriptPath $ConfigManagerScript `
          -Arguments @("disable") `
          -TimeoutMilliseconds 15000)
      } catch {
        # Preserve the original failure.
      }
    }
    throw
  }
}

function Disable-RouterKeepSettings {
  Assert-RouterFiles
  $warnings = New-Object System.Collections.Generic.List[string]

  try {
    [void](Stop-TrackedRouterConsole)
  } catch {
    $warnings.Add($_.Exception.Message)
  }

  try {
    [void](Invoke-RouterNode `
      -ScriptPath $ServiceScript `
      -Arguments @("uninstall") `
      -TimeoutMilliseconds 30000)
  } catch {
    $warnings.Add($_.Exception.Message)
  }

  [void](Invoke-RouterNode `
    -ScriptPath $ConfigManagerScript `
    -Arguments @("disable") `
    -TimeoutMilliseconds 15000)

  if (-not (Wait-RouterHealth -Expected $false -TimeoutSeconds 20)) {
    $warnings.Add(
      "Native Codex was restored, but an untracked process still responds on the configured Router port."
    )
  }

  $message = "Router is OFF. Native Codex is active and Router settings are preserved."
  if ($warnings.Count -gt 0) {
    $message += " Warning: " + ($warnings -join " ")
  }

  return [pscustomobject]@{
    Ok = $true
    State = "Off"
    Message = $message
    Warnings = @($warnings)
    RouterPort = $RouterPort
  }
}

function Write-ResultAndExit {
  param(
    [Parameter(Mandatory = $true)]$Result,
    [int]$ExitCode = 0
  )

  $Result | ConvertTo-Json -Compress -Depth 6
  exit $ExitCode
}

function Invoke-SelfTest {
  Assert-RouterFiles
  $nodePath = Resolve-NodePath
  $config = Get-ConfigStatus
  $rendered = Invoke-RouterNode `
    -ScriptPath $WindowsServiceScript `
    -Arguments @("render") `
    -TimeoutMilliseconds 15000

  if (-not $rendered.StdOut.Contains("src\start.mjs")) {
    throw "Rendered start script validation failed."
  }
  if ($config.mode -notin @("native", "router")) {
    throw "Unexpected Codex configuration mode: $($config.mode)"
  }

  return [pscustomobject]@{
    Ok = $true
    Node = $nodePath
    ConfigMode = $config.mode
    Model = $config.model
    RouterPort = $RouterPort
    StartScriptRender = "valid"
    MutationsPerformed = $false
  }
}

if ($Mode -notin @("UI", "GuiSelfTest")) {
  try {
    switch ($Mode) {
      "On" {
        Write-ResultAndExit -Result (Enable-RouterVisible)
      }
      "Off" {
        Write-ResultAndExit -Result (Disable-RouterKeepSettings)
      }
      "Status" {
        Write-ResultAndExit -Result (Get-SwitchStatus)
      }
      "SelfTest" {
        Write-ResultAndExit -Result (Invoke-SelfTest)
      }
    }
  } catch {
    Write-ResultAndExit -ExitCode 1 -Result ([pscustomobject]@{
      Ok = $false
      State = "Error"
      Message = $_.Exception.Message
      Details = $_.ScriptStackTrace
    })
  }
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type -ReferencedAssemblies @(
  "System.Windows.Forms",
  "System.Drawing"
) -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

public sealed class RouterToggleSwitch : Control
{
    private bool isChecked;

    public event EventHandler CheckedChanged;

    public bool Checked
    {
        get { return isChecked; }
        set
        {
            if (isChecked == value) return;
            isChecked = value;
            Invalidate();
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
        }
    }

    public RouterToggleSwitch()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true
        );
        Size = new Size(92, 44);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleName = "Codex Router ON OFF switch";
    }

    protected override void OnClick(EventArgs e)
    {
        if (Enabled) Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
        {
            Checked = !Checked;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        int h = Height - 2;
        int w = Width - 2;
        Color trackColor = isChecked
            ? Color.FromArgb(34, 197, 94)
            : Color.FromArgb(148, 163, 184);
        if (!Enabled)
        {
            trackColor = Color.FromArgb(203, 213, 225);
        }

        using (SolidBrush track = new SolidBrush(trackColor))
        {
            e.Graphics.FillRectangle(track, 1 + h / 2, 1, w - h, h);
            e.Graphics.FillEllipse(track, 1, 1, h, h);
            e.Graphics.FillEllipse(track, 1 + w - h, 1, h, h);
        }

        int knob = h - 8;
        int knobX = isChecked ? Width - knob - 5 : 5;
        using (SolidBrush shadow = new SolidBrush(Color.FromArgb(45, 15, 23, 42)))
        {
            e.Graphics.FillEllipse(shadow, knobX + 1, 6, knob, knob);
        }
        using (SolidBrush knobBrush = new SolidBrush(Color.White))
        {
            e.Graphics.FillEllipse(knobBrush, knobX, 5, knob, knob);
        }
    }
}
"@

[System.Windows.Forms.Application]::EnableVisualStyles()

$form = New-Object System.Windows.Forms.Form
$form.Text = "Codex Router Switch"
$form.ClientSize = New-Object System.Drawing.Size(470, 280)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false
$form.MinimizeBox = $true
$form.BackColor = [System.Drawing.Color]::FromArgb(248, 250, 252)
$form.Font = New-Object System.Drawing.Font("Segoe UI", 10)

$titleLabel = New-Object System.Windows.Forms.Label
$titleLabel.Text = "Codex Router"
$titleLabel.Font = New-Object System.Drawing.Font("Segoe UI Semibold", 20)
$titleLabel.ForeColor = [System.Drawing.Color]::FromArgb(15, 23, 42)
$titleLabel.AutoSize = $true
$titleLabel.Location = New-Object System.Drawing.Point(28, 24)
$form.Controls.Add($titleLabel)

$subtitleLabel = New-Object System.Windows.Forms.Label
$subtitleLabel.Text = "Visible Router console / Native Codex"
$subtitleLabel.ForeColor = [System.Drawing.Color]::FromArgb(100, 116, 139)
$subtitleLabel.AutoSize = $true
$subtitleLabel.Location = New-Object System.Drawing.Point(31, 67)
$form.Controls.Add($subtitleLabel)

$toggle = New-Object RouterToggleSwitch
$toggle.Location = New-Object System.Drawing.Point(32, 108)
$form.Controls.Add($toggle)

$modeLabel = New-Object System.Windows.Forms.Label
$modeLabel.Font = New-Object System.Drawing.Font("Segoe UI Semibold", 13)
$modeLabel.AutoSize = $true
$modeLabel.Location = New-Object System.Drawing.Point(145, 115)
$form.Controls.Add($modeLabel)

$statusLabel = New-Object System.Windows.Forms.Label
$statusLabel.ForeColor = [System.Drawing.Color]::FromArgb(71, 85, 105)
$statusLabel.Location = New-Object System.Drawing.Point(32, 169)
$statusLabel.Size = New-Object System.Drawing.Size(405, 42)
$form.Controls.Add($statusLabel)

$restartLabel = New-Object System.Windows.Forms.Label
$restartLabel.Text = "After every change, fully quit and reopen Codex yourself."
$restartLabel.ForeColor = [System.Drawing.Color]::FromArgb(180, 83, 9)
$restartLabel.AutoSize = $true
$restartLabel.Location = New-Object System.Drawing.Point(32, 222)
$form.Controls.Add($restartLabel)

$refreshButton = New-Object System.Windows.Forms.Button
$refreshButton.Text = "Refresh"
$refreshButton.Size = New-Object System.Drawing.Size(86, 30)
$refreshButton.Location = New-Object System.Drawing.Point(351, 106)
$refreshButton.FlatStyle = "System"
$form.Controls.Add($refreshButton)

$script:SuppressToggleEvent = $false
$script:WorkerProcess = $null
$script:WorkerTarget = $null

function Set-ToggleChecked {
  param([bool]$Value)
  $script:SuppressToggleEvent = $true
  try {
    $toggle.Checked = $Value
  } finally {
    $script:SuppressToggleEvent = $false
  }
}

function Set-UiBusy {
  param(
    [bool]$Busy,
    [string]$Text = ""
  )
  $toggle.Enabled = -not $Busy
  $refreshButton.Enabled = -not $Busy
  if ($Busy) {
    $modeLabel.Text = "WORKING"
    $modeLabel.ForeColor = [System.Drawing.Color]::FromArgb(37, 99, 235)
    $statusLabel.Text = $Text
    $form.UseWaitCursor = $true
  } else {
    $form.UseWaitCursor = $false
  }
}

function Update-UiFromStatus {
  param($Status)

  Set-ToggleChecked -Value ([bool]$Status.ConfigOn)
  switch ([string]$Status.State) {
    "On" {
      $modeLabel.Text = "ON - ROUTER"
      $modeLabel.ForeColor = [System.Drawing.Color]::FromArgb(22, 163, 74)
    }
    "Degraded" {
      $modeLabel.Text = "ON - NOT HEALTHY"
      $modeLabel.ForeColor = [System.Drawing.Color]::FromArgb(217, 119, 6)
    }
    "Orphaned" {
      $modeLabel.Text = "OFF - PROCESS FOUND"
      $modeLabel.ForeColor = [System.Drawing.Color]::FromArgb(217, 119, 6)
    }
    default {
      $modeLabel.Text = "OFF - NATIVE CODEX"
      $modeLabel.ForeColor = [System.Drawing.Color]::FromArgb(71, 85, 105)
    }
  }
  $statusLabel.Text = [string]$Status.Message
}

function Refresh-UiStatus {
  try {
    Set-UiBusy -Busy $true -Text "Checking Codex and Router state..."
    $status = Get-SwitchStatus
    Update-UiFromStatus -Status $status
  } catch {
    $modeLabel.Text = "STATUS ERROR"
    $modeLabel.ForeColor = [System.Drawing.Color]::FromArgb(220, 38, 38)
    $statusLabel.Text = $_.Exception.Message
  } finally {
    Set-UiBusy -Busy $false
  }
}

$workerTimer = New-Object System.Windows.Forms.Timer
$workerTimer.Interval = 300
$workerTimer.add_Tick({
  if (-not $script:WorkerProcess) {
    $workerTimer.Stop()
    return
  }
  if (-not $script:WorkerProcess.HasExited) {
    return
  }

  $workerTimer.Stop()
  $stdout = $script:WorkerProcess.StandardOutput.ReadToEnd()
  $stderr = $script:WorkerProcess.StandardError.ReadToEnd()
  $exitCode = $script:WorkerProcess.ExitCode
  $script:WorkerProcess.Dispose()
  $script:WorkerProcess = $null

  $result = $null
  try {
    $result = $stdout.Trim() | ConvertFrom-Json
  } catch {
    $result = [pscustomobject]@{
      Ok = $false
      Message = if ($stderr.Trim()) { $stderr.Trim() } else { $stdout.Trim() }
    }
  }

  Set-UiBusy -Busy $false
  Refresh-UiStatus

  if ($exitCode -ne 0 -or -not $result.Ok) {
    [System.Windows.Forms.MessageBox]::Show(
      $form,
      [string]$result.Message,
      "Codex Router switch failed",
      [System.Windows.Forms.MessageBoxButtons]::OK,
      [System.Windows.Forms.MessageBoxIcon]::Error
    ) | Out-Null
  } else {
    [System.Windows.Forms.MessageBox]::Show(
      $form,
      ([string]$result.Message + "`r`n`r`nFully quit and reopen Codex to apply the change."),
      "Codex Router switch",
      [System.Windows.Forms.MessageBoxButtons]::OK,
      [System.Windows.Forms.MessageBoxIcon]::Information
    ) | Out-Null
  }
})

function Start-Worker {
  param([ValidateSet("On", "Off")][string]$Target)

  if ($script:WorkerProcess) {
    return
  }

  $startInfo = New-Object System.Diagnostics.ProcessStartInfo
  $startInfo.FileName = "powershell.exe"
  $startInfo.Arguments = (
    "-NoLogo -NoProfile -ExecutionPolicy Bypass -File " +
    (ConvertTo-CommandLineArgument -Value $PSCommandPath) +
    " -Mode " + $Target
  )
  $startInfo.WorkingDirectory = $PSScriptRoot
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true

  $process = New-Object System.Diagnostics.Process
  $process.StartInfo = $startInfo
  if (-not $process.Start()) {
    throw "Could not start the Router switch worker."
  }

  $script:WorkerProcess = $process
  $script:WorkerTarget = $Target
  $workingMessage = if ($Target -eq "On") {
    "Preparing the catalog and starting the visible Router console..."
  } else {
    "Stopping Router and restoring native Codex..."
  }
  Set-UiBusy -Busy $true -Text $workingMessage
  $workerTimer.Start()
}

$toggle.add_CheckedChanged({
  if ($script:SuppressToggleEvent -or $script:WorkerProcess) {
    return
  }

  $target = if ($toggle.Checked) { "On" } else { "Off" }
  try {
    Start-Worker -Target $target
  } catch {
    Refresh-UiStatus
    [System.Windows.Forms.MessageBox]::Show(
      $form,
      $_.Exception.Message,
      "Codex Router switch failed",
      [System.Windows.Forms.MessageBoxButtons]::OK,
      [System.Windows.Forms.MessageBoxIcon]::Error
    ) | Out-Null
  }
})

$refreshButton.add_Click({
  Refresh-UiStatus
})

$form.add_FormClosing({
  if ($script:WorkerProcess -and -not $script:WorkerProcess.HasExited) {
    $_.Cancel = $true
    [System.Windows.Forms.MessageBox]::Show(
      $form,
      "Wait for the current ON/OFF operation to finish.",
      "Codex Router switch",
      [System.Windows.Forms.MessageBoxButtons]::OK,
      [System.Windows.Forms.MessageBoxIcon]::Information
    ) | Out-Null
  }
})

$form.add_Shown({
  Refresh-UiStatus
})

if ($Mode -eq "GuiSelfTest") {
  Write-ResultAndExit -Result ([pscustomobject]@{
    Ok = $true
    GuiType = $toggle.GetType().FullName
    FormTitle = $form.Text
    Controls = $form.Controls.Count
    WindowDisplayed = $false
    MutationsPerformed = $false
  })
}

[void]$form.ShowDialog()
