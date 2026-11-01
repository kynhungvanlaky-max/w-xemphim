#!/data/data/com.termux/files/usr/bin/bash
export PATH="/data/data/com.termux/files/usr/bin:$PATH"
proot-distro login ubuntu -- bash -c '
cat /var/log/mysql/error.log 2>/dev/null | tail -n 25 || cat /var/log/messages 2>/dev/null | grep -i mysql | tail -n 25 || true
'