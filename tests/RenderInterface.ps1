param([string]$Executable = (Join-Path $PSScriptRoot '..\ZaloManager.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework
[Reflection.Assembly]::LoadFrom($Executable) | Out-Null
$window=New-Object ZaloManagerPoc.ManagerWindow
try {
    $flags=[Reflection.BindingFlags]'Instance,NonPublic'
    $type=$window.GetType()
    $original=[ZaloManagerPoc.Original]::Detect()
    $type.GetMethod('ShowOriginal',$flags).Invoke($window,[object[]]@($original)) | Out-Null
    $row=New-Object ZaloManagerPoc.Profile
    $row.Id='original';$row.Name='Zalo gốc';$row.Source=$original.DirectoryPath;$row.InstalledVersion=$original.Version;$row.UpdateStatus='Nguồn nhân bản'
    $type.GetField('originalRow',$flags).SetValue($window,$row)
    foreach($profile in $type.GetField('profiles',$flags).GetValue($window)) {
        $profile.UpdateStatus=if($profile.SourceHash -eq $original.PackageHash -and $profile.PatchRevision -ge 2){'Đã đồng bộ'}else{'Chưa kiểm tra'}
    }
    $type.GetMethod('RefreshStatus',$flags).Invoke($window,@()) | Out-Null
    foreach($width in @(1120,960)) {
        $content=$window.Content
        $size=New-Object Windows.Size(($width-16),680)
        $content.Measure($size)
        $content.Arrange((New-Object Windows.Rect(0,0,$size.Width,$size.Height)))
        $content.UpdateLayout()
        $bitmap=New-Object Windows.Media.Imaging.RenderTargetBitmap([int]$size.Width,[int]$size.Height,96,96,[Windows.Media.PixelFormats]::Pbgra32)
        $background=New-Object Windows.Media.DrawingVisual
        $context=$background.RenderOpen()
        try { $context.DrawRectangle([Windows.Media.BrushConverter]::new().ConvertFromString('#F3F6FA'),$null,(New-Object Windows.Rect(0,0,$size.Width,$size.Height))) } finally {$context.Close()}
        $bitmap.Render($background)
        $bitmap.Render($content)
        $encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $path=Join-Path ([IO.Path]::GetTempPath()) "ZaloManager-design-$width.png"
        $stream=[IO.File]::Create($path)
        try {$encoder.Save($stream)}finally{$stream.Dispose()}
        $path
    }
} finally {$window.Close()}
