param([string]$GameDirectory='E:\SteamLibrary\steamapps\common\Hollow Knight',[string]$RomPathForChecks)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Verify-IndependentSource.ps1')
$version=[regex]::Match([IO.File]::ReadAllText((Join-Path $root 'Plugin.cs')),'PluginVersion\s*=\s*"([^"]+)"').Groups[1].Value
if(!$version){throw 'Shared mod version missing.'}
. (Join-Path $PSScriptRoot 'Release-SourceStamp.ps1')
$sourceStamp=Get-HollowMarioSourceStamp $root
$builder=Join-Path $root 'IndependentHost/Build-ApiExperiment.ps1'
& $builder -GameDirectory $GameDirectory -SkipNative
if($LASTEXITCODE){throw 'Community API host build failed.'}
$apiHost=[IO.File]::ReadAllText((Join-Path $root '.artifacts/independent-host/latest-package.txt')).Trim()
& dotnet build (Join-Path $root 'Setup/Tests/HollowMario.Setup.Tests.csproj') -c Release -m:1 "-p:HollowKnightDir=$GameDirectory" --nologo -v quiet
if($LASTEXITCODE){throw 'Setup tests did not build.'}
$scratch=Join-Path ([IO.Path]::GetTempPath()) ('hollowmario-checks-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
if($RomPathForChecks){& (Join-Path $root 'Setup/Tests/bin/Release/net472/HollowMario.Setup.Tests.exe') $scratch $GameDirectory $RomPathForChecks}
else{& (Join-Path $root 'Setup/Tests/bin/Release/net472/HollowMario.Setup.Tests.exe') $scratch $GameDirectory}
if($LASTEXITCODE){throw 'Setup regression tests failed.'}
if((Get-HollowMarioSourceStamp $root) -ne $sourceStamp){throw 'Source changed during the community API build. Rebuild before packaging.'}
$releaseRoot=Join-Path $root '.artifacts/releases'
$stage=Join-Path $releaseRoot ('HollowMario-'+$version+'-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
$api=Join-Path $stage 'payloads/api'
New-Item -ItemType Directory -Path $api -Force|Out-Null
function Copy-Host([string]$source,[string]$target){
    foreach($file in Get-ChildItem -LiteralPath $source -Recurse -File){
        $relative=$file.FullName.Substring($source.Length+1)
        if($relative -eq 'build-manifest.json' -or $file.Extension -eq '.pdb'){continue}
        $dest=Join-Path $target $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force|Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $dest
    }
}
Copy-Host $apiHost $api
foreach($name in @('HollowMario-Setup.exe','HollowMario-Setup.exe.config','HollowMario.Setup.Core.dll','Mono.Cecil.dll','Prepare-MarioRuntime.ps1')){
    Copy-Item -LiteralPath (Join-Path $api "Setup/$name") -Destination (Join-Path $stage $name)
}
$licenses=Join-Path $stage 'LICENSES'
New-Item -ItemType Directory -Path $licenses -Force|Out-Null
foreach($name in @('Harmony.txt','HarmonyX.txt','Mono.Cecil.txt','MonoMod.txt')){Copy-Item -LiteralPath (Join-Path $root "Setup/Licenses/$name") -Destination $licenses}
Copy-Item -LiteralPath (Join-Path $root 'ThirdParty/LibTessDotNet/LICENSE.txt') -Destination (Join-Path $licenses 'LibTessDotNet.txt')
Copy-Item -LiteralPath (Join-Path $root 'IndependentHost/NOTICES.md') -Destination (Join-Path $stage 'NOTICES.md')
Copy-Item -LiteralPath (Join-Path $root 'Setup/START-HERE.txt') -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'Setup/melty-recipe.draft.json') -Destination $stage
[Reflection.Assembly]::LoadFrom((Join-Path $stage 'HollowMario.Setup.Core.dll'))|Out-Null
$files=@(Get-ChildItem -LiteralPath $stage -Recurse -File|ForEach-Object {
    $relative=$_.FullName.Substring($stage.Length+1).Replace('\','/')
    if($relative -match '(?i)(^|/)(BepInEx|dispatch|injection)(/|$)|(^|/)(winhttp\.dll|doorstop_config\.ini|HollowMario\.Dispatch\.dll)$'){throw ('Retired injection input in release: '+$relative)}
    if($relative -match '(?i)(^|/)(MarioHollow\.dll|sm64\.dll|Assembly-CSharp[^/]*|UnityPlayer\.dll|hollow_knight\.exe|UnityEngine[^/]*\.dll)$|\.(z64|n64|v64|cfg|log)$|(^|/)settings/|user[1-4]\.dat'){throw ('Private/retired input in release: '+$relative)}
    [ordered]@{Path=$relative;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant();SigningSha256=[HollowMario.Setup.Disk]::SigningHash($_.FullName)}
})
[ordered]@{SchemaVersion=1;Product='Hollow Mario';Version=$version;SupportedGameVersion='1.5.78.11833';CommunityApi='77';BuildMarioLocally=$true;Files=$files}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $stage 'package.json') -Encoding UTF8
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=Join-Path $releaseRoot ('HollowMario-'+$version+'-candidate.zip')
if(Test-Path -LiteralPath $archive){throw 'A candidate for this version already exists; increment the version before rebuilding a distributable.'}
$writer=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($file in Get-ChildItem -LiteralPath $stage -Recurse -File|Sort-Object FullName){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($writer,$file.FullName,$file.FullName.Substring($stage.Length+1).Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal)|Out-Null}}finally{$writer.Dispose()}
[ordered]@{stage=$stage;archive=$archive;api=$apiHost;bytes=(Get-Item -LiteralPath $archive).Length;sha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash;version=$version;sourceStamp=$sourceStamp;platformValidation='pending'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $releaseRoot 'latest.json') -Encoding UTF8
Write-Output 'The community API route is packaged from the replacement host. No runtime, ROM, game assembly or retired bridge was bundled. Nothing was published or launched.'
