#!/data/data/com.termux/files/usr/bin/bash
export PATH="/data/data/com.termux/files/usr/bin:$PATH"

proot-distro login ubuntu -- bash -c '
echo "=== PS AUX ==="
ps aux | grep -E "WebXemPhim|caddy|ngrok|cloudflared|mariadb" || true
'
