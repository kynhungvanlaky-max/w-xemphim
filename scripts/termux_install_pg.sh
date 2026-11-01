#!/data/data/com.termux/files/usr/bin/bash
export PATH="/data/data/com.termux/files/usr/bin:$PATH"
pkg update -y
pkg install -y postgresql
which postgres
which initdb
