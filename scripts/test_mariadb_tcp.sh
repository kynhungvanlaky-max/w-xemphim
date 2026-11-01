#!/data/data/com.termux/files/usr/bin/bash
export PATH="/data/data/com.termux/files/usr/bin:$PATH"
proot-distro login ubuntu -- bash -c '
mariadb -h 127.0.0.1 -P 3306 -u webxemphim -pwebxemphim webxemphim -e "SELECT count(*) FROM movies;"
'