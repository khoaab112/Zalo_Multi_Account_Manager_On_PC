param([string]$Executable, [string]$Fixture, [string]$Archive, [ValidateSet('Capture','Verify')][string]$Stage)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

if (!$Stage) {
    $suffix = [Guid]::NewGuid().ToString('N')
    $work = Join-Path ([IO.Path]::GetTempPath()) ('ZaloManager-MediaTest-' + $suffix)
    $testOutput = "tests\.ZaloManager-MediaTest-$suffix.exe"
    $built = Join-Path $repo $testOutput
    $runner = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    [IO.Directory]::CreateDirectory($work) | Out-Null
    try {
        if ($Executable) { $Executable = (Resolve-Path -LiteralPath $Executable).Path }
        else {
            & (Join-Path $repo 'build.ps1') -OutputName $testOutput
            $Executable = $built
        }
        $Fixture = Join-Path $work 'fixture.json'
        $Archive = Join-Path $work 'archive.dat'
        & node (Join-Path $PSScriptRoot 'RecallMedia.Fixture.mjs') $Fixture
        if ($LASTEXITCODE -ne 0) { throw 'Rendered attachment tests failed.' }
        foreach ($phase in @('Capture','Verify')) {
            & $runner -STA -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -Executable $Executable -Fixture $Fixture -Archive $Archive -Stage $phase
            if ($LASTEXITCODE -ne 0) { throw "Media recall test failed at $phase." }
        }
        'PASS automated fallback, capture, recall, persistence and UI tests. Live Zalo was not exercised.'
    }
    finally {
        foreach ($path in @($built, $Fixture, $Archive, ($Archive + '.tmp'))) {
            if ($path -and [IO.File]::Exists($path)) { [IO.File]::Delete($path) }
        }
        [IO.Directory]::Delete($work)
    }
    return
}

Add-Type -AssemblyName PresentationFramework,System.Web.Extensions
[Reflection.Assembly]::LoadFrom($Executable) | Out-Null
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$instance = [Reflection.BindingFlags]'Instance,NonPublic'
$watcher = [ZaloManagerPoc.CdpMessageWatcher]
$watcher.GetField('RecallArchivePath', $flags).SetValue($null, $Archive)
$fixtureData = [IO.File]::ReadAllText($Fixture) | ConvertFrom-Json
$profile = New-Object ZaloManagerPoc.Profile
$profile.Id = '900001'

if ($Stage -eq 'Capture') {
    $redirect = $watcher.GetMethod('RecallRedirectTarget', $flags)
    $source = [uri]'https://file.zadn.vn/file/start'
    foreach ($status in @(301,302,303,307,308)) {
        $target = $redirect.Invoke($null, @($source, $status, 'https://media.zdn.vn/file/end', 0))
        if ($target.AbsoluteUri -ne 'https://media.zdn.vn/file/end') { throw "Redirect $status rejected" }
    }
    $target = $redirect.Invoke($null, @($source, 302, '../end', 4))
    if ($target.AbsoluteUri -ne 'https://file.zadn.vn/end') { throw 'Relative redirect failed' }
    if ($redirect.Invoke($null, @($source, 403, '/end', 0))) { throw '403 should not be treated as a redirect' }
    foreach ($case in @(
        @('https://zadn.vn.example.com/end', 0, 'redirect-blocked'),
        @('http://file.zadn.vn/end', 0, 'redirect-blocked'),
        @('', 0, 'redirect-blocked'),
        @('/loop', 5, 'redirect-limit')
    )) {
        $failure = $null
        try { $redirect.Invoke($null, @($source, 302, $case[0], $case[1])) | Out-Null }
        catch { $failure = $_.Exception.GetBaseException().Message }
        if ($failure -ne $case[2]) { throw "Unexpected redirect result: $failure" }
    }
    'PASS CDN redirect status, HTTPS destination validation and hop limit'
    $pictureMime = $watcher.GetMethod('RecallPictureMime', $flags)
    foreach ($zaloImageCase in @(
        @('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aUe8AAAAASUVORK5CYII=', 'image/png'),
        @('/9j/4AAQSkZJRgABAg==', 'image/jpeg'),
        @('R0lGODlhAQABAIAAAA==', 'image/gif'),
        @('UklGRhAAAABXRUJQ', 'image/webp')
    )) {
        if ($pictureMime.Invoke($null, @(,[Convert]::FromBase64String($zaloImageCase[0]))) -ne $zaloImageCase[1]) {
            throw "Unexpected image byte signature for $($zaloImageCase[1])"
        }
    }
    if ($pictureMime.Invoke($null, @(,[Text.Encoding]::UTF8.GetBytes('<html>CDN error</html>')))) { throw 'HTML is not a picture' }
    $audioMime = $watcher.GetMethod('RecallAudioMime', $flags)
    $phoneAudio = [byte[]](0xff,0xf1,0x50,0x80,0x02,0x1f,0xfc,0x21,0x10,0x42,0x00,0x00)
    if ($audioMime.Invoke($null, @(,$phoneAudio)) -ne 'audio/aac') { throw 'AAC sent by the phone should use the AAC extension' }
    $allowCdn = $watcher.GetMethod('IsRecallMediaUri', $flags)
    if (!$allowCdn.Invoke($null, @([uri]'https://file-stal-18.dlfl.vn/file')) -or
        $allowCdn.Invoke($null, @([uri]'https://file-stal-18.dlfl.vn.example.com/file')) -or
        $allowCdn.Invoke($null, @([uri]'https://unrelated.dlfl.vn/file'))) { throw 'Zalo file CDN host matching failed' }
    if (!$allowCdn.Invoke($null, @([uri]'https://video-stal-41.dlmd.me/video')) -or
        $allowCdn.Invoke($null, @([uri]'https://video-stal-41.dlmd.me.example.com/video')) -or
        $allowCdn.Invoke($null, @([uri]'https://unrelated.dlmd.me/video'))) { throw 'Zalo video CDN host matching failed' }

    $local = $watcher.GetMethod('ReadDownloadedRecallFile', $flags)
    $folder = [IO.Path]::GetDirectoryName($Fixture)
    $localFile = Join-Path $folder 'verified.txt'
    $sample = [Text.Encoding]::UTF8.GetBytes('Original bytes')
    [IO.File]::WriteAllBytes($localFile, $sample)
    try {
        $checksum = [string](Get-FileHash -LiteralPath $localFile -Algorithm SHA256).Hash
        $noCancel = [Threading.CancellationToken]::None
        $copy = $local.Invoke($null, @($folder, 'verified.txt', 'file', [long]$sample.Length, $checksum, $noCancel))
        if (!$copy -or $copy.GetType().GetField('Data', $instance).GetValue($copy) -ne [Convert]::ToBase64String($sample)) {
            throw 'The verified local copy should preserve the original bytes'
        }
        [IO.File]::WriteAllBytes($localFile, [Text.Encoding]::UTF8.GetBytes('Modified bytes'))
        if ($local.Invoke($null, @($folder, 'verified.txt', 'file', [long]$sample.Length, $checksum, $noCancel))) {
            throw 'A same-size local file with a different checksum should not be archived'
        }
        if ($local.Invoke($null, @($folder, '..\verified.txt', 'file', [long]$sample.Length, $checksum, $noCancel))) {
            throw 'The Downloads lookup must accept only a file name'
        }
    } finally { [IO.File]::Delete($localFile) }
    'PASS bounded Downloads lookup and checksum verification'
    $trackerType = $watcher.GetNestedType('VisibleMessages', [Reflection.BindingFlags]::NonPublic)
    $tracker = [Activator]::CreateInstance($trackerType, $true)
    $accept = $trackerType.GetMethod('AcceptResponse')
    $json = New-Object System.Web.Script.Serialization.JavaScriptSerializer
    $parser = ($json.GetType().GetMethods() | Where-Object { $_.Name -eq 'Deserialize' -and $_.IsGenericMethodDefinition }).MakeGenericMethod([System.Collections.Generic.Dictionary[string,object]])
    foreach ($row in $fixtureData.rows) {
        $response = ConvertTo-Json -InputObject @{id=2;result=@{result=@{value=@($row)}}} -Depth 8 -Compress
        $inputs = New-Object 'System.Object[]' 2
        $inputs[0] = $profile.PSObject.BaseObject
        $inputs[1] = $parser.Invoke($json, @([string]$response))
        $accept.Invoke($tracker, $inputs)
        if ($row.recalled) { $accept.Invoke($tracker, $inputs) }
    }
    if ($watcher::GetRecalledCount($profile.Id) -ne $fixtureData.expected.Count) { throw 'Recall count, deduplication or sender exclusion failed.' }
    if ($watcher::GetRecalledCount('900002') -ne 0) { throw 'Recall appeared in the wrong profile.' }
    if (!(Test-Path -LiteralPath $Archive -PathType Leaf)) { throw 'Recall archive was not saved.' }
    'PASS CDP response parser, including recall before the original card could be read'
    return
}

if ($watcher::GetRecalledCount($profile.Id) -ne $fixtureData.expected.Count) { throw 'Recall count did not survive restart.' }
$profile.Name = 'Recall media test'
$shortcutPath = Join-Path (Split-Path -Parent $Fixture) 'profile.lnk'
try {
    $icon = Join-Path $repo 'assets\app.ico'
    [ZaloManagerPoc.ShellIdentity]::SaveShortcut($shortcutPath, $profile, $icon)
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    if ($shortcut.TargetPath -ne $Executable -or $shortcut.Arguments -ne '--open-profile 900001' -or
        ![ZaloManagerPoc.ShellIdentity]::IsProfileShortcut($shortcutPath, $profile)) {
        throw 'Shortcut does not launch the expected Manager profile or lost its shell identity.'
    }
    'PASS shortcut targets Manager with the clone profile ID'
}
finally { if ([IO.File]::Exists($shortcutPath)) { [IO.File]::Delete($shortcutPath) } }
$read = $watcher.GetMethod('ReadRecallConversation', $flags)
function Get-RecallActions([Windows.DependencyObject]$element) {
    if ($element -is [Windows.Controls.Button]) {
        [Windows.Automation.AutomationProperties]::GetName($element)
    }
    foreach ($child in [Windows.LogicalTreeHelper]::GetChildren($element)) {
        if ($child -is [Windows.DependencyObject]) { Get-RecallActions $child }
    }
}
$manager = New-Object ZaloManagerPoc.ManagerWindow
try {
    $readText = $manager.GetType().GetMethod('ReadRecallText', $flags)
    $sampleText = [Text.Encoding]::UTF8.GetString([byte[]](0x5a,0x61,0x6c,0x6f,0x20,0xe1,0xbb,0x8d))
    if ($readText.Invoke($null, @([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($sampleText)))) -cne $sampleText) {
        throw 'UTF-8 text preview did not preserve the content'
    }
    $bubble = $manager.GetType().GetMethod('RecallBubble', $instance)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        foreach ($expected in $fixtureData.expected) {
            $messages = $read.Invoke($null, @($profile.Id, ('message:' + $expected.id)))
            if ($messages.Length -ne 1) { throw "Missing recall entry $($expected.id)." }
            $message = $messages[0]
            $attachment = $message.GetType().GetField('Attachment', $instance).GetValue($message)
            if (!$expected.kind) {
                if ($attachment -or !$message.Content) { throw 'Missing-content recall should report the event without media.' }
                continue
            }
            $attachmentType = $attachment.GetType()
            if ($attachmentType.GetField('Kind', $instance).GetValue($attachment) -ne $expected.kind) {
                throw "Incorrect media type for $($expected.id)."
            }
            $data = $attachmentType.GetField('Data', $instance).GetValue($attachment)
            if ($expected.sha256) {
                $actualHash = [BitConverter]::ToString($sha.ComputeHash([Convert]::FromBase64String($data))).Replace('-','')
                if ($actualHash -ne $expected.sha256) { throw "Stored bytes differ for $($expected.id)." }
            } elseif ($data) {
                throw "Unexpected bytes for $($expected.id)."
            }
            $inputs = New-Object 'System.Object[]' 2
            $inputs[0] = $message
            $inputs[1] = $manager.PSObject.BaseObject
            $view = $bubble.Invoke($manager, $inputs)
            $view.Measure([Windows.Size]::new(340, [double]::PositiveInfinity))
            $actions = @(Get-RecallActions $view)
            $expectedAction = switch ($expected.kind) {
                'image' { 'Ph?ng to' }
                'sticker' { 'Ph?ng to' }
                'file' { '??c t?p' }
                'audio' { 'Nghe' }
                'video' { 'Xem video' }
            }
            if (($expected.sha256 -and ($actions.Count -ne 2 -or !($actions -like $expectedAction) -or '' -in $actions)) -or
                (!$expected.sha256 -and $actions.Count -ne 0) -or $view.DesiredSize.Height -le 0) {
                throw "Incorrect actions or preview for $($expected.id)."
            }
            "PASS $($expected.id): archived bytes and UI"
        }
    }
    finally { $sha.Dispose() }
    if ($watcher::GetRecalledCount($profile.Id) -ne 0) { throw 'Reading did not clear the unread badge.' }
}
finally { $manager.Close() }
