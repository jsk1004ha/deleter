param([ValidateSet('Build', 'Ui', 'Publish', 'Artifact')] [string]$Mode = 'Ui')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}

switch ($Mode) {
    'Build' {
        Invoke-DotNet @('build', 'src/ForceDeleter/ForceDeleter.csproj', '-c', 'Release', '--nologo', '-warnaserror', '-v', 'minimal')
        Invoke-DotNet @('build', 'tests/ForceDeleter.UiTests.csproj', '-c', 'Release', '--nologo', '-warnaserror', '-v', 'minimal')
    }
    'Ui' {
        Invoke-DotNet @('run', '--project', 'tests/ForceDeleter.UiTests.csproj', '-c', 'Release', '--no-build', '--', '.adhd/acceptance-ui')
        Invoke-DotNet @('run', '--project', 'tests/ForceDeleter.UiTests.csproj', '-c', 'Release', '--no-build', '--', '.adhd/acceptance-ui-96', '--dpi96')
    }
    'Publish' {
        Invoke-DotNet @('publish', 'src/ForceDeleter/ForceDeleter.csproj', '-c', 'Release', '--no-restore', '--nologo', '-o', 'artifacts')
    }
    'Artifact' {
        $exePath = if (Test-Path -LiteralPath 'artifacts/ForceDeleter.exe') { 'artifacts/ForceDeleter.exe' } elseif (Test-Path -LiteralPath 'ReleaseExe/ForceDeleter.exe') { 'ReleaseExe/ForceDeleter.exe' } else { 'ForceDeleter.exe' }
        if (-not (Test-Path -LiteralPath $exePath)) { throw "Executable not found at $exePath. Run with -Mode Publish first." }
        $delivered = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash
        if ((Get-Item -LiteralPath $exePath).Length -lt 1000000) { throw 'Unexpectedly small self-contained executable.' }
        $notice = Get-Content -LiteralPath 'src/ForceDeleter/Assets/Icons/SOURCES.md' -Raw
        $icons = @(Get-ChildItem -LiteralPath 'src/ForceDeleter/Assets/Icons' -Filter '*.svg')
        if ($icons.Count -ne 27) { throw 'Expected exactly 27 pinned SVG files.' }
        foreach ($icon in $icons) {
            $hash = (Get-FileHash -LiteralPath $icon.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            if (-not $notice.Contains("$hash  $($icon.Name)")) { throw "SVG hash mismatch: $($icon.Name)" }
        }
        if (-not (Get-Content -LiteralPath 'src/ForceDeleter/Assets/Icons/LICENSE' -Raw).Contains('MIT License')) { throw 'MIT license missing.' }
        $manifest = Get-Content -LiteralPath 'src/ForceDeleter/app.manifest' -Raw
        if (-not $manifest.Contains('level="requireAdministrator"')) { throw 'Application privilege manifest changed.' }
        Write-Output "PASS: self-contained executable SHA256 $delivered; all 27 SVG hashes and MIT notice verified."
    }
}
