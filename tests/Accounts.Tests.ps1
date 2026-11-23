param([string]$Executable = (Join-Path $PSScriptRoot '..\ZaloManager.exe'))
$ErrorActionPreference = 'Stop'
[Reflection.Assembly]::LoadFrom($Executable) | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class AccountFixture {
 [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_open(byte[] path,out IntPtr db);
 [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_exec(IntPtr db,byte[] sql,IntPtr cb,IntPtr state,IntPtr error);
 [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr db);
 public static void Create(string path,string id) {
  IntPtr db; if(sqlite3_open(System.Text.Encoding.UTF8.GetBytes(path+"\0"),out db)!=0)throw new Exception("Fixture open");
  try { string sql="CREATE TABLE [info-cache] ([key] TEXT,val TEXT); INSERT INTO [info-cache] VALUES ('0_"+id+"','{\"zName\":\"Test account\"}');";
   if(sqlite3_exec(db,System.Text.Encoding.UTF8.GetBytes(sql+"\0"),IntPtr.Zero,IntPtr.Zero,IntPtr.Zero)!=0)throw new Exception("Fixture SQL");
  } finally { sqlite3_close(db); }
 }
}
'@
$profile = New-Object ZaloManagerPoc.Profile
$profile.Id = '998866'; $profile.Name = 'Tên Zalo thử'; $profile.InstalledVersion = 'test.1'; $profile.Source = 'test.1'
$data = $profile.DataPath
if (Test-Path -LiteralPath $data) { throw 'Test profile directory already exists.' }
$temp = Join-Path ([IO.Path]::GetTempPath()) ('ZaloAccountsTest-' + [guid]::NewGuid().ToString('N'))
$transferRoot = Join-Path ([ZaloManagerPoc.Store]::Root) 'Transfers'
$before = @(Get-ChildItem -LiteralPath $transferRoot -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
function Assert($condition, $message) { if (-not $condition) { throw $message } }
try {
    New-Item -ItemType Directory -Path $data,$temp -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $data 'Database\_production') -Force | Out-Null
    [AccountFixture]::Create((Join-Path $data 'Database\_production\Storage.db'),'123456789')
    [IO.File]::WriteAllText((Join-Path $data 'ftsm_123456789.db'),'fixture marker')
    [IO.File]::WriteAllText((Join-Path $data 'sample.txt'),'original test payload')
    $archive = Join-Path $temp 'export.zmdata'
    [ZaloManagerPoc.ProfileActions]::Export($profile,$archive)
    [ZaloManagerPoc.ProfileActions]::ValidateArchive($archive,$profile)
    [IO.File]::WriteAllText((Join-Path $data 'sample.txt'),'changed payload')
    [ZaloManagerPoc.ProfileActions]::Import($profile,$archive)
    Assert ([IO.File]::ReadAllText((Join-Path $data 'sample.txt')) -eq 'original test payload') 'Import round trip failed'
    'PASS export/import round trip with SHA-256 manifest'

    $other = New-Object ZaloManagerPoc.Profile
    $other.Id='998865'; $other.InstalledVersion='test.1'
    [ZaloManagerPoc.ProfileActions]::ValidateArchive($archive,$other)
    'PASS new empty profile accepts account-bound archive independent of manager profile ID'
    $metadata=Join-Path $data 'Database\_production\Storage.db'
    Move-Item -LiteralPath $metadata -Destination ($metadata+'.saved')
    Rename-Item -LiteralPath (Join-Path $data 'ftsm_123456789.db') -NewName 'ftsm_999999999.db'
    [AccountFixture]::Create($metadata,'999999999')
    $rejected=$false
    try { [ZaloManagerPoc.ProfileActions]::ValidateArchive($archive,$profile) } catch { $rejected=$true }
    Assert $rejected 'Unknown or different account accepted'
    Remove-Item -LiteralPath $metadata
    Move-Item -LiteralPath ($metadata+'.saved') -Destination $metadata
    Rename-Item -LiteralPath (Join-Path $data 'ftsm_999999999.db') -NewName 'ftsm_123456789.db'
    'PASS account mismatch rejected before replacing data'

    $zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry=$zip.GetEntry('manifest.json'); $reader=New-Object IO.StreamReader($entry.Open())
        try {$manifest=$reader.ReadToEnd()} finally {$reader.Dispose()}
        $entry.Delete(); $entry=$zip.CreateEntry('manifest.json')
        $writer=New-Object IO.StreamWriter($entry.Open())
        try {$writer.Write($manifest.Replace('123456789','999999999'))} finally {$writer.Dispose()}
    } finally {$zip.Dispose()}
    $rejected=$false
    try {[ZaloManagerPoc.ProfileActions]::Import($other,$archive)} catch {$rejected=$true}
    Assert $rejected 'Manifest identity mismatch with extracted SQLite accepted'
    Assert (-not (Test-Path -LiteralPath $other.DataPath)) 'Invalid archive published to empty profile'
    'PASS forged manifest identity rejected against extracted metadata'
    [ZaloManagerPoc.ProfileActions]::Export($profile,$archive)

    $zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry=$zip.GetEntry('data/sample.txt'); $entry.Delete()
        $entry=$zip.CreateEntry('data/sample.txt')
        $writer=New-Object IO.StreamWriter($entry.Open())
        try { $writer.Write('tampered') } finally { $writer.Dispose() }
    } finally { $zip.Dispose() }
    $rejected=$false
    try { [ZaloManagerPoc.ProfileActions]::Import($profile,$archive) } catch { $rejected=$true }
    Assert $rejected 'Corrupt archive accepted'
    Assert ([IO.File]::ReadAllText((Join-Path $data 'sample.txt')) -eq 'original test payload') 'Failed import modified current data'
    'PASS corrupted archive rejected, current data preserved'

    $profile.AvatarPath=Join-Path $temp 'avatar.png'
    $bitmap=New-Object Drawing.Bitmap(64,64)
    try { $bitmap.Save($profile.AvatarPath,[Drawing.Imaging.ImageFormat]::Png) } finally { $bitmap.Dispose() }
    $profile.ShortcutPath=Join-Path $temp 'test.lnk'
    [ZaloManagerPoc.ProfileActions]::CreateShortcut($profile,$temp)
    Assert (Test-Path -LiteralPath $profile.ShortcutPath) 'Shortcut not created'
    Assert (Test-Path -LiteralPath ([ZaloManagerPoc.ProfileActions]::MakeIcon($profile))) 'Avatar icon not created'
    'PASS avatar icon and shortcut generation'

    $runtime=Join-Path $temp 'runtime'
    New-Item -ItemType Directory -Path (Join-Path $runtime 'resources') -Force | Out-Null
    $stub=Join-Path $runtime 'Stub.cs'
    [IO.File]::WriteAllText($stub,'public static class Stub { public static void Main() { System.Threading.Thread.Sleep(120000); } }')
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo /target:winexe (('/out:')+(Join-Path $runtime 'Zalo.exe')) $stub
    if($LASTEXITCODE -ne 0){throw 'Transfer lifecycle fixture build failed'}
    $profile.ActiveDirectory=$runtime
    $package=Join-Path $runtime 'resources\app.asar'
    [IO.File]::WriteAllText($package,'transfer test')
    $profile.PatchedHash=[ZaloManagerPoc.Instance]::Hash($package)
    $fixture=Start-Process -FilePath $profile.Executable -WindowStyle Hidden -PassThru
    try {
        Start-Sleep -Milliseconds 300
        Assert ([ZaloManagerPoc.Instance]::IsRunning($profile)) 'Fixture not running'
        [ZaloManagerPoc.ProfileActions]::TransferWithRestart($profile,$archive,$false)
        Assert ([ZaloManagerPoc.Instance]::IsRunning($profile)) 'Export did not restart prior running profile'
        [ZaloManagerPoc.ProfileActions]::TransferWithRestart($profile,$archive,$true)
        Assert ([ZaloManagerPoc.Instance]::IsRunning($profile)) 'Import did not restart prior running profile'
        [ZaloManagerPoc.Instance]::Stop($profile)
        [ZaloManagerPoc.ProfileActions]::TransferWithRestart($profile,$archive,$false)
        Assert (-not [ZaloManagerPoc.Instance]::IsRunning($profile)) 'Stopped profile was unexpectedly started'
        'PASS automatic stop/restart for export/import, stopped profile stays stopped'
    } finally { [ZaloManagerPoc.Instance]::Stop($profile); $fixture.Dispose() }

    $original = New-Object ZaloManagerPoc.Profile
    $original.Id='original'; $original.Source='C:\original'
    $rejected=$false
    try { [ZaloManagerPoc.ProfileActions]::ArchiveDeleted($original) } catch {$rejected=$true}
    Assert $rejected 'Original profile deletion accepted'
    'PASS original protected from data deletion'
} finally {
    $cleanup = @($data,$temp)
    $cleanup += @(Get-ChildItem -Path ($data + '.before-import-*') -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
    $cleanup += @(Get-ChildItem -Path ($data + '.import-*') -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
    $cleanup += @(Get-ChildItem -LiteralPath $transferRoot -Directory -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notin $before } | Select-Object -ExpandProperty FullName)
    foreach($path in $cleanup) {
        $full=[IO.Path]::GetFullPath($path)
        $safe = $full -eq [IO.Path]::GetFullPath($data) -or $full -eq [IO.Path]::GetFullPath($temp) -or
            $full.StartsWith([IO.Path]::GetFullPath($data)+'.before-import-',[StringComparison]::OrdinalIgnoreCase) -or
            $full.StartsWith([IO.Path]::GetFullPath($data)+'.import-',[StringComparison]::OrdinalIgnoreCase) -or
            $full.StartsWith([IO.Path]::GetFullPath($transferRoot)+'\',[StringComparison]::OrdinalIgnoreCase)
        if(-not $safe) { throw 'Unsafe test cleanup path' }
        if(Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
    }
}
