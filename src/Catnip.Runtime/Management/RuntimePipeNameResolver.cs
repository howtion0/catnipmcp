using System.Security.Cryptography;
using System.Text;
using Catnip.Shared.Management;

namespace Catnip.Runtime.Management;

public static class RuntimePipeNameResolver
{
    public static string Resolve(string? configuredPipeName)
    {
        if (!string.IsNullOrWhiteSpace(configuredPipeName))
        {
            return configuredPipeName;
        }

        byte[] userHash = SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName));
        string developmentUserHash = Convert.ToHexString(userHash.AsSpan(0, 8)).ToLowerInvariant();
        CryptographicOperations.ZeroMemory(userHash);
        return PipeNames.Management(developmentUserHash);
    }
}

public sealed record RuntimeManagementOptions(string PipeName);
