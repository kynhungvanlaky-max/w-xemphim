namespace WebXemPhim.Server.Data;

/// <summary>
/// Marks a one-shot maintenance task (schema backfill, legacy cleanup, ...) as completed so
/// expensive scans are not repeated on every application start.
/// </summary>
public sealed class AppMaintenanceFlagEntity
{
    public string Flag { get; set; } = string.Empty;
    public DateTime CompletedAtUtc { get; set; }
}
