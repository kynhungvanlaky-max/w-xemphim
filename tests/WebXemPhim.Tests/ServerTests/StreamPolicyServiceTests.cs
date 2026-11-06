using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public class StreamPolicyServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly Mock<IWebHostEnvironment> _envMock;

    public StreamPolicyServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "webxemphim_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        _envMock = new Mock<IWebHostEnvironment>();
        _envMock.Setup(e => e.ContentRootPath).Returns(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
        catch
        {
            // Ignore cleanup errors in tests
        }
    }

    [Fact]
    public async Task GetAllPoliciesAsync_ReturnsExpectedDefaultClusters()
    {
        var service = new StreamPolicyService(_envMock.Object, NullLogger<StreamPolicyService>.Instance);

        var policies = await service.GetAllPoliciesAsync();

        Assert.NotEmpty(policies);
        var vip = policies.FirstOrDefault(p => p.ServerKey == "vip");
        Assert.NotNull(vip);
        Assert.Equal(0, vip.MaxBandwidthKbps); // Unlimited
        Assert.Contains("4K", vip.MaxQuality);
        Assert.True(vip.IsVipOnly);
        Assert.Equal(8192, vip.BufferInitialBurstKb);

        var cdn = policies.FirstOrDefault(p => p.ServerKey == "cdn");
        Assert.NotNull(cdn);
        Assert.Equal(3500, cdn.MaxBandwidthKbps);
        Assert.Contains("1080p", cdn.MaxQuality);
        Assert.False(cdn.IsVipOnly);

        var backup = policies.FirstOrDefault(p => p.ServerKey == "backup");
        Assert.NotNull(backup);
        Assert.Equal(1500, backup.MaxBandwidthKbps);
        Assert.Contains("720p", backup.MaxQuality);
        Assert.False(backup.IsVipOnly);
    }

    [Theory]
    [InlineData("VIP FastServer 1", StreamServerType.VipFastServer, "vip", true)]
    [InlineData("Hydra CDN Hà Nội", StreamServerType.HydraCdn, "cdn", false)]
    [InlineData("Dự phòng 2", StreamServerType.BackupStream, "backup", false)]
    [InlineData(null, StreamServerType.VipFastServer, "vip", true)]
    [InlineData("Máy chủ khác", StreamServerType.HydraCdn, "cdn", false)]
    public void GetPolicyForStream_MatchesCorrectCluster(
        string? serverName,
        StreamServerType serverType,
        string expectedClusterKey,
        bool expectedIsVipOnly)
    {
        var service = new StreamPolicyService(_envMock.Object, NullLogger<StreamPolicyService>.Instance);

        var policy = service.GetPolicyForStream(serverName, serverType);

        Assert.NotNull(policy);
        Assert.Equal(expectedClusterKey, policy.ServerKey);
        Assert.Equal(expectedIsVipOnly, policy.IsVipOnly);
    }

    [Fact]
    public async Task UpdatePolicyAsync_ModifiesPolicyValuesAndPersists()
    {
        var service = new StreamPolicyService(_envMock.Object, NullLogger<StreamPolicyService>.Instance);

        var updateReq = new UpdateStreamServerPolicyRequest
        {
            MaxBandwidthKbps = 2000,
            BufferInitialBurstKb = 1024,
            MaxQuality = "720p HD",
            IsVipOnly = true,
            IsActive = false,
            Description = "Đang test bóp băng thông"
        };

        var updated = await service.UpdatePolicyAsync("cdn", updateReq);

        Assert.NotNull(updated);
        Assert.Equal(2000, updated.MaxBandwidthKbps);
        Assert.Equal("720p HD", updated.MaxQuality);
        Assert.True(updated.IsVipOnly);
        Assert.False(updated.IsActive);

        // Verify retrieval matches updated state
        var fetched = service.GetPolicyForStream("Hydra CDN", StreamServerType.HydraCdn);
        Assert.NotNull(fetched);
        Assert.Equal(2000, fetched.MaxBandwidthKbps);
        Assert.False(fetched.IsActive);
    }

    [Fact]
    public async Task ResetToDefaultsAsync_RestoresOriginalDefaultSettings()
    {
        var service = new StreamPolicyService(_envMock.Object, NullLogger<StreamPolicyService>.Instance);

        // Update first
        await service.UpdatePolicyAsync("vip", new UpdateStreamServerPolicyRequest
        {
            MaxBandwidthKbps = 1000,
            BufferInitialBurstKb = 512,
            MaxQuality = "480p SD",
            IsVipOnly = false,
            IsActive = false
        });

        // Reset
        var resetList = await service.ResetToDefaultsAsync();

        var vip = resetList.FirstOrDefault(p => p.ServerKey == "vip");
        Assert.NotNull(vip);
        Assert.Equal(0, vip.MaxBandwidthKbps);
        Assert.Contains("4K", vip.MaxQuality);
        Assert.True(vip.IsVipOnly);
        Assert.True(vip.IsActive);
    }
}
