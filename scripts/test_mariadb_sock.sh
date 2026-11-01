#!/data/data/com.termux/files/usr/bin/bash
export PATH="/data/data/com.termux/files/usr/bin:$PATH"
proot-distro login ubuntu -- bash -c '
mariadb -h localhost -u webxemphim -pwebxemphim webxemphim -e "SELECT count(*) FROM movies;"
'