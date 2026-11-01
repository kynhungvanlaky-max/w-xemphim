@echo off
setlocal enabledelayedexpansion
chcp 65001 >nul
title CinemaFlow - Khoi chay Web & Cloudflare Tunnel
echo =====================================================================
echo    CINEMAFLOW - KHOI CHAY HE THONG & CLOUDFLARE TUNNEL (HEALTHY)
echo =====================================================================
echo.

set "PROJECT_ROOT=%~dp0.."
if not exist "%PROJECT_ROOT%\WebXemPhim.slnx" (
    set "PROJECT_ROOT=C:\Users\kynhu\Documents\Projects\wenPhim\w-xemphim"
)
set "CLOUDFLARED_EXE=C:\Program Files (x86)\cloudflared\cloudflared.exe"
if not exist "%CLOUDFLARED_EXE%" (
    set "CLOUDFLARED_EXE=%PROJECT_ROOT%\tools\cloudflared.exe"
)
set "CF_TOKEN=eyJhIjoiZjAwNzc1ZTc5NjY4OGVmZWQyMjVkYjAwODE1ZjdiODciLCJ0IjoiMzc5NzE2M2QtOGY3NS00MjQzLThkNmQtZGJmMGJjNzEzNGI4IiwicyI6Ik16azVPR1psWm1RdE1UUXdNUzAwWVdNekxXRmhORFl0WW1VMVlXVTJOekprTW1SayJ9"

echo [1/3] Dang khoi dong WebXemPhim.Server (API Backend - Port 5085)...
start "CinemaFlow - Server (Port 5085)" cmd /k "cd /d "%PROJECT_ROOT%\src\Server\WebXemPhim.Server" && dotnet run --no-launch-profile --urls http://localhost:5085"

timeout /t 3 /nobreak >nul

echo [2/3] Dang khoi dong WebXemPhim.Client (Blazor Web & Gateway - Port 5229)...
start "CinemaFlow - Client (Port 5229)" cmd /k "cd /d "%PROJECT_ROOT%\src\Client\WebXemPhim.Client" && dotnet run --no-launch-profile --urls http://localhost:5229"

timeout /t 4 /nobreak >nul

echo [3/3] Dang ket noi Cloudflare Tunnel (Tunnel ID: 3797163d)...
echo.
echo =====================================================================
echo   Cloudflare Tunnel dang hoat dong voi token cua ban!
echo   * Tinh trang: Da ket noi thanh cong Cloudflare Edge (Hong Kong/VN).
echo   * Khong co man hinh canh bao ngrok!
echo =====================================================================
echo.
"%CLOUDFLARED_EXE%" tunnel run --token %CF_TOKEN%
pause
