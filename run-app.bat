@echo off
title WebXemPhim Launcher
echo ==================================================
echo   Khoi chay WebXemPhim (Server va Client)
echo ==================================================

echo [1/2] Dang khoi dong Server API tai http://localhost:5085...
start "WebXemPhim - Server (5085)" cmd /k "cd /d %~dp0 && dotnet run --project src/Server/WebXemPhim.Server/WebXemPhim.Server.csproj --no-build --launch-profile http"

ping -n 3 127.0.0.1 >nul

echo [2/2] Dang khoi dong Client Blazor tai http://localhost:5229...
start "WebXemPhim - Client (5229)" cmd /k "cd /d %~dp0 && dotnet run --project src/Client/WebXemPhim.Client/WebXemPhim.Client.csproj --no-build --launch-profile http"

echo.
echo ==================================================
echo   Server API: http://localhost:5085
echo   Client Web: http://localhost:5229
echo ==================================================
