$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
foreach($relative in @('Legacy','ThirdParty/MarioBridge','Tools/ApiCompatibility/BridgeBuilder.cs.tool')){
    if(Test-Path -LiteralPath (Join-Path $root $relative)){throw ('Retired implementation remains in the project: '+$relative)}
}
foreach($relative in @('Setup/Dispatch/Entrypoint.cs','Tools/Prepare-InjectionSdk.ps1','Caps/CapBindingMenus.cs','Presentation/InjectionVolumeMenu.cs')){
    if(Test-Path -LiteralPath (Join-Path $root $relative)){throw ('Retired injection implementation remains in '+$relative)}
}
# Obsolete host switches must not silently re-enable their old alternatives.
foreach($relative in (& git -C $root ls-files -- '*.cs' '*.csproj' '*.props')){
    $path=Join-Path $root $relative
    if((Test-Path -LiteralPath $path -PathType Leaf) -and [IO.File]::ReadAllText($path).Contains('INDEPENDENT_HOST')){
        throw ('Retired conditional host path remains in '+$relative)
    }
}
foreach($relative in @('Tools/Build-Release.ps1','Tools/Build-MeltyRelease.ps1','Tools/Build-ApiCompatibility.ps1','Compatibility/ApiEntry.cs','HollowMario.csproj')){
    $text=[IO.File]::ReadAllText((Join-Path $root $relative))
    if($relative -eq 'HollowMario.csproj'){$text=[regex]::Replace($text,'(?s)<Target Name="RejectRetiredHost".*?</Target>','')}
    if($text -match 'ThirdParty[\\/]MarioBridge|BridgeBuilder|gg\.melty\.mariohollow"|"MarioHollow\.dll"'){
        throw ('Retired build/startup input remains in '+$relative)
    }
}
# Stored old engine names are allowed only in removal/migration or rejection
# checks; they must never become build inputs or a runtime loading fallback.
Write-Output 'Active source and build entry points use only the replacement host.'
