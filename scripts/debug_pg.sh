#!/usr/bin/env bash
/etc/init.d/mariadb start

export ASPNETCORE_ENVIRONMENT=Development
export DOTNET_ENVIRONMENT=Development
export AllowedHosts="*"
export Database__Provider=MySql
export Database__SeedDemoData=true
export ConnectionStrings__Default="Server=127.0.0.1;Port=3306;Database=webxemphim;User=webxemphim;Password=webxemphim;"
export Security__DataProtectionKeyPath="/root/keys"
export Security__StreamSigningKey="CinemaFlow_Phone_Stream_Signing_Key_2026_AtLeast32Bytes!"
export Payment__WebhookSecret="CinemaFlow_Phone_Payment_Webhook_Secret_2026_AtLeast32Bytes!"
export ApiBaseUrl="http://127.0.0.1:5085"

cd /root/app/server
./WebXemPhim.Server --urls "http://127.0.0.1:5085" --environment Development
