param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$ReportPath,
    [string]$BuildAssembly
)
$ErrorActionPreference = 'Stop'
$errors = [Collections.Generic.List[string]]::new()
$packages = [Collections.Generic.List[object]]::new()
if ($BuildAssembly) {
    $assembly = [Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($BuildAssembly))
    $lookup = $assembly.GetType('Morphir.Build.Helpers.PathHelper', $true).GetMethod('GetLibraryPackagePath')
}
foreach ($id in @('Morphir.Models', 'Morphir.CodeGeneration.CSharp')) {
    $expected = Join-Path $PackageDirectory "$id.$Version.nupkg"
    $path = if ($lookup) { $lookup.Invoke($null, [object[]]@($id, $Version, [IO.Path]::GetFullPath($PackageDirectory))) } else { $expected }
    if ([IO.Path]::GetFullPath($path) -ne [IO.Path]::GetFullPath($expected)) { $errors.Add("$id compiled lookup differs from exact package path") }
    if (-not (Test-Path -LiteralPath $path)) { $errors.Add("$id package absent: $path"); continue }
    $archive = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        $entry = $archive.Entries | Where-Object FullName -like '*.nuspec' | Select-Object -First 1
        $reader = [IO.StreamReader]::new($entry.Open())
        try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $metadata = $nuspec.package.metadata
        if ($metadata.id -ne $id -or $metadata.version -ne $Version) { $errors.Add("$id identity/version mismatch") }
        if (-not $metadata.releaseNotes -or $metadata.releaseNotes -notmatch '###') { $errors.Add("$id requires meaningful Markdown release notes with a ### section") }
        if ($metadata.license.InnerText -ne 'Apache-2.0') { $errors.Add("$id license mismatch") }
        if ($metadata.readme -ne 'README.md') { $errors.Add("$id README metadata missing") }
        if (-not ($archive.Entries.FullName -contains "lib/net10.0/$id.dll")) { $errors.Add("$id net10.0 assembly absent") }
        $dependencies = @($metadata.dependencies.group.dependency | ForEach-Object { @{id=$_.id;version=$_.version} })
        if ($dependencies.id -match 'Tooling|Ion|CodeAnalysis|IR.Pipeline') { $errors.Add("$id has forbidden dependency") }
        if ($id -eq 'Morphir.CodeGeneration.CSharp' -and $dependencies.id -notcontains 'Morphir.Models') { $errors.Add('Generator does not declare its real Models dependency') }
        $packages.Add(@{id=$id;path=[IO.Path]::GetFullPath($path);sha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant();dependencies=$dependencies;releaseNotes=$metadata.releaseNotes;compiledLookup=[bool]$lookup})
    } finally { $archive.Dispose() }
}
@{packages=$packages;errors=$errors;passed=($errors.Count -eq 0)} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath
if ($errors.Count) { throw ($errors -join "`n") }
Write-Output "Two packages verified: $ReportPath"
