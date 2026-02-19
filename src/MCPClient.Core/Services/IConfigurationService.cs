using System.Threading.Tasks;
using MCPClient.Core.Models;

namespace MCPClient.Core.Services;

public interface IConfigurationService
{
    Task<AppConfig> LoadConfigAsync();
    Task SaveConfigAsync(AppConfig config);
    string GetConfigFilePath();
}
