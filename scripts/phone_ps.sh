#!/data/data/com.termux/files/usr/bin/bash
export PATH="/data/data/com.termux/files/usr/bin:$PATH"
proot-distro login ubuntu -- bash -c 'ps aux; fuser 5229/tcp 5085/tcp 8080/tcp 2>/dev/null || true'