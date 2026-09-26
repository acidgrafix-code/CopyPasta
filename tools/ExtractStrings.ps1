<#
.SYNOPSIS
    Converts Apple .xcstrings catalogues into the JSON string tables the app loads.

.DESCRIPTION
    Clipy's translations are MIT-licensed code resources, unlike its icons, so they are the one
    asset worth carrying over wholesale. This reads the catalogues from the macOS source tree and
    writes one flat JSON file per locale.

    The key is the English source string, exactly as it is in the catalogue, so a missing
    translation falls back to readable English rather than to a placeholder token.

    Two edits are applied on the way through, because the catalogues describe the macOS app:

      - Strings for features that did not port (the Accessibility permission prompt, the donation
        panel, the usage-log notice) are dropped. Windows needs no Accessibility grant to send a
        paste, so the prompt would be untriggerable text.

      - "Clipy" and "ClipMenu" are rewritten to "CopyPasta", in keys as well as values. Shipping
        another project's name in the UI is both a visible bug and the naming constraint in
        PORTING_PLAN.md section 2. Doing it here rather than by hand means a re-extract cannot
        quietly put the old name back.

    Re-run after pulling a newer macOS source tree:
        pwsh tools/ExtractStrings.ps1
#>
[CmdletBinding()]
param(
    [string]$SourceDirectory = "$PSScriptRoot\..\Clipy-develop\Clipy\Resources",
    [string]$OutputDirectory = "$PSScriptRoot\..\src\CopyPasta.App\Resources\Strings"
)

$ErrorActionPreference = 'Stop'

$catalogues = @('Localizable.xcstrings', 'Settings.xcstrings')
$tables = @{}

foreach ($catalogue in $catalogues) {
    $path = Join-Path $SourceDirectory $catalogue
    if (-not (Test-Path $path)) {
        Write-Warning "Skipping missing catalogue: $path"
        continue
    }

    $json = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8) | ConvertFrom-Json

    foreach ($entry in $json.strings.PSObject.Properties) {
        $key = $entry.Name
        if ([string]::IsNullOrEmpty($key)) { continue }

        $localizations = $entry.Value.localizations
        if ($null -eq $localizations) { continue }

        foreach ($locale in $localizations.PSObject.Properties) {
            $value = $locale.Value.stringUnit.value
            if ([string]::IsNullOrEmpty($value)) { continue }

            if (-not $tables.ContainsKey($locale.Name)) {
                $tables[$locale.Name] = [ordered]@{}
            }

            # Later catalogues win on a clash; in practice the two do not overlap.
            $tables[$locale.Name][$key] = $value
        }
    }
}

# Keys describing macOS-only features. Dropped rather than translated: there is no Windows UI
# that could ever show them.
$dropKeys = @(
    'Allow Clipy in System Settings > Privacy & Security > Accessibility.',
    'Please allow Accessibility',
    'Open System Settings',
    'Donation message',
    'Changes take effect the next time Clipy launches. Usage logs do not include personal information or copied content.'
)

# Applied to keys and values alike. Longest first, so "ClipMenu" is not left as "CopyPastaMenu".
$rebrand = [ordered]@{
    'ClipMenu' = 'CopyPasta'
    'Clipy'    = 'CopyPasta'
}

foreach ($locale in @($tables.Keys)) {
    $rebranded = [ordered]@{}

    foreach ($key in @($tables[$locale].Keys)) {
        if ($dropKeys -contains $key) { continue }

        $newKey = $key
        $newValue = $tables[$locale][$key]
        foreach ($from in $rebrand.Keys) {
            $newKey = $newKey.Replace($from, $rebrand[$from])
            $newValue = $newValue.Replace($from, $rebrand[$from])
        }

        $rebranded[$newKey] = $newValue
    }

    $tables[$locale] = $rebranded
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$utf8 = New-Object Text.UTF8Encoding $false

foreach ($locale in ($tables.Keys | Sort-Object)) {
    $table = $tables[$locale]

    # Sorted keys keep the diff readable when the catalogues change.
    $sorted = [ordered]@{}
    foreach ($key in ($table.Keys | Sort-Object)) { $sorted[$key] = $table[$key] }

    $outputPath = Join-Path $OutputDirectory "strings.$locale.json"
    [IO.File]::WriteAllText($outputPath, ($sorted | ConvertTo-Json -Depth 3), $utf8)

    Write-Host ("{0,-8} {1,4} strings -> {2}" -f $locale, $sorted.Count, (Split-Path $outputPath -Leaf))
}

Write-Host "Done. $($tables.Keys.Count) locales."
