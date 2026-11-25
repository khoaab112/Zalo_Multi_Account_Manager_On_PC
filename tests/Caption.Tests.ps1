param(
    [string]$Executable = (Join-Path $PSScriptRoot '..\ZaloManager.exe'),
    $TestProfile
)
$ErrorActionPreference='Stop'
[Reflection.Assembly]::LoadFrom($Executable) | Out-Null
$code=@"
using System;using System.Runtime.InteropServices;
public static class NativeCaptionTest {
 [StructLayout(LayoutKind.Sequential)]public struct Rect{public int Left,Top,Right,Bottom;}
 [DllImport("dwmapi.dll")]public static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out Rect rect,int size);
 [DllImport("user32.dll")]public static extern bool PostMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
 [DllImport("user32.dll")]public static extern bool ShowWindowAsync(IntPtr window,int command);
 [DllImport("user32.dll")]public static extern bool IsIconic(IntPtr window);
 [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr window);
}
"@
Add-Type -TypeDefinition $code
$profile=$TestProfile
if(-not $profile) {
    $profile=[ZaloManagerPoc.Store]::Load() | Where-Object { -not $_.IsOriginal -and [ZaloManagerPoc.Instance]::IsRunning($_) } | Select-Object -First 1
}
if(-not $profile){throw 'No running clone for caption test'}
[ZaloManagerPoc.Instance]::Open($profile)
$processes=[ZaloManagerPoc.Instance]::Processes($profile)
$ids=[int[]]@($processes | Where-Object ProcessName -eq Zalo | ForEach-Object Id)
$windows=[ZaloManagerPoc.WindowActivation]::Find($ids)
foreach($process in $processes){$process.Dispose()}
if(!$windows.Count){throw 'No Chrome window for the selected clone'}
$window=$windows[0]
$readTitle=[ZaloManagerPoc.WindowActivation].GetMethod('Title',[Reflection.BindingFlags]'Static,NonPublic')
[ZaloManagerPoc.ProfileActions]::ApplyAvatar($profile)
if($readTitle.Invoke($null,@($window)) -ne ('Zalo - '+$profile.Name)){throw 'Native caption does not match the Manager profile name'}
'PASS Sandboxie clone native caption uses its Manager name'
$caption=[NativeCaptionTest+Rect]::new()
$status=[NativeCaptionTest]::DwmGetWindowAttribute($window,5,[ref]$caption,16)
if($status -eq 0 -and $caption.Right-$caption.Left -gt 0){
    'INFO Zalo exposes native caption bounds on this screen'
} else {
    'INFO Zalo draws its own caption controls on this screen'
}
try {
    [void][NativeCaptionTest]::ShowWindowAsync($window,6)
    Start-Sleep -Milliseconds 700
    if(-not [NativeCaptionTest]::IsIconic($window)){throw 'Native minimize failed'}
    'PASS minimize changes clone to minimized state'
    if(-not [ZaloManagerPoc.WindowActivation]::Restore($profile)){throw 'Manager failed to restore the minimized clone'}
    [void][NativeCaptionTest]::PostMessage($window,0x10,[IntPtr]::Zero,[IntPtr]::Zero)
    Start-Sleep -Milliseconds 700
    if([NativeCaptionTest]::IsWindowVisible($window)){throw 'Close did not hide clone window'}
    'PASS close hides clone window'
} finally {
    $open=[ZaloManagerPoc.Instance].GetMethod('OpenAndCheckWindow',[Reflection.BindingFlags]'Static,NonPublic')
    if(!$open.Invoke($null,(,$profile.PSObject.BaseObject))){throw 'Manager failed to reopen the closed or hidden clone window'}
}
'PASS Manager reopens the Zalo window'
