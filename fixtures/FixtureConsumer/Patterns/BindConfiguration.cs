using ConfigGap.FixtureSupport;
using Microsoft.Extensions.Options;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class BindConfiguration
{
    public static void Read()
    {
        OptionsBuilder<PaymentOptions> builder = null!;
        builder.BindConfiguration("Payments");
    }

    private sealed class PaymentOptions
    {
        public string? Provider { get; set; }
    }
}
