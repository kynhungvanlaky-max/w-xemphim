using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;

namespace WebXemPhim.Server.Services;

public interface IStreamPolicyService
{
    Task<List<StreamServerPolicyDto>> GetAllPoliciesAsync();
    StreamServerPolicyDto GetPolicyForStream(string? serverName, StreamServerType serverType);
    Task<StreamServerPolicyDto?> UpdatePolicyAsync(string serverKey, UpdateStreamServerPolicyRequest request);
    Task<List<StreamServerPolicyDto>> ResetToDefaultsAsync();
}
