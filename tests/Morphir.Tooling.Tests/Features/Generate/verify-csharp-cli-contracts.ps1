param(
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][string]$RcDotnet,
    [Parameter(Mandatory)][string]$RuntimeFeed,
    [string]$StableDotnet='C:/Users/damre/AppData/Local/dotnetup/dotnet/dotnet.exe',
    [switch]$CancellationOnly
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$proof=[IO.Path]::GetFullPath($EvidenceDirectory)
if(-not $proof.StartsWith('C:\Temp\morphir-csharp-cli-', [StringComparison]::OrdinalIgnoreCase)){throw 'Use an owned C# CLI proof directory'}
if(Test-Path -LiteralPath $proof){throw 'Preserve existing proof and choose a fresh directory'}
New-Item -ItemType Directory -Path $proof | Out-Null
$rc=$RcDotnet
$bounds=@('-m:1','-p:UseSharedCompilation=false','-nodeReuse:false')
$commands=[Collections.Generic.List[object]]::new()
function Run([string]$Name,[string]$Exe,[string]$Directory,[string[]]$Arguments,[hashtable]$Extra=@{},[int]$Expected=0){
    $start=[Diagnostics.ProcessStartInfo]::new($Exe)
    $start.WorkingDirectory=$Directory
    $start.UseShellExecute=$false
    $start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true
    $start.RedirectStandardError=$true
    foreach($arg in $Arguments){$start.ArgumentList.Add($arg)}
    $start.Environment['TEMP']=$proof
    $start.Environment['TMP']=$proof
    $start.Environment['DOTNET_PROCESSOR_COUNT']='2'
    $start.Environment['MSBUILDDISABLENODEREUSE']='1'
    $start.Environment['DOTNET_CLI_TELEMETRY_OPTOUT']='1'
    $start.Environment['DOTNET_ROLL_FORWARD']='LatestPatch'
    $start.Environment['PATH']=(Split-Path $StableDotnet)+';C:/Program Files (x86)/Microsoft Visual Studio/Installer;'+$env:PATH
    foreach($key in $Extra.Keys){$start.Environment[$key]=[string]$Extra[$key]}
    $process=[Diagnostics.Process]::Start($start)
    $watch=[Diagnostics.Stopwatch]::StartNew()
    try{
        $outTask=$process.StandardOutput.ReadToEndAsync()
        $errTask=$process.StandardError.ReadToEndAsync()
        if(-not $process.WaitForExit(900000)){
            $all=Get-CimInstance Win32_Process
            $owned=[Collections.Generic.HashSet[int]]::new()
            [void]$owned.Add($process.Id)
            do{$changed=$false; foreach($p in $all){if($owned.Contains([int]$p.ParentProcessId) -and $owned.Add([int]$p.ProcessId)){$changed=$true}}}while($changed)
            $process.Kill($true)
            if(-not $process.WaitForExit(10000)){throw 'Owned root did not stop'}
            if(Get-Process -Id @($owned) -ErrorAction SilentlyContinue){throw 'Owned timed-out tree remains; do not retry'}
            throw "$Name timed out; owned-tree absence verified"
        }
        $stdout=$outTask.GetAwaiter().GetResult()
        $stderr=$errTask.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $proof "$Name.stdout.log"),$stdout)
        [IO.File]::WriteAllText((Join-Path $proof "$Name.stderr.log"),$stderr)
        $record=@{name=$Name;exe=$Exe;arguments=$Arguments;cwd=$Directory;pid=$process.Id;exit=$process.ExitCode;seconds=$watch.Elapsed.TotalSeconds;environment=$Extra}
        $commands.Add($record)
        $commands | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $proof 'commands.json')
        if($process.ExitCode -ne $Expected){throw "$Name exit $($process.ExitCode), expected $Expected. Retained logs: $proof"}
        return $stdout.Trim()
    }finally{$process.Dispose()}
}

$driver=Join-Path $proof 'driver'
New-Item -ItemType Directory -Path $driver | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CSharpCliProof.cs.txt') -Destination (Join-Path $driver 'Program.cs')
$dlls=@(Get-ChildItem (Join-Path $repo 'tests/Morphir.Tooling.Tests/bin/Release/net10.0/*.dll'); Get-ChildItem (Join-Path $repo 'tests/Morphir.CodeGeneration.CSharp.Tests/bin/Release/net10.0/*.dll')) | Group-Object Name | ForEach-Object {$_.Group[0]}
$references=$dlls | ForEach-Object {'<Reference Include="'+$_.BaseName+'"><HintPath>'+[Security.SecurityElement]::Escape($_.FullName)+'</HintPath></Reference>'}
('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><UseAppHost>false</UseAppHost></PropertyGroup><ItemGroup>'+($references -join '')+'</ItemGroup></Project>') | Set-Content (Join-Path $driver 'Proof.csproj')
'{"sdk":{"version":"10.0.403","rollForward":"disable"}}' | Set-Content (Join-Path $driver 'global.json')
'<configuration><packageSources><clear/></packageSources></configuration>' | Set-Content (Join-Path $driver 'NuGet.Config')
[void](Run 'driver-build' $StableDotnet $driver (@('build','Proof.csproj','-c','Release','-warnaserror')+$bounds))
$dll=Join-Path $driver 'bin/Release/net10.0/Proof.dll'
$feed=[IO.Path]::GetFullPath($RuntimeFeed)
$observations=[Collections.Generic.List[object]]::new()
if(-not $CancellationOnly){
foreach($profile in @(@{name='stable';version='10.0.403';language=14;exe=$StableDotnet},@{name='rc2';version='11.0.100-rc.2.26504.105';language=15;exe=$rc})){
  $root=Split-Path $profile.exe
  $environment=@{DOTNET_ROOT=$root;DOTNET_ROOT_ARM64=$root;DOTNET_CLI_HOME=(Join-Path $proof "$($profile.name)-home")}
  foreach($family in 'classic','v4','classic-car','v4-car'){
    $name="$($profile.name)-$family"
    $directory=Join-Path $proof $name
    New-Item -ItemType Directory -Path $directory | Out-Null
    [void](Run "$name-provider-export" $StableDotnet $driver @('exec',$dll,'export',$directory,$family,[string]$profile.language))
    @{sdk=@{version=$profile.version;rollForward='disable';allowPrerelease=($profile.name -eq 'rc2')}} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $directory 'global.json')
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><UseAppHost>false</UseAppHost><RuntimeFrameworkVersion>10.0.14</RuntimeFrameworkVersion><LangVersion>'+ $profile.language +'</LangVersion></PropertyGroup><ItemGroup><PackageReference Include="Morphir.SDK.CSharp" Version="0.0.0-local"/></ItemGroup></Project>' | Set-Content (Join-Path $directory 'Consumer.csproj')
    '<configuration><packageSources><clear/><add key="runtime" value="'+$feed+'"/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders></configuration>' | Set-Content (Join-Path $directory 'NuGet.Config')
    $expected=if($family.EndsWith('-car')){'15000'}else{'62'}
    ('if(Observation.Run()!='+$expected+')throw new System.Exception("Independent type, alias, generic, constructor and equality observation"); System.Console.WriteLine('+$expected+');') | Set-Content (Join-Path $directory 'Program.cs')
    $sdk=Run "$name-sdk" $profile.exe $directory @('--version') $environment
    if($sdk -ne $profile.version){throw 'SDK mismatch'}
    $compiler=Run "$name-compiler" $profile.exe $directory @('exec',(Join-Path $root "sdk/$sdk/Roslyn/bincore/csc.dll"),'-version') $environment
    [void](Run "$name-restore" $profile.exe $directory (@('restore','--packages',(Join-Path $directory 'packages'),'--configfile','NuGet.Config')+$bounds) $environment)
    [void](Run "$name-build" $profile.exe $directory (@('build','-c','Release','--no-restore','-warnaserror')+$bounds) $environment)
    $observed=Run "$name-execute" $StableDotnet $directory @('exec','bin/Release/net10.0/Consumer.dll')
    if($observed -ne $expected){throw 'Business observation failed'}
    $observations.Add(@{profile=$profile.name;family=$family;language=$profile.language;sdk=$sdk;compiler=$compiler;observed=$observed;parity=$true})
    $observations | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $proof 'consumers.json')
  }
}
}
$cli=Join-Path $repo 'src/Morphir/bin/Release/net10.0/morphir.exe'
$input=Join-Path $repo 'tests/Fixtures/MorphirIR/daily-rate-v4.json'
$output=Join-Path $proof 'cancel-output'
[void](Run 'cancel-prior-generation' $cli $repo @('generate','--input',$input,'--target','csharp','--output',$output,'--json'))
[IO.File]::WriteAllText((Join-Path $output 'User.cs'),'// user-owned')
$before=@(Get-ChildItem $output -Recurse -File | ForEach-Object { @{path=$_.FullName;hash=(Get-FileHash $_.FullName).Hash;time=$_.LastWriteTimeUtc.ToString('o')} }) | ConvertTo-Json -Depth 4
$text=Run 'cancel-real-cli' $StableDotnet $driver @('exec',$dll,'cancel','generate','--input',$input,'--target','csharp','--output',$output,'--option','namespace=Changed.Namespace','--json') @{} 130
$report=$text | ConvertFrom-Json
if($report.success -or $report.exitCode -ne 130 -or $report.failureKind -ne 'cancelled' -or $report.backend -ne 'morphir.csharp' -or $report.artifacts.Count -or $report.diagnostics[0].code -ne 'cancelled'){throw 'Cancellation report mismatch'}
$after=@(Get-ChildItem $output -Recurse -File | ForEach-Object { @{path=$_.FullName;hash=(Get-FileHash $_.FullName).Hash;time=$_.LastWriteTimeUtc.ToString('o')} }) | ConvertTo-Json -Depth 4
if($before -cne $after){throw 'Cancelled CLI changed prior output'}
@{cancellation=$true;compiledConsumers=$observations.Count} | ConvertTo-Json | Set-Content (Join-Path $proof 'scope.json')
"Real CLI cancellation proof passed; compiled consumers in this invocation: $($observations.Count)."
