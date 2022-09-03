param (
    [Parameter(Mandatory = $true)] [string] $vscode
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3

$languages = @()

$packageFileNames = Join-Path -Path $vscode -ChildPath "extensions" | `
    Get-ChildItem -Directory | `
    Get-ChildItem -Filter "package.json"

foreach ($packageFileName in $packageFileNames) {
    $package = Get-Content -Path $packageFileName | ConvertFrom-Json

    if (($package | Get-Member "contributes") -and ($package.contributes | Get-Member "languages")) {
        foreach ($language in $package.contributes.languages) {
            if (($language | Get-Member "extensions") -and ($language | Get-Member "configuration")) {
                $configuration = Split-Path -Path $packageFileName -Parent |  `
                    Join-Path -ChildPath $language.configuration | `
                    Resolve-Path | `
                    Get-Content | `
                    ConvertFrom-Json

                if ($configuration | Get-Member "surroundingPairs") {
                    $pairs = @()

                    # Some surrounding pairs are stored as arrays of two elements, while
                    # others are stored as objects with an "open" and "close" property.
                    # Since we need to load it into .NET objects, we'll convert
                    # everything to objects with "open" and "close" properties.
                    foreach ($pair in $configuration.surroundingPairs) {
                        if ($pair -is [array]) {
                            $pairs += @{ open = $pair[0]; close = $pair[1] }

                        } elseif (($pair | Get-Member "open") -and ($pair | Get-Member "close")) {
                            $pairs += $pair
                        }
                    }

                    # Convert the extensions to lowercase and remove duplicates.
                    $extensions = @($language.extensions | ForEach-Object { $_.ToLowerInvariant() } | Select-Object -Unique)

                    if ($pairs.Length -gt 0) {
                        $data = [ordered]@{
                            id               = $language.id
                            extensions       = $extensions
                            surroundingPairs = $pairs
                        }

                        $languages += $data
                    }
                }
            }
        }
    }
}

$output = Join-Path -Path $PSScriptRoot -ChildPath "source/AutoSurround/Languages/vscode.json"
$json = $languages | ConvertTo-Json -Depth 100 -Compress

# Get the commit hash to add as a comment to the JSON file.
Push-Location $vscode

try {
    $commit = git rev-parse HEAD

} finally {
    Pop-Location
}

$json = "// Generated from microsoft/vscode@$commit`n" + $json
$json | Out-File -FilePath $output

# Format the JSON using Prettier.
Write-Host "Formatting..."
npx prettier $output --write --tab-width 4 --use-tabs false
Write-Host "Done."
