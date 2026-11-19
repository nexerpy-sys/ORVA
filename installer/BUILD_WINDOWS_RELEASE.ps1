<#
.SYNOPSIS
    Builds ORVA (Release | Any CPU -> net48), runs the full xUnit test suite, and compiles the Inno Setup installer.
.DESCRIPTION
    Run from a Windows 10/11 developer machine with Visual Studio 2022 Build Tools (.NET Framework 4.8 targeting pack),
    dotnet SDK (for xUnit test execution), and Inno Setup 6 (ISCC.exe) installed.
#>

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipTests,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$SolutionPath = Join-Path $RepoRoot "ORVA.sln"
$TestProjectPath = Join-Path $RepoRoot "tests\Orva.Billing.Tests\Orva.Billing.Tests.csproj"
$DesktopOutDir = Join-Path $RepoRoot "src\Orva.Desktop\bin\$Configuration\net48"
$IssPath = Join-Path $PSScriptRoot "ORVA.iss"

Write-Host "=== ORVA Windows Release Build ===" -ForegroundColor Cyan
Write-Host "Repo Root : $RepoRoot"
Write-Host "Config    : $Configuration (net48)"
Write-Host "OutputDir : $DesktopOutDir"

# 1. Locate MSBuild via vswhere
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = $null
if (Test-Path $vswhere) {
    $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
}
if (-not $msbuild) {
    $msbuildCmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($msbuildCmd) { $msbuild = $msbuildCmd.Source }
}
if (-not $msbuild) {
    throw "MSBuild.exe not found. Install Visual Studio 2022 or Build Tools for Visual Studio with .NET desktop build tools."
}
Write-Host "MSBuild   : $msbuild"

# 2. Restore NuGet packages
Write-Host "`n[1/4] Restoring NuGet packages..." -ForegroundColor Yellow
& $msbuild $SolutionPath /t:Restore /p:Configuration=$Configuration /nologo /v:minimal
if ($LASTEXITCODE -ne 0) { throw "NuGet restore failed with exit code $LASTEXITCODE." }

# 3. Build Solution
Write-Host "`n[2/4] Building ORVA.sln ($Configuration)..." -ForegroundColor Yellow
& $msbuild $SolutionPath /t:Rebuild /p:Configuration=$Configuration /nologo /v:minimal
if ($LASTEXITCODE -ne 0) { throw "Solution build failed with exit code $LASTEXITCODE." }

if (-not (Test-Path (Join-Path $DesktopOutDir "ORVA.exe"))) {
    throw "Expected build output not found: $(Join-Path $DesktopOutDir 'ORVA.exe')"
}

# Verify native SQLite interop DLL is present in output
$interopDll = Join-Path $DesktopOutDir "x64\SQLite.Interop.dll"
$rootInteropDll = Join-Path $DesktopOutDir "SQLite.Interop.dll"
if (-not (Test-Path $interopDll) -and -not (Test-Path $rootInteropDll)) {
    Write-Warning "SQLite.Interop.dll was not detected in $DesktopOutDir. Verify System.Data.SQLite.Core native interop copy on target machine."
}

# 4. Run xUnit Test Suite
if (-not $SkipTests) {
    Write-Host "`n[3/4] Running Orva.Billing.Tests (xUnit)..." -ForegroundColor Yellow
    $dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        throw "dotnet.exe not found. Install .NET SDK or re-run with -SkipTests."
    }
    & $dotnet.Source test $TestProjectPath -c $Configuration --no-restore --verbosity normal
    if ($LASTEXITCODE -ne 0) { throw "Unit tests failed with exit code $LASTEXITCODE." }
} else {
    Write-Host "`n[3/4] Skipping unit tests (-SkipTests specified)." -ForegroundColor DarkYellow
}

# 5. Build Inno Setup Installer
if (-not $SkipInstaller) {
    Write-Host "`n[4/4] Building Inno Setup installer..." -ForegroundColor Yellow
    $isccCandidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    )
    $iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) {
        $isccCmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($isccCmd) { $iscc = $isccCmd.Source }
    }
    if (-not $iscc) {
        throw "Inno Setup 6 (ISCC.exe) not found. Install Inno Setup 6 or re-run with -SkipInstaller."
    }

    & $iscc "/DSourceBinDir=$DesktopOutDir" $IssPath
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE." }

    Write-Host "`nInstaller generated under: $(Join-Path $PSScriptRoot 'output')" -ForegroundColor Green
} else {
    Write-Host "`n[4/4] Skipping Inno Setup installer (-SkipInstaller specified)." -ForegroundColor DarkYellow
}

Write-Host "`n=== ORVA Release Build Complete ===" -ForegroundColor Green
