#!/data/data/com.termux/files/usr/bin/bash
export PATH="/data/data/com.termux/files/usr/bin:$PATH"
proot-distro login ubuntu -- bash -c '
killall -9 mariadbd mariadbd-safe 2>/dev/null || true
sleep 1
nohup /usr/bin/mariadbd-safe --user=root > /sdcard/mariadb.log 2>&1 &
for i in {1..20}; do
    if mariadb-admin ping >/dev/null 2>&1; then
        echo "MariaDB is alive!"
        break
    fi
    sleep 1
done
'