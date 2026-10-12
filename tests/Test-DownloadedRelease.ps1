param([Parameter(Mandatory=$true)][ValidatePattern('^v\d+\.\d+\.\d+$')][string]$ReleaseTag)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$work = Join-Path ([IO.Path]::GetTempPath()) ('Batchwork-release-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$appProcess = $null
try {
    gh release download $ReleaseTag --repo informent/Batchwork --dir $work --pattern 'Batchwork-*-setup.exe' --pattern 'Batchwork-*-win-x64.zip' --pattern SHA256SUMS
    if ($LASTEXITCODE -ne 0) { throw 'Release asset download failed' }
    $expected = @(('Batchwork-' + $ReleaseTag + '-setup.exe'), ('Batchwork-' + $ReleaseTag + '-win-x64.zip'))
    $hashes = @{}
    foreach ($line in [IO.File]::ReadAllLines((Join-Path $work 'SHA256SUMS'))) {
        if ($line -notmatch '^([0-9a-fA-F]{64})  ([A-Za-z0-9._-]+)$') { throw 'Invalid checksum manifest' }
        if ($hashes.ContainsKey($Matches[2])) { throw 'Duplicate checksum entry' }
        $hashes[$Matches[2]] = $Matches[1].ToLowerInvariant()
    }
    foreach ($name in $expected) {
        $actual = (Get-FileHash -LiteralPath (Join-Path $work $name) -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $hashes[$name]) { throw 'Downloaded asset digest mismatch' }
    }
    $portable = Join-Path $work 'portable'
    New-Item -ItemType Directory -Path $portable | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $work $expected[1]))
    try {
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName -ne $entry.Name -or $entry.Name -notmatch '^[A-Za-z0-9._-]+$') { throw 'Unexpected portable archive path' }
        }
    } finally { $archive.Dispose() }
    [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $work $expected[1]), $portable)
    $exe = Join-Path $portable 'Batchwork.exe'
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    if ($info.ProductName -ne 'Batchwork' -or $info.ProductVersion.Split('+')[0] -ne $ReleaseTag.Substring(1)) { throw 'Published executable version or branding mismatch' }
    $appProcess = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru
    Start-Sleep -Seconds 3
    if ($appProcess.HasExited) { throw 'Published portable app exited during startup' }
    Stop-Process -Id $appProcess.Id -Force
    $appProcess.WaitForExit()
    $appProcess = $null
    & (Join-Path $PSScriptRoot 'Test-Installer.ps1') -Setup (Join-Path $work $expected[0])
    $evidence = [ordered]@{ product = 'Batchwork'; tag = $ReleaseTag; assetSha256 = $hashes; portableSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant(); portableLaunch = $true; installerLifecycle = $true; verifiedUtc = [DateTime]::UtcNow.ToString('o') }
    $evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'downloaded-release-evidence.json') -Encoding utf8
    Write-Output 'PASS: exact downloaded assets, checksums, executable version, launch and installer lifecycle'
}
finally {
    if ($null -ne $appProcess -and !$appProcess.HasExited) { Stop-Process -Id $appProcess.Id -Force; $appProcess.WaitForExit() }
    $resolved = [IO.Path]::GetFullPath($work)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or !([IO.Path]::GetFileName($resolved)).StartsWith('Batchwork-release-')) { throw 'Unsafe cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
