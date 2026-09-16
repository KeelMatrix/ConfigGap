using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class NestedOptions
{
    public static void Read(IConfiguration configuration)
    {
        var options = new PaymentOptions { Provider = new ProviderOptions() };
        configuration.GetSection("Payments").Bind(options);
    }

    private sealed class PaymentOptions
    {
        public ProviderOptions Provider { get; set; } = new();
    }

    private sealed class ProviderOptions
    {
        public string? ApiKey { get; set; }
    }
}
