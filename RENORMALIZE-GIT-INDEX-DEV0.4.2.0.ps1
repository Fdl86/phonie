$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$gitCommand = Get-Command git -ErrorAction SilentlyContinue
$gitPath = if ($gitCommand) { $gitCommand.Source } else { $null }
if (-not $gitPath) {
    $desktopRoot = Join-Path $env:LOCALAPPDATA "GitHubDesktop"
    $candidate = Get-ChildItem $desktopRoot -Filter git.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\resources\\app\\git\\cmd\\git\.exe$' } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    $gitPath = if ($candidate) { $candidate.FullName } else { $null }
}
if (-not $gitPath -or -not (Test-Path $gitPath)) {
    throw "Git introuvable. Ouvrir le dépôt dans GitHub Desktop puis relancer ce script."
}

& $gitPath rev-parse --is-inside-work-tree | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Ce dossier n'est pas le dépôt PHONIE. Conserver le dossier .git avant extraction."
}

$unmerged = @(& $gitPath diff --name-only --diff-filter=U)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($unmerged.Count -gt 0) {
    throw "Conflits Git non résolus : $($unmerged -join ', ')"
}

# Important : enregistrer d'abord les ajouts ET les suppressions du nouveau ZIP.
# Sans cette étape, --renormalize peut tenter de relire un ancien fichier suivi mais
# désormais absent du dossier de travail et échouer avec « unable to stat ».
& $gitPath -c core.autocrlf=false add -- .gitattributes
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $gitPath -c core.autocrlf=false add -A -- .
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Réappliquer ensuite les règles de fins de ligne à tous les fichiers suivis restants.
& $gitPath -c core.autocrlf=false add --renormalize -- .
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$frozenPaths = @(
    "data/radio/france/manifest.json",
    "tests/Phonie.SmokeTests/Fixtures/radio/france/manifest.json",
    "tests/Phonie.SmokeTests/Fixtures/radio/france/current/airports-fr.json",
    "data/airports/LFBI.json"
)
foreach ($path in $frozenPaths) {
    if (-not (Test-Path $path)) {
        throw "Fichier radio requis absent : $path"
    }
    $attribute = & $gitPath check-attr text -- $path
    if ($LASTEXITCODE -ne 0 -or $attribute -notmatch ': text: unset$') {
        throw "Attribut Git incorrect pour $path : $attribute"
    }
}

function Test-RadioManifestIntegrity([string]$manifestPath) {
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    $baseDirectory = Split-Path $manifestPath -Parent
    foreach ($slot in @("previous", "current", "next")) {
        $descriptor = $manifest.$slot
        if ($null -eq $descriptor) { continue }

        $relativePath = [string]$descriptor.relativePath
        $datasetPath = Join-Path $baseDirectory ($relativePath -replace '/', [IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path $datasetPath)) {
            throw "Jeu de données $slot absent : $datasetPath"
        }

        $bytes = [IO.File]::ReadAllBytes($datasetPath)
        for ($index = 0; $index -lt ($bytes.Length - 1); $index++) {
            if ($bytes[$index] -eq 13 -and $bytes[$index + 1] -eq 10) {
                throw "CRLF interdit dans le jeu de données haché : $datasetPath"
            }
        }

        $actual = (Get-FileHash $datasetPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $expected = ([string]$descriptor.sha256).ToLowerInvariant()
        if ($actual -ne $expected) {
            throw "SHA-256 incorrect pour $datasetPath : attendu $expected, obtenu $actual"
        }
    }
}

Test-RadioManifestIntegrity "tests/Phonie.SmokeTests/Fixtures/radio/france/manifest.json"
Test-RadioManifestIntegrity "data/radio/france/manifest.json"

Write-Host "Renormalisation Git, suppressions historiques, attributs -text, fins de ligne et SHA-256 : OK." -ForegroundColor Green
& $gitPath status --short
