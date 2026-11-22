param([string]$Executable = (Join-Path $PSScriptRoot '..\ZaloManager.Poc.Accounts.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework
[Reflection.Assembly]::LoadFrom($Executable) | Out-Null
$window = New-Object ZaloManagerPoc.ManagerWindow
try {
    $flags=[Reflection.BindingFlags]'Instance,NonPublic'
    $type=$window.GetType()
    $original=[ZaloManagerPoc.Original]::Detect()
    $row=New-Object ZaloManagerPoc.Profile
    $row.Id='original';$row.Name='Zalo gốc';$row.Source=$original.DirectoryPath;$row.InstalledVersion=$original.Version
    $type.GetField('originalRow',$flags).SetValue($window,$row)
    $type.GetMethod('RefreshStatus',$flags).Invoke($window,@()) | Out-Null
    $grid=$type.GetField('grid',$flags).GetValue($window)
    if($grid.Items[0].Id -ne 'original'){throw 'Original is not first'}
    if($grid.CanUserSortColumns){throw 'Sorting could displace original'}
    $toolbar=$type.GetField('toolbar',$flags).GetValue($window)
    $buttons=@($toolbar.Children | Where-Object {$_ -is [Windows.Controls.Button]})
    if($buttons.Count -ne 1 -or $buttons[0].Content -notlike '*Thêm hồ sơ'){throw 'Legacy toolbar actions remain'}
    $action=@($grid.Columns | Where-Object Header -eq 'Action')[0]
    if($grid.Columns[$grid.Columns.Count-1].Header -ne 'Action'){throw 'Action is not last'}
    if($grid.Columns[2].Header -ne 'Data chiếm dụng'){throw 'Storage column is not after profile name'}
    if(@($grid.Columns | Where-Object Header -eq 'Thư mục dữ liệu').Count -ne 0){throw 'Data path column remains'}
    $action.CellTemplate.Seal()
    $content=$action.CellTemplate.LoadContent()
    if($content.Children.Count -ne 6){throw 'Row action buttons missing'}
    if($type.GetField('nextCheck',$flags)){throw 'Automatic update schedule remains'}
    'PASS original first, fixed row order, legacy toolbar removed, six per-row actions, no automatic sync schedule'
} finally {$window.Close()}
