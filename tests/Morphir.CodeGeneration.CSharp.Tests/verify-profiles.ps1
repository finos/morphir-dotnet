param(
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][string]$RcDotnet,
    [string]$StableDotnet = 'C:/Users/damre/AppData/Local/dotnetup/dotnet/dotnet.exe'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$proof = [IO.Path]::GetFullPath($EvidenceDirectory)
if (-not $proof.StartsWith('C:\Temp\morphir-csharp-generator-', [StringComparison]::OrdinalIgnoreCase)) { throw 'Use an owned generator proof directory.' }
if (Test-Path -LiteralPath $proof) { throw 'Preserve old evidence and choose a fresh directory.' }
New-Item -ItemType Directory -Path $proof | Out-Null
$testDll = Join-Path $PSScriptRoot 'bin/Release/net10.0/Morphir.CodeGeneration.CSharp.Tests.dll'
function Run([string]$Name, [string]$Exe, [string]$Directory, [string[]]$Arguments, [string]$Root) {
    $start = [Diagnostics.ProcessStartInfo]::new($Exe)
    $start.WorkingDirectory = $Directory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    foreach ($key in @('TEMP','TMP')) { $start.Environment[$key] = $proof }
    $start.Environment['DOTNET_PROCESSOR_COUNT'] = '2'
    $start.Environment['MSBUILDDISABLENODEREUSE'] = '1'
    $start.Environment['DOTNET_ROOT'] = $Root
    $start.Environment['DOTNET_ROOT_ARM64'] = $Root
    $start.Environment['DOTNET_ROLL_FORWARD'] = 'LatestPatch'
    $start.Environment['PATH'] = "$Root;" + $start.Environment['PATH']
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(600000)) {
            $all = Get-CimInstance Win32_Process
            $owned = [Collections.Generic.HashSet[int]]::new()
            [void]$owned.Add($process.Id)
            do { $changed=$false; foreach ($item in $all) { if ($owned.Contains([int]$item.ParentProcessId) -and $owned.Add([int]$item.ProcessId)) { $changed=$true } } } while ($changed)
            $process.Kill($true); $process.WaitForExit()
            if (@($owned | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue }).Count) { throw 'Owned timed-out process tree remains; do not retry.' }
            throw "$Name timed out; owned tree absence verified."
        }
        $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $proof "$Name.log"), $output)
        if ($process.ExitCode -ne 0) { throw "$Name failed: $proof/$Name.log" }
        return $output.Trim()
    } finally { $process.Dispose() }
}
$results = [Collections.Generic.List[object]]::new()
$feed = Join-Path $proof 'feed'
New-Item -ItemType Directory -Path $feed | Out-Null
[void](Run 'pack-runtime' $StableDotnet $repo @('pack','src/Morphir.SDK.CSharp/Morphir.SDK.CSharp.csproj','--no-restore','-o',$feed,'-p:Version=0.0.0-generatorprofiles','-m:1','-p:UseSharedCompilation=false','-nodeReuse:false') (Split-Path $StableDotnet))
foreach ($profile in @(@{name='stable';language=14;sdk='10.0.403';exe=$StableDotnet}, @{name='rc2';language=15;sdk='11.0.100-rc.2.26504.105';exe=$RcDotnet})) {
  foreach ($family in @('classic','v4')) {
    $step = "$($profile.name)-$family"
    $directory = Join-Path $proof $step
    New-Item -ItemType Directory -Path $directory | Out-Null
    [void](Run "$step-export" $StableDotnet $repo @('exec',$testDll,'--export-profile',"$($profile.language)",$family,$directory) (Split-Path $StableDotnet))
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'profile-consumer.cs.txt') -Destination (Join-Path $directory 'Program.cs')
    @{sdk=@{version=$profile.sdk;rollForward='disable';allowPrerelease=($profile.name -eq 'rc2')}} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $directory 'global.json')
    @"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><LangVersion>$($profile.language).0</LangVersion><UseAppHost>false</UseAppHost><RuntimeFrameworkVersion>10.0.14</RuntimeFrameworkVersion></PropertyGroup><ItemGroup><PackageReference Include="Morphir.SDK.CSharp" Version="0.0.0-generatorprofiles" /></ItemGroup></Project>
"@ | Set-Content (Join-Path $directory 'Consumer.csproj')
    "<configuration><packageSources><clear/><add key=`"runtime`" value=`"$feed`"/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders><packageSourceMapping><clear/><packageSource key=`"runtime`"><package pattern=`"Morphir.SDK.CSharp`"/></packageSource></packageSourceMapping></configuration>" | Set-Content (Join-Path $directory 'NuGet.Config')
    $root = Split-Path $profile.exe
    $sdk = Run "$step-sdk" $profile.exe $directory @('--version') $root
    if ($sdk -ne $profile.sdk) { throw 'Unexpected SDK selected.' }
    $compiler = Run "$step-compiler" $profile.exe $directory @('exec',(Join-Path $root "sdk/$sdk/Roslyn/bincore/csc.dll"),'-version') $root
    $bounds = @('-m:1','-p:UseSharedCompilation=false','-nodeReuse:false')
    [void](Run "$step-restore" $profile.exe $directory (@('restore','Consumer.csproj','--configfile','NuGet.Config','--packages',(Join-Path $directory 'packages')) + $bounds) $root)
    [void](Run "$step-build" $profile.exe $directory (@('build','Consumer.csproj','--no-restore','-c','Release','-warnaserror') + $bounds) $root)
    $output = Run "$step-run" $StableDotnet $directory @('exec','bin/Release/net10.0/Consumer.dll') (Split-Path $StableDotnet)
    if ($output -ne 'generated-consumer:types-passed') { throw 'Independent type observation mismatch.' }
    $sources = @(Get-ChildItem -LiteralPath $directory -Filter '*.g.cs' -Recurse | ForEach-Object { @{path=[IO.Path]::GetRelativePath($directory,$_.FullName);sha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant()} })
    $results.Add(@{profile=$profile.name;family=$family;language=$profile.language;sdk=$sdk;compiler=$compiler;runtime='10.0.14';output=$output;sources=$sources})
}
}
foreach ($result in $results) { if (($results[0].sources | ConvertTo-Json -Compress) -ne ($result.sources | ConvertTo-Json -Compress)) { throw 'Profile/family public sources differ.' } }
$results | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $proof 'profiles.json')
Write-Output "Both numeric compiler profiles compiled and executed: $proof/profiles.json"
