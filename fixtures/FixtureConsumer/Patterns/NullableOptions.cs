using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class NullableOptions
{
    public static void Read(IConfiguration configuration)
    {
        PaymentOptions? options = new();
        configuration.GetSection("Payments").Bind(options);
    }

    private sealed class PaymentOptions
    {
        public string? Provider { get; set; }
    }
}
