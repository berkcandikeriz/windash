<#
.SYNOPSIS
    WinDash MSI kurulum paketini üretir: artifacts\WinDash-Setup-<sürüm>.msi

.DESCRIPTION
    1. Uygulamayı tek bir WinDash.exe olarak yayınlar (varsayılan: .NET çalışma zamanı içinde,
       hedef bilgisayara ayrıca .NET kurmak gerekmez).
    2. WiX Toolset v5 ile MSI'ı derler (WiX, NuGet'ten kendiliğinden iner; ayrıca kurmaya gerek yok).
    Gereksinim: .NET 8 SDK.

.PARAMETER Version
    MSI ve exe sürümü (ör. 1.0.1). Yeni sürüm eskisinin üzerine kurulurken eskisi otomatik kaldırılır;
    bu yüzden her yayında sürümü artır.

.PARAMETER FrameworkDependent
    .NET çalışma zamanını gömme (MSI küçülür; hedef bilgisayarda .NET 8 Desktop Runtime gerekir).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build-installer.ps1 -Version 1.1.0
#>
param(
    [string]$Version = '1.1.0',
    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
$root      = Split-Path -Parent $MyInvocation.MyCommand.Path
$artifacts = Join-Path $root 'artifacts'
$publish   = Join-Path $artifacts 'publish'
$app       = Join-Path $root 'src\PcDashboard\PcDashboard.csproj'
$installer = Join-Path $root 'installer\WinDash.Installer\WinDash.Installer.wixproj'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK bulunamadı. https://dotnet.microsoft.com/download/dotnet/8.0"
}

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

Write-Host "1/2 WinDash $Version yayınlanıyor…" -ForegroundColor Cyan
$publishArgs = @(
    'publish', $app, '-c', 'Release', '-r', 'win-x64', '-o', $publish,
    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=none', "-p:Version=$Version"
)
if ($FrameworkDependent) {
    $publishArgs += @('--self-contained', 'false')
} else {
    $publishArgs += @('--self-contained', 'true', '-p:EnableCompressionInSingleFile=true')
}
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish başarısız oldu ($LASTEXITCODE)." }

Write-Host "2/2 MSI derleniyor…" -ForegroundColor Cyan
& dotnet build $installer -c Release "-p:Version=$Version" "-p:AppPublishDir=$publish\" -o $artifacts
if ($LASTEXITCODE -ne 0) { throw "MSI derlemesi başarısız oldu ($LASTEXITCODE)." }

# Dil (tr-TR) seçildiğinde WiX çıktıyı alt klasöre koyabilir; nerede olursa bul
$msi = Get-ChildItem $artifacts -Recurse -Filter 'WinDash-Setup.msi' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $msi) { throw "WinDash-Setup.msi bulunamadı ($artifacts)." }
$final = Join-Path $artifacts "WinDash-Setup-$Version.msi"
if (Test-Path $final) { Remove-Item $final -Force }
Move-Item $msi.FullName $final
Get-ChildItem $artifacts -Recurse -Filter '*.wixpdb' | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem $artifacts -Directory | Where-Object { $_.Name -ne 'publish' -and -not (Get-ChildItem $_.FullName -Recurse -File) } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Hazır: $final" -ForegroundColor Green
Write-Host "Kurmak için çift tıkla. WinDash zaten kuruluysa kurulum güncellemek isteyip istemediğini sorar."
Write-Host "Sessiz güncelleme: msiexec /i `"$final`" /qn"
Write-Host "Windows ile başlatmadan kurmak için:"
Write-Host "  msiexec /i `"$final`" AUTOSTART=0"
