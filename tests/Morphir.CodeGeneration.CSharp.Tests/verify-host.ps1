param([Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$proof = [IO.Path]::GetFullPath($EvidenceDirectory)
if (-not $proof.StartsWith('C:\Temp\morphir-csharp-generator-', [StringComparison]::OrdinalIgnoreCase)) { throw 'Use an owned generator proof path under C:/Temp.' }
if (Test-Path -LiteralPath $proof) { throw 'Preserve existing proof evidence and choose a fresh directory.' }
New-Item -ItemType Directory -Force -Path $proof | Out-Null
$dotnet = (Get-Command dotnet).Source
function Invoke-Proof([string]$Name, [string]$WorkingDirectory, [string[]]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($dotnet)
    $start.WorkingDirectory = $WorkingDirectory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $start.Environment['DOTNET_PROCESSOR_COUNT'] = '2'
    $start.Environment['MSBUILDDISABLENODEREUSE'] = '1'
    $start.Environment['TEMP'] = $proof
    $start.Environment['TMP'] = $proof
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(600000)) {
        $all = Get-CimInstance Win32_Process
        $owned = [Collections.Generic.HashSet[int]]::new()
        [void]$owned.Add($process.Id)
        do {
            $changed = $false
            foreach ($item in $all) { if ($owned.Contains([int]$item.ParentProcessId) -and $owned.Add([int]$item.ProcessId)) { $changed = $true } }
        } while ($changed)
        $process.Kill($true)
        $process.WaitForExit()
        $survivors = @($owned | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
        if ($survivors.Count) { throw "Timed-out owned tree still has processes $survivors. Do not retry." }
        throw "Timed out: $Name"
    }
    $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
    [IO.File]::WriteAllText((Join-Path $proof "$Name.log"), $output)
    if ($process.ExitCode -ne 0) { throw "$Name failed with $($process.ExitCode). See $proof/$Name.log" }
    $process.Dispose()
}
$feed = Join-Path $proof 'feed'
New-Item -ItemType Directory -Force -Path $feed | Out-Null
$version = '0.0.0-generatorproof'
foreach ($project in @('Morphir.Models', 'Morphir.CodeGeneration.CSharp', 'Morphir.SDK.CSharp')) {
    $extension = if ($project -eq 'Morphir.SDK.CSharp') { 'csproj' } else { 'fsproj' }
    Invoke-Proof "pack-$project" $repo @('pack', "src/$project/$project.$extension", '-o', $feed, '-p:Version=0.0.0-generatorproof', '-m:1', '-p:UseSharedCompilation=false', '-nodeReuse:false', '--nologo')
}
$consumer = Join-Path $proof 'consumer'
New-Item -ItemType Directory -Force -Path $consumer | Out-Null
[xml]$sourceProject = Get-Content (Join-Path $PSScriptRoot 'Morphir.CodeGeneration.CSharp.Tests.fsproj') -Raw
$sources = @($sourceProject.Project.ItemGroup.Compile.Include | Where-Object { $_ })
foreach ($source in $sources) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $source) -Destination $consumer }
$compileItems = ($sources | ForEach-Object { '<Compile Include="' + $_ + '" />' }) -join "`n"
$projectText = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><GenerateProgramFile>false</GenerateProgramFile><DisableImplicitFSharpCoreReference>true</DisableImplicitFSharpCoreReference></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Morphir.CodeGeneration.CSharp" Version="0.0.0-generatorproof" />
    <PackageReference Include="Morphir.SDK.CSharp" Version="0.0.0-generatorproof" />
    <PackageReference Include="Expecto" Version="10.2.3" />
    <Reference Include="Microsoft.CodeAnalysis" HintPath="$(MSBuildSDKsPath)/../Roslyn/bincore/Microsoft.CodeAnalysis.dll" />
    <Reference Include="Microsoft.CodeAnalysis.CSharp" HintPath="$(MSBuildSDKsPath)/../Roslyn/bincore/Microsoft.CodeAnalysis.CSharp.dll" />
    <!-- COMPILE -->
  </ItemGroup>
</Project>
'@
$projectText.Replace('<!-- COMPILE -->', $compileItems) | Set-Content -LiteralPath (Join-Path $consumer 'Host.fsproj')
@'
{"sdk":{"version":"10.0.403","rollForward":"disable"}}
'@ | Set-Content -LiteralPath (Join-Path $consumer 'global.json')
$config = @"
<configuration><packageSources><clear/><add key="local" value="$feed"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><packageSource key="local"><package pattern="Morphir.*"/></packageSource><packageSource key="nuget"><package pattern="*"/></packageSource></packageSourceMapping></configuration>
"@
[IO.File]::WriteAllText((Join-Path $consumer 'NuGet.Config'), $config)
Invoke-Proof 'restore-host' $consumer @('restore', 'Host.fsproj', '--packages', (Join-Path $proof 'packages'), '--configfile', 'NuGet.Config', '--nologo')
Invoke-Proof 'build-host' $consumer @('build', 'Host.fsproj', '--no-restore', '-m:1', '-p:UseSharedCompilation=false', '-nodeReuse:false', '--nologo')
Invoke-Proof 'run-host' $consumer @('exec', 'bin/Debug/net10.0/Host.dll', '--sequenced')
$assets = Get-Content (Join-Path $consumer 'obj/project.assets.json') -Raw | ConvertFrom-Json
$closure = @($assets.libraries.PSObject.Properties.Name)
if ($closure -match 'Morphir.(Tooling|Ion|IR.Pipeline)' -or $closure -match 'Microsoft.CodeAnalysis') { throw 'Unexpected production package closure.' }
@{ version=$version; closure=$closure; host='.NET 10.0.14 / SDK 10.0.403 / Windows arm64'; source='same test-only in-memory adapter'; result='passed' } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $proof 'host-proof.json')
Write-Output "Packaged host passed: $proof/host-proof.json"
