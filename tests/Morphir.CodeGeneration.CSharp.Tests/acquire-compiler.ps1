param([Parameter(Mandatory)][string]$ToolchainDirectory)
$ErrorActionPreference = 'Stop'
$owned = [IO.Path]::GetFullPath($ToolchainDirectory)
if (-not $owned.StartsWith('C:\Temp\morphir-csharp-generator-', [StringComparison]::OrdinalIgnoreCase)) { throw 'Use an owned generator directory.' }
if (Test-Path -LiteralPath $owned) { throw 'Preserve the previous toolchain and choose a fresh path.' }
New-Item -ItemType Directory -Path $owned | Out-Null
$version = '11.0.100-rc.2.26504.105'
$expected = 'a74c1ab374a5167ada496dfe0d22367e9b5f2d89eb6d232122beb0e71b30db4bf868e3a06aa5df5aeea8be9fc3194218d2134e1c9de067419e18eed3b34c6e33'
$metadata = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/11.0/releases.json' -TimeoutSec 60
$sdk = @($metadata.releases | ForEach-Object { $_.sdks } | Where-Object version -eq $version) | Select-Object -First 1
$file = $sdk.files | Where-Object { $_.rid -eq 'win-arm64' -and $_.name -like '*.zip' } | Select-Object -First 1
if ($file.hash -ne $expected) { throw 'Official metadata does not match the approved pin.' }
$file | ConvertTo-Json | Set-Content (Join-Path $owned 'official-download.json')
$zip = Join-Path $owned 'sdk.zip'
Invoke-WebRequest $file.url -OutFile $zip -TimeoutSec 1200
$actual = (Get-FileHash -Algorithm SHA512 -LiteralPath $zip).Hash.ToLowerInvariant()
if ($actual -ne $expected) { throw 'Downloaded compiler hash mismatch.' }
$destination = Join-Path $owned 'sdk'
Expand-Archive -LiteralPath $zip -DestinationPath $destination
$stable = Split-Path (Get-Command dotnet).Source
# Resolve a shim through the selected stable SDK's own installation inventory.
$stable = 'C:/Users/damre/AppData/Local/dotnetup/dotnet'
$copied = [Collections.Generic.List[object]]::new()
foreach ($pack in @('Microsoft.NETCore.App.Ref', 'Microsoft.AspNetCore.App.Ref', 'Microsoft.WindowsDesktop.App.Ref')) {
    $source = Join-Path $stable "packs/$pack/10.0.13"
    if (-not (Test-Path -LiteralPath $source)) { throw "Required reference pack absent: $source" }
    $target = Join-Path $destination "packs/$pack/10.0.13"
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Copy-Item -Path "$source/*" -Destination $target -Recurse
    foreach ($inputFile in Get-ChildItem -LiteralPath $source -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($source, $inputFile.FullName)
        $outputFile = Join-Path $target $relative
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $inputFile.FullName).Hash.ToLowerInvariant()
        if ((Get-FileHash -Algorithm SHA256 -LiteralPath $outputFile).Hash.ToLowerInvariant() -ne $hash) { throw "Reference copy mismatch: $relative" }
        $copied.Add(@{source=$inputFile.FullName;destination=$outputFile;sha256=$hash})
    }
}
@{sdk=$version;archiveSha512=$actual;references=$copied;dotnet=(Join-Path $destination 'dotnet.exe')} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $owned 'toolchain.json')
Write-Output "Isolated compiler verified, $($copied.Count) reference files: $destination"
