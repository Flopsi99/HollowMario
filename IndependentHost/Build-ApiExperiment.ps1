param(
    [string]$GameDirectory='E:\SteamLibrary\steamapps\common\Hollow Knight',
    [string]$NativeCacheRoot='',
    [switch]$SkipNative
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
& (Join-Path $root 'Tools/Verify-IndependentSource.ps1')
$game=(Resolve-Path -LiteralPath $GameDirectory).Path
$managed=Join-Path $game 'hollow_knight_Data\Managed'
$services=Join-Path $root '.artifacts\independent-host\services'
# Fresh output prevents files from an older payload surviving unnoticed.
$build=Join-Path $root ('.artifacts\independent-host\builds\'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
$output=Join-Path $build 'HollowMario'
$engine=Join-Path $output 'MarioHollow-native'
$helper=Join-Path $output 'Setup'
$helperBuild=Join-Path $root '.artifacts\independent-host\tools\rom-helper'
New-Item -ItemType Directory -Path $engine,$helper,$helperBuild -Force|Out-Null
& (Join-Path $PSScriptRoot 'Prepare-ApiServices.ps1') -GameDirectory $game -Destination $services
if($LASTEXITCODE){throw 'Independent API services preparation failed.'}
$buildProperties=@('-p:LoaderTarget=HollowKnightApi',"-p:ApiPackageDir=$services","-p:HollowKnightDir=$GameDirectory")
& dotnet build (Join-Path $PSScriptRoot 'Integration\HollowMario.Host.Integration.csproj') -c Release -m:1 @buildProperties --nologo -v quiet
if($LASTEXITCODE){throw 'Independent game integration did not build.'}
& dotnet build (Join-Path $root 'HollowMario.csproj') -c Release -m:1 @buildProperties --nologo -v quiet
if($LASTEXITCODE){throw 'Independent companion did not build.'}
$companionOutput='.artifacts/independent-host/companion'
Copy-Item -LiteralPath (Join-Path $root "$companionOutput/HollowMario.dll") -Destination $output -Force
foreach($name in @('HollowMario.ApiRuntime.dll','0Harmony.dll','HARMONY-LICENSE.txt')){Copy-Item -LiteralPath (Join-Path $services $name) -Destination $output -Force}
$hostOutput='Integration/bin/Release/net472'
foreach($name in @('HollowMario.Host.Core.dll','HollowMario.Host.Unity.dll','HollowMario.Host.Integration.dll')){
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "$hostOutput/$name") -Destination $engine -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "$hostOutput/HollowMario.Setup.Core.dll") -Destination $output -Force
& dotnet build (Join-Path $root 'Setup\Launcher\HollowMario.Setup.csproj') -c Release -m:1 "-p:MonoCecilPath=$managed\Mono.Cecil.dll" "-p:OutputPath=$helperBuild\" --nologo -v quiet
if($LASTEXITCODE){throw 'Our ROM helper did not build.'}
foreach($name in @('HollowMario-Setup.exe','HollowMario-Setup.exe.config','HollowMario.Setup.Core.dll','Mono.Cecil.dll')){
    Copy-Item -LiteralPath (Join-Path $helperBuild $name) -Destination $helper -Force
}
Copy-Item -LiteralPath (Join-Path $root 'Setup\Prepare-MarioRuntime.ps1') -Destination $helper -Force
if(!$SkipNative){
    & (Join-Path $root 'Setup\Prepare-MarioRuntime.ps1') -Destination $engine -CacheRoot $NativeCacheRoot
    if($LASTEXITCODE){throw 'Player-local native preparation failed.'}
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NOTICES.md') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $root 'ThirdParty\LibTessDotNet\LICENSE.txt') -Destination (Join-Path $output 'LIBTESS-LICENSE.txt') -Force
foreach($name in @('Mono.Cecil','MonoMod')){Copy-Item -LiteralPath (Join-Path $root "Setup\Licenses\$name.txt") -Destination (Join-Path $output "$name-LICENSE.txt") -Force}
$utility=Join-Path $root '.artifacts\independent-host\tools\ApiDependencies.exe'
$verification='verify'
if($SkipNative){$verification+='-public'}
& $utility $verification $output
if($LASTEXITCODE){throw 'Independent package dependency verification failed.'}
$manifest=[ordered]@{
    privateTestOnly=(!$SkipNative); originalMashupAssemblyUsed=$false;loader='HollowKnightApi'
    libsm64Commit='fd11813208272b4271d92bd92feb8f3fdbe61be5'
    harmonyPackageSha256='2A901F47618853DF53AE39BCB6109186222AC2A17081A7EDE2B2055DDB3091B2'
    sourceCommit=(& git -C $root rev-parse HEAD)
    sourceHasUncommittedChanges=[bool](& git -C $root status --porcelain)
    files=@(Get-ChildItem -LiteralPath $output -File -Recurse|ForEach-Object {
        [ordered]@{name=$_.FullName.Substring($output.Length+1).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    })
}
$manifest|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $output 'build-manifest.json') -Encoding UTF8
[IO.File]::WriteAllText((Join-Path $root '.artifacts\independent-host\latest-package.txt'),$output)
Write-Output "Hollow Mario Community API built from our host source. No retired mashup input or installed game file was used or changed."
