param([string]$Executable = (Join-Path $PSScriptRoot '..\ZaloManager.Controls.exe'))
$ErrorActionPreference='Stop'
[Reflection.Assembly]::LoadFrom($Executable) | Out-Null
$code=@"
using System;using System.Runtime.InteropServices;
public static class NativeCaptionTest {
 [StructLayout(LayoutKind.Sequential)]public struct Rect{public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr window,out Rect rect);
 [DllImport("dwmapi.dll")]public static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out Rect rect,int size);
 [DllImport("user32.dll")]public static extern bool PostMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
 [DllImport("user32.dll")]public static extern bool IsIconic(IntPtr window);
 [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")]public static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr w,IntPtr l,uint flags,uint timeout,out IntPtr result);
}
"@
Add-Type -TypeDefinition $code
$profile=[ZaloManagerPoc.Store]::Load() | Where-Object { -not $_.IsOriginal -and [ZaloManagerPoc.Instance]::IsRunning($_) } | Select-Object -First 1
if(-not $profile){throw 'No running clone for caption test'}
[void][ZaloManagerPoc.WindowActivation]::Restore($profile)
$processes=[ZaloManagerPoc.Instance]::Processes($profile)
$ids=[int[]]@($processes | Where-Object ProcessName -eq Zalo | ForEach-Object Id)
$windows=[ZaloManagerPoc.WindowActivation]::Find($ids)
foreach($process in $processes){$process.Dispose()}
$window=$windows[0]
$rect=[NativeCaptionTest+Rect]::new();$caption=[NativeCaptionTest+Rect]::new()
[void][NativeCaptionTest]::GetWindowRect($window,[ref]$rect)
$status=[NativeCaptionTest]::DwmGetWindowAttribute($window,5,[ref]$caption,16)
if($status -ne 0 -or $caption.Right-$caption.Left -le 0){throw 'Native Windows caption buttons missing'}
$hits=@()
foreach($offset in 10..180){
    $x=$rect.Right-$offset
    $y=$rect.Top+15
    $packed=($y -shl 16) -bor ($x -band 0xffff)
    $hit=[IntPtr]::Zero
    [void][NativeCaptionTest]::SendMessageTimeout($window,0x84,[IntPtr]::Zero,[IntPtr]$packed,2,300,[ref]$hit)
    $hits+=$hit.ToInt32()
}
if(8 -notin $hits -or 20 -notin $hits){throw "Native minimize/close hit regions missing: $($hits | Select-Object -Unique)"}
'PASS actual caption regions resolve to native minimize and close buttons'
try {
    [void][NativeCaptionTest]::PostMessage($window,0x112,[IntPtr]0xF020,[IntPtr]::Zero)
    Start-Sleep -Milliseconds 700
    if(-not [NativeCaptionTest]::IsIconic($window)){throw 'Native minimize failed'}
    'PASS minimize changes clone to minimized state'
    [void][ZaloManagerPoc.WindowActivation]::Restore($profile)
    [void][NativeCaptionTest]::PostMessage($window,0x10,[IntPtr]::Zero,[IntPtr]::Zero)
    Start-Sleep -Milliseconds 700
    if([NativeCaptionTest]::IsWindowVisible($window)){throw 'Close did not hide clone window'}
    'PASS close hides clone window'
} finally { [void][ZaloManagerPoc.WindowActivation]::Restore($profile) }