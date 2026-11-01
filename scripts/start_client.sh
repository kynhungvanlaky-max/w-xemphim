#!/data/data/com.termux/files/usr/bin/bash
export PATH="/data/data/com.termux/files/usr/bin:$PATH"

proot-distro login ubuntu -- bash -c '
echo "Updating client files..."
cp -f /sdcard/WebXemPhim.Client.dll /root/app/client/WebXemPhim.Client.dll 2>/dev/null || true
if [ -d "/sdcard/client_wwwroot" ]; then
    cp -rf /sdcard/client_wwwroot/* /root/app/client/wwwroot/ 2>/dev/null || true
fi
for f in /sdcard/*.staticwebassets.endpoints.json; do
    if [ -f "$f" ]; then
        cp -f "$f" /root/app/client/ 2>/dev/null || true
    fi
done

export ASPNETCORE_ENVIRONMENT=Development
export DOTNET_ENVIRONMENT=Development
export AllowedHosts="*"
export ApiBaseUrl="http://127.0.0.1:5085"
export Security__BehindReverseProxy=true

cd /root/app/client
nohup ./WebXemPhim.Client --urls "http://127.0.0.1:5229" --environment Development > /sdcard/client.log 2>&1 &
echo "Started client PID: $!"
sleep 3
curl -s -I http://127.0.0.1:5229/ || true
'
