#!/usr/bin/env bash
set -e
cp -f /sdcard/WebXemPhim.Server.dll /root/app/server/WebXemPhim.Server.dll
cp -f /sdcard/WebXemPhim.Shared.dll /root/app/server/WebXemPhim.Shared.dll
cp -f /sdcard/WebXemPhim.Shared.dll /root/app/client/WebXemPhim.Shared.dll

cat << 'EOF' > /root/app/Caddyfile
:8080 {
    encode zstd gzip

    handle /hubs/* {
        reverse_proxy 127.0.0.1:5085
    }
    handle /api/* {
        reverse_proxy 127.0.0.1:5085
    }
    handle /openapi/* {
        reverse_proxy 127.0.0.1:5085
    }
    handle_path /media/* {
        root * /root/data/movies
        file_server
    }
    handle {
        reverse_proxy 127.0.0.1:5229
    }
}
EOF

killall -9 WebXemPhim.Server 2>/dev/null || true
killall -9 caddy 2>/dev/null || true
sleep 1

export ASPNETCORE_ENVIRONMENT=Development
export DOTNET_ENVIRONMENT=Development
export AllowedHosts="*"
export Database__Provider=MySql
export Database__SeedDemoData=false
export Storage__MediaDirectory="/root/data/movies"
export ConnectionStrings__Default="Server=127.0.0.1;Port=3306;Database=webxemphim;User=webxemphim;Password=webxemphim;"
export Security__DataProtectionKeyPath="/root/keys"
export Security__StreamSigningKey="CinemaFlow_Phone_Stream_Signing_Key_2026_AtLeast32Bytes!"
export Payment__WebhookSecret="CinemaFlow_Phone_Payment_Webhook_Secret_2026_AtLeast32Bytes!"
export ApiBaseUrl="http://127.0.0.1:5085"

cd /root/app/server
nohup ./WebXemPhim.Server --urls "http://127.0.0.1:5085" --environment Development > /sdcard/server.log 2>&1 &
echo "Server restarted"

sleep 2
caddy start --config /root/app/Caddyfile > /sdcard/caddy.log 2>&1
echo "Caddy restarted"
