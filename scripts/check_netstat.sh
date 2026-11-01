#!/data/data/com.termux/files/usr/bin/bash
export PATH="/data/data/com.termux/files/usr/bin:$PATH"
proot-distro login ubuntu -- bash -c '
netstat -tlpn 2>/dev/null || ss -tlpn 2>/dev/null || lsof -i :3306 2>/dev/null || true
cat /etc/mysql/mariadb.conf.d/50-server.cnf 2>/dev/null | grep bind-address || true
'