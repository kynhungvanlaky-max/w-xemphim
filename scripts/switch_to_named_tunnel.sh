killall -9 cloudflared 2>/dev/null || true
sleep 1
nohup /usr/local/bin/cloudflared tunnel run --token ***REMOVED*** > /sdcard/cloudflared.log 2>&1 &
sleep 3
cat /sdcard/cloudflared.log | tail -n 20
