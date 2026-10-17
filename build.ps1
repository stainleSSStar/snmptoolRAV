param([switch]$Test)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=$PSScriptRoot
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path $csc)) { throw 'Build on Windows with .NET Framework 4.8 enabled.' }
$out=Join-Path $root 'dist'
New-Item -ItemType Directory -Force $out | Out-Null
$refs=@('/r:System.dll','/r:System.Core.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll')
$sources=@(Get-ChildItem (Join-Path $root 'src\*.cs') | ForEach-Object FullName)
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /warnaserror+ "/win32manifest:$root\src\app.manifest" "/win32icon:$root\src\app.ico" "/out:$out\snmptoolRAV.exe" @refs @sources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
if ($Test) {
    & $csc /nologo /target:exe /platform:anycpu /warnaserror+ "/out:$out\Tests.exe" @refs (Join-Path $root 'src\Snmp.cs') (Join-Path $root 'src\Network.cs') (Join-Path $root 'tests\Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & "$out\Tests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'SNMP tests failed.' }
    $previews=Join-Path $out 'ui-previews'
    $process=Start-Process -FilePath "$out\snmptoolRAV.exe" -ArgumentList @('--preview', ('"'+$previews+'"')) -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw 'Windows UI rendering failed.' }
    Remove-Item "$out\Tests.exe"
}
Copy-Item (Join-Path $root 'README.md') $out
Get-FileHash (Join-Path $out 'snmptoolRAV.exe') -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLower())  snmptoolRAV.exe" } | Set-Content (Join-Path $out 'SHA256SUMS.txt') -Encoding ASCII
