#!/data/data/com.termux/files/usr/bin/bash
export PATH="/data/data/com.termux/files/usr/bin:$PATH"
proot-distro login ubuntu -- mariadb -u webxemphim -pwebxemphim webxemphim -e "SELECT EventType, Success, Detail, CreatedAtUtc FROM security_audit_events ORDER BY CreatedAtUtc DESC LIMIT 10;"