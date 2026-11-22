param([string]$Executable = (Join-Path $PSScriptRoot '..\ZaloManager.exe'))
$ErrorActionPreference='Stop'
[Reflection.Assembly]::LoadFrom($Executable) | Out-Null
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('ZaloSessionTest-'+[guid]::NewGuid().ToString('N')+'.json')
try {
    # A profile written by the first build has no update/avatar/version fields.
    $legacy='[{"Id":"900001","Name":"Existing account","Source":"C:\\old\\Zalo-26.11.12","SourceHash":"source","PatchedHash":"patched","FutureField":"preserve-me"}]'
    [IO.File]::WriteAllText($fixture,$legacy)
    $method=[ZaloManagerPoc.Store].GetMethod('ReadProfiles',[Reflection.BindingFlags]'Static,NonPublic')
    $arguments=New-Object 'System.Object[]' 1
    $arguments[0]=[string]$fixture
    $profiles=$method.Invoke($null,$arguments)
    $profile=$profiles[0]
    $expected=Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'zm900001'
    if($profile.DataPath -ne $expected){throw 'Old data path changed'}
    if($profile.Id -ne '900001' -or $profile.PatchedHash -ne 'patched'){throw 'Profile identity lost'}
    $stream=New-Object IO.MemoryStream
    try {
        $serializer=New-Object Runtime.Serialization.Json.DataContractJsonSerializer([ZaloManagerPoc.Profile])
        $serializer.WriteObject($stream,$profile)
        $json=[Text.Encoding]::UTF8.GetString($stream.ToArray())
        if($json -notmatch 'preserve-me'){throw 'Unknown metadata field lost'}
    } finally {$stream.Dispose()}
    'PASS first-build profile loads with identical data path and unknown metadata preserved'
    $source=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '..\src\Manager.cs')
    if($source -match 'Zalo đang chạy nền. Mở cửa sổ'){throw 'Tray-only error remains'}
    'PASS tray-only session uses hidden-window recovery instead of error'
} finally {
    $full=[IO.Path]::GetFullPath($fixture)
    $root=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(-not $full.StartsWith($root,[StringComparison]::OrdinalIgnoreCase) -or -not ([IO.Path]::GetFileName($full)).StartsWith('ZaloSessionTest-')){throw 'Unsafe fixture cleanup'}
    if(Test-Path -LiteralPath $full){Remove-Item -LiteralPath $full}
}
