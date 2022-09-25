param (
    [Parameter(Mandatory = $true)] [string] $vscode
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3

$languages = @()
$names = @()

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

                $pairs = @()

                if ($configuration | Get-Member "surroundingPairs") {
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

                } elseif ($configuration | Get-Member "autoClosingPairs") {
                    # There are no surrounding pairs in the configuration,
                    # but the auto-closing pairs are a reasonable substitute.
                    foreach ($pair in $configuration.autoClosingPairs) {
                        if (($pair | Get-Member "open") -and ($pair | Get-Member "close")) {
                            $open = $pair.open
                            $close = $pair.close

                            # Auto-closing pairs can be more than just one character. Since we are using
                            # them as surrounding pairs, we only want the ones that are single characters.
                            if (($open.Length -eq 1) -and ($close.Length -eq 1)) {
                                $pairs += @{ open = $open; close = $close }
                            }
                        }
                    }

                } elseif ($configuration | Get-Member "brackets") {
                    # Brackets are the next best thing we can use for surrounding pairs.
                    foreach ($pair in $configuration.brackets) {
                        $open = $pair[0]
                        $close = $pair[1]

                        # Brackets can be more than just one character. Since we are using them
                        # as surrounding pairs, we only want the ones that are single characters.
                        if (($open.Length -eq 1) -and ($close.Length -eq 1)) {
                            $pairs += @{ open = $open; close = $close }
                        }
                    }

                } else {
                    Write-Host "No surrounding pairs found for $($language.id)"
                }

                if ($pairs.Length -gt 0) {
                    # Convert the extensions to lowercase and remove duplicates.
                    $extensions = @($language.extensions | ForEach-Object { $_.ToLowerInvariant() } | Select-Object -Unique)

                    $data = [ordered]@{
                        id               = $language.id
                        extensions       = $extensions
                        surroundingPairs = $pairs
                    }

                    $languages += $data

                    # Remember the language name so that we can print it outer later. Default to
                    # the language ID, but if there are aliases, then we'll use the first alias.
                    $name = $language.id

                    if ($language | Get-Member "aliases") {
                        if ($language.aliases.Length -gt 0) {
                            $name = $language.aliases[0]
                        }
                    }

                    $names += $name
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

# Print the list of language names that can be copied into the readme file.
$names | Sort-Object | ForEach-Object { Write-Host "* $_" }
