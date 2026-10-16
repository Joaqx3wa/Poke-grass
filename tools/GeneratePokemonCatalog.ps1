param(
    [string]$AssetsPath = (Join-Path $PSScriptRoot '..\assets'),
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\assets\data')
)

$ErrorActionPreference = 'Stop'
$AssetsPath = [IO.Path]::GetFullPath($AssetsPath)
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
$previousExcludedPath = Join-Path $OutputPath 'pokemon-excluded.json'
$previousExcluded = if (Test-Path -LiteralPath $previousExcludedPath) {
    @(Get-Content -LiteralPath $previousExcludedPath -Raw | ConvertFrom-Json)
} else { @() }
$baselineExcludedCount = $previousExcluded.Count
$previousSummaryPath = Join-Path $OutputPath 'pokemon-excluded-report.md'
if (Test-Path -LiteralPath $previousSummaryPath) {
    $summaryMatch = [regex]::Match((Get-Content -LiteralPath $previousSummaryPath -Raw), 'IDs excluidos antes(?: de aplicar fallback femenino)?: (\d+)')
    if ($summaryMatch.Success) { $baselineExcludedCount = [int]$summaryMatch.Groups[1].Value }
}

# Exact variant identifiers verified as Mega forms. No suffix rule is used.
# Classic Mega forms are the explicit in-game alternate IDs; newer IDs were
# matched against official Mega Evolution announcements and inspected sprites.
$megaForms = @(
    'VENUSAUR_1','CHARIZARD_1','CHARIZARD_2','BLASTOISE_1','BEEDRILL_1','PIDGEOT_1',
    'ALAKAZAM_1','SLOWBRO_2','GENGAR_1','KANGASKHAN_1','PINSIR_1','GYARADOS_1',
    'AERODACTYL_1','MEWTWO_1','MEWTWO_2','AMPHAROS_1','STEELIX_1','SCIZOR_1',
    'HERACROSS_1','HOUNDOOM_1','TYRANITAR_1','SCEPTILE_1','BLAZIKEN_1','SWAMPERT_1',
    'GARDEVOIR_1','SABLEYE_1','MAWILE_1','AGGRON_1','MEDICHAM_1','MANECTRIC_1',
    'SHARPEDO_1','CAMERUPT_1','ALTARIA_1','BANETTE_1','ABSOL_1','ABSOL_2','GLALIE_1',
    'SALAMENCE_1','METAGROSS_1','LATIAS_1','LATIOS_1','RAYQUAZA_1','LOPUNNY_1',
    'GARCHOMP_1','GARCHOMP_2','LUCARIO_1','LUCARIO_2','ABOMASNOW_1','GALLADE_1',
    'AUDINO_1','DIANCIE_1',
    'DRAGONITE_1','VICTREEBEL_1','HAWLUCHA_1','MALAMAR_1','CHESNAUGHT_1','DELPHOX_1',
    'GRENINJA_3','RAICHU_2','RAICHU_3','MEGANIUM_1','EMBOAR_1','FERALIGATR_1',
    'EELEKTROSS_1','ZERAORA_1','STARMIE_1','DARKRAI_1','ZYGARDE_5','FLOETTE_6'
)
$megaSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($id in $megaForms) { [void]$megaSet.Add($id) }

# Genesect drive forms have their own encounter/walking art but intentionally
# share the base Genesect icons. This fallback applies only to these four IDs.
$genesectIconFallback = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($number in 1..4) { [void]$genesectIconFallback.Add("GENESECT_$number") }

# Camerupt's female Mega encounter has a normal female sprite but no separate
# shiny encounter. Reuse only the shiny sprite of regular Camerupt for this ID.
$femaleEncounterShinyFallback = @{ 'CAMERUPT_1_female' = 'CAMERUPT' }

# Pikachu's newly completed costume sprites are encounter forms of Pikachu,
# not additional weighted species entries. Their form is selected after the
# existing rarity draw, so adding art does not increase Pikachu's appearance rate.
$pikachuFormOnly = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($number in @(2..8 + 15)) { [void]$pikachuFormOnly.Add("PIKACHU_$number") }
[void]$pikachuFormOnly.Add('PIKACHU_2_female')

$folders = [ordered]@{
    encounterNormal = 'pokemon\encounter\normal'
    encounterShiny  = 'pokemon\encounter\shiny'
    walkingNormal   = 'pokemon\walking\normal'
    walkingShiny    = 'pokemon\walking\shiny'
    iconNormal      = 'pokemon\icons\normal'
    iconShiny       = 'pokemon\icons\shiny'
}
$rarityPath = Join-Path $OutputPath 'encounter-rarity.json'
$rarityIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
if (Test-Path -LiteralPath $rarityPath) {
    $rarity = Get-Content -LiteralPath $rarityPath -Raw | ConvertFrom-Json
    foreach ($tier in $rarity.species.PSObject.Properties) {
        foreach ($id in $tier.Value) { [void]$rarityIds.Add([string]$id) }
    }
}
$maps = @{}
foreach ($key in $folders.Keys) {
    $folder = Join-Path $AssetsPath $folders[$key]
    $map = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
    Get-ChildItem -LiteralPath $folder -File -Filter '*.png' | ForEach-Object {
        if (-not $map.ContainsKey($_.BaseName)) { $map.Add($_.BaseName, (Join-Path $folders[$key] $_.Name).Replace('\','/')) }
    }
    $maps[$key] = $map
}

$allIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($map in $maps.Values) { foreach ($id in $map.Keys) { [void]$allIds.Add($id) } }
$catalog = [Collections.Generic.List[object]]::new()
$excluded = [Collections.Generic.List[object]]::new()
foreach ($id in ($allIds | Sort-Object)) {
    if ($id -eq '000') {
        $excluded.Add([pscustomobject]@{ id=$id; reason='Marcador de recurso; no es una especie de Pokémon.'; missing=@(); mega=$false })
        continue
    }
    if ($megaSet.Contains($id)) {
        $excluded.Add([pscustomobject]@{ id=$id; reason='Forma Mega identificada explícitamente en mega-form-exclusions.json.'; missing=@(); mega=$true })
        continue
    }
    $resolved = @{}
    $missing = [Collections.Generic.List[string]]::new()
    $isFemaleVariant = $id.EndsWith('_female', [StringComparison]::OrdinalIgnoreCase)
    $baseId = if ($isFemaleVariant) { $id.Substring(0, $id.Length - '_female'.Length) } else { $id }
    foreach ($key in $folders.Keys) {
        if ($maps[$key].ContainsKey($id)) {
            $resolved[$key] = $maps[$key][$id]
            continue
        }
        $canFallback = $isFemaleVariant -and $key -notin @('encounterNormal', 'encounterShiny')
        if ($canFallback -and $maps[$key].ContainsKey($baseId)) {
            $resolved[$key] = $maps[$key][$baseId]
            continue
        }
        $canUseGenesectIcon = $genesectIconFallback.Contains($id) -and $key -in @('iconNormal', 'iconShiny')
        if ($canUseGenesectIcon -and $maps[$key].ContainsKey('GENESECT')) {
            $resolved[$key] = $maps[$key]['GENESECT']
            continue
        }
        if ($key -eq 'encounterShiny' -and $femaleEncounterShinyFallback.ContainsKey($id)) {
            $fallbackId = $femaleEncounterShinyFallback[$id]
            if ($maps[$key].ContainsKey($fallbackId)) {
                $resolved[$key] = $maps[$key][$fallbackId]
                continue
            }
        }
        $missing.Add($folders[$key])
    }
    if ($missing.Count -gt 0) {
        $reason = if ($isFemaleVariant) {
            'Falta un sprite femenino exacto de encounter o no existe el asset femenino ni el fallback de su misma forma.'
        } else {
            'Faltan recursos exactos compatibles; no se usan alias ni sprites de otra forma.'
        }
        $excluded.Add([pscustomobject]@{ id=$id; reason=$reason; missing=@($missing); mega=$false })
        continue
    }
    $catalog.Add([pscustomobject]@{
        id=$id
        encounterPoolEntry=($rarityIds.Contains($id) -and -not $pikachuFormOnly.Contains($id))
        encounterNormal=$resolved.encounterNormal
        encounterShiny=$resolved.encounterShiny
        walkingNormal=$resolved.walkingNormal
        walkingShiny=$resolved.walkingShiny
        iconNormal=$resolved.iconNormal
        iconShiny=$resolved.iconShiny
    })
}

$jsonOptions = @{ Depth=8; Compress=$false }
$catalog | ConvertTo-Json @jsonOptions | Set-Content -LiteralPath (Join-Path $OutputPath 'pokemon-catalog.json') -Encoding utf8
$excluded | ConvertTo-Json @jsonOptions | Set-Content -LiteralPath (Join-Path $OutputPath 'pokemon-excluded.json') -Encoding utf8
$excluded | ForEach-Object {
    [pscustomobject]@{ id=$_.id; reason=$_.reason; missingResources=($_.missing -join ';'); mega=$_.mega }
} | Export-Csv -LiteralPath (Join-Path $OutputPath 'pokemon-excluded.csv') -NoTypeInformation -Encoding utf8
$megaManifest = [pscustomobject]@{
    description='Lista explícita de IDs de formas Mega. La aplicación nunca excluye formas solo por acabar en _1, _2 u otro sufijo.'
    verificationSources=@(
        'https://mega.pokemon.com/en-us/news/',
        'https://legends.pokemon.com/en-us/news/mega-stones',
        'https://legends.pokemon.com/en-us/news/mega_lucario_z',
        'https://legends.pokemon.com/en-us/news/mega_zeraora',
        'https://www.pokemon.com/us/news/mega-garchomp-z-comes-to-pokemon-legends-z-a-mega-dimension',
        'https://www.pokemon.com/us/features/sneak-a-peek-at-cards-from-the-pokemon-tcg-mega-evolution-pitch-black-expansion',
        'https://www.pokemon.com/us/pokemon-news/rosas-encouragement-mega-zygarde-ex-and-more-from-mega-evolution-perfect-order'
    )
    ids=@($megaForms)
}
$megaManifest | ConvertTo-Json @jsonOptions | Set-Content -LiteralPath (Join-Path $OutputPath 'mega-form-exclusions.json') -Encoding utf8

$megaEvolutionPath = Join-Path $OutputPath 'mega-evolutions.json'
$megaEvolutionNames = @{}
if (Test-Path -LiteralPath $megaEvolutionPath) {
    foreach ($form in (Get-Content -LiteralPath $megaEvolutionPath -Raw | ConvertFrom-Json)) {
        $megaEvolutionNames[[string]$form.formId] = [string]$form.displayName
    }
}
$megaReport = [Collections.Generic.List[string]]::new()
$megaReport.Add('# Formas Mega registradas')
$megaReport.Add('')
$megaReport.Add("El registro explícito `mega-form-exclusions.json` contiene $($megaForms.Count) IDs Mega. $($megaEvolutionNames.Count) tienen además una definición de evolución utilizable en `mega-evolutions.json`; los demás solo constan como formas excluidas de los encuentros.")
$megaReport.Add('')
$megaReport.Add('| ID Mega | Nombre registrado | Evolución utilizable |')
$megaReport.Add('|---|---|---|')
foreach ($id in $megaForms) {
    $baseId = $id -replace '_\d+$', ''
    $displayName = if ($megaEvolutionNames.ContainsKey($id)) { $megaEvolutionNames[$id] } else { "Mega $($baseId.Substring(0,1))$($baseId.Substring(1).ToLowerInvariant())" }
    $evolutionStatus = if ($megaEvolutionNames.ContainsKey($id)) { 'Sí' } else { 'No; solo excluida del encuentro' }
    $megaReport.Add("| ``$id`` | $displayName | $evolutionStatus |")
}
$megaReport | Set-Content -LiteralPath (Join-Path $OutputPath 'pokemon-mega-forms.md') -Encoding utf8

$nonMegaExcluded = @($excluded | Where-Object { -not $_.mega -and $_.id -ne '000' } | Sort-Object id)
$femaleExcluded = @($nonMegaExcluded | Where-Object { $_.id.EndsWith('_female', [StringComparison]::OrdinalIgnoreCase) })
$previousFemaleExcluded = @($previousExcluded | Where-Object {
    -not $_.mega -and $_.id -ne '000' -and $_.id.EndsWith('_female', [StringComparison]::OrdinalIgnoreCase)
})
$baselineFemaleCount = $previousFemaleExcluded.Count
$femaleReportPath = Join-Path $OutputPath 'pokemon-excluded-female-report.md'
if (Test-Path -LiteralPath $femaleReportPath) {
    $femaleSummaryMatch = [regex]::Match((Get-Content -LiteralPath $femaleReportPath -Raw), 'Antes había (\d+) variantes femeninas')
    if ($femaleSummaryMatch.Success) { $baselineFemaleCount = [int]$femaleSummaryMatch.Groups[1].Value }
}
$femaleFallbackEntries = @($catalog | Where-Object {
    $candidate = $_
    $fallbackPaths = @($candidate.walkingNormal, $candidate.walkingShiny, $candidate.iconNormal, $candidate.iconShiny)
    $candidate.id.EndsWith('_female', [StringComparison]::OrdinalIgnoreCase) -and @(
        $fallbackPaths | Where-Object { [IO.Path]::GetFileNameWithoutExtension($_) -ine $candidate.id }
    ).Count -gt 0
})
$recoveredFemale = @($femaleFallbackEntries | Sort-Object id)

@"
# Informe del catálogo Pokémon

Generado desde las seis carpetas de encuentro, caminata e icono. Las variantes `_female` requieren encounter normal y shiny propios; para walking e iconos se permite fallback independiente a la misma forma sin `_female`. Los demás IDs requieren los seis recursos exactos.

- Incluidos: $($catalog.Count)
- Excluidos: $($excluded.Count)
- IDs excluidos antes de aplicar fallback femenino: $baselineExcludedCount
- IDs no Mega excluidos ahora: $($nonMegaExcluded.Count)
- Variantes femeninas recuperadas del informe anterior: $($recoveredFemale.Count)
- Variantes femeninas no Mega aún excluidas: $($femaleExcluded.Count)
- Formas de Genesect que usan iconos base por fallback explícito: $(($catalog | Where-Object { $genesectIconFallback.Contains($_.id) }).Count)
- Encuentros shiny femeninos resueltos con sprites de especie base: $(($catalog | Where-Object { $femaleEncounterShinyFallback.ContainsKey($_.id) }).Count)
- Formas Mega excluidas por ID explícito: $(($excluded | Where-Object mega).Count)
- Marcadores/otros no Pokémon: $(($excluded | Where-Object { $_.id -eq '000' }).Count)
- Excluidos por recursos faltantes: $(($excluded | Where-Object { $_.missing.Count -gt 0 }).Count)

El detalle por especie/variante, con la lista de carpetas faltantes, está en `pokemon-excluded.json`. Los archivos de origen se conservan intactos.

La clasificación de rareza se configura por separado en assets/data/encounter-rarity.json; este generador no inventa rarezas.
"@ | Set-Content -LiteralPath (Join-Path $OutputPath 'pokemon-excluded-report.md') -Encoding utf8

$femaleReport = [Collections.Generic.List[string]]::new()
$femaleReport.Add('# Variantes femeninas que siguen excluidas')
$femaleReport.Add('')
$femaleReport.Add("Antes había $baselineFemaleCount variantes femeninas no Mega excluidas. Se recuperaron $($recoveredFemale.Count); quedan $($femaleExcluded.Count). Solo se aceptan las que tienen encounter normal y shiny propios y assets válidos para walking/iconos (propios o fallback de la misma forma).")
$femaleReport.Add('')
$femaleReport.Add('| ID | Recursos exactos faltantes |')
$femaleReport.Add('|---|---|')
foreach ($entry in $femaleExcluded) { $femaleReport.Add("| ``$($entry.id)`` | $($entry.missing -join ', ') |") }
$femaleReport | Set-Content -LiteralPath (Join-Path $OutputPath 'pokemon-excluded-female-report.md') -Encoding utf8

$recoveredReport = [Collections.Generic.List[string]]::new()
$recoveredReport.Add('# Variantes femeninas recuperadas')
$recoveredReport.Add('')
$recoveredReport.Add("Se recuperaron $($recoveredFemale.Count) variantes femeninas con encounter normal y shiny propios. Los nombres indicados son las carpetas donde se usa el sprite base sin `_female`.")
$recoveredReport.Add('')
$recoveredReport.Add('| ID | Recursos con fallback |')
$recoveredReport.Add('|---|---|')
foreach ($entry in $recoveredFemale) {
    $fallbacks = [Collections.Generic.List[string]]::new()
    foreach ($mapping in @(
        @{ key='walkingNormal'; label='walking normal' }, @{ key='walkingShiny'; label='walking shiny' },
        @{ key='iconNormal'; label='icon normal' }, @{ key='iconShiny'; label='icon shiny' }
    )) {
        if ([IO.Path]::GetFileNameWithoutExtension($entry.($mapping.key)) -ine $entry.id) { $fallbacks.Add($mapping.label) }
    }
    $recoveredReport.Add("| ``$($entry.id)`` | $($fallbacks -join ', ') |")
}
$recoveredReport | Set-Content -LiteralPath (Join-Path $OutputPath 'pokemon-recovered-female-report.md') -Encoding utf8

$noCatch = [Collections.Generic.List[string]]::new()
$noCatch.Add('# Pokémon y variantes fuera de los encuentros capturables')
$noCatch.Add('')
$noCatch.Add("Lista generada desde `pokemon-excluded.json`, sin formas Mega: $($nonMegaExcluded.Count) IDs. Las variantes `_female` solo permanecen aquí si falta un encounter femenino normal/shiny exacto o no existe un recurso de apoyo ni su fallback de la misma forma.")
$noCatch.Add('')
$noCatch.Add('La lista contiene IDs de sprites/formas; una variante excluida no implica que toda la especie esté fuera de los encuentros.')
$orphanNameIds = @($nonMegaExcluded | Where-Object {
    $_.id -cmatch '[a-z]' -and -not $_.id.EndsWith('_female', [StringComparison]::OrdinalIgnoreCase)
} | Sort-Object id | ForEach-Object { $_.id })
if ($orphanNameIds.Count -gt 0) {
    $noCatch.Add('')
    $noCatch.Add("Nombres con mayúsculas/minúsculas no canónicas detectados entre los sprites excluidos: $($orphanNameIds -join ', '). Son IDs sueltos/incompletos; las especies base con nombre correcto se validan por separado.")
}
$noCatch.Add('')
$noCatch.Add('| ID | Recursos faltantes |')
$noCatch.Add('|---|---|')
foreach ($entry in $nonMegaExcluded) { $noCatch.Add("| ``$($entry.id)`` | $($entry.missing -join ', ') |") }
$noCatch | Set-Content -LiteralPath (Join-Path $OutputPath 'pokemon-no-capturables.md') -Encoding utf8

Write-Host "Catalog $($catalog.Count); excluded $($excluded.Count) -> $OutputPath"
