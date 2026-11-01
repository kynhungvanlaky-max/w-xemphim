#!/usr/bin/env bash
killall -9 ngrok 2>/dev/null || true
sleep 1

# Đảm bảo ngrok ARM64 đã được cài đặt
if [ ! -f "/usr/local/bin/ngrok" ]; then
    echo "Đang tải ngrok Linux ARM64..."
    wget -q https://bin.equinox.io/c/bNyj1mQVY4c/ngrok-v3-stable-linux-arm64.tgz -O /tmp/ngrok.tgz
    tar -xzf /tmp/ngrok.tgz -C /usr/local/bin/
    rm -f /tmp/ngrok.tgz
    chmod +x /usr/local/bin/ngrok
fi

if [ -f "/sdcard/ngrok_token.txt" ]; then
    NGROK_TOKEN=$(cat /sdcard/ngrok_token.txt | tr -d '\r\n ')
    if [ -n "$NGROK_TOKEN" ]; then
        ngrok config add-authtoken "$NGROK_TOKEN" >/dev/null 2>&1 || true
    fi
fi

rm -f /sdcard/ngrok.log /sdcard/tunnel_url.txt
nohup ngrok http 8080 --log=stdout > /sdcard/ngrok.log 2>&1 &
echo "Đang đợi ngrok tạo URL..."

for i in {1..35}; do
    URL=$(curl -s http://127.0.0.1:4040/api/tunnels 2>/dev/null | jq -r '.tunnels[]? | select(.proto=="https") | .public_url' | head -n 1 || true)
    if [ -z "$URL" ]; then
        URL=$(grep -o 'https://[a-zA-Z0-9.-]*\.ngrok-free\.app' /sdcard/ngrok.log 2>/dev/null | head -n 1 || true)
    fi
    if [ -n "$URL" ]; then
        echo "NEW_TUNNEL_URL:$URL"
        echo "$URL" > /sdcard/tunnel_url.txt
        break
    fi
    sleep 1
done
