param(
    [Parameter(Mandatory = $true)][string]$InnoCompiler,
    [string]$SandboxieInstaller
)
$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $InnoCompiler).Path
$release = 'https://github.com/sandboxie-plus/Sandboxie/releases/download/v1.19.1'
$sha256 = '443B528766F4E6448632FD94AFA7DF5D9E1C52E13940DB79139D4070DA3AA384'
$stage = Join-Path ([IO.Path]::GetTempPath()) ('ZaloManagerSetup-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
try {
    if (!$SandboxieInstaller) {
        $SandboxieInstaller = Join-Path $stage 'Sandboxie-Plus-x64-v1.19.1.exe'
        Invoke-WebRequest -UseBasicParsing -Uri "$release/Sandboxie-Plus-x64-v1.19.1.exe" -OutFile $SandboxieInstaller
    }
    $SandboxieInstaller = (Resolve-Path -LiteralPath $SandboxieInstaller).Path
    if ((Get-FileHash -LiteralPath $SandboxieInstaller).Hash -ne $sha256 -or
        (Get-AuthenticodeSignature -LiteralPath $SandboxieInstaller).Status -ne 'Valid') {
        throw 'Sandboxie installer checksum or Authenticode verification failed.'
    }
    $license = Join-Path $stage 'LICENSE.Plus'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://raw.githubusercontent.com/sandboxie-plus/Sandboxie/v1.19.1/LICENSE.Plus' -OutFile $license
    & (Join-Path $PSScriptRoot 'build.ps1')
    $manager = Join-Path $PSScriptRoot 'ZaloManager.exe'
    & $compilerPath "/DManagerBinary=$manager" "/DSandboxieInstaller=$SandboxieInstaller" "/DSandboxieLicense=$license" "/O$PSScriptRoot" (Join-Path $PSScriptRoot 'setup\ZaloManager.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed ($LASTEXITCODE)." }
} finally {
    foreach ($file in @('Sandboxie-Plus-x64-v1.19.1.exe', 'LICENSE.Plus')) {
        $stagedFile = Join-Path $stage $file
        if ([IO.File]::Exists($stagedFile)) { [IO.File]::Delete($stagedFile) }
    }
    [IO.Directory]::Delete($stage)
}
