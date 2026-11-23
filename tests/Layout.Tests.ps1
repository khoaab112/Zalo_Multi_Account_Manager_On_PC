param([string]$Executable = (Join-Path $PSScriptRoot '..\ZaloManager.exe'))
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
    $controls=$content.Children
    if($controls.Count -ne 5 -or $controls[0].Tag -ne 'Mở Zalo' -or $controls[1].Content -ne 'Data' -or $controls[3].Tag -ne '…' -or $controls[4].Tag -ne 'Xóa'){throw 'Profile actions out of order'}
    $recallAction=$controls[2]
    if($recallAction.Children[0].Content -ne 'Thu hồi'){throw 'Recall button missing'}
    if($recallAction.Children[1] -isnot [Windows.Controls.Border]){throw 'Recall count badge missing beside the button'}
    $runningTrigger=$action.CellTemplate.Triggers[0]
    if($runningTrigger.Value -ne 'Đang chạy' -or @($runningTrigger.Setters | Where-Object { $_.Property -eq [Windows.Controls.Button]::ContentProperty -and $_.Value -like '*Dừng' }).Count -ne 1){throw 'Running state does not switch to Stop'}
    $menuMethod=$type.GetMethod('CreateProfileMenu',$flags)
    $menuArgs=New-Object 'System.Object[]' 1
    $menuArgs[0]=$row.PSObject.BaseObject
    $originalMenu=$menuMethod.Invoke($window,$menuArgs)
    if($originalMenu.Items.Count -ne 1 -or $originalMenu.Items[0].Header -ne 'Tạo shortcut'){throw 'Original menu must offer Shortcut only'}
    $clone=New-Object ZaloManagerPoc.Profile
    $clone.Id='998873'
    $menuArgs[0]=$clone.PSObject.BaseObject
    $cloneMenu=$menuMethod.Invoke($window,$menuArgs)
    if($cloneMenu.Items[0].Header -ne 'Tạo shortcut' -or $cloneMenu.Items.Count -le 1){throw 'Clone menu lost shortcuts or existing options'}
    if($type.GetField('nextCheck',$flags)){throw 'Automatic update schedule remains'}
    'PASS original first, fixed row order, legacy toolbar removed, ordered actions, single power button and per-profile shortcut menus, no automatic sync schedule'
} finally {$window.Close()}
