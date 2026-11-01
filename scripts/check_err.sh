#!/data/data/com.termux/files/usr/bin/bash
export PATH="/data/data/com.termux/files/usr/bin:$PATH"
proot-distro login ubuntu -- bash -c '
cat /var/lib/mariadb/*.err 2>/dev/null | tail -n 35 || ls -la /var/lib/mariadb/
'