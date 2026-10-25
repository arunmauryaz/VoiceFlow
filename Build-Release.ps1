#requires -Version 5.1
[CmdletBinding()]
param([string]$InnoCompiler, [switch]$KeepBuildFiles)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSCommandPath
$projectFile = Join-Path $projectRoot 'VoiceFlow\VoiceFlow.csproj'
$projectXml = [xml](Get-Content -LiteralPath $projectFile -Raw)
$releaseVersion = @($projectXml.Project.PropertyGroup.Version | Where-Object { $_ })[0]
if (-not $releaseVersion) { throw 'The application version is missing from VoiceFlow.csproj.' }

if (-not $InnoCompiler) {
    $compilerCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($compilerCommand) { $InnoCompiler = $compilerCommand.Source }
    else {
        $compilerLocations = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
        )
        $InnoCompiler = $compilerLocations | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
}
if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler)) {
    throw 'Install Inno Setup 6, or pass -InnoCompiler with the full path to ISCC.exe.'
}

$releaseRoot = Join-Path $projectRoot 'release'
$stagingRoot = Join-Path $projectRoot ('out\release-' + [Guid]::NewGuid().ToString('N'))
$publishRoot = Join-Path $stagingRoot 'win-x64'
$testOutput = Join-Path $stagingRoot 'tests'
New-Item -ItemType Directory -Force -Path $releaseRoot, $publishRoot | Out-Null

try {
Write-Host "Testing VoiceFlow $releaseVersion..."
& dotnet test (Join-Path $projectRoot 'VoiceFlow.Tests\VoiceFlow.Tests.csproj') -c Release --nologo "-p:BaseOutputPath=$testOutput\"
if ($LASTEXITCODE -ne 0) { throw 'Release tests failed.' }

Write-Host 'Publishing the self-contained Windows x64 application...'
# Keep files alongside the executable: model workers and native libraries use physical paths.
& dotnet publish $projectFile -c Release -r win-x64 --self-contained true --nologo `
    -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false "-p:BaseOutputPath=$stagingRoot\build\" -o $publishRoot
if ($LASTEXITCODE -ne 0) { throw 'Publishing failed.' }

foreach ($requiredFile in @('VoiceFlow.exe', 'hostfxr.dll', 'hostpolicy.dll',
    'LocalRuntime\bark_worker.py', 'LocalRuntime\bark_loader.py',
    'LocalRuntime\parakeet\tokenizer.json', 'runtimes\win-x64\whisper.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishRoot $requiredFile))) {
        throw "Required release file is missing: $requiredFile"
    }
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $publishRoot

Write-Host 'Compiling the Windows installer...'
& $InnoCompiler "/DMyAppVersion=$releaseVersion" "/DPublishDir=$publishRoot" "/DReleaseDir=$releaseRoot" (Join-Path $projectRoot 'VoiceFlow-Setup.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }

$installer = Join-Path $releaseRoot "VoiceFlow-v$releaseVersion-Setup.exe"
$portable = Join-Path $releaseRoot "VoiceFlow-v$releaseVersion-win-x64.zip"
$stagedPortable = Join-Path $stagingRoot ([IO.Path]::GetFileName($portable))
Write-Host 'Creating the portable ZIP...'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($publishRoot, $stagedPortable, [IO.Compression.CompressionLevel]::Optimal, $false)
Move-Item -LiteralPath $stagedPortable -Destination $portable -Force
$checksums = foreach ($artifact in @($installer, $portable)) {
    $checksum = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
    "$checksum  $([IO.Path]::GetFileName($artifact))"
}
Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding ascii -Value $checksums
Set-Content -LiteralPath (Join-Path $releaseRoot 'INSTALL.txt') -Encoding ascii -Value @"
VoiceFlow $releaseVersion - Windows x64

Close VoiceFlow, then run VoiceFlow-v$releaseVersion-Setup.exe.
Or extract VoiceFlow-v$releaseVersion-win-x64.zip and run VoiceFlow.exe.
Keep all portable files together.
The .NET runtime is included in both packages. Local models download on selection.
CPU/GPU model setup is automatic and needs internet on first use.
Start with Windows is optional and unchecked by default.
This installer is unsigned; no code-signing certificate was provided.

"@
Write-Host "Installer: $installer"
Write-Host "Portable:  $portable"
if ($KeepBuildFiles) { Write-Host "Payload:   $publishRoot" }
}
finally {
    if (-not $KeepBuildFiles -and (Test-Path -LiteralPath $stagingRoot)) {
        $resolvedStaging = [IO.Path]::GetFullPath($stagingRoot)
        $expectedParent = [IO.Path]::GetFullPath((Join-Path $projectRoot 'out'))
        if ((Split-Path -Parent $resolvedStaging) -ne $expectedParent -or
            (Split-Path -Leaf $resolvedStaging) -notmatch '^release-[0-9a-f]{32}$') {
            throw 'Refusing to clean a staging directory outside the release workspace.'
        }
        Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
        Write-Host 'Temporary build files removed.'
    }
}
