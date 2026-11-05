param(
    [Parameter(Mandatory=$true)][string]$RomPath,
    [string]$GameDirectory='E:\SteamLibrary\steamapps\common\Hollow Knight',
    [string]$PackageDirectory,
    [string]$Python='python',
    [string]$Only,
    [switch]$BuildPackage
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$logs=Join-Path $root '.artifacts\independent-host\regressions'
New-Item -ItemType Directory -Path $logs -Force|Out-Null
if($BuildPackage){
    & (Join-Path $PSScriptRoot 'Build-ApiExperiment.ps1') -GameDirectory $GameDirectory
    if($LASTEXITCODE){throw 'Independent package build failed.'}
}
if(!$PackageDirectory){$PackageDirectory=[IO.File]::ReadAllText((Join-Path $root '.artifacts\independent-host\latest-package.txt')).Trim()}
$library=Join-Path $PackageDirectory 'MarioHollow-native\sm64.dll'
foreach($path in @($RomPath,$library,(Join-Path $PackageDirectory 'HollowMario.dll'))){if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing test input: $path"}}

# Focused reruns avoid rebuilding/rerunning unrelated checks for routine edits.
$arguments=@('-X','utf8',(Join-Path $PSScriptRoot 'RegressionChecks\run.py'),'--game',$GameDirectory,'--package',$PackageDirectory,'--library',$library,'--rom',$RomPath)
if($Only){
    if(@($Only.Split(',')|Where-Object {$_ -in @('DebugToolsChecks','DebugRefreshChecks')}).Count){
        dotnet build (Join-Path $root 'LocalTools\DebugTools\DebugTools.csproj') -c Release --no-restore -m:1 --nologo -v quiet
        if($LASTEXITCODE){throw 'Fresh debug utility is required for its checks.'}
    }
    & $Python @arguments --only $Only;exit $LASTEXITCODE
}

$results=[Collections.Generic.List[object]]::new()
function Check-Step([string]$Name,[scriptblock]$Work){
    $path=Join-Path $logs "$Name.log"
    try{& $Work *> $path;$passed=$LASTEXITCODE -eq 0}catch{$_|Out-File -LiteralPath $path -Append;$passed=$false}
    $results.Add([ordered]@{name=$Name;passed=$passed;log=$path})
    if($passed){Write-Output "PASS $Name"}else{Write-Output "FAIL $Name (see its log)"}
}

# Policy fixtures never needed a host. Keep them explicitly labeled as such.
Check-Step 'RoomEntryPolicy' {dotnet run --project (Join-Path $root 'Tools\RoomEntryChecks\RoomEntryChecks.csproj') -c Release --no-restore --nologo}
Check-Step 'BenchReturnPolicy' {dotnet run --project (Join-Path $root 'Tools\BenchReturnChecks\BenchReturnChecks.csproj') -c Release --no-restore --nologo}
Check-Step 'DebugUtilityBuild' {dotnet build (Join-Path $root 'LocalTools\DebugTools\DebugTools.csproj') -c Release --no-restore -m:1 --nologo -v quiet}
Check-Step 'CoreNative' {
    dotnet build (Join-Path $PSScriptRoot 'Checks\HollowMario.Host.Checks.csproj') -c Release --no-restore -m:1 --nologo -v quiet
    if($LASTEXITCODE){throw 'Core check build failed.'}
    & (Join-Path $PSScriptRoot 'Checks\bin\Release\net472\HollowMario.Host.Checks.exe') $library $RomPath
}
Check-Step 'IntegrationNative' {
    dotnet build (Join-Path $PSScriptRoot 'IntegrationChecks\HollowMario.Host.IntegrationChecks.csproj') -c Release --no-restore -m:1 --nologo -v quiet
    if($LASTEXITCODE){throw 'Integration check build failed.'}
    & $Python -X utf8 (Join-Path $PSScriptRoot 'IntegrationChecks\run.py') $GameDirectory $PackageDirectory $library $RomPath
}
Check-Step 'MigratedRegressions' {& $Python @arguments}
[ordered]@{gameStarted=$false;finishedUtc=[DateTime]::UtcNow.ToString('o');package=$PackageDirectory;steps=@($results.ToArray())}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $logs 'summary.json') -Encoding UTF8
if(@($results|Where-Object {!$_.passed}).Count){exit 1}
Write-Output 'All offline suites passed. No game, save or window was opened. This is not an in-game parity certificate.'
