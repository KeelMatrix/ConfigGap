using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class OptionsBind
{
    public static void Read(IConfiguration configuration)
    {
        var options = new PaymentOptions();
        configuration.GetSection("Payments").Bind(options);
    }

    private sealed class PaymentOptions
    {
        public string? Provider { get; set; }
    }
}
