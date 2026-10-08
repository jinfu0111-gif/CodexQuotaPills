param([Parameter(Mandatory=$true)][string]$PlanPath, [switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
$taskPlanFile = (Resolve-Path -LiteralPath $PlanPath).Path
$taskPlan = Get-Content -LiteralPath $taskPlanFile -Raw -Encoding UTF8 | ConvertFrom-Json
$taskManaged = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'CodexQuotaPills\GitHubUpdates')) + '\'
$taskOld = [IO.Path]::GetFullPath([string]$taskPlan.OldExe)
$taskNew = [IO.Path]::GetFullPath([string]$taskPlan.NewExe)
if (-not $taskPlanFile.StartsWith($taskManaged, [StringComparison]::OrdinalIgnoreCase) -or
    -not $taskNew.StartsWith($taskManaged, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($taskOld) -ne 'CodexQuotaPills.exe' -or
    [IO.Path]::GetFileName($taskNew) -ne 'CodexQuotaPills.exe' -or $taskNew -eq $taskOld -or
    [string]$taskPlan.Version -notmatch '^\d+\.\d+\.\d+$' -or
    [string]$taskPlan.Sha256 -notmatch '^[0-9a-f]{64}$') { throw 'Invalid update plan.' }
foreach ($taskFile in @($taskOld, $taskNew, $taskPlanFile)) {
    $taskItem = Get-Item -LiteralPath $taskFile
    while ($taskItem) {
        if ($taskItem.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Update paths must not contain reparse points.' }
        if ($taskItem.PSIsContainer) { $taskItem = $taskItem.Parent }
        else { $taskItem = $taskItem.Directory }
    }
}
if ((Get-Item -LiteralPath $taskNew).VersionInfo.FileVersion -ne ($taskPlan.Version + '.0') -or
    (Get-FileHash -LiteralPath $taskNew -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskPlan.Sha256) {
    throw 'Staged executable version or hash mismatch.'
}
if ([version]$taskPlan.Version -le [version](Get-Item -LiteralPath $taskOld).VersionInfo.FileVersion) {
    throw 'Update must be newer than the running executable.'
}
if ($ValidateOnly) { Write-Output 'UpdatePlanValidated=True'; return }

$taskFolder = Split-Path -Parent $taskPlanFile
$taskBackup = Join-Path $taskFolder 'startup-backup'
New-Item -ItemType Directory -Path $taskBackup | Out-Null
$taskShell = New-Object -ComObject WScript.Shell
$taskLinks = @()
$taskStopped = $false
$taskStarted = $false
try {
    foreach ($taskLinkFile in (Get-ChildItem -LiteralPath ([Environment]::GetFolderPath('Startup')) -Filter '*.lnk' -File)) {
        $taskShortcut = $taskShell.CreateShortcut($taskLinkFile.FullName)
        if ($taskShortcut.TargetPath -ne $taskOld) { continue }
        if ($taskShortcut.Arguments -ne '') { throw 'Existing startup arguments require manual review.' }
        Copy-Item -LiteralPath $taskLinkFile.FullName -Destination $taskBackup
        $taskLinks += $taskLinkFile.FullName
    }
    $taskOldProcesses = @(Get-CimInstance Win32_Process -Filter "Name = 'CodexQuotaPills.exe'" |
        Where-Object { $_.ExecutablePath -eq $taskOld })
    $taskOldIds = @($taskOldProcesses | ForEach-Object { $_.ProcessId })
    $taskReaders = @(Get-CimInstance Win32_Process -Filter "Name = 'codex.exe'" | Where-Object {
        $_.ParentProcessId -in $taskOldIds -and $_.CommandLine -match '(?:^|\s)app-server(?:\s|$)'
    })
    $taskStopped = $true
    foreach ($taskProcess in ($taskOldProcesses | Sort-Object @{ Expression = { $_.CommandLine -like '*--codex-child*' } })) {
        $taskCurrent = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + $taskProcess.ProcessId)
        if ($taskCurrent -and $taskCurrent.ExecutablePath -eq $taskOld) { Stop-Process -Id $taskProcess.ProcessId }
    }
    foreach ($taskProcess in $taskReaders) {
        $taskCurrent = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + $taskProcess.ProcessId)
        if ($taskCurrent -and $taskCurrent.ParentProcessId -in $taskOldIds -and
            $taskCurrent.CommandLine -match '(?:^|\s)app-server(?:\s|$)') { Stop-Process -Id $taskProcess.ProcessId }
    }
    $taskSettings = Join-Path (Split-Path -Parent $taskOld) 'settings.ini'
    if (Test-Path -LiteralPath $taskSettings) {
        Copy-Item -LiteralPath $taskSettings -Destination (Join-Path (Split-Path -Parent $taskNew) 'settings.ini')
    }
    foreach ($taskLink in $taskLinks) {
        $taskShortcut = $taskShell.CreateShortcut($taskLink)
        if ($taskShortcut.TargetPath -ne $taskOld -or $taskShortcut.Arguments -ne '') { throw 'Startup target changed during update.' }
        $taskShortcut.TargetPath = $taskNew
        $taskShortcut.WorkingDirectory = Split-Path -Parent $taskNew
        $taskShortcut.Save()
        if ($taskShell.CreateShortcut($taskLink).TargetPath -ne $taskNew) { throw 'Startup switch failed.' }
    }
    Start-Process -FilePath $taskNew -WorkingDirectory (Split-Path -Parent $taskNew) -WindowStyle Hidden
    $taskStarted = $true
    $taskDeadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 500
        $taskRunning = @(Get-CimInstance Win32_Process -Filter "Name = 'CodexQuotaPills.exe'" | Where-Object { $_.ExecutablePath -eq $taskNew })
    } while ($taskRunning.Count -eq 0 -and [DateTime]::UtcNow -lt $taskDeadline)
    if ($taskRunning.Count -eq 0) { throw 'New executable did not stay running.' }
    @{ Success=$true; Version=$taskPlan.Version; StartupLinksUpdated=$taskLinks.Count } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskFolder 'result.json') -Encoding UTF8
}
catch {
    if ($taskStarted) {
        $taskNewProcesses = @(Get-CimInstance Win32_Process -Filter "Name = 'CodexQuotaPills.exe'" | Where-Object { $_.ExecutablePath -eq $taskNew })
        $taskNewIds = @($taskNewProcesses | ForEach-Object { $_.ProcessId })
        $taskNewReaders = @(Get-CimInstance Win32_Process -Filter "Name = 'codex.exe'" | Where-Object {
            $_.ParentProcessId -in $taskNewIds -and $_.CommandLine -match '(?:^|\s)app-server(?:\s|$)'
        })
        foreach ($taskProcess in $taskNewProcesses) { Stop-Process -Id $taskProcess.ProcessId -ErrorAction SilentlyContinue }
        foreach ($taskProcess in $taskNewReaders) { Stop-Process -Id $taskProcess.ProcessId -ErrorAction SilentlyContinue }
    }
    foreach ($taskLink in $taskLinks) {
        $taskCurrentLink = $taskShell.CreateShortcut($taskLink)
        if ($taskCurrentLink.TargetPath -eq $taskNew) {
            Copy-Item -LiteralPath (Join-Path $taskBackup ([IO.Path]::GetFileName($taskLink))) -Destination $taskLink -Force
        }
    }
    if ($taskStopped) { Start-Process -FilePath $taskOld -WorkingDirectory (Split-Path -Parent $taskOld) -WindowStyle Hidden }
    @{ Success=$false; Error='Update failed; old startup target restored where unchanged.' } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskFolder 'result.json') -Encoding UTF8
    throw
}
