param([Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$taskDist = Join-Path $taskRoot 'dist'
$taskExe = Join-Path $taskRoot 'bin\CodexQuotaPills.exe'
if ((Get-Item -LiteralPath $taskExe).VersionInfo.FileVersion -ne "$Version.0") { throw 'Executable version does not match release.' }
$taskPortable = Join-Path $taskDist "CodexQuotaPills-$Version-portable"
$taskSource = Join-Path $taskDist "CodexQuotaPills-$Version-source"
$taskPublicDocs = @('README.md','CHANGELOG.md','LICENSE','NOTICE.md','THIRD_PARTY_NOTICES.md','SECURITY.md','FALSE_POSITIVE_GUIDE.md')
# Explicit current product documents and illustrations, never the whole docs tree.
$taskProductDocs = @(
    'docs\desktop-ipc-compatibility.md',
    'docs\reset-radar-banner-preview.md',
    'docs\licenses\codex-auto-resume-MIT.txt',
    'docs\images\composer-pills-preview.png',
    'docs\images\unified-plus-menu-preview.png',
    'docs\images\codex-update-preview.png',
    'docs\images\auto-resume-menu-preview.png',
    'docs\images\auto-resume-preview.png',
    'docs\images\quota-hover-card-preview.png',
    'docs\images\reset-credits-hover-preview.png',
    'docs\images\placement-selector-preview.png',
    'docs\images\reset-radar-banner-preview.png',
    'docs\images\reset-radar-banner-preview@2x.png',
    'docs\images\reset-radar-banner-scheduled-preview.png',
    'docs\images\reset-radar-banner-scheduled-preview@2x.png',
    'docs\images\reset-radar-banner-close-preview.png',
    'docs\images\reset-radar-banner-close-preview@2x.png'
)
$taskInstallerAssets = @('app-icon.ico','app-icon.png','usage-cache.ini')
foreach ($taskRequired in @($taskPublicDocs + $taskProductDocs + @($taskInstallerAssets | ForEach-Object { 'installer-assets\' + $_ }))) {
    if (-not (Test-Path -LiteralPath (Join-Path $taskRoot $taskRequired) -PathType Leaf)) { throw "Missing public package input: $taskRequired" }
}
foreach ($taskPath in @($taskPortable, $taskSource, "$taskPortable.zip", "$taskSource.zip", (Join-Path $taskDist "SHA256SUMS-$Version.txt"))) {
    if (Test-Path -LiteralPath $taskPath) { throw "Independent release already exists: $taskPath" }
}
New-Item -ItemType Directory -Path $taskPortable,$taskSource | Out-Null
foreach ($taskName in $taskPublicDocs) {
    Copy-Item -LiteralPath (Join-Path $taskRoot $taskName) -Destination $taskPortable
    Copy-Item -LiteralPath (Join-Path $taskRoot $taskName) -Destination $taskSource
}
Copy-Item -LiteralPath $taskExe -Destination $taskPortable
Copy-Item -LiteralPath (Join-Path $taskRoot 'installer-assets\usage-cache.ini') -Destination $taskPortable
foreach ($taskItem in (Get-ChildItem -LiteralPath $taskRoot -File)) {
    if ($taskItem.Extension -in @('.cs','.ps1','.iss','.manifest') -or $taskItem.Name -in @('.gitignore','CONTRIBUTING.md','AGENT_INSTALL_PROMPT.md')) {
        Copy-Item -LiteralPath $taskItem.FullName -Destination $taskSource
    }
}
# Public source only; assets and docs have their own explicit lists below.
foreach ($taskFolder in @('tests','tools','MsixUpdater','.github')) {
    $taskFolderPath = Join-Path $taskRoot $taskFolder
    foreach ($taskItem in (Get-ChildItem -LiteralPath $taskFolderPath -File -Recurse)) {
        $taskRelative = $taskItem.FullName.Substring($taskRoot.Length + 1)
        if ($taskRelative -match '(^|\\)(bin|packages|\.tools|AutoResume)(\\|$)' -or
            $taskItem.Name -match '^(settings\.ini|snapshot\.txt|reset-radar-.*|auto-resume-readonly-check\.txt|ledger\.json.*)$' -or
            $taskItem.Extension -notin @('.cs','.ps1','.py','.yml','.yaml')) { continue }
        $taskTarget = Join-Path $taskSource $taskRelative
        New-Item -ItemType Directory -Path (Split-Path -Parent $taskTarget) -Force | Out-Null
        Copy-Item -LiteralPath $taskItem.FullName -Destination $taskTarget
    }
}
foreach ($taskName in $taskInstallerAssets) {
    $taskTarget = Join-Path $taskSource ('installer-assets\' + $taskName)
    New-Item -ItemType Directory -Path (Split-Path -Parent $taskTarget) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskRoot ('installer-assets\' + $taskName)) -Destination $taskTarget
}
foreach ($taskRelative in $taskProductDocs) {
    foreach ($taskFolderPath in @($taskPortable,$taskSource)) {
        $taskTarget = Join-Path $taskFolderPath $taskRelative
        New-Item -ItemType Directory -Path (Split-Path -Parent $taskTarget) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $taskRoot $taskRelative) -Destination $taskTarget
    }
}
foreach ($taskFolderPath in @($taskPortable,$taskSource)) {
    Compress-Archive -LiteralPath $taskFolderPath -DestinationPath "$taskFolderPath.zip" -CompressionLevel Optimal
}
$taskLines = foreach ($taskZip in @("$taskPortable.zip","$taskSource.zip")) {
    $taskHash = Get-FileHash -LiteralPath $taskZip -Algorithm SHA256
    '{0}  {1}' -f $taskHash.Hash.ToLowerInvariant(), (Split-Path -Leaf $taskZip)
}
$taskLines | Set-Content -LiteralPath (Join-Path $taskDist "SHA256SUMS-$Version.txt") -Encoding ascii
$taskLines
