param([switch]$Test)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=$PSScriptRoot
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path $csc)) { throw 'Build on Windows with .NET Framework 4.8 enabled.' }
$cache=Join-Path $root '.build'
New-Item -ItemType Directory -Force $cache | Out-Null
$package=Join-Path $cache 'sharpsnmp-12.5.7.zip'
if (!(Test-Path $package)) {
    [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing 'https://api.nuget.org/v3-flatcontainer/lextm.sharpsnmplib/12.5.7/lextm.sharpsnmplib.12.5.7.nupkg' -OutFile $package
}
if ((Get-FileHash $package -Algorithm SHA256).Hash.ToLower() -ne 'c795ec0b981defb24fc187b4562b9587bd43c4673ef9776a9e0995275c3d707a') { throw 'SharpSNMP package checksum mismatch.' }
Expand-Archive -Path $package -DestinationPath (Join-Path $cache 'sharpsnmp') -Force
$dll=Join-Path $cache 'sharpsnmp\lib\net471\SharpSnmpLib.dll'
$out=Join-Path $root 'dist'
New-Item -ItemType Directory -Force $out | Out-Null
$refs=@('/r:System.dll','/r:System.Core.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll',('/r:'+$dll))
$sources=@(Get-ChildItem (Join-Path $root 'src\*.cs') | ForEach-Object FullName)
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /warnaserror+ "/win32manifest:$root\src\app.manifest" "/win32icon:$root\src\app.ico" "/resource:$dll,SharpSnmpLib.dll" "/resource:$root\THIRD-PARTY-NOTICES.txt,ThirdPartyNotices.txt" "/out:$out\snmptoolRAV.exe" @refs @sources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
if ($Test) {
    & $csc /nologo /target:exe /platform:anycpu /warnaserror+ "/resource:$dll,SharpSnmpLib.dll" "/resource:$root\THIRD-PARTY-NOTICES.txt,ThirdPartyNotices.txt" "/out:$out\Tests.exe" @refs (Join-Path $root 'src\Snmp.cs') (Join-Path $root 'src\Network.cs') (Join-Path $root 'src\V3.cs') (Join-Path $root 'src\Embedded.cs') (Join-Path $root 'tests\Tests.cs') (Join-Path $root 'tests\V3Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    if (Test-Path (Join-Path $out 'SharpSnmpLib.dll')) { Remove-Item (Join-Path $out 'SharpSnmpLib.dll') }
    & "$out\Tests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'SNMP tests failed.' }
    $previews=Join-Path $out 'ui-previews'
    $process=Start-Process -FilePath "$out\snmptoolRAV.exe" -ArgumentList @('--preview', ('"'+$previews+'"')) -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw 'Windows UI rendering failed.' }
    Remove-Item "$out\Tests.exe"
}
Copy-Item (Join-Path $root 'README.md') $out
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.txt') $out
Get-FileHash (Join-Path $out 'snmptoolRAV.exe') -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLower())  snmptoolRAV.exe" } | Set-Content (Join-Path $out 'SHA256SUMS.txt') -Encoding ASCII
