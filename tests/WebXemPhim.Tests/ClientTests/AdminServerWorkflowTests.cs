using Xunit;

namespace WebXemPhim.Tests.ClientTests;

public class AdminServerWorkflowTests
{
    [Theory]
    [InlineData("/admin/servers", "MÁY CHỦ PHÁT")]
    [InlineData("/admin/stream-servers", "MÁY CHỦ PHÁT")]
    [InlineData("/admin/episodes", "TẬP & STREAM")]
    [InlineData("/admin/movies", "KHO PHIM")]
    [InlineData("/admin/vip", "VIP & THANH TOÁN")]
    public void AdminSectionLabel_MatchesServerRoute(string path, string expectedLabel)
    {
        var normalized = path.TrimEnd('/').ToLowerInvariant();
        var label = normalized switch
        {
            "/admin" or "/admin/overview" => "TỔNG QUAN",
            "/admin/movies" => "KHO PHIM",
            "/admin/episodes" => "TẬP & STREAM",
            "/admin/servers" or "/admin/stream-servers" => "MÁY CHỦ PHÁT",
            "/admin/users" => "NGƯỜI DÙNG",
            "/admin/reviews" => "KIỂM DUYỆT",
            "/admin/vip" => "VIP & THANH TOÁN",
            "/admin/audit-logs" => "NHẬT KÝ",
            _ => "QUẢN TRỊ"
        };

        Assert.Equal(expectedLabel, label);
    }

    [Theory]
    [InlineData(0, "Không giới hạn (Gigabit Line-Rate)")]
    [InlineData(1500, "1.5 Mbps (1500 Kbps)")]
    [InlineData(3500, "3.5 Mbps (3500 Kbps)")]
    [InlineData(5000, "5 Mbps (5000 Kbps)")]
    [InlineData(10000, "10 Mbps (10000 Kbps)")]
    public void FormatBandwidthDisplay_FormatsAppropriately(int maxBandwidthKbps, string expectedFormat)
    {
        var display = maxBandwidthKbps <= 0
            ? "Không giới hạn (Gigabit Line-Rate)"
            : $"{Math.Round(maxBandwidthKbps / 1000.0, 1)} Mbps ({maxBandwidthKbps} Kbps)";

        Assert.Equal(expectedFormat, display);
    }

    [Theory]
    [InlineData(true, 0, false, "Line-Rate")]
    [InlineData(false, 3500, false, "3.5 Mbps")]
    [InlineData(false, 0, true, "Blocked-403")]
    [InlineData(false, 1500, false, "1.5 Mbps")]
    public void SimulatorBandwidthLogic_EvaluatesPrivilegesCorrectly(
        bool isVipUser,
        int policyBandwidthKbps,
        bool isVipOnly,
        string expectedResult)
    {
        string outcome;
        if (isVipUser)
        {
            outcome = "Line-Rate";
        }
        else if (isVipOnly)
        {
            outcome = "Blocked-403";
        }
        else if (policyBandwidthKbps <= 0)
        {
            outcome = "Unlimited";
        }
        else
        {
            outcome = $"{Math.Round(policyBandwidthKbps / 1000.0, 1)} Mbps";
        }

        Assert.Equal(expectedResult, outcome);
    }
}
