param([string]$Executable = (Join-Path $PSScriptRoot '..\ZaloManager.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
[Reflection.Assembly]::LoadFrom($Executable) | Out-Null
$temp=Join-Path ([IO.Path]::GetTempPath()) ('ZaloIconTest-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $profile=New-Object ZaloManagerPoc.Profile
    $profile.Id='998866'; $profile.AvatarPath=Join-Path $temp 'avatar.png'
    $bitmap=New-Object Drawing.Bitmap(64,64)
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([Drawing.Color]::FromArgb(255,230,30,70))
        $graphics.FillRectangle([Drawing.Brushes]::LimeGreen,32,0,32,32)
        $graphics.FillRectangle([Drawing.Brushes]::RoyalBlue,0,32,32,32)
        $graphics.FillRectangle([Drawing.Brushes]::Yellow,32,32,32,32)
        $bitmap.Save($profile.AvatarPath,[Drawing.Imaging.ImageFormat]::Png)
    } finally {$graphics.Dispose();$bitmap.Dispose()}
    $iconPath=[ZaloManagerPoc.ProfileActions]::MakeIcon($profile)
    $icon=New-Object Drawing.Icon($iconPath,64,64)
    $decoded=$icon.ToBitmap()
    try {
        $samples=@(@(16,16,[Drawing.Color]::FromArgb(255,230,30,70)),@(48,16,[Drawing.Color]::LimeGreen),@(16,48,[Drawing.Color]::RoyalBlue),@(48,48,[Drawing.Color]::Yellow))
        foreach($sample in $samples) {
            $pixel=$decoded.GetPixel($sample[0],$sample[1]);$expected=$sample[2]
            if([Math]::Abs($pixel.R-$expected.R) -gt 2 -or [Math]::Abs($pixel.G-$expected.G) -gt 2 -or [Math]::Abs($pixel.B-$expected.B) -gt 2){throw 'Icon colors corrupted'}
        }
    } finally {$decoded.Dispose();$icon.Dispose()}
    $reader=New-Object IO.BinaryReader([IO.File]::OpenRead($iconPath))
    try {
        if($reader.ReadUInt16() -ne 0 -or $reader.ReadUInt16() -ne 1 -or $reader.ReadUInt16() -ne 6){throw 'Missing icon sizes'}
    } finally {$reader.Dispose()}
    'PASS six-size PNG-backed ICO preserves avatar colors through Windows icon decoding'
} finally {
    $root=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(-not [IO.Path]::GetFullPath($temp).StartsWith($root,[StringComparison]::OrdinalIgnoreCase) -or -not ([IO.Path]::GetFileName($temp)).StartsWith('ZaloIconTest-')){throw 'Unsafe test cleanup'}
    Remove-Item -LiteralPath $temp -Recurse -Force
}
