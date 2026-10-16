param(
    [string]$Version = "",
    [string]$OutputDirectory = "publish"
)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot '..\src\PokeGrass.App\PokeGrass.App.csproj'
$projectPath = [IO.Path]::GetFullPath($projectPath)
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$project = Get-Content -LiteralPath $projectPath
    $Version = [string]($project.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
}
if ([string]::IsNullOrWhiteSpace($Version)) { throw 'Define una versión SemVer, por ejemplo 1.0.0.' }
if ($Version -notmatch '^\d+\.\d+\.\d+([-.+][0-9A-Za-z.-]+)?$') { throw "La versión '$Version' no tiene formato SemVer válido." }
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw 'Falta Velopack CLI 1.2.161. Instálalo con: dotnet tool install --global vpk --version 1.2.161'
}

dotnet publish $projectPath -c Release -f net10.0-windows -r win-x64 --self-contained true `
    -p:Version=$Version -p:PublishSingleFile=false -p:PublishTrimmed=false -o $outputPath
if ($LASTEXITCODE -ne 0) { throw "dotnet publish terminó con código $LASTEXITCODE." }

$releasePath = Join-Path $repoRoot 'Releases'
New-Item -ItemType Directory -Force -Path $releasePath | Out-Null
vpk pack --packId PokeGrass --packVersion $Version --packDir $outputPath --mainExe 'PokéGrass.exe' `
    --packTitle 'PokéGrass' --packAuthors 'Joaqx3wa' --icon (Join-Path $repoRoot 'assets\ui\pokegrass-masterball.ico') `
    --runtime win-x64 --outputDir $releasePath
if ($LASTEXITCODE -ne 0) { throw "vpk pack terminó con código $LASTEXITCODE." }

Get-ChildItem -LiteralPath $releasePath -File | Select-Object Name, @{Name='MiB'; Expression={[math]::Round($_.Length / 1MB, 2)}} | Format-Table -AutoSize
