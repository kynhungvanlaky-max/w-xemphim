-- =======================================================
-- WebXemPhim - MySQL 8 Seed Data Script
-- DEVELOPMENT ONLY. Không chạy trên production hoặc DB có dữ liệu thật.
-- Cung cấp tài khoản test, thanh toán và đánh giá phim. Không seed token.
-- =======================================================

USE webxemphim;

-- Tắt kiểm tra khóa ngoại tạm thời để xóa và nạp dữ liệu sạch sẽ
SET FOREIGN_KEY_CHECKS = 0;
TRUNCATE TABLE reviews;
TRUNCATE TABLE payments;
TRUNCATE TABLE password_reset_tokens;
TRUNCATE TABLE access_tokens;
TRUNCATE TABLE users;
SET FOREIGN_KEY_CHECKS = 1;

-- =======================================================
-- 1. USERS (Tất cả tài khoản có mật khẩu: Password123!)
-- Salt: UNHEX('123456789ABCDEF01122334455667788')
-- Hash: UNHEX('33736C1F6FCFCE05F5BA382CDC310FA878FF56442F4A9556A8627BC2B9A581A5')
-- =======================================================
INSERT INTO users (Id, FullName, Email, NormalizedEmail, PasswordSalt, PasswordHash, Membership, VipExpiresAtUtc, CreatedAtUtc) VALUES
('11111111-1111-1111-1111-111111111111', 'Quản Trị Viên', 'admin@cinemaflow.vn', 'admin@cinemaflow.vn', 
 UNHEX('123456789ABCDEF01122334455667788'), UNHEX('33736C1F6FCFCE05F5BA382CDC310FA878FF56442F4A9556A8627BC2B9A581A5'), 
 2, '2027-01-01 00:00:00.000000', '2026-01-01 10:00:00.000000'),

('22222222-2222-2222-2222-222222222222', 'Trần Hoàng Nam', 'hoangnam@cinemaflow.vn', 'hoangnam@cinemaflow.vn', 
 UNHEX('123456789ABCDEF01122334455667788'), UNHEX('33736C1F6FCFCE05F5BA382CDC310FA878FF56442F4A9556A8627BC2B9A581A5'), 
 2, '2026-12-15 00:00:00.000000', '2026-01-15 14:30:00.000000'),

('33333333-3333-3333-3333-333333333333', 'VIP Member', 'vip@cinemaflow.vn', 'vip@cinemaflow.vn', 
 UNHEX('123456789ABCDEF01122334455667788'), UNHEX('33736C1F6FCFCE05F5BA382CDC310FA878FF56442F4A9556A8627BC2B9A581A5'), 
 2, '2026-10-01 00:00:00.000000', '2026-02-01 08:00:00.000000'),

('44444444-4444-4444-4444-444444444444', 'Nguyễn Thùy Linh', 'thuylinh@cinemaflow.vn', 'thuylinh@cinemaflow.vn', 
 UNHEX('123456789ABCDEF01122334455667788'), UNHEX('33736C1F6FCFCE05F5BA382CDC310FA878FF56442F4A9556A8627BC2B9A581A5'), 
 1, NULL, '2026-02-15 19:45:00.000000'),

('55555555-5555-5555-5555-555555555555', 'Lê Bảo Long', 'longle@cinemaflow.vn', 'longle@cinemaflow.vn', 
 UNHEX('123456789ABCDEF01122334455667788'), UNHEX('33736C1F6FCFCE05F5BA382CDC310FA878FF56442F4A9556A8627BC2B9A581A5'), 
 1, NULL, '2026-03-01 11:20:00.000000');

-- =======================================================
-- 2. ACCESS TOKENS: đăng nhập qua API để tạo token ngẫu nhiên được băm.
-- =======================================================

-- =======================================================
-- 3. PAYMENTS (Lịch sử thanh toán gói VIP)
-- Method: 1=BankTransfer, 2=Momo, 3=Visa
-- Status: 1=Pending, 2=Completed, 3=Failed
-- =======================================================
INSERT INTO payments (Id, OrderCode, UserId, PlanId, PlanName, Amount, Method, Status, CreatedAtUtc, PaidAtUtc, VipExpiresAtUtc) VALUES
('a1111111-1111-1111-1111-111111111111', 'CF20260101VIP365ADMIN', '11111111-1111-1111-1111-111111111111', 'vip-365', 'VIP 365 ngày', 499000.00, 1, 2, '2026-01-01 10:05:00.000000', '2026-01-01 10:06:30.000000', '2027-01-01 00:00:00.000000'),
('a2222222-2222-2222-2222-222222222222', 'CF20260215VIP90NAM', '22222222-2222-2222-2222-222222222222', 'vip-90', 'VIP 90 ngày', 149000.00, 2, 2, '2026-01-15 14:35:00.000000', '2026-01-15 14:36:12.000000', '2026-12-15 00:00:00.000000'),
('a3333333-3333-3333-3333-333333333333', 'CF20260301VIP30MEMBER', '33333333-3333-3333-3333-333333333333', 'vip-30', 'VIP 30 ngày', 59000.00, 3, 2, '2026-02-01 08:05:00.000000', '2026-02-01 08:05:45.000000', '2026-10-01 00:00:00.000000');

-- =======================================================
-- 4. REVIEWS (Đánh giá và nhận xét phim chân thực)
-- =======================================================
INSERT INTO reviews (Id, MovieId, UserId, Rating, Comment, CreatedAtUtc, UpdatedAtUtc) VALUES
-- Dune 3 (Movie 1)
('b1111111-1111-1111-1111-111111111111', 1, '11111111-1111-1111-1111-111111111111', 10, 
 'Đỉnh cao điện ảnh khoa học viễn tưởng! Denis Villeneuve đã khép lại thiên anh hùng ca Xứ Cát bằng một kiệt tác hoàn hảo từ âm thanh, kỹ xảo đến chiều sâu tư tưởng.', '2026-03-01 12:00:00.000000', '2026-03-01 12:00:00.000000'),

('b1111111-1111-1111-1111-111111111112', 1, '22222222-2222-2222-2222-222222222222', 9, 
 'Cảnh đại chiến cát sa mạc quá choáng ngợp. Âm nhạc của Hans Zimmer rung chuyển cả phòng chiếu, xứng đáng xem bản 4K HDR.', '2026-03-02 14:15:00.000000', '2026-03-02 14:15:00.000000'),

('b1111111-1111-1111-1111-111111111113', 1, '44444444-4444-4444-4444-444444444444', 9, 
 'Diễn xuất của Timothée Chalamet và Zendaya ngày càng chín muồi. Một cái kết đầy ám ảnh.', '2026-03-04 18:20:00.000000', '2026-03-04 18:20:00.000000'),

-- Oppenheimer (Movie 2)
('b2222222-2222-2222-2222-222222222221', 2, '22222222-2222-2222-2222-222222222222', 10, 
 'Cillian Murphy xứng đáng 10/10 với ánh mắt đầy dằn vặt. Đoạn thử nghiệm Trinity khiến cả phòng nín thở.', '2026-02-20 21:00:00.000000', '2026-02-20 21:00:00.000000'),

('b2222222-2222-2222-2222-222222222222', 2, '55555555-5555-5555-5555-555555555555', 9, 
 'Nolan tiếp tục chứng minh khả năng kể chuyện phi tuyến tính bậc thầy. Thoại và âm nhạc phối hợp không một vết gợn.', '2026-02-25 10:30:00.000000', '2026-02-25 10:30:00.000000'),

-- Arcane 2 (Movie 3)
('b3333333-3333-3333-3333-333333333331', 3, '44444444-4444-4444-4444-444444444444', 10, 
 'Mùa 2 vượt ngoài mọi kỳ vọng! Phần hoạt họa của Fortiche đẹp như tranh vẽ, âm nhạc quá cảm xúc.', '2026-03-05 22:15:00.000000', '2026-03-05 22:15:00.000000'),

('b3333333-3333-3333-3333-333333333332', 3, '33333333-3333-3333-3333-333333333333', 9, 
 'Mối quan hệ giữa Vi và Jinx đau lòng đến nghẹn thở. Phim hoạt hình xuất sắc nhất thập kỷ.', '2026-03-06 17:40:00.000000', '2026-03-06 17:40:00.000000'),

-- The Dark Knight (Movie 4)
('b4444444-4444-4444-4444-444444444441', 4, '11111111-1111-1111-1111-111111111111', 10, 
 'Heath Ledger với vai Joker là huyền thoại sống mãi trong lịch sử điện ảnh thế giới.', '2026-01-20 16:00:00.000000', '2026-01-20 16:00:00.000000'),

-- Solo Leveling 2 (Movie 5)
('b5555555-5555-5555-5555-555555555551', 5, '55555555-5555-5555-5555-555555555555', 9, 
 'Phần 2 hành động mãn nhãn, đội quân bóng đêm trỗi dậy ngầu đét! Đồ họa của A-1 Pictures rất mượt.', '2026-03-07 19:10:00.000000', '2026-03-07 19:10:00.000000'),

-- Shogun (Movie 6)
('b6666666-6666-6666-6666-666666666661', 6, '22222222-2222-2222-2222-222222222222', 9, 
 'Tái hiện văn hóa võ sĩ đạo và mưu lược chính trị thời phong kiến Nhật Bản chuẩn chỉ đến từng chi tiết nhỏ.', '2026-02-18 20:45:00.000000', '2026-02-18 20:45:00.000000'),

-- Parasite (Movie 11)
('b7777777-7777-7777-7777-777777777771', 11, '44444444-4444-4444-4444-444444444444', 10, 
 'Kịch bản xuất sắc không một hạt sạn. Phản ánh chân thực sự phân hóa giàu nghèo sâu sắc.', '2026-01-10 15:30:00.000000', '2026-01-10 15:30:00.000000'),

-- Mai (Movie 13)
('b8888888-8888-8888-8888-888888888881', 13, '33333333-3333-3333-3333-333333333333', 8, 
 'Phương Anh Đào diễn xuất rất có hồn, ánh mắt chất chứa nỗi niềm. Góc quay và ánh sáng tiến bộ rõ rệt.', '2026-03-03 11:20:00.000000', '2026-03-03 11:20:00.000000'),

('b8888888-8888-8888-8888-888888888882', 13, '44444444-4444-4444-4444-444444444444', 9, 
 'Một bộ phim chạm đến cảm xúc của nhiều người phụ nữ hiện đại. Nhạc phim của Phan Mạnh Quỳnh rất hay.', '2026-03-04 14:00:00.000000', '2026-03-04 14:00:00.000000'),

-- Lật Mặt 7 (Movie 14)
('b9999999-9999-9999-9999-999999999991', 14, '22222222-2222-2222-2222-222222222222', 9, 
 'Lấy đi rất nhiều nước mắt. Cảnh bà Hai ngồi chờ các con nơi quê nhà làm ai xa quê cũng phải quặn lòng.', '2026-03-02 21:30:00.000000', '2026-03-02 21:30:00.000000'),

-- Exhuma (Movie 17)
('baaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 17, '11111111-1111-1111-1111-111111111111', 9, 
 'Không khí kinh dị tâm linh rùng rợn, lồng ghép lịch sử tinh tế. Choi Min-sik và Kim Go-eun phối hợp ăn ý.', '2026-02-28 23:00:00.000000', '2026-02-28 23:00:00.000000');
