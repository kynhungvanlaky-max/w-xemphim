[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$Host.UI.RawUI.WindowTitle = "CinemaFlow - Khoi chay Cloudflare Tunnel cho Dien Thoai"

Write-Host "=====================================================================" -ForegroundColor Cyan
Write-Host "       CINEMAFLOW - TU DONG KHOI CHAY HE THONG CHO DIEN THOAI        " -ForegroundColor Yellow
Write-Host "=====================================================================" -ForegroundColor Cyan
Write-Host ""

$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path "$projectRoot\WebXemPhim.slnx")) {
    $projectRoot = "C:\Users\kynhu\Documents\Projects\wenPhim\w-xemphim"
}

$cloudflared = "C:\Program Files (x86)\cloudflared\cloudflared.exe"
if (-not (Test-Path $cloudflared)) {
    $cloudflared = "$projectRoot\tools\cloudflared.exe"
}

# 1. Don dep tien trinh cu neu co
Get-Process -Name "cloudflared" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# 2. Khoi dong WebXemPhim.Server (5085)
Write-Host "[1/3] Dang khoi dong Backend Server (Port 5085)..." -ForegroundColor Green
$serverProc = Start-Process dotnet -ArgumentList "run --no-launch-profile --urls http://localhost:5085" -WorkingDirectory "$projectRoot\src\Server\WebXemPhim.Server" -PassThru -WindowStyle Minimized

Start-Sleep -Seconds 3

# 3. Khoi dong WebXemPhim.Client (5229 - Gateway)
Write-Host "[2/3] Dang khoi dong Blazor Web & Gateway (Port 5229)..." -ForegroundColor Green
$clientProc = Start-Process dotnet -ArgumentList "run --no-launch-profile --urls http://localhost:5229" -WorkingDirectory "$projectRoot\src\Client\WebXemPhim.Client" -PassThru -WindowStyle Minimized

Start-Sleep -Seconds 4

# 4. Khoi dong Cloudflare Tunnel va bat URL
Write-Host "[3/3] Dang tao duong ham Cloudflare HTTPS sieu toc..." -ForegroundColor Green
$logFile = "$env:TEMP\cf_quick_tunnel.log"
if (Test-Path $logFile) { Remove-Item $logFile -Force -ErrorAction SilentlyContinue }

$cfProc = Start-Process $cloudflared -ArgumentList "tunnel --url http://localhost:5229 --logfile `"$logFile`"" -PassThru -WindowStyle Hidden

$tunnelUrl = ""
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Milliseconds 500
    if (Test-Path $logFile) {
        $content = Get-Content $logFile -Raw -ErrorAction SilentlyContinue
        if ($content -match '(https://[a-zA-Z0-9.-]+\.trycloudflare\.com)') {
            $tunnelUrl = $matches[1]
            break
        }
    }
}

Clear-Host
Write-Host "=====================================================================" -ForegroundColor Green
Write-Host "    🎉 CINEMAFLOW DA SAN SANG - TRUY CAP TREN DIEN THOAI NGAY!       " -ForegroundColor Yellow
Write-Host "=====================================================================" -ForegroundColor Green
Write-Host ""
if ($tunnelUrl) {
    Write-Host "👉 DUONG LINK XEM PHIM TREN DIEN THOAI:" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "       $tunnelUrl" -ForegroundColor White -BackgroundColor DarkGreen
    Write-Host ""
    
    # Luu ra Desktop
    $desktopFile = "$env:USERPROFILE\Desktop\LINK_XEM_PHIM.txt"
    $txtContent = @"
=====================================================================
CINEMAFLOW - DUONG LINK XEM PHIM TREN DIEN THOAI (CLOUDFLARE)
=====================================================================
Link xem phim:
$tunnelUrl

* Uu diem:
- Tu dong ket noi Cloudflare Edge sieu toc tai Viet Nam.
- Hoan toan KHONG co man hinh canh bao 'Visit Site' nhu ngrok.
- Video phat muot ma Full HD / 4K.
=====================================================================
"@
    Set-Content -Path $desktopFile -Value $txtContent -Encoding UTF8
    Write-Host "✓ Da luu duong link vao Desktop: LINK_XEM_PHIM.txt" -ForegroundColor Gray

    # Tao va mo ma QR de quet bang camera dien thoai
    $qrPath = "$env:TEMP\cf_qrcode.png"
    try {
        curl.exe -s "https://api.qrserver.com/v1/create-qr-code/?size=350x350&data=$tunnelUrl" -o $qrPath
        if (Test-Path $qrPath) {
            Write-Host "✓ Dang mo ma QR Code tren man hinh (quet camera la vao xem phim ngay)..." -ForegroundColor Green
            Start-Process $qrPath
        }
    } catch {}

} else {
    Write-Host "Chua bat duoc URL tu dong. Vui long kiem tra file log: $logFile" -ForegroundColor Red
}

Write-Host ""
Write-Host "=====================================================================" -ForegroundColor Cyan
Write-Host "LUU Y: Giu nguyen cua so nay trong khi xem phim tren dien thoai." -ForegroundColor Yellow
Write-Host "Khi nao muon tat toan bo he thong, chi can nhan Enter." -ForegroundColor Gray
Write-Host "=====================================================================" -ForegroundColor Cyan
Write-Host ""

Read-Host "Nhan Enter de dung toan bo he thong..."

Stop-Process -Id $serverProc.Id -Force -ErrorAction SilentlyContinue
Stop-Process -Id $clientProc.Id -Force -ErrorAction SilentlyContinue
if ($cfProc) { Stop-Process -Id $cfProc.Id -Force -ErrorAction SilentlyContinue }
Get-Process -Name "cloudflared" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Write-Host "Da tat toan bo he thong CinemaFlow an toan." -ForegroundColor Yellow
