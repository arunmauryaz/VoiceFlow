#requires -Version 5.1
[CmdletBinding()]
param([string]$InnoCompiler)

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

Write-Host "Testing VoiceFlow $releaseVersion..."
& dotnet test (Join-Path $projectRoot 'VoiceFlow.Tests\VoiceFlow.Tests.csproj') -c Release --nologo "-p:BaseOutputPath=$testOutput\"
if ($LASTEXITCODE -ne 0) { throw 'Release tests failed.' }

Write-Host 'Publishing the self-contained Windows x64 application...'
# Keep files alongside the executable: model workers and native libraries use physical paths.
& dotnet publish $projectFile -c Release -r win-x64 --self-contained true --nologo `
    -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $publishRoot
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
$checksum = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding ascii -Value "$checksum  $([IO.Path]::GetFileName($installer))"
Set-Content -LiteralPath (Join-Path $releaseRoot 'INSTALL.txt') -Encoding ascii -Value @"
VoiceFlow $releaseVersion - Windows x64

Close VoiceFlow, then run VoiceFlow-v$releaseVersion-Setup.exe.
The .NET runtime is included. Local models download on selection.
CPU/GPU model setup is automatic and needs internet on first use.
Start with Windows is optional and unchecked by default.
This installer is unsigned; no code-signing certificate was provided.

Build payload: $publishRoot
"@
Write-Host "Installer: $installer"
Write-Host "Payload:   $publishRoot"
