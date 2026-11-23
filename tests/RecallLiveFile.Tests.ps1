param(
    [Parameter(Mandatory=$true)][ValidatePattern('^(original|[0-9]{6})$')][string]$ProfileId,
    [Parameter(Mandatory=$true)][string]$ExpectedFile,
    [ValidateRange(0,300)][int]$WaitSeconds = 0
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$expected = Get-Item -LiteralPath $ExpectedFile
if ($expected.PSIsContainer -or $expected.Length -eq 0 -or $expected.Length -gt 16MB) {
    throw 'Use a nonempty test file no larger than 16 MiB.'
}
$hash = (Get-FileHash -LiteralPath $expected.FullName -Algorithm SHA256).Hash
$root = Join-Path $env:LOCALAPPDATA 'ZaloManagerPoC'
$archive = Join-Path $root 'recalled-messages.dat'
$deadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
$latestStatus = 'No recalled entry with this name in the selected profile.'
if ($WaitSeconds -gt 0) { Write-Host "Watching the live archive for $($expected.Name) on $ProfileId..." }

do {
    if ([IO.File]::Exists($archive)) {
        # FileShare.Delete lets Manager replace the archive while the test reads it.
        $stream = [IO.FileStream]::new($archive, [IO.FileMode]::Open, [IO.FileAccess]::Read,
            ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
        $buffer = [IO.MemoryStream]::new()
        try {
            $stream.CopyTo($buffer)
            $plain = [Security.Cryptography.ProtectedData]::Unprotect(
                $buffer.ToArray(), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
        } finally { $buffer.Dispose(); $stream.Dispose() }

        $profiles = [Text.Encoding]::UTF8.GetString($plain) | ConvertFrom-Json
        $entries = @($profiles | Where-Object Key -EQ $ProfileId | ForEach-Object { $_.Value } |
            ForEach-Object { $_.Value.Messages } | Where-Object {
                $_.Attachment -and ($_.Content -ceq $expected.Name -or $_.Attachment.FileName -ceq $expected.Name)
            })
        foreach ($entry in $entries) {
            $data = $entry.Attachment.Data
            if (!$data) {
                $latestStatus = 'The archive has the file name but no bytes. Capture status: ' + $entry.Attachment.CaptureStatus
                continue
            }
            $sha = [Security.Cryptography.SHA256]::Create()
            try {
                $bytes = [Convert]::FromBase64String($data)
                $actual = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '')
            } finally { $sha.Dispose() }
            if ($actual -eq $hash) {
                $origin = if ($entry.Attachment.Origin) { $entry.Attachment.Origin } else { 'unspecified' }
                "PASS live archive: profile $ProfileId, $($bytes.Length) bytes, SHA-256 matches, source $origin."
                exit 0
            }
            $latestStatus = 'An archived copy exists but its SHA-256 differs from the original.'
        }
    }
    if ([DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 500 }
} while ([DateTime]::UtcNow -lt $deadline)

Write-Error "FAIL live archive: $latestStatus"
