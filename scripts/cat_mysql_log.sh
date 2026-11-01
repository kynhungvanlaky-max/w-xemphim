#!/data/data/com.termux/files/usr/bin/bash
export PATH="/data/data/com.termux/files/usr/bin:$PATH"
proot-distro login ubuntu -- bash -c "
/etc/init.d/mariadb status
/etc/init.d/mariadb start
sleep 2
/etc/init.d/mariadb status
mariadb-admin ping
"
