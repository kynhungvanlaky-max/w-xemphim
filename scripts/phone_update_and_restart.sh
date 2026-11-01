#!/data/data/com.termux/files/usr/bin/bash
export PATH="/data/data/com.termux/files/usr/bin:$PATH"
proot-distro login ubuntu -- bash -c '
cp -f /sdcard/WebXemPhim.Client.dll /root/app/client/WebXemPhim.Client.dll
cp -f /sdcard/WebXemPhim.Server.dll /root/app/server/WebXemPhim.Server.dll
cp -f /sdcard/WebXemPhim.Shared.dll /root/app/server/WebXemPhim.Shared.dll
cp -f /sdcard/WebXemPhim.Shared.dll /root/app/client/WebXemPhim.Shared.dll

killall -9 WebXemPhim.Client 2>/dev/null || true
killall -9 WebXemPhim.Server 2>/dev/null || true
sleep 1

export ASPNETCORE_ENVIRONMENT=Development
export DOTNET_ENVIRONMENT=Development
export AllowedHosts="*"
export Database__Provider=MySql
export Database__SeedDemoData=true
export ConnectionStrings__Default="Server=127.0.0.1;Port=3306;Database=webxemphim;User=webxemphim;Password=webxemphim;"
export Security__DataProtectionKeyPath="/root/keys"
export Security__StreamSigningKey="***REMOVED***"
export Payment__WebhookSecret="***REMOVED***"
export ApiBaseUrl="http://127.0.0.1:5085"
export Storage__MediaDirectory="/root/data/movies"

cd /root/app/server
nohup ./WebXemPhim.Server --urls "http://127.0.0.1:5085" --environment Development > /sdcard/server.log 2>&1 &
echo "Server restarted: $!"

sleep 2

cd /root/app/client
nohup ./WebXemPhim.Client --urls "http://127.0.0.1:5229" --environment Development > /sdcard/client.log 2>&1 &
echo "Client restarted: $!"
'