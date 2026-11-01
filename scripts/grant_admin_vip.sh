#!/data/data/com.termux/files/usr/bin/bash
export PATH="/data/data/com.termux/files/usr/bin:$PATH"
proot-distro login ubuntu -- mariadb -u webxemphim -pwebxemphim webxemphim -e "
UPDATE users SET Membership = 1, VipExpiresAtUtc = DATE_ADD(NOW(), INTERVAL 1 YEAR) WHERE NormalizedEmail IN ('admin@cinemaflow.vn', 'demo@cinemaflow.vn');
UPDATE user_security SET IsAdmin = 1 WHERE UserId IN (SELECT Id FROM users WHERE NormalizedEmail = 'admin@cinemaflow.vn');
SELECT u.NormalizedEmail, u.Membership, u.VipExpiresAtUtc, s.IsAdmin FROM users u LEFT JOIN user_security s ON u.Id = s.UserId WHERE u.NormalizedEmail IN ('admin@cinemaflow.vn', 'demo@cinemaflow.vn');
"