@echo off
chcp 65001 >nul
title CinemaFlow - Khoi tao ngrok Tunnel cho Dien Thoai
echo =====================================================================
echo       CINEMAFLOW - KHOI TAO NGROK TUNNEL TRUY CAP TU DIEN THOAI
echo =====================================================================
echo.

set "NGROK_DIR=%LOCALAPPDATA%\ngrok"
set "NGROK_EXE=%NGROK_DIR%\ngrok.exe"

:: 1. Kiem tra va tai ngrok.exe neu chua co
if not exist "%NGROK_EXE%" (
    where ngrok >nul 2>&1
    if %errorlevel% neq 0 (
        echo [1/3] Khong tim thay ngrok tren may. Dang tu dong tai ngrok Windows 64-bit...
        if not exist "%NGROK_DIR%" mkdir "%NGROK_DIR%"
        powershell -NoProfile -Command "Invoke-WebRequest -Uri 'https://bin.equinox.io/c/bNyj1mQVY4c/ngrok-v3-stable-windows-amd64.zip' -OutFile '%TEMP%\ngrok.zip'; Expand-Archive -Path '%TEMP%\ngrok.zip' -DestinationPath '%NGROK_DIR%' -Force; Remove-Item '%TEMP%\ngrok.zip' -Force"
        if not exist "%NGROK_EXE%" (
            echo [LOI] Khong the tai ngrok. Vui long kiem tra ket noi mang.
            pause
            exit /b 1
        )
        echo [1/3] Da cai dat ngrok thanh cong vao %NGROK_DIR%!
    ) else (
        for /f "delims=" %%i in ('where ngrok') do set "NGROK_EXE=%%i"
    )
)

:: 2. Kiem tra Authtoken
"%NGROK_EXE%" config check >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo [2/3] ngrok yeu cau Authtoken de khoi chay.
    echo       (Ban co the lay token mien phi tai: https://dashboard.ngrok.com/get-started/your-authtoken)
    echo.
    set /p "USER_TOKEN=Nhap Authtoken cua ban roi an Enter: "
    if not "!USER_TOKEN!"=="" (
        "%NGROK_EXE%" config add-authtoken !USER_TOKEN!
    )
)

:: 3. Lua chon cong can mo
echo.
echo [3/3] Chon cong can tao tunnel ra Internet:
echo       [1] Port 8080 (Caddy Reverse Proxy - Khuyen nghi neu chay full stack)
echo       [2] Port 5229 (WebXemPhim.Client - Giao dien Web Blazor)
echo       [3] Port 5085 (WebXemPhim.Server - API Backend)
echo.
set "PORT=8080"
set /p "CHOICE=Nhap lua chon [1, 2, 3] (Mac dinh 1): "
if "%CHOICE%"=="2" set "PORT=5229"
if "%CHOICE%"=="3" set "PORT=5085"

echo.
echo =====================================================================
echo  Dang mo duong ham ngrok cho cong %PORT%...
echo  Sau khi duong ham hien len, ban copy link HTTPS (hoac quet ma QR)
echo  de mo tren trinh duyet dien thoai!
echo =====================================================================
echo.
"%NGROK_EXE%" http %PORT%
pause
