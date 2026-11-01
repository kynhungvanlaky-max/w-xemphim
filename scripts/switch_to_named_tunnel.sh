killall -9 cloudflared 2>/dev/null || true
sleep 1
nohup /usr/local/bin/cloudflared tunnel run --token eyJhIjoiZjAwNzc1ZTc5NjY4OGVmZWQyMjVkYjAwODE1ZjdiODciLCJ0IjoiMzc5NzE2M2QtOGY3NS00MjQzLThkNmQtZGJmMGJjNzEzNGI4IiwicyI6Ik16azVPR1psWm1RdE1UUXdNUzAwWVdNekxXRmhORFl0WW1VMVlXVTJOekprTW1SayJ9 > /sdcard/cloudflared.log 2>&1 &
sleep 3
cat /sdcard/cloudflared.log | tail -n 20
