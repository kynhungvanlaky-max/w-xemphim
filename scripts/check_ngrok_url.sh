#!/bin/bash
/usr/local/bin/ngrok http 8080 --log=stdout --log-level=info > /sdcard/ngrok_test.log 2>&1 &
sleep 5
killall ngrok 2>/dev/null || true
cat /sdcard/ngrok_test.log
