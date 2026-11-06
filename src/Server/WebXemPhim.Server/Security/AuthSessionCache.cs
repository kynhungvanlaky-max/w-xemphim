using System.Collections.Concurrent;
using WebXemPhim.Shared.Models;

namespace WebXemPhim.Server.Security;

/// <summary>
/// Cache-in-memory kết quả xác thực opaque token. Mỗi request Bearer trước đây chạy
/// ~5 round-trip Postgres chỉ để dựng claims; cửa admin mở nhiều endpoint song song
/// nên tổng RTT rất lớn. Cache theo hash token với TTL ngắn và bị đánh sập ngay khi:
/// logout, đổi/khoá mật khẩu, khoá tài khoản, đổi quyền admin, thu hồi toàn bộ token.
/// </summary>
public interface IAuthSessionCache
{
    bool TryGet(string tokenHash, out UserDto? user);
    void Set(string tokenHash, UserDto user);
    void InvalidateToken(string tokenHash);
    void InvalidateUser(Guid userId);
}

public sealed class AuthSessionCache : IAuthSessionCache
{
    /// <summary>
    /// TTL ngắn: thay đổi bảo mật ngoài danh sách móc hàng hoá bên dưới tối đa bị trễ
    /// <see cref="DefaultTtl"/>, trong khi vẫn xoá ~95% round-trip xác thực lặp lại.
    /// </summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(30);

    private sealed record Entry(UserDto User, DateTimeOffset ExpiresAtUtc);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Guid> _tokenOwners = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public AuthSessionCache(TimeProvider timeProvider) => _timeProvider = timeProvider;

    public bool TryGet(string tokenHash, out UserDto? user)
    {
        user = null;
        if (string.IsNullOrWhiteSpace(tokenHash)) return false;
        if (!_entries.TryGetValue(tokenHash, out var entry)) return false;
        if (entry.ExpiresAtUtc <= _timeProvider.GetUtcNow())
        {
            Remove(tokenHash);
            return false;
        }

        user = entry.User;
        return true;
    }

    public void Set(string tokenHash, UserDto user)
    {
        if (string.IsNullOrWhiteSpace(tokenHash) || user is null) return;
        _entries[tokenHash] = new Entry(user, _timeProvider.GetUtcNow().Add(DefaultTtl));
        _tokenOwners[tokenHash] = user.Id;
    }

    public void InvalidateToken(string tokenHash)
    {
        if (!string.IsNullOrWhiteSpace(tokenHash)) Remove(tokenHash);
    }

    public void InvalidateUser(Guid userId)
    {
        // Snapshot an toàn vì ConcurrentDictionary enumeration không ném khi bị sửa đồng thời.
        foreach (var (tokenHash, owner) in _tokenOwners)
        {
            if (owner == userId) Remove(tokenHash);
        }
    }

    private void Remove(string tokenHash)
    {
        _entries.TryRemove(tokenHash, out _);
        _tokenOwners.TryRemove(tokenHash, out _);
    }
}
