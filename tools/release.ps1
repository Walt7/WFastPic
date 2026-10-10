# Pacchetto di release di WFastPic: publish, prova di avvio, zip portatile e setup.
#
#   powershell -ExecutionPolicy Bypass -File tools\release.ps1
#
# Risultato in artifacts\<versione>\: WFastPic-<versione>-win-x64.zip e WFastPic-<versione>-win-x64-setup.exe
# (versione letta da <Version> del csproj). Serve Inno Setup 6 (winget install JRSoftware.InnoSetup).
# Non pubblica niente: la release su GitHub si crea a mano con i due file.

param(
    # Salta la prova di avvio (es. su un PC senza sessione grafica). Sconsigliato.
    [switch]$SenzaProvaAvvio
)

$ErrorActionPreference = 'Stop'
$radice = Split-Path $PSScriptRoot -Parent
$csproj = Join-Path $radice 'src\WFastPic.App\WFastPic.App.csproj'

$versione = ([xml](Get-Content $csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $versione) { throw "Versione non trovata in $csproj" }

$uscita = Join-Path $radice "artifacts\$versione"
$publish = Join-Path $uscita 'publish'
$zip = Join-Path $uscita "WFastPic-$versione-win-x64.zip"

$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 non trovato: winget install JRSoftware.InnoSetup' }

Write-Host "WFastPic $versione -> $uscita"
if (Test-Path $uscita) { Remove-Item $uscita -Recurse -Force }
New-Item -ItemType Directory -Force $uscita | Out-Null

# 1. Publish (app autonoma: .NET e Windows App SDK inclusi).
Write-Host '1/4 publish'
& dotnet publish $csproj -c Release -r win-x64 -p:Platform=x64 -p:DebugType=None -p:DebugSymbols=false -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish non riuscito ($LASTEXITCODE)" }

# 2. Prova di avvio dell'exe del pacchetto, non di quello di Debug: senza *.xbf e .pri l'app muore
#    subito (0xC000027B) e la build da sola non se ne accorge.
$exe = Join-Path $publish 'WFastPic.App.exe'
foreach ($necessario in @($exe, (Join-Path $publish 'WFastPic.App.pri'))) {
    if (-not (Test-Path $necessario)) { throw "Manca nel pacchetto: $necessario" }
}
if (-not (Get-ChildItem $publish -Recurse -Filter *.xbf)) { throw 'Mancano i file XAML compilati (*.xbf) nel pacchetto' }

if ($SenzaProvaAvvio) {
    Write-Host '2/4 prova di avvio SALTATA'
} else {
    Write-Host '2/4 prova di avvio'
    $processo = Start-Process $exe -PassThru
    Start-Sleep -Seconds 8
    $processo.Refresh()
    if ($processo.HasExited) { throw "L'app del pacchetto si è chiusa all'avvio (codice $($processo.ExitCode))" }
    [void]$processo.CloseMainWindow()
    if (-not $processo.WaitForExit(5000)) { Stop-Process -Id $processo.Id -Force }
}

# 3. Zip portatile.
Write-Host '3/4 zip'
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal

# 4. Setup.
Write-Host '4/4 setup'
& $iscc /Q "/DVersione=$versione" "/DCartellaPublish=$publish" "/O$uscita" (Join-Path $radice 'installer\WFastPic.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup non riuscito ($LASTEXITCODE)" }

Write-Host ''
Get-ChildItem $uscita -File | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
    Write-Host ("{0}  {1:N1} MB  SHA256 {2}" -f $_.Name, ($_.Length / 1MB), $hash)
}
