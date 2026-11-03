param(
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][string]$StableDotnet,
    [Parameter(Mandatory)][string]$RcDotnet,
    [ValidateSet('All', 'Ordinary', 'Publish')][string]$Mode = 'All',
    [ValidatePattern('^[a-zA-Z0-9]+$')][string]$PublicationAttempt = '01'
)

# Run only while holding the delivery-wide expensive-command lease.
# Toolchain installation is separate. This script never changes repository or
# machine configuration and never deletes an earlier evidence directory.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (!$evidence.StartsWith('C:\Temp\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Use an owned directory below C:/Temp.' }
$stableVersion = '10.0.403'
$rcVersion = '11.0.100-rc.2.26504.105'
$packageVersion = '0.0.0-sdkproof.0b01ff0'
$stableRoot = Split-Path ([IO.Path]::GetFullPath($StableDotnet))
$rcRoot = Split-Path ([IO.Path]::GetFullPath($RcDotnet))
$feed = Join-Path $evidence 'feed'
$temp = Join-Path $evidence 'temp'
$reportPath = Join-Path $evidence 'results.json'
$bounds = @('-m:1', '-p:UseSharedCompilation=false', '-nodeReuse:false')
$expectedOutput = 'sdk-consumer:passed'
New-Item -ItemType Directory -Path $evidence, $temp -Force | Out-Null

function Invoke-Checked([string]$Name, [string]$Exe, [string[]]$ArgumentList, [string]$WorkingDirectory, [string]$DotnetRoot, [string]$Packages, [int]$TimeoutSeconds = 1200) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $Exe
    $info.WorkingDirectory = $WorkingDirectory
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $ArgumentList) { $info.ArgumentList.Add($argument) }
    $info.Environment['TEMP'] = $temp
    $info.Environment['TMP'] = $temp
    $info.Environment['DOTNET_PROCESSOR_COUNT'] = '2'
    $info.Environment['MSBUILDDISABLENODEREUSE'] = '1'
    $info.Environment['BuildInParallel'] = 'false'
    $info.Environment['UseSharedCompilation'] = 'false'
    $info.Environment['DOTNET_ROOT'] = $DotnetRoot
    $info.Environment['DOTNET_ROOT_ARM64'] = $DotnetRoot
    $info.Environment['DOTNET_HOST_PATH'] = Join-Path $DotnetRoot 'dotnet.exe'
    $info.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    # SDK selection remains exact via each global.json. Framework-dependent SDK
    # tools may request a servicing runtime build and must resolve its stable patch.
    $info.Environment['DOTNET_ROLL_FORWARD'] = 'LatestPatch'
    $info.Environment['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    $info.Environment['DOTNET_NOLOGO'] = '1'
    $info.Environment['PATH'] = "$DotnetRoot;C:/Users/damre/scoop/apps/git/current/cmd;" + $info.Environment['PATH']
    $vsInstaller = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio/Installer'
    if (Test-Path -LiteralPath (Join-Path $vsInstaller 'vswhere.exe')) { $info.Environment['PATH'] = "$vsInstaller;" + $info.Environment['PATH'] }
    if ($Packages) { $info.Environment['NUGET_PACKAGES'] = $Packages }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    $stdoutPath = Join-Path $evidence "$Name.log"
    $stderrPath = Join-Path $evidence "$Name.stderr.log"
    $stdoutFile = [IO.File]::Open($stdoutPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    $stderrFile = [IO.File]::Open($stderrPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try {
        if (!$process.Start()) { throw "Could not start $Name" }
        $processId = $process.Id
        @{name=$Name;pid=$processId;executable=$Exe;arguments=$ArgumentList;workingDirectory=$WorkingDirectory;dotnetRoot=$DotnetRoot;packages=$Packages} |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence "$Name.command.json")
        $stdout = $process.StandardOutput.BaseStream.CopyToAsync($stdoutFile)
        $stderr = $process.StandardError.BaseStream.CopyToAsync($stderrFile)
        if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
            $all = Get-CimInstance Win32_Process
            $owned = [Collections.Generic.HashSet[int]]::new()
            [void]$owned.Add($processId)
            do {
                $changed = $false
                foreach ($item in $all) { if ($owned.Contains([int]$item.ParentProcessId) -and $owned.Add([int]$item.ProcessId)) { $changed = $true } }
            } while ($changed)
            $process.Kill($true)
            $process.WaitForExit()
            $survivors = @($owned | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
            if ($survivors.Count) { throw "Timed-out owned tree still has processes $survivors. Do not retry." }
            throw "$Name timed out; its whole process tree was terminated and absence verified."
        }
        [Threading.Tasks.Task]::WaitAll(@($stdout, $stderr))
        $exitCode = $process.ExitCode
    }
    finally { $stdoutFile.Dispose(); $stderrFile.Dispose(); $process.Dispose() }
    if ($exitCode -ne 0) { throw "$Name exited $exitCode. See $stdoutPath and $stderrPath." }
    return [IO.File]::ReadAllText($stdoutPath).Trim()
}

function New-Consumer([string]$Name, [string]$Sdk, [string]$Language, [bool]$PublishTools) {
    $dir = Join-Path $evidence $Name
    if (Test-Path -LiteralPath $dir) { throw "Consumer already exists. Preserve it and choose a fresh proof directory: $dir" }
    New-Item -ItemType Directory -Path $dir | Out-Null
    $global = @{sdk=@{version=$Sdk;rollForward='disable';allowPrerelease=$Sdk.Contains('-')}}
    $global | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $dir 'global.json')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'consumer.cs.txt') -Destination (Join-Path $dir 'Program.cs')
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>$Language</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UseAppHost>false</UseAppHost>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
    <RuntimeFrameworkVersion>10.0.14</RuntimeFrameworkVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Morphir.SDK.CSharp" Version="$packageVersion" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $dir 'Consumer.csproj')
    $escapedFeed = [Security.SecurityElement]::Escape($feed)
    $tools = if ($PublishTools) { '<add key="framework-tools" value="https://api.nuget.org/v3/index.json" />' } else { '' }
    $toolMapping = if ($PublishTools) { '<packageSource key="framework-tools"><package pattern="Microsoft.*"/><package pattern="runtime.*"/><package pattern="System.*"/></packageSource>' } else { '' }
    @"
<configuration>
  <packageSources><clear/><add key="sdk-local" value="$escapedFeed"/>$tools</packageSources>
  <fallbackPackageFolders><clear/></fallbackPackageFolders>
  <packageSourceMapping><clear/><packageSource key="sdk-local"><package pattern="Morphir.SDK.CSharp"/></packageSource>$toolMapping</packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath (Join-Path $dir 'NuGet.config')
    return $dir
}

function Assert-OrdinaryClosure([string]$Directory) {
    $assets = Get-Content (Join-Path $Directory 'obj/project.assets.json') -Raw | ConvertFrom-Json
    $libraries = @($assets.libraries.PSObject.Properties.Name)
    if ($libraries.Count -ne 1 -or $libraries[0] -ne "Morphir.SDK.CSharp/$packageVersion") { throw "Unexpected consumer dependency closure: $libraries" }
    $config = [xml](Get-Content (Join-Path $Directory 'Consumer.csproj') -Raw)
    if (@($config.Project.ItemGroup.ProjectReference | Where-Object { $_ }).Count) { throw 'Consumer must not reference repository projects.' }
}

if ($Mode -ne 'Publish') {
    if (Test-Path -LiteralPath $reportPath) { throw 'Preserve the existing report and use a fresh evidence directory.' }
    New-Item -ItemType Directory -Path $feed | Out-Null
    $packArgs = @('pack', (Join-Path $repo 'src/Morphir.SDK.CSharp/Morphir.SDK.CSharp.csproj'), '-c', 'Release', '--no-restore', '-o', $feed, "-p:Version=$packageVersion") + $bounds
    [void](Invoke-Checked 'pack' $StableDotnet $packArgs $repo $stableRoot '')
    $package = Join-Path $feed "Morphir.SDK.CSharp.$packageVersion.nupkg"
    $archive = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        $entry = $archive.Entries | Where-Object FullName -like '*.nuspec' | Select-Object -First 1
        $reader = [IO.StreamReader]::new($entry.Open())
        try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($nuspec.package.metadata.id -ne 'Morphir.SDK.CSharp' -or @($nuspec.package.metadata.dependencies.group.dependency | Where-Object { $_ }).Count) { throw 'Runtime package dependency contract failed.' }
        $nuspec.Save((Join-Path $evidence 'package.nuspec'))
    }
    finally { $archive.Dispose() }
    $report = [ordered]@{packageVersion=$packageVersion;packageSha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $package).Hash.ToLowerInvariant();runtimeSourceCommit=$nuspec.package.metadata.repository.commit;targetFramework='net10.0';runtimeVersion='10.0.14';sourceSha256=(Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $PSScriptRoot 'consumer.cs.txt')).Hash.ToLowerInvariant();profiles=@();publications=@()}
    foreach ($profile in @(@{name='stable';sdk=$stableVersion;language='14.0';dotnet=$StableDotnet;root=$stableRoot}, @{name='rc';sdk=$rcVersion;language='15.0';dotnet=$RcDotnet;root=$rcRoot})) {
        $dir = New-Consumer $profile.name $profile.sdk $profile.language $false
        $packages = Join-Path $dir 'packages'
        $actualSdk = Invoke-Checked "$($profile.name)-sdk" $profile.dotnet @('--version') $dir $profile.root $packages
        if ($actualSdk -ne $profile.sdk) { throw "Wrong compiler SDK selected: $actualSdk" }
        $compiler = Join-Path $profile.root "sdk/$($profile.sdk)/Roslyn/bincore/csc.dll"
        $compilerVersion = Invoke-Checked "$($profile.name)-compiler" $profile.dotnet @('exec', $compiler, '-version') $dir $profile.root $packages
        [void](Invoke-Checked "$($profile.name)-restore" $profile.dotnet (@('restore', 'Consumer.csproj', '--configfile', 'NuGet.config', '--packages', $packages) + $bounds) $dir $profile.root $packages)
        Assert-OrdinaryClosure $dir
        [void](Invoke-Checked "$($profile.name)-build" $profile.dotnet (@('build', 'Consumer.csproj', '-c', 'Release', '--no-restore', '-warnaserror') + $bounds) $dir $profile.root $packages)
        $output = Invoke-Checked "$($profile.name)-run" $StableDotnet @('exec', (Join-Path $dir 'bin/Release/net10.0/Consumer.dll')) $dir $stableRoot $packages
        if ($output -ne $expectedOutput) { throw "Wrong $($profile.name) consumer output: $output" }
        $report.profiles += @{name=$profile.name;sdk=$actualSdk;compiler=$compilerVersion;languageVersion=$profile.language;output=$output;packageDependencies=@("Morphir.SDK.CSharp/$packageVersion")}
        $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath
        Write-Output "$($profile.name) numeric C#$($profile.language) package consumer PASSED"
    }
}
else { $report = Get-Content $reportPath -Raw | ConvertFrom-Json -AsHashtable }

if ($Mode -ne 'Ordinary') {
    if ($report.profiles.Count -ne 2 -or @($report.profiles | Where-Object { $_.output -ne $expectedOutput }).Count) { throw 'Both ordinary profiles must pass before publication comparison.' }
    foreach ($kind in @('trimmed', 'nativeaot')) {
        $step = "$kind-$PublicationAttempt"
        $dir = New-Consumer $step $stableVersion '14.0' $true
        $packages = Join-Path $dir 'packages'
        $publishDir = Join-Path $dir 'publish'
        $settings = @('-r', 'win-arm64', '-p:SelfContained=true', '-p:UseAppHost=true', '-p:RuntimeFrameworkVersion=10.0.14', '-p:IlcMaxDegreeOfParallelism=2', '-warnaserror')
        if ($kind -eq 'trimmed') { $settings += @('-p:PublishTrimmed=true', '-p:TrimMode=link', '-p:ILLinkTreatWarningsAsErrors=true') }
        else { $settings += @('-p:PublishAot=true') }
        [void](Invoke-Checked "$step-restore" $StableDotnet (@('restore', 'Consumer.csproj', '--configfile', 'NuGet.config', '--packages', $packages) + $settings + $bounds) $dir $stableRoot $packages)
        [void](Invoke-Checked "$step-publish" $StableDotnet (@('publish', 'Consumer.csproj', '-c', 'Release', '--no-restore', '-o', $publishDir) + $settings + $bounds) $dir $stableRoot $packages)
        $exe = Join-Path $publishDir 'Consumer.exe'
        $output = Invoke-Checked "$step-run" $exe @() $publishDir $stableRoot $packages
        if ($output -ne $expectedOutput -or @($report.profiles | Where-Object { $_.output -ne $output }).Count -gt 0) { throw "$kind output differs from ordinary consumers." }
        $report.publications += @{kind=$kind;rid='win-arm64';sdk=$stableVersion;languageVersion='14.0';output=$output;executableBytes=(Get-Item -LiteralPath $exe).Length;executableSha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $exe).Hash.ToLowerInvariant()}
        $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath
        Write-Output "$kind win-arm64 published executable PASSED"
    }
}
Write-Output "Evidence: $reportPath"
