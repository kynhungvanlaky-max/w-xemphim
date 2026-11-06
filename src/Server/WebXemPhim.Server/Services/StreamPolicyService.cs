using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;

namespace WebXemPhim.Server.Services;

public sealed class StreamPolicyService : IStreamPolicyService
{
    private readonly string _storageFilePath;
    private readonly ILogger<StreamPolicyService>? _logger;
    private readonly ConcurrentDictionary<string, StreamServerPolicyDto> _policies = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public StreamPolicyService(
        IWebHostEnvironment? environment = null,
        ILogger<StreamPolicyService>? logger = null)
    {
        _logger = logger;
        var contentRoot = environment?.ContentRootPath ?? Directory.GetCurrentDirectory();
        var dataDir = Path.Combine(contentRoot, "data");
        Directory.CreateDirectory(dataDir);
        _storageFilePath = Path.Combine(dataDir, "stream_server_policies.json");

        LoadInitialPolicies();
    }

    private void LoadInitialPolicies()
    {
        try
        {
            if (File.Exists(_storageFilePath))
            {
                var json = File.ReadAllText(_storageFilePath);
                var loaded = JsonSerializer.Deserialize<List<StreamServerPolicyDto>>(json);
                if (loaded is { Count: > 0 })
                {
                    foreach (var p in loaded)
                    {
                        _policies[p.ServerKey] = p;
                    }
                    _logger?.LogInformation("Đã tải {Count} chính sách máy chủ luồng phát từ tệp lưu trữ.", _policies.Count);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Không thể đọc tệp chính sách luồng phát, khởi tạo mặc định.");
        }

        // Initialize defaults
        foreach (var def in GetDefaultPolicies())
        {
            _policies[def.ServerKey] = def;
        }
    }

    private static List<StreamServerPolicyDto> GetDefaultPolicies() =>
    [
        new StreamServerPolicyDto
        {
            ServerKey = "vip",
            ServerNamePrefix = "VIP",
            DisplayName = "Cụm VIP FastServer (Đặc quyền VIP)",
            ServerType = StreamServerType.VipFastServer,
            IsActive = true,
            IsVipOnly = true,
            MaxBandwidthKbps = 0, // 0 = Không giới hạn, tốc độ tối đa đường truyền
            MaxQuality = "4K UHD",
            BufferInitialBurstKb = 8192, // 8 MB pre-buffer phát tức thì siêu mượt
            Priority = 1,
            Description = "Đường truyền VIP Fast-Lane ưu tiên cao nhất, không giới hạn băng thông, hỗ trợ 4K 60fps và HDR cho trải nghiệm mượt mà tuyệt đối.",
            UpdatedAtUtc = DateTime.UtcNow
        },
        new StreamServerPolicyDto
        {
            ServerKey = "cdn",
            ServerNamePrefix = "Hydra",
            DisplayName = "Cụm Hydra CDN (Phổ thông / Free)",
            ServerType = StreamServerType.HydraCdn,
            IsActive = true,
            IsVipOnly = false,
            MaxBandwidthKbps = 3500, // 3.5 Mbps cho luồng thường
            MaxQuality = "1080p FHD",
            BufferInitialBurstKb = 2048, // 2 MB pre-buffer
            Priority = 2,
            Description = "Phân phối cho người dùng phổ thông qua mạng lưới Hydra Anycast CDN, giới hạn băng thông ~3.5 Mbps để tiết kiệm chi phí và duy trì tải ổn định.",
            UpdatedAtUtc = DateTime.UtcNow
        },
        new StreamServerPolicyDto
        {
            ServerKey = "backup",
            ServerNamePrefix = "Backup",
            DisplayName = "Cụm Server Dự Phòng (Backup Stream)",
            ServerType = StreamServerType.BackupStream,
            IsActive = true,
            IsVipOnly = false,
            MaxBandwidthKbps = 1500, // 1.5 Mbps
            MaxQuality = "720p HD",
            BufferInitialBurstKb = 1024, // 1 MB
            Priority = 3,
            Description = "Máy chủ dự phòng khi các cụm chính quá tải hoặc gặp sự cố, giới hạn băng thông 1.5 Mbps và trần chất lượng 720p HD.",
            UpdatedAtUtc = DateTime.UtcNow
        }
    ];

    public Task<List<StreamServerPolicyDto>> GetAllPoliciesAsync()
    {
        var list = _policies.Values.OrderBy(p => p.Priority).ToList();
        return Task.FromResult(list);
    }

    public StreamServerPolicyDto GetPolicyForStream(string? serverName, StreamServerType serverType)
    {
        var name = serverName ?? string.Empty;

        if (serverType == StreamServerType.VipFastServer || name.Contains("VIP", StringComparison.OrdinalIgnoreCase))
        {
            if (_policies.TryGetValue("vip", out var vipPolicy)) return vipPolicy;
        }

        if (serverType == StreamServerType.HydraCdn || name.Contains("Hydra", StringComparison.OrdinalIgnoreCase) || name.Contains("CDN", StringComparison.OrdinalIgnoreCase))
        {
            if (_policies.TryGetValue("cdn", out var cdnPolicy)) return cdnPolicy;
        }

        if (serverType == StreamServerType.BackupStream || name.Contains("Backup", StringComparison.OrdinalIgnoreCase) || name.Contains("Dự phòng", StringComparison.OrdinalIgnoreCase))
        {
            if (_policies.TryGetValue("backup", out var backupPolicy)) return backupPolicy;
        }

        return _policies.Values.FirstOrDefault() ?? GetDefaultPolicies()[0];
    }

    public async Task<StreamServerPolicyDto?> UpdatePolicyAsync(string serverKey, UpdateStreamServerPolicyRequest request)
    {
        var key = serverKey.ToLowerInvariant().Trim();
        if (!_policies.TryGetValue(key, out var existing))
        {
            return null;
        }

        existing.IsActive = request.IsActive;
        existing.IsVipOnly = request.IsVipOnly;
        existing.MaxBandwidthKbps = Math.Max(0, request.MaxBandwidthKbps);
        existing.MaxQuality = string.IsNullOrWhiteSpace(request.MaxQuality) ? "1080p FHD" : request.MaxQuality.Trim();
        existing.BufferInitialBurstKb = Math.Max(256, request.BufferInitialBurstKb);
        existing.Priority = Math.Clamp(request.Priority, 1, 10);
        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            existing.Description = request.Description.Trim();
        }
        existing.UpdatedAtUtc = DateTime.UtcNow;

        _policies[key] = existing;

        await SaveToFileAsync();
        return existing;
    }

    public async Task<List<StreamServerPolicyDto>> ResetToDefaultsAsync()
    {
        _policies.Clear();
        foreach (var def in GetDefaultPolicies())
        {
            _policies[def.ServerKey] = def;
        }
        await SaveToFileAsync();
        return _policies.Values.OrderBy(p => p.Priority).ToList();
    }

    private async Task SaveToFileAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            var list = _policies.Values.OrderBy(p => p.Priority).ToList();
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_storageFilePath, json);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Lỗi khi lưu chính sách máy chủ luồng phát vào tệp.");
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
