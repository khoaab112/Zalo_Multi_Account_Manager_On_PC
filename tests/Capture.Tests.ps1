$ErrorActionPreference='Stop'
[Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot '..\ZaloManager.Poc.Fixes.exe')) | Out-Null
$profile=New-Object ZaloManagerPoc.Profile
$profile.Id='998855'; $profile.Name='Smoke test'; $profile.Source=[ZaloManagerPoc.Instance]::DetectSource()
$profile.ActiveDirectory=Join-Path ([ZaloManagerPoc.Store]::Root) ('Smoke-'+[guid]::NewGuid().ToString('N'))
if(Test-Path -LiteralPath $profile.DataPath){throw 'Test data exists'}
$observed=New-Object 'Collections.Generic.HashSet[int]'
try {
    [ZaloManagerPoc.Instance]::Prepare($profile)
    [ZaloManagerPoc.Instance]::Open($profile)
    1..12 | ForEach-Object {
        Start-Sleep -Seconds 2
        Get-Process -Name ZaloCap -ErrorAction SilentlyContinue | ForEach-Object {
            try { $path=$_.Path; if($path -and $path.StartsWith($profile.ActiveDirectory+'\',[StringComparison]::OrdinalIgnoreCase)){[void]$observed.Add($_.Id)} }
            finally {$_.Dispose()}
        }
    }
    $processes=[ZaloManagerPoc.Instance]::Processes($profile)
    $hasWindow=@($processes | Where-Object {$_.MainWindowHandle -ne 0}).Count -gt 0
    foreach($process in $processes){$process.Dispose()}
    if(-not $hasWindow){throw 'Clone window missing'}
    if($observed.Count -gt 2){throw "Capture still restarting: $($observed.Count) PIDs"}
    "PASS clone window running; $($observed.Count) capture PIDs observed in 24 seconds"
} finally {
    [ZaloManagerPoc.Instance]::Stop($profile)
    Start-Sleep -Seconds 2
    if([ZaloManagerPoc.Instance]::IsRunning($profile)){throw 'Probe running, cleanup deferred'}
    $root=[IO.Path]::GetFullPath([ZaloManagerPoc.Store]::Root)+'\'
    $program=[IO.Path]::GetFullPath($profile.ActiveDirectory)
    if(-not $program.StartsWith($root,[StringComparison]::OrdinalIgnoreCase) -or -not ([IO.Path]::GetFileName($program)).StartsWith('Smoke-')){throw 'Unsafe program cleanup'}
    if(Test-Path -LiteralPath $program){Remove-Item -LiteralPath $program -Recurse -Force}
    $data=Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'zm998855'
    if([IO.Path]::GetFullPath($profile.DataPath) -ne [IO.Path]::GetFullPath($data)){throw 'Unsafe data cleanup'}
    if(Test-Path -LiteralPath $data){Remove-Item -LiteralPath $data -Recurse -Force}
}
