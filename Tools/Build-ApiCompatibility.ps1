param([string]$GameDirectory='E:\SteamLibrary\steamapps\common\Hollow Knight')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
& (Join-Path $root 'IndependentHost/Build-ApiExperiment.ps1') -GameDirectory $GameDirectory -SkipNative
if($LASTEXITCODE){throw 'Community API build failed.'}
