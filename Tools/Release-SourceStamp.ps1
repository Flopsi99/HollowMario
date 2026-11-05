function Get-HollowMarioSourceStamp([string]$Root) {
    $inputs=@(& git -C $Root ls-files --cached --others --exclude-standard -- '*.cs' '*.csproj' '*.props' '*.ps1' '*.test' 'Assets/*.png' 'Setup/*.txt' 'Setup/*.json' 'IndependentHost/*.md')
    if($LASTEXITCODE){throw 'Release source inventory failed.'}
    $inputs+=@('Tools/Verify-IndependentSource.ps1','IndependentHost/PROVENANCE.md')
    $hashes=@($inputs|Sort-Object -Unique|ForEach-Object {
        $path=Join-Path $Root $_
        if(Test-Path -LiteralPath $path -PathType Leaf){$_+':'+(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
    })
    $sha=[Security.Cryptography.SHA256]::Create()
    try{return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($hashes -join "`n")))).Replace('-','')}
    finally{$sha.Dispose()}
}
