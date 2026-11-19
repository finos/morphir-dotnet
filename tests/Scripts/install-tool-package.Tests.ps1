# Copyright 2026 FINOS
# SPDX-License-Identifier: Apache-2.0
#
# Pester tests of .github/scripts/install-tool-package.ps1. cli-offering.yml, and the parent's release qualification,
# start the dotnet-tool offering from the package that was built. The script installs that package, globally into a
# tool path or locally into a tool manifest, so that a package with the same id and version elsewhere (nuget.org,
# another configured feed, a machine cache) cannot be chosen, and checks that what the SDK installed is the built bytes.
# These tests use two packages with the same id and version and different content: A, the built one, and B, a conflict
# that is on every other path NuGet reads. The entry point of both is the stub morphir-mck (tests/Morphir.Mck.Stub),
# which reports the folder it runs from.
#
# Run them with the TestScripts target of the build (./build.ps1 --target TestScripts), which gives -StubFolder.

param(
    # The build output of tests/Morphir.Mck.Stub: morphir-mck.dll and morphir-mck.runtimeconfig.json.
    [Parameter(Mandatory)] [string] $StubFolder
)

BeforeAll {
    $Script = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../.github/scripts/install-tool-package.ps1'))
    $PackageId = 'Probe.Tool'
    $PackageVersion = '1.2.3'
    $ToolCommand = 'dotnet-probe'
    $Pwsh = (Get-Process -Id $PID).Path
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

    # The functions of the script, for the checks; dot-sourcing does not run the install.
    . $Script

    foreach ($file in 'morphir-mck.dll', 'morphir-mck.runtimeconfig.json') {
        if (-not (Test-Path -LiteralPath (Join-Path $StubFolder $file))) { throw "The stub $file is not in $StubFolder. Build tests/Morphir.Mck.Stub first." }
    }

    # A folder of its own for each test, below the temporary directory that the build gives (a physical path on macOS).
    function New-Sandbox {
        $root = Join-Path ([IO.Path]::GetTempPath()) ('tool-install-' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
        $sandbox = [pscustomobject]@{
            Root          = $root
            Home          = Join-Path $root 'home'
            AppData       = Join-Path $root 'appdata'
            LocalAppData  = Join-Path $root 'localappdata'
            ProgramData   = Join-Path $root 'programdata'
            XdgConfig     = Join-Path $root 'xdg-config'
            XdgCache      = Join-Path $root 'xdg-cache'
            DotnetCliHome = Join-Path $root 'dotnet-cli-home'
            MorphirHome   = Join-Path $root 'morphir-home'
            Work          = Join-Path $root 'work'
        }
        foreach ($folder in $sandbox.PSObject.Properties.Value) { New-Item -ItemType Directory -Force -Path $folder | Out-Null }
        $sandbox
    }

    function Join-Sandbox($Sandbox, [string[]] $Segments) { [IO.Path]::Combine([string[]] (@($Sandbox.Root) + $Segments)) }

    # Each child gets this long, then its process tree is killed: a hung dotnet tool install must not hold the job.
    $Timeout = [TimeSpan]::FromMinutes(3)

    # Runs $File with $Arguments, from $WorkingDirectory (the work folder of the sandbox by default), with the variables
    # that TestSandbox gives a child process: no MORPHIR_* of the caller, MORPHIR_ISOLATED, and the home, application
    # data and CLI folders of the sandbox; then $Environment, where a null value removes a variable. The variables of
    # this process do not change. Returns the exit code, all the output for a message, and the last line of stdout.
    function Invoke-Contained {
        param($Sandbox, [string] $File, [string[]] $Arguments, [string] $WorkingDirectory, [System.Collections.IDictionary] $Environment = @{})
        $start = [Diagnostics.ProcessStartInfo]::new($File)
        foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
        $start.WorkingDirectory = $WorkingDirectory ? $WorkingDirectory : $Sandbox.Work
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardInput = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        foreach ($name in @($start.Environment.Keys | Where-Object { $_ -like 'MORPHIR_*' })) { [void] $start.Environment.Remove($name) }
        $variables = [ordered]@{
            MORPHIR_HOME                      = $Sandbox.MorphirHome
            MORPHIR_DISCOVERY_BOUNDARY        = $Sandbox.Root
            MORPHIR_ISOLATED                  = '1'
            HOME                              = $Sandbox.Home
            USERPROFILE                       = $Sandbox.Home
            APPDATA                           = $Sandbox.AppData
            LOCALAPPDATA                      = $Sandbox.LocalAppData
            PROGRAMDATA                       = $Sandbox.ProgramData
            XDG_CONFIG_HOME                   = $Sandbox.XdgConfig
            XDG_CACHE_HOME                    = $Sandbox.XdgCache
            DOTNET_CLI_HOME                   = $Sandbox.DotnetCliHome
            DOTNET_NOLOGO                     = '1'
            DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
            DOTNET_CLI_TELEMETRY_OPTOUT       = '1'
            MSBUILDDISABLENODEREUSE           = '1'
        }
        foreach ($name in $Environment.Keys) { $variables[$name] = $Environment[$name] }
        foreach ($name in $variables.Keys) {
            if ($null -eq $variables[$name]) { [void] $start.Environment.Remove($name) } else { $start.Environment[$name] = $variables[$name] }
        }

        $command = "$File $($Arguments -join ' ')"
        $process = [Diagnostics.Process]::Start($start)
        try {
            $process.StandardInput.Close()
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            $exited = $process.WaitForExit([int] $Timeout.TotalMilliseconds)
            if (-not $exited) { $process.Kill($true) }
            # A descendant that kept the pipes would block the read: it gets a bounded wait too.
            $closed = [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]] @($stdout, $stderr), 30000)
            $output = $stdout.IsCompletedSuccessfully ? $stdout.Result : ''
            $errors = $stderr.IsCompletedSuccessfully ? $stderr.Result : ''
            if (-not $exited) { throw "'$command' did not finish within $($Timeout.TotalMinutes) minutes, and its process tree was killed.`nstdout:`n$output`nstderr:`n$errors" }
            if (-not $closed) { throw "'$command' exited, but a process it started still holds its output.`nstdout:`n$output`nstderr:`n$errors" }
            $process.WaitForExit()
            $lines = @($output -split "`r?`n" | Where-Object { $_.Trim() })
            [pscustomobject]@{
                ExitCode = $process.ExitCode
                Output   = "$output`n$errors".Trim()
                LastLine = $lines.Count -gt 0 ? $lines[-1].Trim() : $null
            }
        } finally { $process.Dispose() }
    }

    # The variables of a contained run: the conflicting cache is the global and the fallback packages folder.
    function Get-ConflictEnvironment($Conflict) { [ordered]@{ NUGET_PACKAGES = $Conflict.Cache; NUGET_FALLBACK_PACKAGES = $Conflict.Cache } }

    # Runs the script as cli-offering.yml does, with pwsh -File.
    function Invoke-InstallScript {
        param($Conflict, [string[]] $Arguments, [string] $WorkingDirectory)
        Invoke-Contained $Conflict.Sandbox $Pwsh (@('-NoProfile', '-NonInteractive', '-File', $Script) + $Arguments) $WorkingDirectory (Get-ConflictEnvironment $Conflict)
    }

    function Invoke-Native {
        param($Conflict, [string] $File, [string[]] $Arguments, [string] $WorkingDirectory, [System.Collections.IDictionary] $Environment = (Get-ConflictEnvironment $Conflict))
        Invoke-Contained $Conflict.Sandbox $File $Arguments $WorkingDirectory $Environment
    }

    # Exact (ordinal) checks, as the TUnit tests had: Pester's -Be, -Contain and -BeLike ignore case.
    function Assert-StartsWith([string] $Text, [string] $Prefix, [string] $Because) {
        $Text.StartsWith($Prefix, [StringComparison]::Ordinal) | Should -BeTrue -Because "$Text starts with $Prefix$($Because ? ": $Because" : '')"
    }

    function Assert-JsonKinds([string] $Text, [string] $Because) {
        $json = [Text.Json.JsonDocument]::Parse($Text)
        try {
            $invocation = $json.RootElement.GetProperty('invocation')
            $invocation.GetProperty('arguments').ValueKind | Should -BeExactly 'Array' -Because $Because
            $invocation.GetProperty('environment').ValueKind | Should -BeExactly 'Object' -Because $Because
            $invocation.GetProperty('arguments').GetArrayLength()
        } finally { $json.Dispose() }
    }

    function Get-Sha256([string] $Path) { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($Path))).ToLowerInvariant() }

    function Get-Folder([string[]] $Parts) { [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath([IO.Path]::Combine($Parts))) }

    # The folder from which the stub ran: the last line of its output is a JSON report.
    function Get-StubBase([string] $LastLine) { Get-Folder @((ConvertFrom-Json $LastLine).base) }

    # The marker text of the one package extracted under $Root.
    function Get-Marker([string] $Root) {
        $markers = @(Get-ChildItem -LiteralPath $Root -Recurse -File -Filter 'marker.txt')
        $markers.Count | Should -Be 1 -Because "one package is extracted under $Root"
        (Get-Content -LiteralPath $markers[0].FullName -Raw).Trim()
    }

    function Assert-StoredAreBuilt($Result, [string] $BuiltSha256, [string] $Under) {
        $Result.package.sha256 | Should -BeExactly $BuiltSha256
        $stored = @($Result.stored)
        $stored.Count | Should -BeGreaterThan 0
        foreach ($archive in $stored) {
            $archive.sha256 | Should -BeExactly $BuiltSha256
            Get-Sha256 $archive.path | Should -BeExactly $BuiltSha256 -Because $archive.path
            Assert-StartsWith ([IO.Path]::GetFullPath($archive.path)) ([IO.Path]::GetFullPath($Under))
        }
    }

    function Add-Text([IO.Compression.ZipArchive] $Zip, [string] $Name, [string] $Text) {
        $writer = New-Object IO.StreamWriter($Zip.CreateEntry($Name).Open())
        try { $writer.Write($Text) } finally { $writer.Dispose() }
    }

    # A dotnet tool package whose entry point is the stub morphir-mck, with a marker file that tells A from B.
    function New-ToolPackage([string] $Path, [string] $Marker) {
        New-Item -ItemType Directory -Force -Path (Split-Path $Path) | Out-Null
        $zip = [IO.Compression.ZipFile]::Open($Path, [IO.Compression.ZipArchiveMode]::Create)
        try {
            Add-Text $zip "$PackageId.nuspec" @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>$PackageId</id>
    <version>$PackageVersion</version>
    <authors>morphir-dotnet tests</authors>
    <description>A tool package for the install tests.</description>
    <packageTypes><packageType name="DotnetTool" /></packageTypes>
  </metadata>
</package>
"@
            Add-Text $zip 'tools/net11.0/any/DotnetToolSettings.xml' @"
<?xml version="1.0" encoding="utf-8"?>
<DotNetCliTool Version="1"><Commands><Command Name="$ToolCommand" EntryPoint="morphir-mck.dll" Runner="dotnet" /></Commands></DotNetCliTool>
"@
            foreach ($file in 'morphir-mck.dll', 'morphir-mck.runtimeconfig.json') {
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $StubFolder $file), "tools/net11.0/any/$file") | Out-Null
            }
            Add-Text $zip 'tools/net11.0/any/marker.txt' $Marker
        } finally { $zip.Dispose() }
        $Path
    }

    # The built package A in its own folder, and B on every other path NuGet reads: a user-level NuGet.Config and a
    # NuGet.config in the current directory that list a feed with B, and a global packages folder and a fallback folder
    # that hold B in the layout NuGet writes.
    function New-Conflict {
        $sandbox = New-Sandbox
        $built = New-ToolPackage (Join-Sandbox $sandbox 'built', "$PackageId.$PackageVersion.nupkg") 'A-built'
        $feed = Split-Path (New-ToolPackage (Join-Sandbox $sandbox 'conflict feed', "$PackageId.$PackageVersion.nupkg") 'B-conflict')
        $config = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="conflict" value="$feed" />
  </packageSources>
</configuration>
"@
        foreach ($folder in (Join-Path $sandbox.AppData 'NuGet'), (Join-Path $sandbox.Home '.nuget' 'NuGet'), $sandbox.Work) {
            New-Item -ItemType Directory -Force -Path $folder | Out-Null
            Set-Content -LiteralPath (Join-Path $folder ($folder -eq $sandbox.Work ? 'NuGet.config' : 'NuGet.Config')) -Value $config -Encoding utf8NoBOM
        }

        # NuGet itself installs B once, and its store folder becomes the conflicting cache.
        $conflict = [pscustomobject]@{
            Sandbox     = $sandbox
            Built       = $built
            BuiltSha256 = Get-Sha256 $built
            SeededTools = Join-Sandbox $sandbox 'seeded-tools'
            Cache       = Join-Sandbox $sandbox 'conflict cache'
        }
        $seed = Invoke-Native $conflict dotnet @('tool', 'install', $PackageId, '--version', $PackageVersion, '--tool-path', $conflict.SeededTools) `
            -Environment @{ NUGET_PACKAGES = (Join-Sandbox $sandbox 'seed packages'); NUGET_FALLBACK_PACKAGES = $null }
        if ($seed.ExitCode -ne 0) { throw "Seeding B failed: $($seed.Output)" }
        $lowerId = $PackageId.ToLowerInvariant()
        Copy-Item -LiteralPath ([IO.Path]::Combine($conflict.SeededTools, '.store', $lowerId, $PackageVersion, $lowerId)) -Destination (Join-Path $conflict.Cache $lowerId) -Recurse
        $conflict
    }
}

Describe 'install-tool-package.ps1' {
    BeforeEach {
        $conflict = New-Conflict
        $sandbox = $conflict.Sandbox
    }

    AfterEach {
        if ($sandbox) { Remove-Item -LiteralPath $sandbox.Root -Recurse -Force -ErrorAction SilentlyContinue }
    }

    It 'a tool-path install takes only the built package and runs it' {
        $tools = Join-Sandbox $sandbox 'tools'
        $resultFile = Join-Sandbox $sandbox 'result.json'

        $result = Invoke-InstallScript $conflict @('-Package', $conflict.Built, '-ToolPath', $tools, '-WorkDir', (Join-Sandbox $sandbox 'nuget work'), '-ResultFile', $resultFile)

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $command = $result.LastLine
        $command | Should -BeExactly (Join-Path $tools ($IsWindows ? "$ToolCommand.exe" : $ToolCommand)) -Because 'stdout is the command path'
        Get-Marker (Join-Path $tools '.store') | Should -BeExactly 'A-built'

        $text = Get-Content -LiteralPath $resultFile -Raw
        $evidence = ConvertFrom-Json $text
        $evidence.mode | Should -BeExactly 'tool-path'
        Assert-StoredAreBuilt $evidence $conflict.BuiltSha256 -Under (Join-Path $tools '.store')
        $evidence.install.arguments -ccontains '--configfile' | Should -BeTrue
        $evidence.install.arguments -ccontains '--tool-path' | Should -BeTrue
        $evidence.install.arguments | Should -Not -Contain '--add-source'
        $evidence.invocation.file | Should -BeExactly $command
        Assert-JsonKinds $text 'callers pass it through, not a shell string' | Should -Be 0 -Because 'the command takes no arguments'

        # The command runs the built bytes: the stub reports the folder it ran from.
        $run = Invoke-Native $conflict $command @('a', 'b c')
        $run.ExitCode | Should -Be 0 -Because $run.Output
        Get-StubBase $run.LastLine | Should -BeExactly (Get-Folder @($evidence.packageFolder, 'tools', 'net11.0', 'any'))
    }

    It 'a local install takes only the built package and dotnet runs it' {
        $manifest = Join-Sandbox $sandbox 'manifest'
        $resultFile = Join-Sandbox $sandbox 'result.json'

        $result = Invoke-InstallScript $conflict @('-Package', $conflict.Built, '-ToolPath', $manifest, '-WorkDir', (Join-Sandbox $sandbox 'nuget work'), '-Local', '-ResultFile', $resultFile)

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $result.LastLine | Should -BeExactly $manifest -Because 'in local mode stdout is the manifest folder'
        $text = Get-Content -LiteralPath $resultFile -Raw
        $evidence = ConvertFrom-Json $text
        $evidence.mode | Should -BeExactly 'local'
        Assert-JsonKinds $text 'callers pass it through, not a shell string' | Should -Be 1 -Because 'the words of the command'
        $packages = $evidence.caches.packagesFolder
        Get-Marker $packages | Should -BeExactly 'A-built'
        Assert-StoredAreBuilt $evidence $conflict.BuiltSha256 -Under $packages
        $evidence.install.arguments -ccontains '--local' | Should -BeTrue
        $evidence.install.arguments -ccontains '--configfile' | Should -BeTrue
        $evidence.install.arguments | Should -Not -Contain '--add-source'

        # The normal invocation: dotnet <command without dotnet->, from the manifest folder, with the recorded environment.
        $invocation = $evidence.invocation
        $invocation.file | Should -BeExactly 'dotnet'
        $words = @($invocation.arguments)
        $words.Count | Should -Be 1
        $words[0] | Should -BeExactly 'probe'
        $invocation.workingDirectory | Should -BeExactly $manifest
        $invocation.entryAssembly.EndsWith('morphir-mck.dll', [StringComparison]::Ordinal) | Should -BeTrue -Because 'the entry assembly is recorded, not run as a command'
        $environment = Get-ConflictEnvironment $conflict
        foreach ($variable in $invocation.environment.PSObject.Properties) { $environment[$variable.Name] = $variable.Value }
        $run = Invoke-Native $conflict dotnet (@($words) + @('a', 'b c')) -WorkingDirectory $manifest -Environment $environment
        $run.ExitCode | Should -Be 0 -Because $run.Output
        Get-StubBase $run.LastLine | Should -BeExactly (Get-Folder @($evidence.packageFolder, 'tools', 'net11.0', 'any'))
    }

    It 'without isolation the same setup installs the conflict, and the check refuses it' {
        # The control: the conflicts are real, so the results above come from the isolation. And the check refuses what
        # a plain install produced.
        Get-Marker (Join-Path $conflict.SeededTools '.store') | Should -BeExactly 'B-conflict' -Because 'a plain --tool-path install took B'
        { Assert-InstalledToolPackage $conflict.Built $conflict.SeededTools } | Should -Throw '*SHA-256*' -Because 'the tool store holds B, and A was built'
    }

    It 'without isolation a local install takes the cached conflict, and the check refuses it' {
        $manifest = Join-Sandbox $sandbox 'plain manifest'
        New-Item -ItemType Directory -Path $manifest | Out-Null
        Set-Content -LiteralPath (Join-Path $manifest 'dotnet-tools.json') -Value '{ "version": 1, "isRoot": true, "tools": {} }' -Encoding utf8NoBOM

        $install = Invoke-Native $conflict dotnet @('tool', 'install', $PackageId, '--local', '--version', $PackageVersion) -WorkingDirectory $manifest

        $install.ExitCode | Should -Be 0 -Because $install.Output
        $resolver = Get-Content -LiteralPath ([IO.Path]::Combine($sandbox.DotnetCliHome, '.dotnet', 'toolResolverCache', '1', $PackageId.ToLowerInvariant())) -Raw | ConvertFrom-Json
        @($resolver).Count | Should -Be 1
        Assert-StartsWith ([IO.Path]::GetFullPath(@($resolver)[0].PathToExecutable)) ([IO.Path]::GetFullPath($conflict.Cache)) 'a plain --local install resolves the command into the cached B'
        Get-Marker $conflict.Cache | Should -BeExactly 'B-conflict'
        { Assert-LocalToolPackage $conflict.Built $manifest $conflict.Cache $sandbox.DotnetCliHome } | Should -Throw '*SHA-256*'
    }

    It 'an extracted file that the built package lacks fails the check' {
        $tools = Join-Sandbox $sandbox 'tools'
        $installed = Invoke-InstallScript $conflict @('-Package', $conflict.Built, '-ToolPath', $tools, '-WorkDir', (Join-Sandbox $sandbox 'nuget work'))
        $installed.ExitCode | Should -Be 0 -Because $installed.Output
        $any = @(Get-ChildItem -LiteralPath (Join-Path $tools '.store') -Recurse -Directory -Filter 'any')
        $any.Count | Should -Be 1
        Set-Content -LiteralPath (Join-Path $any[0].FullName 'planted.dll') -Value 'not from the built package'

        { Assert-InstalledToolPackage $conflict.Built $tools } | Should -Throw '*planted.dll*'
    }

    It 'every path in the result is absolute when the arguments are relative' {
        $result = Invoke-InstallScript $conflict @('-Package', (Join-Path 'built' "$PackageId.$PackageVersion.nupkg"), '-ToolPath', 'manifest', '-WorkDir', 'nuget work', '-Local', '-ResultFile', 'result.json') `
            -WorkingDirectory $sandbox.Root

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $text = Get-Content -LiteralPath (Join-Sandbox $sandbox 'result.json') -Raw
        $evidence = ConvertFrom-Json $text
        $invocation = $evidence.invocation
        Assert-JsonKinds $text 'callers apply them as they are' | Out-Null
        $paths = @(
            $evidence.package.path
            $evidence.toolPath
            $evidence.packageFolder
            $evidence.install.workingDirectory
            $evidence.install.configFile
            $evidence.install.feed
            $evidence.caches.PSObject.Properties.Value
            $evidence.stored.path
            $invocation.workingDirectory
            $invocation.manifest
            $invocation.resolverCache
            $invocation.entryAssembly
            $invocation.environment.PSObject.Properties.Value
        )
        $paths.Count | Should -BeGreaterThan 12
        foreach ($path in $paths) {
            [IO.Path]::IsPathFullyQualified($path) | Should -BeTrue -Because "$path must not depend on a later working directory"
        }
    }

    It 'a failed run leaves no result file' {
        $resultFile = Join-Sandbox $sandbox 'result.json'
        Set-Content -LiteralPath $resultFile -Value '{ "mode": "tool-path", "stale": true }'
        # An existing tool path makes the run fail.
        $tools = Join-Sandbox $sandbox 'tools'
        New-Item -ItemType Directory -Path $tools | Out-Null

        $result = Invoke-InstallScript $conflict @('-Package', $conflict.Built, '-ToolPath', $tools, '-WorkDir', (Join-Sandbox $sandbox 'nuget work'), '-ResultFile', $resultFile)

        $result.ExitCode | Should -Not -Be 0
        Test-Path -LiteralPath $resultFile | Should -BeFalse -Because 'a stale or partial result must not be read as success'
    }
}
