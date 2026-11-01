#!/data/data/com.termux/files/usr/bin/bash
export PATH="/data/data/com.termux/files/usr/bin:$PATH"
proot-distro login ubuntu -- mariadb -u webxemphim -pwebxemphim -e "SELECT Id, FullName, Email, NormalizedEmail FROM webxemphim.users;"
