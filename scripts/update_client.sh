#!/data/data/com.termux/files/usr/bin/bash
export PATH="/data/data/com.termux/files/usr/bin:$PATH"

proot-distro login ubuntu -- bash -c '
set -e
echo "Updating client files..."
if [ -f "/sdcard/WebXemPhim.Client.dll" ]; then
    cp -f /sdcard/WebXemPhim.Client.dll /root/app/client/WebXemPhim.Client.dll
fi
if [ -d "/sdcard/client_wwwroot" ]; then
    mkdir -p /root/app/client/wwwroot
    cp -rf /sdcard/client_wwwroot/* /root/app/client/wwwroot/ 2>/dev/null || true
fi
for f in /sdcard/*.staticwebassets.endpoints.json; do
    if [ -f "$f" ]; then
        cp -f "$f" /root/app/client/ 2>/dev/null || true
    fi
done

echo "Restarting WebXemPhim.Client..."
killall -9 WebXemPhim.Client 2>/dev/null || true
sleep 1

export ASPNETCORE_ENVIRONMENT=Development
export DOTNET_ENVIRONMENT=Development
export AllowedHosts="*"
export ApiBaseUrl="http://127.0.0.1:5085"
export Security__BehindReverseProxy=true

cd /root/app/client
nohup ./WebXemPhim.Client --urls "http://127.0.0.1:5229" --environment Development > /sdcard/client.log 2>&1 &
sleep 2

for i in {1..15}; do
    if curl -s http://127.0.0.1:5229 >/dev/null 2>&1; then
        echo "Client is UP and responding on 5229!"
        break
    fi
    sleep 1
done

echo "Client update complete."
'
