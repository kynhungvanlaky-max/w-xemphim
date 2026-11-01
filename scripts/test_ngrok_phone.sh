#!/bin/bash
set -e
echo "Downloading ngrok Linux ARM64..."
curl -sSL https://bin.equinox.io/c/bNyj1mQVY4c/ngrok-v3-stable-linux-arm64.tgz -o /tmp/ngrok.tgz
tar -xzf /tmp/ngrok.tgz -C /usr/local/bin/
rm -f /tmp/ngrok.tgz
chmod +x /usr/local/bin/ngrok
echo "Ngrok version:"
/usr/local/bin/ngrok version
/usr/local/bin/ngrok config add-authtoken 3LNTkKDrQTo2SZnCOrMKDeh8x51_76XPN2jRKbAqTTmNEm5JB
echo "Token added successfully!"
