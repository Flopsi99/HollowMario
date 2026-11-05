param(
    [Parameter(Mandatory=$true)][string]$GameDirectory,
    [Parameter(Mandatory=$true)][string]$Destination
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$managed=Join-Path $GameDirectory 'hollow_knight_Data\Managed'
$destinationPath=[IO.Path]::GetFullPath($Destination)
$cache=Join-Path $root '.artifacts\dependencies'
$tools=Join-Path $root '.artifacts\independent-host\tools'
New-Item -ItemType Directory -Path $destinationPath,$cache,$tools -Force|Out-Null
$package=Join-Path $cache 'harmonyx.2.5.2.nupkg'
if(!(Test-Path -LiteralPath $package)){
    [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri 'https://api.nuget.org/v3-flatcontainer/harmonyx/2.5.2/harmonyx.2.5.2.nupkg' -OutFile $package
}
if((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne '2A901F47618853DF53AE39BCB6109186222AC2A17081A7EDE2B2055DDB3091B2'){
    throw 'Official HarmonyX package checksum does not match.'
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead($package)
try{
    foreach($item in @(@('lib/net45/0Harmony.dll',(Join-Path $tools 'HarmonyX-input.dll')),@('LICENSE',(Join-Path $destinationPath 'HARMONY-LICENSE.txt')))){
        $entry=$archive.GetEntry($item[0]);if(!$entry){throw 'Verified HarmonyX package is incomplete.'}
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$item[1],$true)
    }
}finally{$archive.Dispose()}
& dotnet build (Join-Path $root 'Compatibility\ApiRuntime\HollowMario.ApiRuntime.csproj') -c Release -m:1 "-p:HollowKnightDir=$GameDirectory" "-p:OutputPath=$destinationPath\" --nologo -v quiet
if($LASTEXITCODE){throw 'Our community API services did not build.'}
$compiler=Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
if(!(Test-Path -LiteralPath $compiler)){$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'}
$cecil=Join-Path $managed 'Mono.Cecil.dll'
$utility=Join-Path $tools 'ApiDependencies.exe'
& $compiler /nologo /optimize+ /out:$utility /r:$cecil (Join-Path $PSScriptRoot 'Tools\ApiDependencies.cs')
if($LASTEXITCODE){throw 'Independent API dependency utility did not compile.'}
Copy-Item -LiteralPath $cecil -Destination (Join-Path $tools 'Mono.Cecil.dll') -Force
& $utility $managed (Join-Path $tools 'HarmonyX-input.dll') (Join-Path $destinationPath '0Harmony.dll')
if($LASTEXITCODE){throw 'Upstream HarmonyX preparation failed.'}
