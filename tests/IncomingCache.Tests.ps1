param([string]$Executable)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

if (!$Executable) {
    $Executable = Join-Path $repo 'ZaloManager.exe'
}

Add-Type -AssemblyName PresentationFramework,System.Web.Extensions,System.Security,System.Runtime.Serialization
[Reflection.Assembly]::LoadFrom($Executable) | Out-Null

$watcher = [ZaloManagerPoc.CdpMessageWatcher]
$cacheClass = $watcher.GetNestedType('IncomingMessageCache', [Reflection.BindingFlags]'Static,NonPublic')
$cachedMsgClass = $watcher.GetNestedType('CachedIncomingMessage', [Reflection.BindingFlags]'Instance,NonPublic')
$storeMethod = $cacheClass.GetMethod('Store', [Reflection.BindingFlags]'Static,Public')
$tryGetMethod = $cacheClass.GetMethod('TryGet', [Reflection.BindingFlags]'Static,Public')
$removeMethod = $cacheClass.GetMethod('Remove', [Reflection.BindingFlags]'Static,Public')
$pruneMethod = $cacheClass.GetMethod('PruneExpired', [Reflection.BindingFlags]'Static,Public')

Write-Host "=== TEST 1: Luu tin nhan vao Cache DB (incoming-cache.dat) ==="
$msg1 = [Activator]::CreateInstance($cachedMsgClass)
$msg1.Id = 'test_msg_recent_1'
$msg1.ProfileId = '900001'
$msg1.ConversationId = 'conv_alice'
$msg1.ConversationName = 'Alice Test'
$msg1.Content = 'Tin nhan thu nghiem trong 24h'
$msg1.Outgoing = $false
$msg1.Timestamp = [DateTimeOffset]::UtcNow

$storeMethod.Invoke($null, @($msg1))

$outObj = $null
$args = @('test_msg_recent_1', $outObj)
$found = $tryGetMethod.Invoke($null, $args)
if (!$found) { throw "FAIL: Khong tim thay tin nhan vua luu trong Cache!" }
Write-Host "PASS: Luu va doc thanh cong tin nhan 'test_msg_recent_1'."

Write-Host "=== TEST 2: Kiem tra tin nhan cu qua 24 gio va tinh nang Prune (tu dong xoa) ==="
$msgOld = [Activator]::CreateInstance($cachedMsgClass)
$msgOld.Id = 'test_msg_old_1'
$msgOld.ProfileId = '900001'
$msgOld.ConversationId = 'conv_bob'
$msgOld.ConversationName = 'Bob Test'
$msgOld.Content = 'Tin nhan tu 25 gio truoc'
$msgOld.Outgoing = $false
$msgOld.Timestamp = [DateTimeOffset]::UtcNow.AddHours(-25)

$storeMethod.Invoke($null, @($msgOld))

# Chay PruneExpired
$pruneMethod.Invoke($null, @())

$argsOld = @('test_msg_old_1', $null)
$foundOld = $tryGetMethod.Invoke($null, $argsOld)
if ($foundOld) { throw "FAIL: Tin nhan qua 24h van chua bi xoa sau khi Prune!" }
Write-Host "PASS: Tin nhan cu qua 24 gio da bi xoa sach khoi Cache."

$argsRecent = @('test_msg_recent_1', $null)
$foundRecent = $tryGetMethod.Invoke($null, $argsRecent)
if (!$foundRecent) { throw "FAIL: Tin nhan trong 24h bi xoa nham!" }
Write-Host "PASS: Tin nhan trong 24h van duoc giu nguyen an toan."

Write-Host "=== TEST 3: Thu hoi tin nhan va xoa khoi Cache ==="
$removeMethod.Invoke($null, @('test_msg_recent_1'))
$foundAfterRemove = $tryGetMethod.Invoke($null, @('test_msg_recent_1', $null))
if ($foundAfterRemove) { throw "FAIL: Tin nhan da thu hoi van con trong Cache!" }
Write-Host "PASS: Tin nhan da thu hoi duoc don dep khoi Cache thanh cong."

Write-Host "=== TEST 4: Kiem tra chan tin thu hoi gia khi moi khoi dong (tin khong co trong Cache) ==="
$trackerType = $watcher.GetNestedType('VisibleMessages', [Reflection.BindingFlags]'NonPublic')
$tracker = [Activator]::CreateInstance($trackerType, $true)
$acceptBindingMethod = $trackerType.GetMethod('AcceptBindingPayload')
$profile = New-Object ZaloManagerPoc.Profile
$profile.Id = 'test_profile_999'

$countBefore = $watcher::GetRecalledCount('test_profile_999')

# Gia lap nhan 1 event recall cho tin nhan cu 'unknown_old_msg' chua tung co trong Cache
$fakeOldRecallPayload = '[{"id":"unknown_old_msg","recalled":true,"conversation":"conv_stranger"}]'
$inputs = New-Object 'System.Object[]' 3
$inputs[0] = $profile.PSObject.BaseObject
$inputs[1] = $fakeOldRecallPayload
$inputs[2] = [Threading.CancellationToken]::None
$acceptBindingMethod.Invoke($tracker, $inputs)

$countAfter = $watcher::GetRecalledCount('test_profile_999')
if ($countAfter -ne $countBefore) {
    throw "FAIL: Tin cu tu truoc khi mo app bi bat nham lam tin thu hoi moi!"
}
Write-Host "PASS: Tin nhan cu ngoai Cache hoan toan bi bo qua, khong gay thong bao thu hoi gia."

Write-Host "=== TEST 5: Kiem tra thu hoi thanh cong khi tin co trong Cache ==="
$newMsg = [Activator]::CreateInstance($cachedMsgClass)
$newMsg.Id = 'test_msg_valid_recall'
$newMsg.ProfileId = 'test_profile_999'
$newMsg.ConversationId = 'conv_alice'
$newMsg.ConversationName = 'Alice Test'
$newMsg.Content = 'Noi dung can thu hoi'
$newMsg.Outgoing = $false
$newMsg.Timestamp = [DateTimeOffset]::UtcNow
$storeMethod.Invoke($null, @($newMsg))

$validRecallPayload = '[{"id":"test_msg_valid_recall","recalled":true,"conversation":"conv_alice"}]'
$inputs[1] = $validRecallPayload
$acceptBindingMethod.Invoke($tracker, $inputs)

$countAfterValid = $watcher::GetRecalledCount('test_profile_999')
if ($countAfterValid -le $countAfter) {
    throw "FAIL: Tin nhan hop le trong Cache khong duoc ghi nhan thu hoi!"
}
$inCacheAfter = $tryGetMethod.Invoke($null, @('test_msg_valid_recall', $null))
if ($inCacheAfter) {
    throw "FAIL: Tin nhan da thu hoi khong duoc xoa khoi Cache!"
}
Write-Host "PASS: Tin nhan trong Cache duoc chuyen sang kho thu hoi va xoa khoi Cache thanh cong."

# Clean up test profile
$deleteMethod = $watcher.GetMethod('DeleteAllRecalls', [Reflection.BindingFlags]'Static,NonPublic')
if ($deleteMethod) { $deleteMethod.Invoke($null, @('test_profile_999')) | Out-Null }

Write-Host "=== TAT CA CAC TEST CHO YEU CAU MOI DEU PASS 100% ==="
