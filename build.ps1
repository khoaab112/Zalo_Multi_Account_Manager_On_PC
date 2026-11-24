param([string]$OutputName = 'ZaloManager.exe')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path (Split-Path $compiler) 'WPF'
$output = Join-Path $PSScriptRoot $OutputName
$outputDirectory = Split-Path -Parent $output
$stageDirectory = Join-Path $outputDirectory ('.ZaloManager-build-' + [guid]::NewGuid().ToString('N'))
$staged = Join-Path $stageDirectory (Split-Path -Leaf $output)
$references = @('System.dll', 'System.Core.dll', 'System.Management.dll', 'System.Security.dll', 'System.Web.Extensions.dll', 'System.Runtime.Serialization.dll', 'System.Xaml.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll')
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001', "/out:$staged")
$arguments += '/win32icon:' + (Join-Path $PSScriptRoot 'assets\app.ico')
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'assets\app-logo.png') + ',ZaloManager.Logo.png'
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'assets\app.ico') + ',ZaloManager.Icon.ico'
$arguments += $references | ForEach-Object { "/r:$_" }
$arguments += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll','UIAutomationClient.dll','UIAutomationTypes.dll') | ForEach-Object { "/r:$wpf\$_" }
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'src\ConfirmDialog.xaml') + ',ZaloManager.ConfirmDialog.xaml'
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'src\RecallDeleteConfirmation.xaml') + ',ZaloManager.RecallDeleteConfirmation.xaml'
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'src\ModernContextMenu.xaml') + ',ZaloManager.ModernContextMenu.xaml'
$arguments += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'src\Theme.xaml') + ',ZaloManager.Theme.xaml'
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'src\Picker.xaml') + ',ZaloManager.Picker.xaml'
try {
    [IO.Directory]::CreateDirectory($stageDirectory) | Out-Null
    & $compiler $arguments
    if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }
    try {
        if ([IO.File]::Exists($output)) {
            [IO.File]::Replace($staged, $output, [NullString]::Value)
        } else {
            [IO.File]::Move($staged, $output)
        }
    } catch [IO.IOException] {
        throw "Could not replace $output. If Manager is open, close it and build again. $($_.Exception.Message)"
    }
} finally {
    if ([IO.File]::Exists($staged)) { [IO.File]::Delete($staged) }
    if ([IO.Directory]::Exists($stageDirectory)) { [IO.Directory]::Delete($stageDirectory) }
}
Get-Item -LiteralPath $output | Select-Object FullName,Length
