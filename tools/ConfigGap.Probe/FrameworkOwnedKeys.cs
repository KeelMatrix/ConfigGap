namespace KeelMatrix.ConfigGap.Probe;

internal static class FrameworkOwnedKeys
{
    // These roots are intentionally explicit. Application sections such as Authentication,
    // Identity, and Serilog are not framework-owned and remain in the evaluated domain.
    private static readonly string[] Roots =
    [
        // Standard app/host configuration sections and keys.
        "AllowedHosts",
        "ApplicationName",
        "ConnectionStrings",
        "ContentRoot",
        "Environment",
        "ForwardedHeaders",
        "Host",
        "HTTP_PORTS",
        "HTTPS_PORTS",
        "HttpLogging",
        "Kestrel",
        "Logging",
        "WebRoot",
        "Urls",
        "https_port",
        "shutdownTimeoutSeconds",
        "hostBuilder:reloadConfigOnChange",
        "captureStartupErrors",
        "detailedErrors",
        "hostingStartupAssemblies",
        "hostingStartupExcludeAssemblies",
        "preferHostingUrls",
        "preventHostingStartup",
        "startupAssembly",
        "suppressStatusMessages",
        "FORWARDEDHEADERS_ENABLED"
    ];

    // The environment-variable provider strips either recognized prefix. A
    // raw prefixed spelling is still reserved because source code may read it
    // directly from a provider-specific configuration surface.
    private static readonly string[] PrefixedHostKeys =
    [
        "APPLICATIONNAME",
        "CONTENTROOT",
        "ENVIRONMENT",
        "SHUTDOWNTIMEOUTSECONDS",
        "HOSTBUILDER__RELOADCONFIGONCHANGE",
        "CAPTURESTARTUPERRORS",
        "DETAILEDERRORS",
        "HOSTINGSTARTUPASSEMBLIES",
        "HOSTINGSTARTUPEXCLUDEASSEMBLIES",
        "HTTPS_PORT",
        "HTTPS_PORTS",
        "HTTP_PORTS",
        "PREFERHOSTINGURLS",
        "PREVENTHOSTINGSTARTUP",
        "STARTUPASSEMBLY",
        "SUPPRESSSTATUSMESSAGES",
        "URLS",
        "WEBROOT",
        "FORWARDEDHEADERS_ENABLED"
    ];

    private static readonly string[] EnvironmentPrefixes = ["ASPNETCORE_", "DOTNET_"];

    public static bool IsOwned(string key)
    {
        var normalized = KeyNormalizer.Normalize(key);
        return Roots.Any(root =>
            normalized.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(root + ":", StringComparison.OrdinalIgnoreCase)) ||
            EnvironmentPrefixes.Any(prefix => PrefixedHostKeys.Any(hostKey =>
                normalized.Equals(prefix + hostKey, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(prefix + hostKey + ":", StringComparison.OrdinalIgnoreCase)));
    }
}
