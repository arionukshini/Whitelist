$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$publishDir = Join-Path $repoRoot "artifacts\publish\win-x64-v2.0.0"
$installerDir = Join-Path $repoRoot "artifacts\installer"
$installerName = "WhitelistLockIn-Setup-2.0.0-x64.exe"
$innoCompiler = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
$publishRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts\publish")) + [IO.Path]::DirectorySeparatorChar
$publishFullPath = [IO.Path]::GetFullPath($publishDir)
if (-not $publishFullPath.StartsWith($publishRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Publish output must remain inside artifacts\publish."
}

Write-Host "Running automated tests..."
dotnet test (Join-Path $repoRoot "FocusGuard.sln") -c Release
if ($LASTEXITCODE -ne 0) { throw "Tests failed; release publish was stopped." }
node --test (Join-Path $repoRoot "tests\extension-tests.mjs")
if ($LASTEXITCODE -ne 0) { throw "Browser extension tests failed; release publish was stopped." }

Write-Host "Publishing self-contained Windows x64 application..."
if (Test-Path -LiteralPath $publishFullPath) {
    Remove-Item -LiteralPath $publishFullPath -Recurse -Force
}
dotnet publish (Join-Path $repoRoot "src\FocusGuard.UI\FocusGuard.UI.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "Release publish failed." }

$nativeHostDir = Join-Path $publishDir "NativeHost"
New-Item -ItemType Directory -Force -Path $nativeHostDir | Out-Null
Write-Host "Publishing the browser native messaging host..."
dotnet publish (Join-Path $repoRoot "src\FocusGuard.NativeHost\FocusGuard.NativeHost.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false -o $nativeHostDir
if ($LASTEXITCODE -ne 0) { throw "Native messaging host publish failed." }

if (-not (Test-Path (Join-Path $publishDir "WhitelistLockIn.exe"))) { throw "Published executable is missing." }
if (-not (Test-Path (Join-Path $publishDir "BrowserExtension\manifest.json"))) { throw "Published browser extension is missing." }
if (-not (Test-Path (Join-Path $nativeHostDir "WhitelistLockIn.NativeHost.exe"))) { throw "Published native messaging host is missing." }
if (-not (Test-Path $innoCompiler)) { throw "Inno Setup 6 compiler was not found at $innoCompiler." }

New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
Write-Host "Building the Windows installer..."
& $innoCompiler "/DPublishDir=$publishDir" (Join-Path $repoRoot "installer\WhitelistLockIn.iss")
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed." }

Write-Host "Release files:"
Get-Item (Join-Path $publishDir "WhitelistLockIn.exe"), (Join-Path $installerDir $installerName) | Select-Object FullName, Length
