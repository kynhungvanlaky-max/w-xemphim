#!/data/data/com.termux/files/usr/bin/bash
export PATH="/data/data/com.termux/files/usr/bin:$PATH"
proot-distro login ubuntu -- bash -c '
grep -E "port|bind-address|skip-networking" /etc/mysql/mariadb.conf.d/*.cnf /etc/mysql/*.cnf 2>/dev/null || true
'