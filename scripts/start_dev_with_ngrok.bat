@echo off
setlocal enabledelayedexpansion
chcp 65001 >nul
title CinemaFlow - Khoi chay Web & ngrok cho Dien Thoai
echo =====================================================================
echo       CINEMAFLOW - KHOI CHAY HE THONG & NGROK CHO DIEN THOAI
echo =====================================================================
echo.

set "PROJECT_ROOT=%~dp0.."
if not exist "%PROJECT_ROOT%\WebXemPhim.slnx" (
    set "PROJECT_ROOT=C:\Users\kynhu\Documents\Projects\wenPhim\w-xemphim"
)
:: ngrok >= 3.28 bat buoc phai cap nhat (ban cu bi tu choi voi ERR_NGROK_121).
set "NGROK_EXE=%LOCALAPPDATA%\ngrok\bin\ngrok.exe"
if not exist "%NGROK_EXE%" set "NGROK_EXE=%LOCALAPPDATA%\ngrok\ngrok.exe"
if not exist "%NGROK_EXE%" set "NGROK_EXE=%PROJECT_ROOT%\tools\ngrok.exe"
if not exist "%NGROK_EXE%" set "NGROK_EXE=ngrok"

:: 1. Kiem tra Authtoken
"%NGROK_EXE%" config check >nul 2>&1
if %errorlevel% neq 0 (
    echo [THONG BAO] ngrok can Authtoken de khoi tao duong ham ra Internet.
    echo           (Lay token mien phi tai: https://dashboard.ngrok.com/get-started/your-authtoken)
    echo.
    set /p "TOKEN=Dan ma Authtoken cua ban vao day roi an Enter: "
    if not "!TOKEN!"=="" (
        "%NGROK_EXE%" config add-authtoken !TOKEN!
    )
)

echo.
echo [1/3] Dang khoi dong WebXemPhim.Server (API Backend - Port 5085)...
start "CinemaFlow - Server (Port 5085)" cmd /k "cd /d "%PROJECT_ROOT%\src\Server\WebXemPhim.Server" && dotnet run --launch-profile http"

timeout /t 3 /nobreak >nul

echo [2/3] Dang khoi dong WebXemPhim.Client (Blazor Web & Gateway - Port 5229)...
:: AllowedHosts=* de chap nhan ten mien ngrok (vi du xxx.ngrok-free.dev) o moi truong Development.
start "CinemaFlow - Client (Port 5229)" cmd /k "cd /d "%PROJECT_ROOT%\src\Client\WebXemPhim.Client" && set AllowedHosts=* && dotnet run --launch-profile http"

timeout /t 4 /nobreak >nul

echo [3/3] Dang khoi tao duong ham ngrok HTTPS cho Web (Port 5229)...
echo.
echo =====================================================================
echo   Sau khi ngrok ket noi thanh cong:
echo   1. Copy duong link HTTPS (vi du: https://xxxx.ngrok-free.app)
echo      hoac quet ma QR de mo ngay tren trinh duyet dien thoai.
echo.
echo   Luu y lan dau tren dien thoai:
echo   Neu ngrok hien thi man hinh xanh "You are about to visit...",
echo   ban chi can nhan nut "Visit Site" mot lan de vao web xem phim!
echo =====================================================================
echo.
"%NGROK_EXE%" http 5229
pause
