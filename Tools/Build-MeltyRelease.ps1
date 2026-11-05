param([string]$GameDirectory='E:\SteamLibrary\steamapps\common\Hollow Knight')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Build-Release.ps1') -GameDirectory $GameDirectory
if($LASTEXITCODE){throw 'Community API release build failed.'}
$manual=Get-Content -LiteralPath (Join-Path $root '.artifacts/releases/latest.json') -Raw|ConvertFrom-Json
$releaseRoot=Join-Path $root '.artifacts/releases'
$stamp=[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$stage=Join-Path $releaseRoot ('HollowMario-'+$manual.version+'-melty-'+$stamp)
function Copy-Tree([string]$source,[string]$target){foreach($file in Get-ChildItem -LiteralPath $source -Recurse -File){$dest=Join-Path $target $file.FullName.Substring($source.Length+1);New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force|Out-Null;Copy-Item -LiteralPath $file.FullName -Destination $dest}}
Copy-Tree $manual.stage $stage
Copy-Item -LiteralPath (Join-Path $root 'Setup/MELTY-START-HERE.txt') -Destination (Join-Path $stage 'START-HERE.txt') -Force
$fileName='HollowMario-'+$manual.version+'-melty-'+$stamp+'.zip'
$recipe=Get-Content -LiteralPath (Join-Path $root 'Setup/melty-auto-recipe.draft.json') -Raw|ConvertFrom-Json
# Keep the repository recipe synchronized from the same canonical definition;
# only the archive's copy pins this candidate's exact file name.
$recipe|ConvertTo-Json -Depth 15|Set-Content -LiteralPath (Join-Path $root 'melty.json') -Encoding UTF8
$recipe.components[0].fileName=$fileName
if(@($recipe.requirements|Where-Object {$_.kind -eq 'loader'}).Count -or ($recipe.launch.args -join ' ') -match '(?i)doorstop|bepinex'){throw 'The recipe must use the community API setup without an injection loader.'}
if($recipe.launch.path -ne '{managed}/setup/HollowMario-Setup.exe' -or @($recipe.mappings).Count -ne 1 -or $recipe.mappings[0].from -ne '' -or $recipe.mappings[0].to -ne '{managed}/setup'){throw 'The API setup package must be mapped completely before launch.'}
$recipe|ConvertTo-Json -Depth 15|Set-Content -LiteralPath (Join-Path $stage 'melty.json') -Encoding UTF8
$files=@(Get-ChildItem -LiteralPath $stage -Recurse -File|ForEach-Object {
    if($_.Name -eq 'package.json' -and $_.DirectoryName -eq $stage){return}
    $relative=$_.FullName.Substring($stage.Length+1).Replace('\','/')
    if($relative -match '(?i)(^|/)(BepInEx|dispatch|injection)(/|$)|(^|/)(winhttp\.dll|doorstop_config\.ini|HollowMario\.Dispatch\.dll)$'){throw ('Retired injection input in release: '+$relative)}
    if($relative -match '(?i)(^|/)(MarioHollow\.dll|sm64\.dll|Assembly-CSharp[^/]*|UnityPlayer\.dll|hollow_knight\.exe|UnityEngine[^/]*\.dll)$|\.(z64|n64|v64|cfg|log)$|(^|/)settings/|user[1-4]\.dat'){throw ('Private/retired input in release: '+$relative)}
    [ordered]@{Path=$relative;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
})
[ordered]@{SchemaVersion=1;Product='Hollow Mario';Version=$manual.version;SupportedGameVersion='1.5.78.11833';CommunityApi='77';BuildMarioLocally=$true;Files=$files}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $stage 'package.json') -Encoding UTF8
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=Join-Path $releaseRoot $fileName
$writer=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($file in Get-ChildItem -LiteralPath $stage -Recurse -File|Sort-Object FullName){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($writer,$file.FullName,$file.FullName.Substring($stage.Length+1).Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal)|Out-Null}}finally{$writer.Dispose()}
[ordered]@{stage=$stage;archive=$archive;bytes=(Get-Item -LiteralPath $archive).Length;sha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash;version=$manual.version;platformValidation='pending'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $releaseRoot 'latest-melty.json') -Encoding UTF8
Write-Output 'Melty candidate uses only the replacement host. Platform recipe checks and installed gameplay tests remain required; nothing was uploaded or published.'
