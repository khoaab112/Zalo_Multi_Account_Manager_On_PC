param([string]$Executable=(Join-Path $PSScriptRoot '..\ZaloManager.exe'))
$ErrorActionPreference='Stop'
[Reflection.Assembly]::LoadFrom($Executable)|Out-Null
$profile=New-Object ZaloManagerPoc.Profile
$profile.Id='998864'
$data=$profile.DataPath
$app=Join-Path ([ZaloManagerPoc.Store]::Root) 'Instances\998864'
if((Test-Path -LiteralPath $data) -or (Test-Path -LiteralPath $app)){throw 'Fixture already exists'}
try {
    New-Item -ItemType Directory -Path $data,$app | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $data 'data.bin'),(New-Object byte[] 8192))
    [IO.File]::WriteAllBytes((Join-Path $app 'app.bin'),(New-Object byte[] 16384))
    $profile.ActiveDirectory=$app
    $usage=[ZaloManagerPoc.StorageUsage]::Measure($profile)
    if($usage.Incomplete -or $usage.DataBytes -lt 8192 -or $usage.AppBytes -lt 16384){throw 'Data or application not measured'}
    if($usage.AppBytes -ge 32768){throw 'Current and legacy application path counted twice'}
    if([ZaloManagerPoc.StorageMeasurement]::Format(1073741824) -ne '1 GB'){throw 'Unit formatting failed'}
    'PASS data plus clone binaries, no duplicate roots, readable units'
} finally {
    foreach($path in @($data,$app)) {
        $full=[IO.Path]::GetFullPath($path)
        if($full -ne [IO.Path]::GetFullPath($data) -and $full -ne [IO.Path]::GetFullPath($app)){throw 'Unsafe cleanup'}
        if(Test-Path -LiteralPath $full){Remove-Item -LiteralPath $full -Recurse -Force}
    }
}
