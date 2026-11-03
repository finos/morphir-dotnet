param(
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][string]$RcDotnet,
    [string]$StableDotnet='C:/Users/damre/AppData/Local/dotnetup/dotnet/dotnet.exe',
    [string]$ConsumerProofDirectory
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$proof=[IO.Path]::GetFullPath($EvidenceDirectory)
if(-not $proof.StartsWith('C:\Temp\morphir-csharp-cli-', [StringComparison]::OrdinalIgnoreCase)){throw 'Use an owned C# CLI proof directory'}
if(Test-Path -LiteralPath $proof){throw 'Preserve existing proof and choose a fresh directory'}
New-Item -ItemType Directory -Path $proof | Out-Null
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
function Report([string]$Name,[string]$Cli,[string]$InputPath,[string]$Output,[int]$Language,[string]$Namespace,[hashtable]$Extra=@{}){
    $text=Run $Name $Cli $repo @('generate','--input',$InputPath,'--target','csharp','--output',$Output,'--option',"language-version=$Language",'--option',"namespace=$Namespace",'--json') $Extra
    $report=$text | ConvertFrom-Json
    if(-not $report.success -or $report.exitCode -ne 0 -or $report.target -ne 'csharp' -or $report.backend -ne 'morphir.csharp' -or -not $report.artifacts.Count){throw 'Expected a nonempty registered-provider report'}
    foreach($path in $report.artifacts){if(-not (Test-Path -LiteralPath (Join-Path $Output $path))){throw 'Reported source is missing'}}
    return $report
}
$feed=Join-Path $proof 'feed'
New-Item -ItemType Directory -Path $feed | Out-Null
if($ConsumerProofDirectory){
    $consumerProof=[IO.Path]::GetFullPath($ConsumerProofDirectory)
    $prior=Get-Content -LiteralPath (Join-Path $consumerProof 'consumers.json') -Raw | ConvertFrom-Json
    if(@($prior).Count -ne 16){throw 'Expected all 16 retained compiler observations'}
    Copy-Item -LiteralPath (Join-Path $consumerProof 'feed/Morphir.SDK.CSharp.0.0.0-local.nupkg') -Destination $feed
}else{
    $consumerProof=$proof
    [void](Run 'pack-runtime' $StableDotnet $repo (@('pack','src/Morphir.SDK.CSharp/Morphir.SDK.CSharp.csproj','-c','Release','--no-restore','-o',$feed,'-p:Version=0.0.0-local')+$bounds))
}
$cli=Join-Path $repo 'src/Morphir/bin/Release/net10.0/morphir.exe'
$results=[Collections.Generic.List[object]]::new()
if(-not $ConsumerProofDirectory){
foreach($profile in @(@{name='stable';language=14;sdk='10.0.403';exe=$StableDotnet},@{name='rc2';language=15;sdk='11.0.100-rc.2.26504.105';exe=$RcDotnet})){
    $root=Split-Path $profile.exe
    $environment=@{DOTNET_ROOT=$root;DOTNET_ROOT_ARM64=$root;DOTNET_CLI_HOME=(Join-Path $proof "$($profile.name)-home")}
    foreach($case in @('daily-v3','daily-v4','car-v3','escaped','unicode','combining','formatting','contextual')){
        $name="$($profile.name)-$case"
        $directory=Join-Path $proof $name
        New-Item -ItemType Directory -Path $directory | Out-Null
        @{sdk=@{version=$profile.sdk;rollForward='disable';allowPrerelease=($profile.name -eq 'rc2')}} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $directory 'global.json')
        $namespace=switch($case){'escaped'{'@class.@namespace'} 'unicode'{'Δοκιμή.模型'} 'combining'{"A$([char]0x20d0).B"} 'formatting'{"A$([char]0x200c).B"} 'contextual'{'global.record'} default{'Pricing.Generated'}}
        $inputRelative=switch($case){'car-v3'{'examples/car-rental/models/car-rental-v3.json'} 'daily-v4'{'tests/Fixtures/MorphirIR/daily-rate-v4.json'} default{'tests/Fixtures/MorphirIR/daily-rate-v3.json'}}
        $report=Report "$name-generate" $cli (Join-Path $repo $inputRelative) (Join-Path $directory 'Generated') $profile.language $namespace
        if($case -eq 'car-v3'){
            $program=@"
using $namespace;
using Morphir.SDK.CSharp;
var initial = InventoryModule.Car();
var billed = WorkflowModule.Rent(3).Match(e => throw new System.Exception("Lifecycle failure"), x => x);
var invoice = BillingModule.Invoice(billed).Match(x => x, () => throw new System.Exception("Missing invoice"));
if (invoice.TotalCents != 15000 || InventoryModule.Availability(initial) is not DomainModule.Availability.Available) throw new System.Exception("Independent business observation");
if (!Basics.Equal(billed,WorkflowModule.Rent(3).Match(e => throw new System.Exception(),x => x))) throw new System.Exception("SDK structural protocol");
System.Console.WriteLine("15000");
"@
            $expected='15000'
        }else{
            $program=@"
using $namespace;
var cents = InventoryModule.DailyRate();
if(cents != 5000) throw new System.Exception("Independent daily rate");
System.Console.WriteLine(cents);
"@
            $expected='5000'
        }
        [IO.File]::WriteAllText((Join-Path $directory 'Program.cs'),$program,[Text.UTF8Encoding]::new($false))
        "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><LangVersion>$($profile.language)</LangVersion><UseAppHost>false</UseAppHost><RuntimeFrameworkVersion>10.0.14</RuntimeFrameworkVersion></PropertyGroup><ItemGroup><PackageReference Include=`"Morphir.SDK.CSharp`" Version=`"0.0.0-local`"/></ItemGroup></Project>" | Set-Content (Join-Path $directory 'Consumer.csproj')
        "<configuration><packageSources><clear/><add key=`"runtime`" value=`"$feed`"/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders></configuration>" | Set-Content (Join-Path $directory 'NuGet.Config')
        $sdk=Run "$name-sdk" $profile.exe $directory @('--version') $environment
        if($sdk -ne $profile.sdk){throw "Unexpected SDK $sdk"}
        $compiler=Run "$name-compiler" $profile.exe $directory @('exec',(Join-Path $root "sdk/$sdk/Roslyn/bincore/csc.dll"),'-version') $environment
        [void](Run "$name-restore" $profile.exe $directory (@('restore','Consumer.csproj','--configfile','NuGet.Config','--packages',(Join-Path $directory 'packages'))+$bounds) $environment)
        [void](Run "$name-build" $profile.exe $directory (@('build','Consumer.csproj','-c','Release','--no-restore','-warnaserror')+$bounds) $environment)
        $observed=Run "$name-execute" $StableDotnet $directory @('exec','bin/Release/net10.0/Consumer.dll')
        if($observed -ne $expected){throw 'Independent consumer expectation failed'}
        $results.Add(@{profile=$profile.name;case=$case;language=$profile.language;sdk=$sdk;compiler=$compiler;namespace=$namespace;observed=$observed;artifacts=$report.artifacts})
        $results | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $proof 'consumers.json')
    }
}
}
$empty=Join-Path $proof 'no-compiler'
New-Item -ItemType Directory -Path $empty | Out-Null
foreach($kind in @('trimmed','native')){
    $published=Join-Path $proof "published-$kind"
    $properties=@('-p:PublishTrimmed=true','-p:TrimMode=link','-p:TreatWarningsAsErrors=false','-p:TrimmerSingleWarn=false')
    if($kind -eq 'native'){$properties+= '-p:PublishAot=true'}
    [void](Run "$kind-publish" $StableDotnet $repo (@('publish','src/Morphir/Morphir.csproj','-c','Release','-r','win-arm64','--self-contained','true','-o',$published)+$properties+$bounds))
    $exe=Join-Path $published 'morphir.exe'
    if(-not (Test-Path -LiteralPath $exe)){throw 'Published executable missing'}
    if($kind -eq 'native'){
        $bytes=[IO.File]::ReadAllBytes($exe)
        $pe=[BitConverter]::ToInt32($bytes,0x3c)
        $machine=[BitConverter]::ToUInt16($bytes,$pe+4)
        $magic=[BitConverter]::ToUInt16($bytes,$pe+24)
        $clr=[BitConverter]::ToUInt32($bytes,$pe+24+112+14*8)
        if($machine -ne 0xaa64 -or $magic -ne 0x20b -or $clr -ne 0){throw 'Expected a native ARM64 PE without a CLR header'}
        @{machine=$machine;optionalHeaderMagic=$magic;clrHeaderRva=$clr} | ConvertTo-Json | Set-Content (Join-Path $proof 'native-pe.json')
    }
    $output=Join-Path $proof "$kind-consumer/Generated"
    $report=Report "$kind-no-compiler-generate" $exe (Join-Path $repo 'examples/car-rental/models/car-rental-v3.json') $output 14 'Pricing.Generated' @{PATH=$empty;DOTNET_ROOT=$empty;DOTNET_ROOT_ARM64=$empty;DOTNET_CLI_HOME=(Join-Path $proof "$kind-home")}
    foreach($artifact in $report.artifacts){
        $old=Join-Path $consumerProof "stable-car-v3/Generated/$artifact"
        $new=Join-Path $output $artifact
        if((Get-FileHash -LiteralPath $old -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $new -Algorithm SHA256).Hash){throw 'Core diagnostic changes altered emitted source bytes'}
    }
    if($kind -eq 'native'){
        $source=[IO.File]::ReadAllText((Join-Path $repo 'tests/Fixtures/MorphirIR/daily-rate-v4.json'))
        foreach($rule in @('document-literal','type-mismatch')){
            $inputPath=Join-Path $proof "native-$rule.json"
            $payload=if($rule -eq 'document-literal'){'"DocumentLiteral": {"unsupported":true}'}else{'"BoolLiteral": true'}
            [IO.File]::WriteAllText($inputPath,$source.Replace('"WholeNumberLiteral": 5000',$payload))
            $refusedOutput=Join-Path $proof "native-refused-$rule"
            $json=Run "native-$rule-refusal" $exe $repo @('generate','--input',$inputPath,'--target','csharp','--output',$refusedOutput,'--json') @{PATH=$empty;DOTNET_ROOT=$empty;DOTNET_ROOT_ARM64=$empty} 1
            $refusal=$json | ConvertFrom-Json
            if($refusal.success -or $refusal.failureKind -ne 'semantic' -or $refusal.diagnostics[0].code -ne $rule -or -not $refusal.diagnostics[0].location -or $refusal.artifacts.Count -or (Test-Path -LiteralPath $refusedOutput)){throw 'Native semantic refusal contract failed'}
        }
    }
    $directory=Split-Path $output
    Copy-Item -LiteralPath (Join-Path $consumerProof 'stable-car-v3/Program.cs') -Destination $directory
    Copy-Item -LiteralPath (Join-Path $consumerProof 'stable-car-v3/Consumer.csproj') -Destination $directory
    Copy-Item -LiteralPath (Join-Path $consumerProof 'stable-car-v3/NuGet.Config') -Destination $directory
    Copy-Item -LiteralPath (Join-Path $consumerProof 'stable-car-v3/global.json') -Destination $directory
    [void](Run "$kind-consumer-restore" $StableDotnet $directory (@('restore','Consumer.csproj','--configfile','NuGet.Config','--packages',(Join-Path $directory 'packages'))+$bounds))
    [void](Run "$kind-consumer-build" $StableDotnet $directory (@('build','Consumer.csproj','-c','Release','--no-restore','-warnaserror')+$bounds))
    $observed=Run "$kind-consumer-execute" $StableDotnet $directory @('exec','bin/Release/net10.0/Consumer.dll')
    if($observed -ne '15000'){throw 'Published CLI consumer business observation failed'}
    $results.Add(@{publication=$kind;executable=$exe;sha256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash;bytes=(Get-Item -LiteralPath $exe).Length;noCompilerPath=$empty;observed=$observed;artifacts=$report.artifacts})
    $results | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $proof 'consumers.json')
}
Write-Output "C# CLI proof passed: $proof/consumers.json"
