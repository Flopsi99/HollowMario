param([Parameter(Mandatory=$true)][string]$Destination,[string]$CacheRoot='')
$ErrorActionPreference='Stop'
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
if(!$CacheRoot){$CacheRoot=Join-Path $env:LOCALAPPDATA 'HollowMario\native'}
$CacheRoot=[IO.Path]::GetFullPath($CacheRoot)
New-Item -ItemType Directory -Path $CacheRoot -Force|Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Fetch($name,$url,$sha){
    $file=Join-Path $CacheRoot $name
    if(!(Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $sha){
        Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile ($file+'.download')
        if((Get-FileHash -LiteralPath ($file+'.download') -Algorithm SHA256).Hash -ne $sha){throw ('Download checksum differs: '+$name)}
        Move-Item -LiteralPath ($file+'.download') -Destination $file -Force
    }
    return $file
}
function Run($exe,$arguments,$directory){
    $start=New-Object Diagnostics.ProcessStartInfo
    $start.FileName=$exe;$start.Arguments=$arguments;$start.WorkingDirectory=$directory
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    $process=[Diagnostics.Process]::Start($start)
    $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
    if(!$process.WaitForExit(600000)){throw 'Local Mario engine build timed out; retry setup.'}
    [IO.File]::AppendAllText((Join-Path $CacheRoot 'build.log'),$stdout.Result+$stderr.Result)
    if($process.ExitCode -ne 0){throw ('Local preparation failed. See '+(Join-Path $CacheRoot 'build.log'))}
    $process.Dispose()
}
$commit='fd11813208272b4271d92bd92feb8f3fdbe61be5'
$output=Join-Path $CacheRoot ($commit+'\dist\sm64.dll')
if(!(Test-Path -LiteralPath $output)){
    Write-Output 'Preparing Mario locally. Getting verified build tools and public source...'
    $source=Fetch 'libsm64.zip' ('https://codeload.github.com/libsm64/libsm64/zip/'+$commit) '693D94A895388E8F60DFF09D65739F75C4628946B5460ECD0DADBEBEC6293578'
    $compiler=Fetch 'w64devkit.7z.exe' 'https://github.com/skeeto/w64devkit/releases/download/v2.10.0/w64devkit-x64-2.10.0.7z.exe' '18D0A4C71A166F8401AB6305781BEC5882B40B5E06BA9807C61CB5F3B3C6325E'
    $python=Fetch 'python.zip' 'https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip' '4ACBED6DD1C744B0376E3B1CF57CE906F9DC9E95E68824584C8099A63025A3C3'
    $geo=Fetch 'geo.inc.c' 'https://raw.githubusercontent.com/n64decomp/sm64/06ec56df7f951f88da05f468cdcacecba496145a/actors/mario/geo.inc.c' 'FC276E326A9CB22F55DFAE8349B0FF746B9A7DB7C108074A47508F3E2B225364'
    $model=Fetch 'model.inc.c' 'https://raw.githubusercontent.com/n64decomp/sm64/06ec56df7f951f88da05f468cdcacecba496145a/actors/mario/model.inc.c' '3C28959F915121CD94DAAB7941E144078098B3CAC668FDC70817F4127C549E8F'
    $tools=Join-Path $CacheRoot 'tools';New-Item -ItemType Directory -Path $tools -Force|Out-Null
    if(!(Test-Path -LiteralPath (Join-Path $tools 'w64devkit\bin\gcc.exe'))){Run $compiler ('-y -o"'+$tools+'"') $CacheRoot}
    $pythonDir=Join-Path $tools 'python'
    if(!(Test-Path -LiteralPath (Join-Path $pythonDir 'python.exe'))){[IO.Compression.ZipFile]::ExtractToDirectory($python,$pythonDir)}
    $work=Join-Path $CacheRoot $commit
    if(!(Test-Path -LiteralPath $work)){
        $unpack=Join-Path $CacheRoot ('unpack-'+[Guid]::NewGuid().ToString('N'))
        [IO.Compression.ZipFile]::ExtractToDirectory($source,$unpack)
        $extracted=Join-Path $unpack ('libsm64-'+$commit)
        Move-Item -LiteralPath $extracted -Destination $work
    }
    # Only the downloaded importer touches its own generated directory.
    $generated=[IO.Path]::GetFullPath((Join-Path $work 'src\decomp\mario'))
    if(!$generated.StartsWith(($CacheRoot.TrimEnd('\')+'\'),[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid local build path'}
    $import=[IO.File]::ReadAllText((Join-Path $work 'import-mario-geo.py'))
    $import=$import.Replace('https://raw.githubusercontent.com/n64decomp/sm64/06ec56df7f951f88da05f468cdcacecba496145a/actors/mario/geo.inc.c',([Uri]$geo).AbsoluteUri)
    $import=$import.Replace('https://raw.githubusercontent.com/n64decomp/sm64/06ec56df7f951f88da05f468cdcacecba496145a/actors/mario/model.inc.c',([Uri]$model).AbsoluteUri)
    [IO.File]::WriteAllText((Join-Path $work 'import-local.py'),$import)
    Run (Join-Path $pythonDir 'python.exe') 'import-local.py' $work
    $bin=Join-Path $tools 'w64devkit\bin';$env:PATH=$bin+';'+$env:PATH
    Write-Output 'Building the Mario engine on this PC...'
    Run (Join-Path $bin 'make.exe') '-j2 CC=gcc lib' $work
}
if(!(Test-Path -LiteralPath $output)){throw 'Local build did not produce its engine.'}
New-Item -ItemType Directory -Path $Destination -Force|Out-Null
Copy-Item -LiteralPath $output -Destination (Join-Path $Destination 'sm64.dll') -Force
Write-Output 'Mario engine prepared locally. No ROM or extracted assets were uploaded.'
