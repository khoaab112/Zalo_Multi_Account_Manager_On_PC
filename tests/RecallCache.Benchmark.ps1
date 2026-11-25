param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [ValidateSet(2,5,10)][int]$Profiles = 2,
    [string]$FixtureRoot
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
if (!$FixtureRoot) {
    $worker = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $root = Join-Path ([IO.Path]::GetTempPath()) ('ZaloCacheBench-' + [guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($root) | Out-Null
    try {
        & $worker -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -Executable $Executable -Profiles $Profiles -FixtureRoot $root
        if ($LASTEXITCODE) { throw 'Cache benchmark failed' }
    } finally {
        foreach ($file in @('fixture-cache.dat','fixture-cache.dat.tmp')) {
            [IO.File]::Delete((Join-Path $root $file))
        }
        [IO.Directory]::Delete($root)
    }
    return
}

Add-Type -AssemblyName PresentationFramework,System.Runtime.Serialization,System.Security
[Reflection.Assembly]::LoadFrom($Executable) | Out-Null
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$watcher = [ZaloManagerPoc.CdpMessageWatcher]
$cache = $watcher.GetNestedType('IncomingMessageCache',[Reflection.BindingFlags]::NonPublic)
$type = $watcher.GetNestedType('CachedIncomingMessage',[Reflection.BindingFlags]::NonPublic)
$file = Join-Path $FixtureRoot 'fixture-cache.dat'
$cache.GetField('CachePath',$flags).SetValue($null,$file)
$watcher.GetField('RecallSettingsPath',$flags).SetValue($null,(Join-Path $FixtureRoot 'settings.fixture'))
$store = $cache.GetMethod('Store')
$flush = $cache.GetMethod('Flush')
$avatar = 'A' * 6000
$storeTimes = [Collections.Generic.List[double]]::new()
$flushTimes = [Collections.Generic.List[double]]::new()
$clock = [Diagnostics.Stopwatch]::new()

for ($i=0; $i -lt 1800; $i++) {
    $owner = if (($i % 10) -lt 7) { 0 } else { 1 + ([int][Math]::Floor($i / 10) % ($Profiles - 1)) }
    $item = [Activator]::CreateInstance($type)
    $item.Id = 'fixture-' + $i
    $item.ProfileId = 'profile-' + $owner
    $item.ConversationId = 'chat-' + ($i % 20)
    $item.ConversationName = 'Fixture'
    $item.Content = 'Synthetic cache benchmark'
    $item.AvatarPng = $avatar
    $item.Timestamp = [DateTimeOffset]::UtcNow
    $clock.Restart()
    $stored = $store.Invoke($null,@($item))
    $storeTimes.Add($clock.Elapsed.TotalMilliseconds)
    if (!$stored) { throw 'Cache rejected synthetic input' }
    if (($i % 150) -eq 149) {
        $clock.Restart()
        if (!$flush.Invoke($null,@())) { throw 'Cache flush failed' }
        $flushTimes.Add($clock.Elapsed.TotalMilliseconds)
    }
}

function Percentile95($samples) {
    $ordered = @($samples | Sort-Object)
    [Math]::Round($ordered[([int][Math]::Ceiling($ordered.Count * 0.95)) - 1], 3)
}
$process = [Diagnostics.Process]::GetCurrentProcess()
[pscustomobject]@{
    Build = [IO.Path]::GetFileName($Executable)
    Profiles = $Profiles
    Rows = $storeTimes.Count
    StoreP95Ms = Percentile95 $storeTimes
    FlushP95Ms = Percentile95 $flushTimes
    SnapshotBytes = [IO.FileInfo]::new($file).Length
    ProcessPeakMiB = [Math]::Round($process.PeakWorkingSet64 / 1MB,1)
} | ConvertTo-Json -Compress
