#!/data/data/com.termux/files/usr/bin/bash
export PATH="/data/data/com.termux/files/usr/bin:$PATH"
proot-distro login ubuntu -- bash -c '
/etc/init.d/mariadb restart
sleep 2
mariadb-admin ping
'