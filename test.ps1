# Runs protocol and real WinForms checks as libraries. Does not build an EXE.
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=$PSScriptRoot
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path $csc)) { throw 'Run on Windows with .NET Framework 4.8 enabled.' }
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'Run powershell.exe -STA -File ./test.ps1.' }
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
$checks=Join-Path $cache 'checks'
New-Item -ItemType Directory -Force $checks | Out-Null
$refs=@('/r:System.dll','/r:System.Core.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll',('/r:'+$dll))
$resources=@("/resource:$dll,SharpSnmpLib.dll","/resource:$root\THIRD-PARTY-NOTICES.txt,ThirdPartyNotices.txt")
$protocol=@('Snmp.cs','Network.cs','V3.cs','Embedded.cs') | ForEach-Object { Join-Path $root "src\$_" }
& $csc /nologo /target:library /warnaserror+ "/out:$checks\ProtocolChecks.dll" @refs @resources @protocol "$root\tests\Tests.cs" "$root\tests\V3Tests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Protocol check compilation failed.' }
$assembly=[Reflection.Assembly]::LoadFile((Join-Path $checks 'ProtocolChecks.dll'))
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$result=$assembly.GetType('Tests').GetMethod('Main',$flags).Invoke($null,@())
if ($result -ne 0) { throw 'Protocol tests failed.' }
$sources=@(Get-ChildItem (Join-Path $root 'src\*.cs') | ForEach-Object FullName)
& $csc /nologo /target:library /warnaserror+ "/out:$checks\UiChecks.dll" @refs @resources @sources
if ($LASTEXITCODE -ne 0) { throw 'UI check compilation failed.' }
$ui=[Reflection.Assembly]::LoadFile((Join-Path $checks 'UiChecks.dll'))
$arguments=New-Object object[] 1
$arguments[0]=[string[]]@('--preview',(Join-Path $checks 'ui-previews'))
$result=$ui.GetType('SnmptoolRAV.Program').GetMethod('Main',$flags).Invoke($null,$arguments)
if ($result -ne 0) {
    $errorFile=Join-Path $checks 'ui-previews.error.txt'
    if (Test-Path $errorFile) { Get-Content $errorFile | Write-Host }
    throw 'WinForms rendering or UI regression checks failed.'
}
Write-Host 'Protocol and WinForms checks passed. No EXEs were built.'
