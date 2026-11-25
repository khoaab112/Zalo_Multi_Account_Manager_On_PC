param([string]$Executable = (Join-Path $PSScriptRoot '..\ZaloManager.exe'))
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') {
    $runner = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    & $runner -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -Executable (Resolve-Path -LiteralPath $Executable).Path
    if ($LASTEXITCODE -ne 0) { throw 'Incoming cache tests failed.' }
    return
}
Add-Type -AssemblyName PresentationFramework,System.Web.Extensions,System.Security,System.Runtime.Serialization
[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Executable).Path) | Out-Null

$watcher = [ZaloManagerPoc.CdpMessageWatcher]
$static = [Reflection.BindingFlags]'Static,NonPublic'
$instance = [Reflection.BindingFlags]'Instance,NonPublic'
$cache = $watcher.GetNestedType('IncomingMessageCache', [Reflection.BindingFlags]::NonPublic)
$message = $watcher.GetNestedType('CachedIncomingMessage', [Reflection.BindingFlags]::NonPublic)
$tracker = [Activator]::CreateInstance($watcher.GetNestedType('VisibleMessages', [Reflection.BindingFlags]::NonPublic), $true)
$accept = $tracker.GetType().GetMethod('AcceptBindingPayload')
$read = $watcher.GetMethod('ReadRecallConversation', $static)
$noCancel = [Threading.CancellationToken]::None

function Receive-Row($profile, [hashtable]$row) {
    $payload = [string](ConvertTo-Json -InputObject $row -Compress)
    $accept.Invoke($tracker, @($profile.PSObject.BaseObject, $payload, $noCancel))
}
function Read-Thread($profileId, $conversation) {
    $read.Invoke($null, @($profileId, "conversation:$conversation"))
}
function Test-Cached($profileId, $conversation, $id) {
    $cache.GetMethod('TryGet').Invoke($null, @($profileId, $conversation, $id, $null, $null))
}

$root = Join-Path ([IO.Path]::GetTempPath()) ('ZaloRecallTests-' + [Guid]::NewGuid().ToString('N'))
$archive = Join-Path $root 'archive.dat'
$blocked = Join-Path $root 'blocked.dat'
$media = Join-Path $root 'media'
$stage = Join-Path $root 'media-staging'
[IO.Directory]::CreateDirectory($stage) | Out-Null
[IO.Directory]::CreateDirectory($media) | Out-Null
[IO.Directory]::CreateDirectory($blocked) | Out-Null
$archiveField = $watcher.GetField('RecallArchivePath', $static)
$archiveField.SetValue($null, $archive)
$watcher.GetField('RecallSettingsPath', $static).SetValue($null, (Join-Path $root 'settings.txt'))
$watcher.GetField('RecallMediaDir', $static).SetValue($null, $media)
$watcher.GetField('RecallStagingDir', $static).SetValue($null, $stage)
$cachePath = Join-Path $root 'cache.dat'
$cache.GetField('CachePath', $static).SetValue($null, $cachePath)

try {
    $legacy = [Activator]::CreateInstance($message)
    $legacy.Id = 'legacy_full'; $legacy.ProfileId = 'legacy-profile'; $legacy.ConversationId = 'legacy'
    $legacy.Content = 'Legacy cache'; $legacy.Timestamp = [DateTimeOffset]::UtcNow
    $legacyType = [System.Collections.Generic.Dictionary[string,string]].GetGenericTypeDefinition().MakeGenericType(@([string], $message))
    $legacyCache = [Activator]::CreateInstance($legacyType)
    $legacyCache.Add('legacy_full', $legacy)
    $legacyCache.Add('legacy', $legacy)
    $legacySerializer = [Runtime.Serialization.Json.DataContractJsonSerializer]::new($legacyType)
    $stream = [IO.MemoryStream]::new()
    try {
        $legacySerializer.WriteObject($stream, $legacyCache)
        [IO.File]::WriteAllBytes($cachePath, [Security.Cryptography.ProtectedData]::Protect(
            $stream.ToArray(), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser))
    } finally { $stream.Dispose() }
    if (!(Test-Cached 'legacy-profile' 'legacy' 'legacy_full') -or (Test-Cached 'legacy-profile' 'legacy' 'legacy')) {
        throw 'Legacy cache migration lost its full ID or retained its prefix alias'
    }
    'PASS legacy cache loads without prefix aliases'

    $profile = New-Object ZaloManagerPoc.Profile
    $profile.Id = 'fixture-profile'
    foreach ($conversation in @('alice', 'bob')) {
        Receive-Row $profile @{id="123_${conversation}"; conversation=$conversation; name='Same name'; preview=$conversation; outgoing=$false}
    }
    if (!(Test-Cached $profile.Id 'alice' '123_alice') -or (Test-Cached $profile.Id '' '123') -or
        (Test-Cached 'other-profile' 'alice' '123_alice')) { throw 'Cache scope or full ID matching failed' }
    foreach ($conversation in @('alice', 'bob')) {
        Receive-Row $profile @{id="123_${conversation}"; conversation=$conversation; recalled=$true; observedTransition=$true}
    }
    if ((Read-Thread $profile.Id 'alice').Content -ne 'alice' -or (Read-Thread $profile.Id 'bob').Content -ne 'bob') {
        throw 'Same display name mixed two conversations'
    }
    $watcher.GetMethod('DeleteRecallConversation', $static).Invoke($null, @($profile.Id, 'conversation:bob')) | Out-Null
    if (@(Read-Thread $profile.Id 'alice').Count -ne 1 -or @(Read-Thread $profile.Id 'bob').Count -ne 0) {
        throw 'Delete crossed conversation IDs'
    }
    'PASS full ID/profile scope and independent same-name conversations'

    Receive-Row $profile @{id='missing'; conversation='unknown'; recalled=$true}
    if (@(Read-Thread $profile.Id 'unknown').Count -ne 0) { throw 'Unverified unknown recall became a record' }
    Receive-Row $profile @{id='early'; conversation='delay'; recalled=$true; observedTransition=$true}
    Receive-Row $profile @{id='early'; conversation='delay'; preview='before deadline'; outgoing=$false}
    Receive-Row $profile @{id='late'; cliMsgId='alias-late'; recalled=$true; observedTransition=$true}
    $tracker.GetType().GetMethod('Maintain').Invoke($tracker, @($profile.PSObject.BaseObject, $false))
    Receive-Row $profile @{id='late'; cliMsgId='alias-late'; conversation='delay'; preview='late original'; outgoing=$false}
    Receive-Row $profile @{id='late'; conversation='delay'; recalled=$true; observedTransition=$true}
    $resolved = @(Read-Thread $profile.Id 'delay')
    if ($resolved.Count -ne 2 -or $resolved[0].Content -ne 'before deadline' -or $resolved[1].Content -ne 'late original') {
        throw 'Matching an undo to an original that arrived later failed'
    }
    'PASS recalls before original, delayed matching and deduplication'

    foreach ($order in @('pm-first','cdm-first','undo-before-cdm')) {
        $id = "group-$order"
        $pm = @{id=$id; conversation='member'; provisionalConversation=$true; preview=$order; outgoing=$false}
        $cdm = @{id=$id; conversation="thread-$order"; preview=$order; outgoing=$false}
        $undo = @{id=$id; recalled=$true; observedTransition=$true}
        if ($order -eq 'cdm-first') {
            Receive-Row $profile $cdm
            Receive-Row $profile $pm
        } else {
            Receive-Row $profile $pm
            if ($order -eq 'undo-before-cdm') { Receive-Row $profile $undo }
            Receive-Row $profile $cdm
        }
        if ($order -ne 'undo-before-cdm') { Receive-Row $profile $undo }
        $group = @(Read-Thread $profile.Id "thread-$order")
        if ($group.Count -ne 1 -or $group[0].Content -ne $order -or (Test-Cached $profile.Id 'member' $id)) {
            throw "Recall group scope or deduplication failed for $order"
        }
    }
    'PASS group scope promotion under both hook orders and early undo'

    $sentAt = [DateTimeOffset]::UtcNow.AddHours(-4)
    $fileExpiry = Join-Path $media (([Guid]::NewGuid().ToString('N')) + '.zip')
    [IO.File]::WriteAllText($fileExpiry,'group expiry fixture')
    Receive-Row $profile @{id='ttl-group'; conversation='ttl-group'; preview='Group retained'; outgoing=$false;
        isGroup=$true; timestamp=$sentAt.ToUnixTimeMilliseconds()}
    $entryType = $watcher.GetNestedType('CachedIncomingMessage',[Reflection.BindingFlags]::NonPublic)
    $ttlAttachment = [Activator]::CreateInstance($watcher.GetNestedType('RecallAttachment',[Reflection.BindingFlags]::NonPublic))
    $ttlAttachment.GetType().GetField('Data',$instance).SetValue($ttlAttachment,"local:$fileExpiry")
    $ttlRecord = [Activator]::CreateInstance($entryType)
    $ttlRecord.Id='ttl-media'; $ttlRecord.ProfileId=$profile.Id; $ttlRecord.ConversationId='ttl-media'
    $ttlRecord.Content='fixture.zip'; $ttlRecord.Attachment=$ttlAttachment
    $ttlRecord.Timestamp=$sentAt; $ttlRecord.GroupExpires=$sentAt.AddHours(5)
    $cache.GetMethod('Store').Invoke($null,@($ttlRecord)) | Out-Null
    Receive-Row $profile @{id='ttl-group'; conversation='ttl-group'; recalled=$true; observedTransition=$true}
    Receive-Row $profile @{id='ttl-direct'; conversation='ttl-direct'; preview='Direct retained'; outgoing=$false}
    Receive-Row $profile @{id='ttl-direct'; conversation='ttl-direct'; recalled=$true; observedTransition=$true}
    $list = @(Read-Thread $profile.Id 'ttl-group')
    $expiry = $list[0].GetType().GetField('GroupExpires',$instance).GetValue($list[0])
    if ([Math]::Abs(($expiry - $sentAt.AddHours(5)).TotalSeconds) -gt 1) { throw 'Group expiry was extended by the recall' }
    $candidates = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $pruneGroup = $watcher.GetMethod('PruneGroupRecalls',$static)
    $pruneGroup.Invoke($null,@($sentAt.AddHours(5).AddSeconds(-1),$candidates))
    if (@(Read-Thread $profile.Id 'ttl-group').Count -ne 1) { throw 'Group data disappeared before the cutoff' }
    $now = $sentAt.AddHours(5).AddSeconds(1)
    $pruneGroup.Invoke($null,@($now,$candidates))
    $cache.GetMethod('PruneExpired',$static).Invoke($null,@($now,$false))
    $candidates.UnionWith($cache.GetField('PendingMediaCleanup',$static).GetValue($null))
    $watcher.GetMethod('DeleteUnreferencedMedia',$static).Invoke($null,(,$candidates)) | Out-Null
    if (@(Read-Thread $profile.Id 'ttl-group').Count -ne 0 -or @(Read-Thread $profile.Id 'ttl-direct').Count -ne 1 -or
        (Test-Cached $profile.Id 'ttl-media' 'ttl-media') -or [IO.File]::Exists($fileExpiry)) {
        throw 'The group cutoff failed to remove text and media, or removed direct chat data'
    }
    'PASS five-hour cutoff removes group cache, recall and unreferenced media only'

    $senderProfile = New-Object ZaloManagerPoc.Profile
    $senderProfile.Id = 'sender-profile'
    $senderTracker = [Activator]::CreateInstance($tracker.GetType(), $true)
    $receiverTracker = $tracker
    try {
        $tracker = $senderTracker
        Receive-Row $senderProfile @{id='own-file'; cliMsgId='own-client'; conversation='peer'; preview='fixture.zip'; kind='file'; outgoing=$true}
        Receive-Row $senderProfile @{id='own-file-final'; cliMsgId='own-client'; conversation='peer'; preview='fixture.zip'; kind='file'; outgoing=$true}
        Receive-Row $senderProfile @{id='own-undo-unknown'; recalled=$true; observedTransition=$true}
        Receive-Row $senderProfile @{id='own-undo-ack'; cliMsgId='own-client'; recalled=$true; observedTransition=$true}
        Receive-Row $senderProfile @{id='own-undo-exact'; outgoing=$true; recalled=$true; observedTransition=$true}
        Receive-Row $senderProfile @{id='own-late'; conversation='peer'; recalled=$true; observedTransition=$true}
        Receive-Row $senderProfile @{id='own-late'; conversation='peer'; preview='own text'; outgoing=$true}
        $maintain = $tracker.GetType().GetMethod('Maintain')
        $maintain.Invoke($tracker, @($senderProfile.PSObject.BaseObject, $true))
        if ($watcher::GetRecalledCount($senderProfile.Id) -ne 0) { throw 'Sender-side undo became a recall' }
    } finally { $tracker = $receiverTracker }
    'PASS sender-side recalls with unknown direction, ambiguous aliases and late outgoing originals'

    $queueProfile = New-Object ZaloManagerPoc.Profile
    $queueProfile.Id = 'queue-profile'
    $session = [Activator]::CreateInstance($watcher.GetNestedType('Session', [Reflection.BindingFlags]::NonPublic), $true)
    $post = $session.GetType().GetMethod('Post')
    $post.Invoke($session, @('{"id":"queued","conversation":"ordered","preview":"queued content","outgoing":false}'))
    $post.Invoke($session, @('{"id":"queued","conversation":"ordered","recalled":true,"observedTransition":true}'))
    for ($i = 0; $i -lt 255; $i++) { $post.Invoke($session, @('[]')) }
    $session.GetType().GetMethod('Complete').Invoke($session, @())
    $run = $session.GetType().GetMethod('Process').Invoke($session, @($queueProfile.PSObject.BaseObject))
    $run.GetAwaiter().GetResult() | Out-Null
    if ((Read-Thread $queueProfile.Id 'ordered').Content -ne 'queued content' -or
        $session.GetType().GetField('Dropped').GetValue($session) -ne 1) {
        throw 'Delivery order or queue overflow count failed'
    }
    'PASS bounded queue order, overflow reporting and drain on close'

    Receive-Row $profile @{id='retry'; conversation='failure'; preview='preserve on write failure'; outgoing=$false}
    $archiveField.SetValue($null, $blocked)
    Receive-Row $profile @{id='retry'; conversation='failure'; recalled=$true; observedTransition=$true}
    if (!(Test-Cached $profile.Id 'failure' 'retry') -or @(Read-Thread $profile.Id 'failure').Count -ne 0) {
        throw 'Failed write consumed the original or exposed an uncommitted recall'
    }
    $archiveField.SetValue($null, $archive)
    $tracker.GetType().GetMethod('Maintain').Invoke($tracker, @($profile.PSObject.BaseObject, $true))
    if ((Test-Cached $profile.Id 'failure' 'retry') -or (Read-Thread $profile.Id 'failure').Content -ne 'preserve on write failure') {
        throw 'Retry did not finish the pending recall'
    }
    'PASS failed archive write preserves original and retry succeeds'

    $linked = Join-Path $media (([Guid]::NewGuid().ToString('N')) + '.bin')
    $orphan = Join-Path $media (([Guid]::NewGuid().ToString('N')) + '.bin')
    foreach ($file in @($linked, $orphan)) {
        [IO.File]::WriteAllText($file, 'fixture')
        [IO.File]::SetLastWriteTimeUtc($file, [DateTime]::UtcNow.AddHours(-2))
    }
    $scan = $watcher.GetMethod('ScanRecallMedia', $static)
    $scanArgs = @([DateTime]::UtcNow.AddHours(-1))
    $invalidArchive = $watcher.GetField('archiveLoadFailed', $static)
    $invalidArchive.SetValue($null, $true)
    $skipped = $scan.Invoke($null, $scanArgs)
    $invalidArchive.SetValue($null, $false)
    if ($null -ne $skipped -or ![IO.File]::Exists($orphan)) { throw 'An unreadable archive should block the report' }
    $entry = [Activator]::CreateInstance($message)
    $entry.Id = 'media'; $entry.ProfileId = $profile.Id; $entry.ConversationId = 'files'
    $entry.Content = 'fixture'; $entry.Timestamp = [DateTimeOffset]::UtcNow
    $attachment = [Activator]::CreateInstance($watcher.GetNestedType('RecallAttachment', [Reflection.BindingFlags]::NonPublic))
    $attachment.GetType().GetField('Data', $instance).SetValue($attachment, "local:$linked")
    $entry.Attachment = $attachment
    $cache.GetMethod('Store').Invoke($null, @($entry)) | Out-Null
    $report = $scan.Invoke($null, $scanArgs)
    if ($report.CandidateCount -ne 1 -or $report.CandidateBytes -lt 7 -or
        ![IO.File]::Exists($linked) -or ![IO.File]::Exists($orphan)) {
        throw 'The report must identify only the orphan and leave both files intact'
    }
    Receive-Row $profile @{id='media'; conversation='files'; recalled=$true; observedTransition=$true}
    $report = $scan.Invoke($null, $scanArgs)
    if ($report.CandidateCount -ne 1 -or ![IO.File]::Exists($linked) -or ![IO.File]::Exists($orphan)) {
        throw 'Moving a media reference to the recall archive must preserve it'
    }
    'PASS report-only media scan, byte count and reference transfer'
    $entry.Id = 'shared-media'; $entry.ConversationId = 'other-files'
    $cache.GetMethod('Store').Invoke($null, @($entry)) | Out-Null
    Receive-Row $profile @{id='shared-media'; conversation='other-files'; recalled=$true; observedTransition=$true}
    $delete = $watcher.GetMethod('DeleteRecalls', $static)
    $first = $delete.Invoke($null, @($profile.Id, 'conversation:files'))
    if (!$first.Saved -or $first.ReleasedBytes -ne 0 -or ![IO.File]::Exists($linked)) { throw 'Deleted shared media too early' }
    $archiveField.SetValue($null, $blocked)
    $failed = $delete.Invoke($null, @($profile.Id, 'conversation:other-files'))
    $archiveField.SetValue($null, $archive)
    if ($failed.Saved -or ![IO.File]::Exists($linked)) { throw 'Failed delete removed media' }
    $last = $delete.Invoke($null, @($profile.Id, 'conversation:other-files'))
    if (!$last.Saved -or $last.ReleasedBytes -lt 7 -or [IO.File]::Exists($linked) -or ![IO.File]::Exists($orphan)) {
        throw 'Deleting the final reference did not release exactly the intended media'
    }
    'PASS deletes release only the last reference and preserve files when saving fails'

    $deleteUnreferenced = $watcher.GetMethod('DeleteUnreferencedMedia',$static)
    $pendingCleanup = $cache.GetField('PendingMediaCleanup',$static).GetValue($null)
    $expiredPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($i in 1..37) {
        $file = Join-Path $media (([Guid]::NewGuid().ToString('N')) + '.bin')
        [IO.File]::WriteAllText($file, 'expired direct media')
        $entry = [Activator]::CreateInstance($message)
        $entry.Id = "expiry-$i"; $entry.ProfileId = $profile.Id; $entry.ConversationId = 'expiry'
        $entry.Timestamp = [DateTimeOffset]::UtcNow.AddHours(-25)
        $entry.Attachment = [Activator]::CreateInstance($ttlAttachment.GetType())
        $entry.Attachment.GetType().GetField('Data',$instance).SetValue($entry.Attachment,"local:$file")
        $cache.GetMethod('Store').Invoke($null, @($entry)) | Out-Null
        [void]$expiredPaths.Add($file)
    }
    $cache.GetMethod('PruneExpired').Invoke($null,@())
    if ((Test-Cached $profile.Id 'expiry' 'expiry-1') -or !$pendingCleanup.IsSupersetOf($expiredPaths)) {
        throw 'Expired direct message did not queue media for cleanup'
    }
    $cachePathField = $cache.GetField('CachePath',$static)
    $cachePathField.SetValue($null,$blocked)
    try { $deleteUnreferenced.Invoke($null,(,$expiredPaths)) | Out-Null }
    finally { $cachePathField.SetValue($null,$cachePath) }
    if ($expiredPaths.Count -ne 37 -or @($expiredPaths | Where-Object { ![IO.File]::Exists($_) }).Count) {
        throw 'Media was deleted before the cache could commit'
    }
    $locked = @($expiredPaths)[0]
    $hold = [IO.File]::Open($locked,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try {
        $deleteUnreferenced.Invoke($null,(,$expiredPaths)) | Out-Null
        if ($expiredPaths.Count -ne 1 -or !$pendingCleanup.Contains($locked) -or ![IO.File]::Exists($locked)) {
            throw 'Batched cleanup did not process past the locked expiry file'
        }
        $plain = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($cachePath), $null,
            [Security.Cryptography.DataProtectionScope]::CurrentUser)
        $savedCleanup = ([Text.Encoding]::UTF8.GetString($plain) | ConvertFrom-Json).PendingMediaCleanup
        if ($locked -notin $savedCleanup) { throw 'Cleanup retry was not recorded in the durable cache' }
    } finally { $hold.Dispose() }
    $deleteUnreferenced.Invoke($null,(,$expiredPaths)) | Out-Null
    if ($expiredPaths.Count -ne 0 -or $pendingCleanup.Contains($locked) -or [IO.File]::Exists($locked)) {
        throw 'Locked file was not cleaned up on retry'
    }
    'PASS direct expiry cleans media in batches, preserves files after a failed commit, and retries locked files'

    $slotsType = $watcher.GetNestedType('RecallDownloadSlots',[Reflection.BindingFlags]::NonPublic)
    $slots = [Activator]::CreateInstance($slotsType,$true)
    $acquire = $slotsType.GetMethod('WaitAsync')
    $release = $slotsType.GetMethod('Release')
    $cancel = [Threading.CancellationTokenSource]::new()
    try {
        foreach ($owner in @('a','a','b')) {
            $lease = $acquire.Invoke($slots,@($owner,$false,$noCancel))
            if (!$lease.IsCompleted) { throw 'An eligible transfer waited for a free slot' }
        }
        $bLarge = $acquire.Invoke($slots,@('b',$false,$noCancel))
        $aSmall = $acquire.Invoke($slots,@('a',$true,$cancel.Token))
        $cSmall = $acquire.Invoke($slots,@('c',$true,$noCancel))
        if ($bLarge.IsCompleted -or $aSmall.IsCompleted -or !$cSmall.IsCompleted) {
            throw 'Larger transfers took the reserved slot or one profile exceeded its limit'
        }
        $cancel.Cancel()
        if (!$aSmall.IsCanceled) {
            try { $aSmall.GetAwaiter().GetResult() | Out-Null } catch [OperationCanceledException] { }
            if (!$aSmall.IsCanceled) { throw 'Queued transfer was not cancelled' }
        }
        $release.Invoke($slots,@('a',$false))
        if (!$bLarge.Wait(2000)) { throw 'A waiting large transfer could not resume' }
        foreach ($owner in @('a','b','b')) { $release.Invoke($slots,@($owner,$false)) }
        $release.Invoke($slots,@('c',$true))
    } finally { $cancel.Dispose() }
    'PASS global and per-profile download limits, reserved slot and queued cancellation'

    $avatar = 'QUJDRA=='
    foreach ($i in 1..20) {
        Receive-Row $profile @{id="avatar-$i"; conversation='avatar-fixture'; preview="message $i"; outgoing=$false; avatar=$avatar}
    }
    if (!$cache.GetMethod('Flush').Invoke($null,@())) { throw 'V2 avatar snapshot did not save' }
    $plain = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($cachePath), $null,
        [Security.Cryptography.DataProtectionScope]::CurrentUser)
    $snapshot = [Text.Encoding]::UTF8.GetString($plain) | ConvertFrom-Json
    $avatarRows = @($snapshot.Messages | Where-Object ConversationId -EQ 'avatar-fixture')
    $avatarEntries = @($snapshot.Avatars | Where-Object Conversation -EQ 'avatar-fixture')
    if ($avatarRows.Count -ne 20 -or @($avatarRows | Where-Object AvatarPng).Count -ne 0 -or
        $avatarEntries.Count -ne 1 -or $avatarEntries[0].Png -ne $avatar) {
        throw 'Avatar was not stored once outside the 20 message rows'
    }
    'PASS snapshot stores one avatar outside message rows'

    $bubble = [ZaloManagerPoc.ManagerWindow].GetMethod('SameRecallBubble',$static)
    $firstMessage = [Activator]::CreateInstance([ZaloManagerPoc.CdpMessageWatcher+RecalledMessage])
    $secondMessage = [Activator]::CreateInstance([ZaloManagerPoc.CdpMessageWatcher+RecalledMessage])
    $attachmentField = $firstMessage.GetType().GetField('Attachment',$instance)
    $firstAttachment = [Activator]::CreateInstance($attachment.GetType())
    $secondAttachment = [Activator]::CreateInstance($attachment.GetType())
    $attachmentField.SetValue($firstMessage,$firstAttachment)
    $attachmentField.SetValue($secondMessage,$secondAttachment)
    if (!$bubble.Invoke($null,@($firstMessage,$secondMessage))) { throw 'Identical bubbles did not compare equal' }
    $secondAttachment.GetType().GetField('PreviewData',$instance).SetValue($secondAttachment,'VEhVTUI=')
    if ($bubble.Invoke($null,@($firstMessage,$secondMessage))) { throw 'Late preview did not invalidate the bubble' }
    'PASS late thumbnail changes display equality'

    $serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
    $hook = $tracker.GetType().GetMethod('CreateHookScript').Invoke($tracker, @($serializer.PSObject.BaseObject, 'fixture'))
    $script = Join-Path $root 'hook.js'
    [IO.File]::WriteAllText($script, $hook)
    & node (Join-Path $PSScriptRoot 'RecallHook.Fixture.mjs') $script
    if ($LASTEXITCODE -ne 0) { throw 'Hook fixture failed' }

    $evictedFile = Join-Path $media (([Guid]::NewGuid().ToString('N')) + '.bin')
    [IO.File]::WriteAllText($evictedFile,'group file reduced by bounds')
    $record = [Activator]::CreateInstance($message)
    $record.Id='bound-media'; $record.ProfileId='quota'; $record.ConversationId='quota'
    $record.Timestamp=[DateTimeOffset]::UtcNow.AddHours(-2)
    $record.GroupExpires=$record.Timestamp.AddHours(5)
    $record.Attachment = [Activator]::CreateInstance($ttlAttachment.GetType())
    $record.Attachment.GetType().GetField('Data',$instance).SetValue($record.Attachment,"local:$evictedFile")
    $cache.GetMethod('Store').Invoke($null,@($record)) | Out-Null
    $fillCount = 20000 - $cache.GetField('cache',$static).GetValue($null).Count
    $indexEntry = $cache.GetMethod('IndexMessage',$static)
    $now = [DateTimeOffset]::UtcNow
    $cacheGate = $cache.GetField('CacheGate',$static).GetValue($null)
    [Threading.Monitor]::Enter($cacheGate)
    try {
        foreach ($i in 1..$fillCount) {
            $row = [Activator]::CreateInstance($message)
            $row.Id = "bounds-$i"; $row.ProfileId = 'quota'; $row.ConversationId='quota'; $row.Timestamp=$now
            $indexEntry.Invoke($null,@($row))
        }
    } finally { [Threading.Monitor]::Exit($cacheGate) }
    $row = [Activator]::CreateInstance($message)
    $row.Id='bounds-trigger'; $row.ProfileId='quota'; $row.ConversationId='quota'; $row.Timestamp=$now
    $cache.GetMethod('Store').Invoke($null,@($row)) | Out-Null
    if ((Test-Cached 'quota' 'quota' 'bound-media') -or !$pendingCleanup.Contains($evictedFile)) {
        throw 'Cache bounds did not queue the evicted group attachment'
    }
    $cache.GetMethod('RunRetention',$static).Invoke($null,@())
    if ([IO.File]::Exists($evictedFile) -or ![IO.File]::Exists($orphan)) {
        throw 'Retention did not release the evicted file or deleted an unrelated orphan'
    }
    'PASS cache quota eviction queues and releases group media before its TTL'
}
finally {
    $cache.GetMethod('Flush').Invoke($null, @()) | Out-Null
    foreach ($folder in @($media, $stage, $blocked, $root)) {
        if ([IO.Directory]::Exists($folder)) {
            foreach ($file in [IO.Directory]::EnumerateFiles($folder)) { [IO.File]::Delete($file) }
            [IO.Directory]::Delete($folder)
        }
    }
}
