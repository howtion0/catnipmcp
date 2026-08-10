using Catnip.Shared.Configuration;

namespace Catnip.Shared.Management;

public sealed record EmptyCommand;

public sealed record SetMasterEnabledCommand(bool Enabled);

public sealed record SetGatewayModeCommand(GatewayMode Mode);

public sealed record SetModuleEnabledCommand(string ModuleId, bool Enabled);

public sealed record SetConnectorEnabledCommand(string ConnectorId, bool Enabled);

public sealed record TestConnectorCommand(string ConnectorId);

public sealed record SaveSettingsCommand(GatewaySettingsDto Settings);

public sealed record SaveSecretCommand(string SecretId, string SecretValue);

public sealed record DeleteSecretCommand(string SecretId);

public sealed record ShutdownRuntimeCommand(bool Graceful);

public static class PipeNames
{
    public static string Management(string userSidHash) =>
        $"Catnip.Management.{userSidHash}";

    public static string Events(string userSidHash) =>
        $"Catnip.Events.{userSidHash}";
}
