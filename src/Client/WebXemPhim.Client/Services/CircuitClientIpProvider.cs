namespace WebXemPhim.Client.Services;

public interface ICircuitClientIpProvider
{
    string? ClientIp { get; set; }
}

public sealed class CircuitClientIpProvider : ICircuitClientIpProvider
{
    public string? ClientIp { get; set; }
}
