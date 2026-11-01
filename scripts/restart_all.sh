#!/data/data/com.termux/files/usr/bin/bash
export PATH="/data/data/com.termux/files/usr/bin:$PATH"

killall -9 proot 2>/dev/null || true
pkill -9 -f phone_daemon 2>/dev/null || true
pkill -9 -f WebXemPhim 2>/dev/null || true
pkill -9 -f caddy 2>/dev/null || true
pkill -9 -f ngrok 2>/dev/null || true
pkill -9 -f cloudflared 2>/dev/null || true

sleep 1
exec /data/data/com.termux/files/usr/bin/bash /sdcard/phone_daemon.sh
