# CinematicCam kurulum dosyasını üretir: artifacts\CinematicCam-Setup-<sürüm>.exe
#
#   powershell -ExecutionPolicy Bypass -File build-setup.ps1
#
# Adımlar:
#   1. Uygulamayı .NET çalışma zamanı dahil, tek dosya olarak yayınlar (nuget.org'dan çalışma zamanı paketleri gerekir).
#   2. Yayını zip'ler (uygulama yükü).
#   3. Kurulum sihirbazını (.NET Framework 4.8) derler.
#   4. Sihirbaz exe'sinin sonuna zip'i ve 16 baytlık bir altbilgi ekler.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'
$payloadZip = Join-Path $artifacts 'payload.zip'

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = $props.Project.PropertyGroup.Version
Write-Host "CinematicCam $version kurulumu hazırlanıyor..." -ForegroundColor Cyan

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
New-Item -ItemType Directory -Force $artifacts | Out-Null

Write-Host '1/4 Uygulama yayınlanıyor...'
& dotnet publish (Join-Path $root 'src\CinematicCam\CinematicCam.csproj') -c Release -r win-x64 --self-contained true -o $publish `
    --source https://api.nuget.org/v3/index.json `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:SatelliteResourceLanguages=en -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw 'Uygulama yayınlanamadı.' }
foreach ($required in 'CinematicCam.exe', 'SimConnect.dll') {
    if (-not (Test-Path (Join-Path $publish $required))) { throw "$required yayında yok. MSFS2024_SDK ortam değişkenini kontrol edin." }
}

Write-Host '2/4 Yük paketleniyor...'
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory($publish, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host '3/4 Kurulum sihirbazı derleniyor...'
& dotnet build (Join-Path $root 'installer\Setup\Setup.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Kurulum sihirbazı derlenemedi.' }
$setupExe = Join-Path $root 'installer\Setup\bin\Release\net48\CinematicCam-Setup.exe'

Write-Host '4/4 Kurulum dosyası birleştiriliyor...'
$output = Join-Path $artifacts "CinematicCam-Setup-$version.exe"
$out = [System.IO.File]::Create($output)
try {
    $bytes = [System.IO.File]::ReadAllBytes($setupExe)
    $out.Write($bytes, 0, $bytes.Length)
    $offset = [Int64]$bytes.Length
    $zip = [System.IO.File]::OpenRead($payloadZip)
    try { $zip.CopyTo($out) } finally { $zip.Dispose() }
    $footer = [BitConverter]::GetBytes($offset) + [System.Text.Encoding]::ASCII.GetBytes('CCSETUP1')
    $out.Write($footer, 0, $footer.Length)
}
finally { $out.Dispose() }
Remove-Item $payloadZip -Force

$size = [Math]::Round((Get-Item $output).Length / 1MB, 1)
Write-Host "Hazır: $output ($size MB)" -ForegroundColor Green
