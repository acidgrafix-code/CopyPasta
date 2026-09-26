<#
.SYNOPSIS
    Builds the CopyPasta installer.

.DESCRIPTION
    Publishes a self-contained build and packages it with Velopack, producing a Setup.exe, a
    portable zip and the update-feed files.

    Self-contained on purpose: the installer works on a machine with no .NET runtime, which is what
    you want when handing a build to someone else. It costs about 160 MB unpacked.

    Requires nothing installed globally — vpk is a local tool restored from .config/dotnet-tools.json.

.EXAMPLE
    pwsh build-installer.ps1
    pwsh build-installer.ps1 -Version 1.0.1
#>
[CmdletBinding()]
param(
    [string]$Version = '1.0.0',

    # Authenticode signing. Leave empty for an unsigned build; downloaders then see a SmartScreen
    # warning on first run.
    [string]$SigningCertificate = '',
    [string]$SigningPassword = ''
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$publish = Join-Path $PSScriptRoot 'artifacts\publish'
$releases = Join-Path $PSScriptRoot 'artifacts\releases'

Write-Host "== Restoring tools ==" -ForegroundColor Cyan
dotnet tool restore

Write-Host "== Testing ==" -ForegroundColor Cyan
dotnet test CopyPasta.sln -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Tests failed; not packaging." }

Write-Host "== Publishing (self-contained, win-x64) ==" -ForegroundColor Cyan
if (Test-Path $publish) { [IO.Directory]::Delete($publish, $true) }
dotnet publish src\CopyPasta.App -c Release -r win-x64 --self-contained true `
    -p:DebugType=none -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

Write-Host "== Packaging ==" -ForegroundColor Cyan
if (Test-Path $releases) { [IO.Directory]::Delete($releases, $true) }

$packArgs = @(
    'vpk', 'pack',
    '--packId', 'CopyPasta',
    '--packVersion', $Version,
    '--packDir', $publish,
    '--mainExe', 'CopyPasta.exe',
    '--packAuthors', 'ACIDgrafix, LLC',
    '--packTitle', 'CopyPasta',
    '--icon', 'src\CopyPasta.App\Resources\Icons\app.ico',
    # Must match the publish runtime, or the package is labelled x86 and may misbehave.
    '--runtime', 'win-x64',
    '--outputDir', $releases
)

if ($SigningCertificate) {
    $packArgs += @('--signParams', "/f `"$SigningCertificate`" /p `"$SigningPassword`" /fd sha256 /tr http://timestamp.digicert.com /td sha256")
}

dotnet @packArgs
if ($LASTEXITCODE -ne 0) { throw "Packaging failed." }

Write-Host ""
Write-Host "== Done ==" -ForegroundColor Green
Get-ChildItem $releases | ForEach-Object { "  {0,-34} {1,10:N0} bytes" -f $_.Name, $_.Length }
Write-Host ""
Write-Host "Installer: $(Join-Path $releases 'CopyPasta-win-Setup.exe')"
Write-Host "To publish an update, upload the contents of artifacts\releases to a GitHub release."
