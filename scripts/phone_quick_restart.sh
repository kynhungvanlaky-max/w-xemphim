#!/data/data/com.termux/files/usr/bin/bash
export PATH="/data/data/com.termux/files/usr/bin:$PATH"
proot-distro login ubuntu -- bash -c '
cp -f /sdcard/WebXemPhim.Server.dll /root/app/server/WebXemPhim.Server.dll
killall -9 WebXemPhim.Server 2>/dev/null || true
sleep 1
cd /root/app/server
nohup ./WebXemPhim.Server --urls "http://127.0.0.1:5085" --environment Development > /sdcard/server.log 2>&1 &
echo "Server restarted: $!"
'