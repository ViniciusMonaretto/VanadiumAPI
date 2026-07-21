# Build VanadiumWebApp + publish a self-contained Windows x64 executable.
# Output: dist\win-x64\
#
# Usage (PowerShell):
#   .\scripts\publish-windows.ps1
#   $env:SKIP_WEB_BUILD = "1"; .\scripts\publish-windows.ps1
$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ApiDir = Resolve-Path (Join-Path $ScriptDir "..")
$OutputDir = Join-Path $ApiDir "dist\win-x64"
$Runtime = if ($env:RUNTIME) { $env:RUNTIME } else { "win-x64" }
$Csproj = Join-Path $ApiDir "VanadiumAPI.csproj"
$BuildWebSh = Join-Path $ScriptDir "build-web.sh"

Write-Host "==> VanadiumAPI Windows publish ($Runtime)"

if ($env:SKIP_WEB_BUILD -ne "1") {
    if (Get-Command bash -ErrorAction SilentlyContinue) {
        & bash $BuildWebSh
    } else {
        $WebAppDir = Resolve-Path (Join-Path $ApiDir "..\VanadiumWebApp")
        $WwwRoot = Join-Path $ApiDir "wwwroot"
        $DistBrowser = Join-Path $WebAppDir "dist\web-app\browser"

        Push-Location $WebAppDir
        if (-not (Test-Path "node_modules")) {
            if (Test-Path "package-lock.json") { npm ci } else { npm install }
        }
        npm run build -- --configuration production
        Pop-Location

        if (-not (Test-Path $DistBrowser)) {
            throw "Angular build output not found at $DistBrowser"
        }

        New-Item -ItemType Directory -Force -Path $WwwRoot | Out-Null
        Get-ChildItem $WwwRoot -Exclude ".gitkeep" | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        Copy-Item -Path (Join-Path $DistBrowser "*") -Destination $WwwRoot -Recurse -Force
        @{
            apiUrl = "/panelReadingsHub"
        } | ConvertTo-Json | Set-Content (Join-Path $WwwRoot "assets\appSettings.json") -Encoding UTF8
    }
} else {
    Write-Host "==> SKIP_WEB_BUILD=1, using existing wwwroot"
}

if (Test-Path $OutputDir) {
    Remove-Item $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Write-Host "==> dotnet publish ($Runtime)"
Write-Host "    Output: $OutputDir"

dotnet publish $Csproj `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $OutputDir

$Readme = @"
VanadiumAPI deployment ($Runtime)
===================================

Run: VanadiumAPI.exe  (or run.bat)

Dashboard + API + SignalR hub on the port in appsettings.json (default http://localhost:5010).

Requirements on target machine:
  - MongoDB reachable (Mongo:ConnectionString in appsettings.json)
  - MQTT broker if using live gateways (MqttOptions in appsettings.json)
  - SQLite app.db is created on first run next to the executable

Build date: $((Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"))
"@

Set-Content -Path (Join-Path $OutputDir "README-DEPLOY.txt") -Value $Readme -Encoding UTF8

@'
@echo off
cd /d "%~dp0"
VanadiumAPI.exe %*
'@ | Set-Content -Path (Join-Path $OutputDir "run.bat") -Encoding ASCII

Write-Host ""
Write-Host "==> Windows build complete"
Write-Host "    $OutputDir\VanadiumAPI.exe"
Write-Host "    $OutputDir\run.bat"
