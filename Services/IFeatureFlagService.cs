using System.Security.Claims;
using SerbleAPI.Config;

namespace SerbleAPI.Services;

public interface IFeatureFlagService {
    Task<bool?> IsEnabled(string feature, ClaimsPrincipal? principal = null);
    Task<bool> IsEnabled(FeatureFlagDefinition feature, ClaimsPrincipal? principal = null);
}
