#!/usr/bin/env bash
cd /root/app/server
export ASPNETCORE_ENVIRONMENT=Development
export Database__Provider=MySql
export Database__SeedDemoData=false
export Storage__MediaDirectory="/root/data/movies"
export ConnectionStrings__Default="Server=127.0.0.1;Port=3306;Database=webxemphim;User=webxemphim;Password=webxemphim;"
export Security__DataProtectionKeyPath="/root/keys"
export Security__StreamSigningKey="***REMOVED***"
./WebXemPhim.Server --urls "http://127.0.0.1:5085"
