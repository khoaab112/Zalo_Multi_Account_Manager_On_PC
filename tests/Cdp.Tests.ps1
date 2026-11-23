param([string]$Executable = (Join-Path $PSScriptRoot '..\ZaloManager.exe'))
$ErrorActionPreference = 'Stop'
[Reflection.Assembly]::LoadFrom($Executable) | Out-Null
$profile = New-Object ZaloManagerPoc.Profile
$profile.Id = '900001'
$profile.ActiveDirectory = 'C:\Zalo Test\clone'
$secondProfile = New-Object ZaloManagerPoc.Profile
$secondProfile.Id = '900002'
$secondProfile.ActiveDirectory = 'C:\Zalo Test\clone2'
$method = [ZaloManagerPoc.CdpMessageWatcher].GetMethod('MatchesProfileTarget', [Reflection.BindingFlags]'Static,NonPublic')
$cases = @(
    @('file:///C:/Zalo%20Test/clone/resources/app.asar/pc-dist/index.html?type=24&auth=https%3A%2F%2Fexample.com', $true),
    @('file:///C:/Zalo%20Test/clone/resources/app.asar/pc-dist/index.html#main', $true),
    @('file:///C:/other/resources/app.asar/pc-dist/index.html', $false),
    @('file:///C:/Zalo%20Test/clone/resources/app.asar/pc-dist/shared-worker.html', $false),
    @('https://example.com/index.html', $false)
)
foreach ($case in $cases) {
    $arguments = New-Object 'System.Object[]' 2
    $arguments[0] = $profile.PSObject.BaseObject
    $arguments[1] = [string]$case[0]
    if ($method.Invoke($null, $arguments) -ne $case[1]) { throw ('Target matching failed for ' + $case[0]) }
}
$target = 'file:///C:/Zalo%20Test/clone2/resources/app.asar/pc-dist/index.html'
if ($method.Invoke($null, @($profile.PSObject.BaseObject, $target)) -or
    !$method.Invoke($null, @($secondProfile.PSObject.BaseObject, $target))) { throw 'CDP target leaked across clone profiles' }
if ($profile.DataPath -eq $secondProfile.DataPath -or $profile.Executable -eq $secondProfile.Executable) {
    throw 'Clone launch paths or data paths overlap'
}
$ports = @([ZaloManagerPoc.CdpMessageWatcher]::AllocatePort(), [ZaloManagerPoc.CdpMessageWatcher]::AllocatePort())
foreach ($port in $ports) {
    if ($port -lt 1 -or $port -gt 65535) { throw 'Allocated port out of range' }
}
[ZaloManagerPoc.CdpMessageWatcher]::Stop($profile)
[ZaloManagerPoc.CdpMessageWatcher]::StopAll()
'PASS Chromium file URLs with query strings, profile isolation, valid ports and empty shutdown'
