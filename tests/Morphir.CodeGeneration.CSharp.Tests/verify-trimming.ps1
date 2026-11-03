param([Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$proof = [IO.Path]::GetFullPath($EvidenceDirectory)
if (-not $proof.StartsWith('C:\Temp\morphir-csharp-generator-', [StringComparison]::OrdinalIgnoreCase)) { throw 'Use an owned generator proof path.' }
if (Test-Path -LiteralPath $proof) { throw 'Preserve existing evidence and choose a fresh directory.' }
New-Item -ItemType Directory -Path $proof | Out-Null
$dotnet = 'C:/Users/damre/AppData/Local/dotnetup/dotnet/dotnet.exe'
function Run([string]$Name, [string]$Exe, [string]$Directory, [string[]]$Arguments) {
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
            if (@($owned | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue }).Count) { throw 'Owned timed-out tree remains; do not retry.' }
            throw "$Name timed out; owned tree absence verified."
        }
        $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $proof "$Name.log"), $output)
        if ($process.ExitCode -ne 0) { throw "$Name failed: $proof/$Name.log" }
        return $output.Trim()
    } finally { $process.Dispose() }
}
$feed = Join-Path $proof 'feed'
$consumer = Join-Path $proof 'consumer'
New-Item -ItemType Directory -Path $feed,$consumer | Out-Null
foreach ($project in @('Morphir.Models','Morphir.CodeGeneration.CSharp')) {
    [void](Run "pack-$project" $dotnet $repo @('pack',"src/$project/$project.fsproj",'-o',$feed,'-p:Version=0.0.0-generatortrim','-m:1','-p:UseSharedCompilation=false','-nodeReuse:false'))
}
# Only typed input builders are reused. Their C# observation strings introduce no package dependencies.
$files = @('TypeCases.fs','FunctionCases.fs','PatternCases.fs','SdkCases.fs')
foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $consumer }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'trimming/Program.fs') -Destination $consumer
$compile = (($files + 'Program.fs') | ForEach-Object { '<Compile Include="' + $_ + '"/>' }) -join "`n"
@"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><GenerateProgramFile>false</GenerateProgramFile><DisableImplicitFSharpCoreReference>true</DisableImplicitFSharpCoreReference><RuntimeFrameworkVersion>10.0.14</RuntimeFrameworkVersion></PropertyGroup><ItemGroup><PackageReference Include="Morphir.CodeGeneration.CSharp" Version="0.0.0-generatortrim"/>$compile</ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $consumer 'Consumer.fsproj')
'{"sdk":{"version":"10.0.403","rollForward":"disable"}}' | Set-Content -LiteralPath (Join-Path $consumer 'global.json')
"<configuration><packageSources><clear/><add key=`"local`" value=`"$feed`"/><add key=`"nuget`" value=`"https://api.nuget.org/v3/index.json`"/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders><packageSourceMapping><clear/><packageSource key=`"local`"><package pattern=`"Morphir.*`"/></packageSource><packageSource key=`"nuget`"><package pattern=`"*`"/></packageSource></packageSourceMapping></configuration>" | Set-Content -LiteralPath (Join-Path $consumer 'NuGet.Config')
$bounds = @('-m:1','-p:UseSharedCompilation=false','-nodeReuse:false')
[void](Run 'restore-consumer' $dotnet $consumer (@('restore','Consumer.fsproj','--packages',(Join-Path $proof 'packages'),'--configfile','NuGet.Config') + $bounds))
[void](Run 'build-consumer' $dotnet $consumer (@('build','Consumer.fsproj','--no-restore','-c','Release') + $bounds))
$baseline = Run 'run-consumer' $dotnet $consumer @('exec','bin/Release/net10.0/Consumer.dll')
if (-not $baseline.EndsWith('fresh-in-memory-consumer:passed')) { throw 'Fresh consumer observations failed.' }
$assets = Get-Content -LiteralPath (Join-Path $consumer 'obj/project.assets.json') -Raw | ConvertFrom-Json
$closure = @($assets.libraries.PSObject.Properties.Name)
if ($closure -match 'Expecto|Mono.Cecil|Microsoft.CodeAnalysis|Morphir\.(SDK|Tooling|Ion|IR.Pipeline)') { throw 'Unexpected fresh production dependency closure.' }
$published = Join-Path $proof 'published'
[void](Run 'publish-trimmed' $dotnet $consumer (@('publish','Consumer.fsproj','-c','Release','-r','win-arm64','--self-contained','true','-p:PublishTrimmed=true','-p:TrimMode=link',('-p:NuGetPackageRoot='+(Join-Path $proof 'packages')),('-p:RestorePackagesPath='+(Join-Path $proof 'packages')),'-o',$published) + $bounds))
$trimmed = Run 'run-trimmed' (Join-Path $published 'Consumer.exe') $consumer @()
if ($baseline -ne $trimmed) { throw 'Trimming changed typed generation, cancellation, refusal or deterministic source observations.' }
$publishLog = Get-Content -LiteralPath (Join-Path $proof 'publish-trimmed.log') -Raw
$warnings = @([regex]::Matches($publishLog,'(?m)^.*warning IL\d+.*$') | ForEach-Object { $_.Value })
@{ result='passed';host='Windows arm64 / SDK 10.0.403 / net10.0 runtime 10.0.14';closure=$closure;trimming='self-contained win-arm64, TrimMode=link';warnings=$warnings;sourceEquality='untrimmed and trimmed output byte-identical';tested='both typed families, numeric 14/15, nonempty type/function/pattern/record/SDK text, determinism, pre-cancellation, refusal';untested=@('Native AOT','other operating systems/architectures','legacy IDE/netstandard compiler hosts') } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $proof 'trimming-proof.json')
Write-Output "Fresh packaged consumer and trimming passed: $proof/trimming-proof.json"
