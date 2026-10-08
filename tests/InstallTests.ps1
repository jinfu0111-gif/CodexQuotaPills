#requires -Version 5.1
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$installer = Join-Path $project 'install.ps1'
. $installer -FunctionsOnly
$fixtureExe = Join-Path $project 'bin\CodexQuotaPills.exe'
if (-not (Test-Path -LiteralPath $fixtureExe)) { throw 'Run build.ps1 before installation tests.' }
$fixtureVersion = (Get-Item -LiteralPath $fixtureExe).VersionInfo.FileVersion -replace '\.0$',''
$unicodeName = [string][char]0x4E2D + [char]0x6587
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('Quota InstallTests ' + $unicodeName + ' ' + [guid]::NewGuid().ToString('N'))
$testRoot = Assert-InstallPath $testRoot
New-Item -ItemType Directory -Path $testRoot | Out-Null
$passed = 0
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message }; $script:passed++ }
function Reject([scriptblock]$Action, [string]$Message) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    Check $rejected $Message
}
function Write-TestZip([string]$Path, [string[]]$Names, [int]$Attributes = 0) {
    $archive = [IO.Compression.ZipFile]::Open($Path,[IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in $Names) {
            $entry = $archive.CreateEntry($name)
            if ($Attributes) { $entry.ExternalAttributes = $Attributes }
            $writer = New-Object IO.StreamWriter ($entry.Open())
            try { $writer.Write('test') } finally { $writer.Dispose() }
        }
    } finally { $archive.Dispose() }
}
try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    Add-Type -AssemblyName System.IO.Compression
    $prefix = "CodexQuotaPills-$fixtureVersion-portable/"
    $packageFolder = Join-Path $testRoot $prefix.TrimEnd('/')
    New-Item -ItemType Directory -Path $packageFolder | Out-Null
    Copy-Item -LiteralPath $fixtureExe -Destination $packageFolder
    foreach ($name in @('LICENSE','NOTICE.md','THIRD_PARTY_NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $project $name) -Destination $packageFolder }
    $zip = Join-Path $testRoot 'fixture.zip'
    # Windows PowerShell Compress-Archive may emit backslash paths. Release
    # archives use forward slashes; build that format explicitly on both hosts.
    $fixtureArchive = [IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in (Get-ChildItem -LiteralPath $packageFolder -File)) {
            $entry = $fixtureArchive.CreateEntry($prefix + $file.Name)
            $inputStream = [IO.File]::OpenRead($file.FullName)
            $entryStream = $entry.Open()
            try { $inputStream.CopyTo($entryStream) } finally { $inputStream.Dispose(); $entryStream.Dispose() }
        }
    } finally { $fixtureArchive.Dispose() }
    $release = [ordered]@{
        version=$fixtureVersion
        url=('https://github.com/jinfu0111-gif/CodexQuotaPills/releases/download/v{0}/CodexQuotaPills-{0}-portable.zip' -f $fixtureVersion)
        size=(Get-Item -LiteralPath $zip).Length
        sha256=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $bootstrap = Join-Path $testRoot 'bootstrap'
    New-Item -ItemType Directory -Path $bootstrap | Out-Null
    Copy-Item -LiteralPath $installer -Destination $bootstrap
    $release | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $bootstrap 'install-release.json') -Encoding UTF8
    $testInstaller = Join-Path $bootstrap 'install.ps1'
    $destination = Join-Path $testRoot 'install'
    $first = & $testInstaller -InstallRoot $destination -PackagePath $zip -NoLaunch -NoShortcuts
    Check ($first.Installed -and $first.PackageVerified -and -not $first.ReusedExisting) 'Fresh installation failed.'
    Check (-not $first.LaunchRequested -and $null -eq $first.ConnectionReady -and -not $first.Visible) 'NoLaunch falsely reports runtime verification.'
    Check ((Get-FileHash -LiteralPath $first.Executable).Hash -eq (Get-FileHash -LiteralPath $fixtureExe).Hash) 'Installed executable changed.'
    $settingsPath = Join-Path (Split-Path -Parent $first.Executable) 'settings.ini'
    'Placement=Composer' | Set-Content -LiteralPath $settingsPath -Encoding UTF8
    $settingsHash = (Get-FileHash -LiteralPath $settingsPath).Hash
    $second = & $testInstaller -InstallRoot $destination -PackagePath $zip -NoLaunch -NoShortcuts
    Check ($second.ReusedExisting -and $second.Executable -eq $first.Executable) 'Repeated install did not reuse verified version.'
    Check ((Get-FileHash -LiteralPath $settingsPath).Hash -eq $settingsHash) 'Repeated install changed display settings.'
    $manifestPath = Join-Path $bootstrap 'install-release.json'
    $release.sha256 = '0' * 64
    $release | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    $badDestination = Join-Path $testRoot 'wrong-hash'
    Reject { & $testInstaller -InstallRoot $badDestination -PackagePath $zip -NoLaunch -NoShortcuts } 'Bad hash accepted.'
    Check (-not (Test-Path -LiteralPath (Join-Path $badDestination 'versions'))) 'Bad hash wrote installation files.'
    $release.sha256 = (Get-FileHash -LiteralPath $zip).Hash
    $release.url = 'https://example.com/package.zip'
    Reject { Assert-InstallRelease ([pscustomobject]$release) } 'Untrusted URL accepted.'
    $metadata = [pscustomobject]@{
        draft=$false; prerelease=$false; tag_name='v0.2.24'
        html_url='https://github.com/jinfu0111-gif/CodexQuotaPills/releases/tag/v0.2.24'
        assets=@([pscustomobject]@{name='CodexQuotaPills-0.2.24-portable.zip';state='uploaded';digest=('sha256:' + ('a' * 64));size=100;browser_download_url='https://github.com/jinfu0111-gif/CodexQuotaPills/releases/download/v0.2.24/CodexQuotaPills-0.2.24-portable.zip'})
    }
    Check ((Convert-InstallRelease $metadata).version -eq '0.2.24') 'Stable metadata rejected.'
    $metadata.prerelease = $true
    Reject { Convert-InstallRelease $metadata } 'Prerelease accepted.'
    $metadata.prerelease = $false
    $metadata.assets += $metadata.assets[0]
    Reject { Convert-InstallRelease $metadata } 'Duplicate asset accepted.'
    $maliciousNames = @(
        @($prefix+'../escaped.txt'), @($prefix+'CON.txt'), @($prefix+'name. '),
        @($prefix+'data.txt:stream'), @($prefix+'dir\file.txt'), @('other/file.txt'), @($prefix+'dir//file.txt'),
        @(($prefix+'same.txt'),($prefix+'SAME.txt'))
    )
    $case = 0
    foreach ($names in $maliciousNames) {
        $case++
        $badZip = Join-Path $testRoot "bad-$case.zip"
        Write-TestZip $badZip $names
        $badTarget = Join-Path $testRoot "extract-$case"
        Reject { Expand-InstallPackage $badZip $badTarget $fixtureVersion } "Unsafe archive $case accepted."
        Check (-not (Test-Path -LiteralPath $badTarget)) "Unsafe archive $case wrote files before validation."
    }
    $linkZip = Join-Path $testRoot 'symlink.zip'
    Write-TestZip $linkZip @($prefix+'link') (-1610612736)
    Reject { Expand-InstallPackage $linkZip (Join-Path $testRoot 'symlink-target') $fixtureVersion } 'Symlink accepted.'
    $extractTarget = Join-Path $testRoot 'valid-extract'
    Expand-InstallPackage $zip $extractTarget $fixtureVersion
    Check (Test-Path -LiteralPath (Join-Path $extractTarget 'CodexQuotaPills.exe')) 'Valid archive extraction failed.'
    Reject { Expand-InstallPackage $zip $extractTarget $fixtureVersion } 'Existing extraction directory overwritten.'
    # A higher-version local executable must never be replaced by the pinned ZIP.
    'using System.Reflection; [assembly: AssemblyFileVersion("99.0.0.0")] public class InstallNewerFixture { public static void Main() {} }' | Set-Content -LiteralPath (Join-Path $testRoot 'newer.cs') -Encoding ascii
    & (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:exe ('/out:' + (Join-Path $testRoot 'newer.exe')) (Join-Path $testRoot 'newer.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Version fixture build failed.' }
    Copy-Item -LiteralPath (Join-Path $testRoot 'newer.exe') -Destination $first.Executable -Force
    $release.url = ('https://github.com/jinfu0111-gif/CodexQuotaPills/releases/download/v{0}/CodexQuotaPills-{0}-portable.zip' -f $fixtureVersion)
    $release | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Reject { & $testInstaller -InstallRoot $destination -PackagePath $zip -NoLaunch -NoShortcuts } 'Newer local version downgraded.'
    Check ((Get-Item -LiteralPath $first.Executable).VersionInfo.FileVersion -eq '99.0.0.0') 'Higher-version executable changed.'
    $testLedger = Join-Path $testRoot 'ledger\ledger.json'
    Check ((Initialize-InstallLedger $testLedger) -eq 'paused-on-first-install') 'Fresh continuation ledger was not paused.'
    $initial = Get-Content -LiteralPath $testLedger -Raw | ConvertFrom-Json
    Check ($initial.version -eq 2 -and $initial.enabled -eq $false) 'Fresh ledger schema invalid.'
    '{"version":2,"enabled":true,"accountHash":"private-test","outcomes":{"test":"cancelled"},"counts":{"test":3},"fingerprints":{}}' | Set-Content -LiteralPath $testLedger -Encoding UTF8
    $ledgerHash = (Get-FileHash -LiteralPath $testLedger).Hash
    Check ((Initialize-InstallLedger $testLedger) -eq 'unchanged') 'Existing continuation ledger reinitialized.'
    Check ((Get-FileHash -LiteralPath $testLedger).Hash -eq $ledgerHash) 'Existing continuation preferences or records changed.'
    $shortcutPath = Join-Path $testRoot 'Quota.lnk'
    $unicodeTarget = Join-Path $packageFolder 'CodexQuotaPills.exe'
    Save-InstallShortcut $shortcutPath $unicodeTarget
    Save-InstallShortcut $shortcutPath $unicodeTarget
    Check (@(Get-ChildItem -LiteralPath $testRoot -Filter '*.bak').Count -eq 0) 'Repeated shortcut created unnecessary backups.'
    $savedShortcut = Get-InstallShortcut $shortcutPath
    Check ($savedShortcut.TargetPath -eq $unicodeTarget) 'Unicode shortcut target incorrect.'
    Check ($savedShortcut.WorkingDirectory -eq $packageFolder -and -not $savedShortcut.Arguments) 'Unicode shortcut working directory or arguments changed.'
    Write-Output "Installation tests passed: $passed"
} finally {
    # Only delete the newly-created, resolved task directory below system Temp.
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $cleanupPath = Assert-InstallPath $testRoot
    if (-not $cleanupPath.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path -Leaf $cleanupPath) -notlike 'Quota InstallTests *') { throw 'Refusing test cleanup outside task Temp.' }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}
