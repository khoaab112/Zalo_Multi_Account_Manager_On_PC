param([string]$Executable = (Join-Path $PSScriptRoot '..\ZaloManager.Picker.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework
[Reflection.Assembly]::LoadFrom($Executable) | Out-Null
$picker=New-Object ZaloManagerPoc.SourcePicker
try {
    if($picker.WindowStyle -ne 'None' -or -not $picker.AllowsTransparency){throw 'Picker shell differs from design'}
    if($picker.Width -ne 460 -or $picker.SizeToContent -ne 'Height'){throw 'Picker size differs from design'}
    $states=@(
        @('neutral','Rê chuột ra ngoài bảng chọn, đến ứng dụng Zalo.',$false,$false),
        @('success','Đã nhận diện Zalo · Click trái để chọn nguồn này.',$true,$false),
        @('error','Đây là bản clone. Hãy trỏ vào Zalo gốc.',$false,$true)
    )
    foreach($state in $states){
        $picker.SetHoverStatus($state[1],$state[2],$state[3])
        $content=$picker.Content
        $content.Measure((New-Object Windows.Size(460,[double]::PositiveInfinity)))
        $height=[Math]::Ceiling($content.DesiredSize.Height)
        $content.Arrange((New-Object Windows.Rect(0,0,460,$height)))
        $content.UpdateLayout()
        $bitmap=New-Object Windows.Media.Imaging.RenderTargetBitmap(460,[int]$height,96,96,[Windows.Media.PixelFormats]::Pbgra32)
        $back=New-Object Windows.Media.DrawingVisual
        $context=$back.RenderOpen()
        try {$context.DrawRectangle([Windows.Media.BrushConverter]::new().ConvertFromString('#F3F6FA'),$null,(New-Object Windows.Rect(0,0,460,$height)))}finally{$context.Close()}
        $bitmap.Render($back);$bitmap.Render($content)
        $encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $path=Join-Path ([IO.Path]::GetTempPath()) ('ZaloPicker-'+$state[0]+'.png')
        $stream=[IO.File]::Create($path)
        try {$encoder.Save($stream)}finally{$stream.Dispose()}
        $box=$content.FindName('StatusBox');$text=$content.FindName('TxtStatus')
        if($text.Text -ne $state[1]){throw 'Status text mismatch'}
        $path
    }
    'PASS picker geometry, status transitions and rendering'
} finally {$picker.Close()}
