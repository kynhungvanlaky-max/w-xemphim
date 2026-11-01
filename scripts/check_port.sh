#!/data/data/com.termux/files/usr/bin/bash
export PATH="/data/data/com.termux/files/usr/bin:$PATH"
proot-distro login ubuntu -- bash -c '
mariadb-admin -u webxemphim -pwebxemphim variables | grep -E "\bport\b|\bsocket\b|\bskip_networking\b"
'