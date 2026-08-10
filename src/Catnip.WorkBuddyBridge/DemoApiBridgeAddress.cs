namespace Catnip.WorkBuddyBridge;

public static class DemoApiBridgeAddress
{
    public const string EnvironmentVariable = "CATNIP_DEMO_API";
    public const string DefaultValue = "http://127.0.0.1:5220";

    public static Uri Resolve(string? configured = null)
    {
        string value = configured
            ?? Environment.GetEnvironmentVariable(EnvironmentVariable)
            ?? DefaultValue;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || !uri.IsLoopback
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException("Demo API address must be an HTTP loopback origin.", nameof(configured));
        }

        return uri;
    }
}
