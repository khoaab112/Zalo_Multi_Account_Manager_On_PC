param([string]$Executable = (Join-Path $PSScriptRoot '..\ZaloManager.exe'))
$ErrorActionPreference = 'Stop'
[Reflection.Assembly]::LoadFrom($Executable) | Out-Null
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('ZaloManagerUpdateTest-' + [guid]::NewGuid().ToString('N'))
$testId = '998877'
$releaseRoot = Join-Path ([ZaloManagerPoc.Store]::Root) "Releases\$testId"
if (Test-Path -LiteralPath $releaseRoot) { throw 'Test release directory already exists.' }
function Assert($condition, $message) { if (-not $condition) { throw $message } }
function Write-TestArchive($path, $payloadText) {
    $header = [Text.Encoding]::UTF8.GetBytes('{"files":{}}')
    $payload = [Text.Encoding]::ASCII.GetBytes($payloadText)
    $offset = 16 + $header.Length
    $archive = [byte[]]::new($offset + $payload.Length)
    [BitConverter]::GetBytes([uint32]4).CopyTo($archive,0)
    [BitConverter]::GetBytes([uint32]($offset-8)).CopyTo($archive,4)
    [BitConverter]::GetBytes([uint32]$header.Length).CopyTo($archive,12)
    $header.CopyTo($archive,16)
    $payload.CopyTo($archive,$offset)
    [IO.File]::WriteAllBytes($path,$archive)
}
try {
    $original = [ZaloManagerPoc.Original]::Detect()
    Assert ($original.Version -and $original.PackageHash) 'Original detection failed'
    Assert ($original.DirectoryPath -notlike '*ZaloManagerPoC*') 'Clone was selected as original'
    'PASS original detection and VNG signature verification'

    $source = Join-Path $testRoot 'Source'
    New-Item -ItemType Directory -Path (Join-Path $source 'resources') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $original.DirectoryPath 'Zalo.exe') -Destination $source
    $asar = Join-Path $source 'resources\app.asar'
    Write-TestArchive $asar 'ZaloData com.vng.zalo PipeZCallSend PipeZCallRecv generation-one'
    $profile = [ZaloManagerPoc.Profile]::new()
    $profile.Id = $testId; $profile.Name = 'Regression test'; $profile.Source = $source
    $profile.ActiveDirectory = Join-Path $testRoot 'OldGeneration'
    [ZaloManagerPoc.Instance]::Prepare($profile)
    $oldHash = [ZaloManagerPoc.Instance]::Hash((Join-Path $profile.DirectoryPath 'resources\app.asar'))

    Write-TestArchive $asar 'ZaloData com.vng.zalo PipeZCallSend PipeZCallRecv generation-two'
    $nextOriginal = [ZaloManagerPoc.OriginalInstallation]::new()
    $nextOriginal.DirectoryPath = $source
    $nextOriginal.Version = $original.Version
    $nextOriginal.PackageHash = [ZaloManagerPoc.Instance]::Hash($asar)
    $next = [ZaloManagerPoc.Instance]::PrepareUpdate($profile, $nextOriginal)
    Assert ($next.DirectoryPath -ne $profile.DirectoryPath) 'Update overwrote old generation'
    Assert ($next.SourceHash -eq $nextOriginal.PackageHash) 'New source hash mismatch'
    Assert ($next.DataPath -eq $profile.DataPath) 'Update changed data identity'
    Assert ($next.Id -eq $profile.Id) 'Update changed profile identity'
    Assert ([ZaloManagerPoc.Instance]::Hash((Join-Path $profile.DirectoryPath 'resources\app.asar')) -eq $oldHash) 'Old generation was modified'
    Assert ([ZaloManagerPoc.Instance]::Hash($asar) -eq $nextOriginal.PackageHash) 'Original package was modified'
    'PASS side-by-side update, preserved identity/data path and unchanged old/original binaries'

    Write-TestArchive $asar 'unsupported-format-without-markers'
    $failed = $false
    try { [ZaloManagerPoc.Instance]::PrepareUpdate($profile, $nextOriginal) | Out-Null } catch { $failed = $true }
    Assert $failed 'Unsupported package was accepted'
    Assert ([ZaloManagerPoc.Instance]::Hash((Join-Path $profile.DirectoryPath 'resources\app.asar')) -eq $oldHash) 'Failed update damaged old generation'
    'PASS incompatible update rejected and previous generation preserved'
} finally {
    # Only directories uniquely owned by this test are removed.
    $tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $resolvedTest = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolvedTest.StartsWith($tempParent,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path' }
    if (Test-Path -LiteralPath $resolvedTest) { Remove-Item -LiteralPath $resolvedTest -Recurse -Force }
    $expectedRelease = [IO.Path]::GetFullPath((Join-Path ([ZaloManagerPoc.Store]::Root) 'Releases')) + '\'
    $resolvedRelease = [IO.Path]::GetFullPath($releaseRoot)
    if (-not $resolvedRelease.StartsWith($expectedRelease,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe release cleanup path' }
    if (Test-Path -LiteralPath $resolvedRelease) { Remove-Item -LiteralPath $resolvedRelease -Recurse -Force }
}
