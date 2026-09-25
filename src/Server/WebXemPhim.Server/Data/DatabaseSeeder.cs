using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Shared.Enums;

namespace WebXemPhim.Server.Data;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(AppDbContext context, TimeProvider timeProvider)
    {
        // Chỉ seed khi bảng Users chưa có dữ liệu
        if (await context.Users.AnyAsync())
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // 1. Tạo Users mẫu với mật khẩu "Password123!"
        var (saltAdmin, hashAdmin) = HashPassword("Password123!");
        var adminUser = new UserEntity
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            FullName = "Quản Trị Viên",
            Email = "admin@cinemaflow.vn",
            NormalizedEmail = "admin@cinemaflow.vn",
            PasswordSalt = saltAdmin,
            PasswordHash = hashAdmin,
            Membership = MembershipTier.Vip,
            VipExpiresAtUtc = now.AddYears(1),
            CreatedAtUtc = now.AddMonths(-6)
        };

        var (saltVip1, hashVip1) = HashPassword("Password123!");
        var vipUser1 = new UserEntity
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            FullName = "Trần Hoàng Nam",
            Email = "hoangnam@cinemaflow.vn",
            NormalizedEmail = "hoangnam@cinemaflow.vn",
            PasswordSalt = saltVip1,
            PasswordHash = hashVip1,
            Membership = MembershipTier.Vip,
            VipExpiresAtUtc = now.AddDays(90),
            CreatedAtUtc = now.AddMonths(-3)
        };

        var (saltVip2, hashVip2) = HashPassword("Password123!");
        var vipUser2 = new UserEntity
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            FullName = "VIP Member",
            Email = "vip@cinemaflow.vn",
            NormalizedEmail = "vip@cinemaflow.vn",
            PasswordSalt = saltVip2,
            PasswordHash = hashVip2,
            Membership = MembershipTier.Vip,
            VipExpiresAtUtc = now.AddDays(30),
            CreatedAtUtc = now.AddDays(-15)
        };

        var (saltUser1, hashUser1) = HashPassword("Password123!");
        var regularUser1 = new UserEntity
        {
            Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            FullName = "Nguyễn Thùy Linh",
            Email = "thuylinh@cinemaflow.vn",
            NormalizedEmail = "thuylinh@cinemaflow.vn",
            PasswordSalt = saltUser1,
            PasswordHash = hashUser1,
            Membership = MembershipTier.Free,
            CreatedAtUtc = now.AddMonths(-1)
        };

        var (saltUser2, hashUser2) = HashPassword("Password123!");
        var regularUser2 = new UserEntity
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
            FullName = "Lê Bảo Long",
            Email = "longle@cinemaflow.vn",
            NormalizedEmail = "longle@cinemaflow.vn",
            PasswordSalt = saltUser2,
            PasswordHash = hashUser2,
            Membership = MembershipTier.Free,
            CreatedAtUtc = now.AddDays(-20)
        };

        context.Users.AddRange(adminUser, vipUser1, vipUser2, regularUser1, regularUser2);

        // Demo roles are explicit; never seed predictable session tokens.
        context.UserSecurity.Add(new UserSecurityEntity { UserId = adminUser.Id, IsAdmin = true });

        // 3. Lịch sử thanh toán gói VIP
        context.Payments.AddRange(
            new PaymentEntity
            {
                Id = Guid.NewGuid(),
                OrderCode = "CF20260101VIP365ADMIN",
                UserId = adminUser.Id,
                PlanId = "vip-365",
                PlanName = "VIP 365 ngày",
                Amount = 499000m,
                Method = PaymentMethod.BankTransfer,
                Status = PaymentStatus.Completed,
                CreatedAtUtc = now.AddMonths(-6),
                PaidAtUtc = now.AddMonths(-6),
                VipExpiresAtUtc = adminUser.VipExpiresAtUtc
            },
            new PaymentEntity
            {
                Id = Guid.NewGuid(),
                OrderCode = "CF20260215VIP90NAM",
                UserId = vipUser1.Id,
                PlanId = "vip-90",
                PlanName = "VIP 90 ngày",
                Amount = 149000m,
                Method = PaymentMethod.Momo,
                Status = PaymentStatus.Completed,
                CreatedAtUtc = now.AddDays(-3),
                PaidAtUtc = now.AddDays(-3),
                VipExpiresAtUtc = vipUser1.VipExpiresAtUtc
            },
            new PaymentEntity
            {
                Id = Guid.NewGuid(),
                OrderCode = "CF20260301VIP30MEMBER",
                UserId = vipUser2.Id,
                PlanId = "vip-30",
                PlanName = "VIP 30 ngày",
                Amount = 59000m,
                Method = PaymentMethod.Visa,
                Status = PaymentStatus.Completed,
                CreatedAtUtc = now.AddDays(-15),
                PaidAtUtc = now.AddDays(-15),
                VipExpiresAtUtc = vipUser2.VipExpiresAtUtc
            },
            new PaymentEntity
            {
                Id = Guid.NewGuid(),
                OrderCode = "CF20260320VIP180PENDING",
                UserId = vipUser1.Id,
                PlanId = "vip-180",
                PlanName = "VIP 6 Tháng",
                Amount = 349000m,
                Method = PaymentMethod.BankTransfer,
                Status = PaymentStatus.Pending,
                CreatedAtUtc = now.AddHours(-2),
                TransactionReference = "FT2609081239MB",
                Note = "Chuyển khoản VietQR chờ đối soát sao kê ngân hàng"
            },
            new PaymentEntity
            {
                Id = Guid.NewGuid(),
                OrderCode = "CF20260318VIP30FAILED",
                UserId = vipUser2.Id,
                PlanId = "vip-30",
                PlanName = "VIP 1 Tháng",
                Amount = 79000m,
                Method = PaymentMethod.Momo,
                Status = PaymentStatus.Failed,
                CreatedAtUtc = now.AddDays(-1),
                Note = "Từ chối duyệt: Giao dịch MoMo bị hủy hoặc quá hạn thanh toán"
            }
        );

        // 4. Đánh giá & bình luận phim (Reviews)
        context.Reviews.AddRange(
            // Phim 1: Dune 3
            new ReviewEntity
            {
                Id = Guid.NewGuid(),
                MovieId = 1,
                UserId = adminUser.Id,
                Rating = 10,
                Comment = "Đỉnh cao điện ảnh khoa học viễn tưởng! Denis Villeneuve đã khép lại thiên anh hùng ca Xứ Cát bằng một kiệt tác hoàn hảo từ âm thanh, kỹ xảo đến chiều sâu tư tưởng.",
                CreatedAtUtc = now.AddDays(-10),
                UpdatedAtUtc = now.AddDays(-10)
            },
            new ReviewEntity
            {
                Id = Guid.NewGuid(),
                MovieId = 1,
                UserId = vipUser1.Id,
                Rating = 9,
                Comment = "Cảnh đại chiến cát sa mạc quá choáng ngợp. Âm nhạc của Hans Zimmer rung chuyển cả phòng chiếu, xứng đáng xem bản 4K HDR.",
                CreatedAtUtc = now.AddDays(-8),
                UpdatedAtUtc = now.AddDays(-8)
            },
            new ReviewEntity
            {
                Id = Guid.NewGuid(),
                MovieId = 1,
                UserId = regularUser1.Id,
                Rating = 9,
                Comment = "Diễn xuất của Timothée Chalamet và Zendaya ngày càng chín muồi. Một cái kết đầy ám ảnh.",
                CreatedAtUtc = now.AddDays(-5),
                UpdatedAtUtc = now.AddDays(-5)
            },

            // Phim 2: Oppenheimer
            new ReviewEntity
            {
                Id = Guid.NewGuid(),
                MovieId = 2,
                UserId = vipUser1.Id,
                Rating = 10,
                Comment = "Cillian Murphy xứng đáng 10/10 với ánh mắt đầy dằn vặt. Đoạn thử nghiệm Trinity khiến cả phòng nín thở.",
                CreatedAtUtc = now.AddDays(-12),
                UpdatedAtUtc = now.AddDays(-12)
            },
            new ReviewEntity
            {
                Id = Guid.NewGuid(),
                MovieId = 2,
                UserId = regularUser2.Id,
                Rating = 9,
                Comment = "Nolan tiếp tục chứng minh khả năng kể chuyện phi tuyến tính bậc thầy. Thoại và âm nhạc phối hợp không một vết gợn.",
                CreatedAtUtc = now.AddDays(-9),
                UpdatedAtUtc = now.AddDays(-9)
            },

            // Phim 3: Arcane 2
            new ReviewEntity
            {
                Id = Guid.NewGuid(),
                MovieId = 3,
                UserId = regularUser1.Id,
                Rating = 10,
                Comment = "Mùa 2 vượt ngoài mọi kỳ vọng! Phần hoạt họa của Fortiche đẹp như tranh vẽ, âm nhạc quá cảm xúc.",
                CreatedAtUtc = now.AddDays(-4),
                UpdatedAtUtc = now.AddDays(-4)
            },
            new ReviewEntity
            {
                Id = Guid.NewGuid(),
                MovieId = 3,
                UserId = vipUser2.Id,
                Rating = 9,
                Comment = "Mối quan hệ giữa Vi và Jinx đau lòng đến nghẹn thở. Phim hoạt hình xuất sắc nhất thập kỷ.",
                CreatedAtUtc = now.AddDays(-2),
                UpdatedAtUtc = now.AddDays(-2)
            },

            // Phim 4: The Dark Knight
            new ReviewEntity
            {
                Id = Guid.NewGuid(),
                MovieId = 4,
                UserId = adminUser.Id,
                Rating = 10,
                Comment = "Heath Ledger với vai Joker là huyền thoại sống mãi trong lịch sử điện ảnh thế giới.",
                CreatedAtUtc = now.AddDays(-20),
                UpdatedAtUtc = now.AddDays(-20)
            },

            // Phim 5: Solo Leveling 2
            new ReviewEntity
            {
                Id = Guid.NewGuid(),
                MovieId = 5,
                UserId = regularUser2.Id,
                Rating = 9,
                Comment = "Phần 2 hành động mãn nhãn, đội quân bóng đêm trỗi dậy ngầu đét! Đồ họa của A-1 Pictures rất mượt.",
                CreatedAtUtc = now.AddDays(-3),
                UpdatedAtUtc = now.AddDays(-3)
            },

            // Phim 6: Shogun
            new ReviewEntity
            {
                Id = Guid.NewGuid(),
                MovieId = 6,
                UserId = vipUser1.Id,
                Rating = 9,
                Comment = "Tái hiện văn hóa võ sĩ đạo và mưu lược chính trị thời phong kiến Nhật Bản chuẩn chỉ đến từng chi tiết nhỏ.",
                CreatedAtUtc = now.AddDays(-15),
                UpdatedAtUtc = now.AddDays(-15)
            },

            // Phim 11: Parasite
            new ReviewEntity
            {
                Id = Guid.NewGuid(),
                MovieId = 11,
                UserId = regularUser1.Id,
                Rating = 10,
                Comment = "Kịch bản xuất sắc không một hạt sạn. Phản ánh chân thực sự phân hóa giàu nghèo sâu sắc.",
                CreatedAtUtc = now.AddDays(-25),
                UpdatedAtUtc = now.AddDays(-25)
            },

            // Phim 13: Mai
            new ReviewEntity
            {
                Id = Guid.NewGuid(),
                MovieId = 13,
                UserId = vipUser2.Id,
                Rating = 8,
                Comment = "Phương Anh Đào diễn xuất rất có hồn, ánh mắt chất chứa nỗi niềm. Góc quay và ánh sáng tiến bộ rõ rệt.",
                CreatedAtUtc = now.AddDays(-6),
                UpdatedAtUtc = now.AddDays(-6)
            },
            new ReviewEntity
            {
                Id = Guid.NewGuid(),
                MovieId = 13,
                UserId = regularUser1.Id,
                Rating = 9,
                Comment = "Một bộ phim chạm đến cảm xúc của nhiều người phụ nữ hiện đại. Nhạc phim của Phan Mạnh Quỳnh rất hay.",
                CreatedAtUtc = now.AddDays(-4),
                UpdatedAtUtc = now.AddDays(-4)
            },

            // Phim 14: Lật Mặt 7
            new ReviewEntity
            {
                Id = Guid.NewGuid(),
                MovieId = 14,
                UserId = vipUser1.Id,
                Rating = 9,
                Comment = "Lấy đi rất nhiều nước mắt. Cảnh bà Hai ngồi chờ các con nơi quê nhà làm ai xa quê cũng phải quặn lòng.",
                CreatedAtUtc = now.AddDays(-7),
                UpdatedAtUtc = now.AddDays(-7)
            },

            // Phim 17: Exhuma
            new ReviewEntity
            {
                Id = Guid.NewGuid(),
                MovieId = 17,
                UserId = adminUser.Id,
                Rating = 9,
                Comment = "Không khí kinh dị tâm linh rùng rợn, lồng ghép lịch sử tinh tế. Choi Min-sik và Kim Go-eun phối hợp ăn ý.",
                CreatedAtUtc = now.AddDays(-11),
                UpdatedAtUtc = now.AddDays(-11)
            }
        );

        await context.SaveChangesAsync();
    }

    private static (byte[] Salt, byte[] Hash) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            100_000,
            HashAlgorithmName.SHA256,
            32);
        return (salt, hash);
    }
}
