namespace ConfigGap.FixtureConsumer.Patterns;

public static class OptionsBuilderImpostor
{
    public static void Read()
    {
        var builder = new OptionsBuilder<PaymentOptions>();
        builder.BindConfiguration("Impostor:Key");
    }

    private sealed class PaymentOptions
    {
    }

    private sealed class OptionsBuilder<T>
    {
        public OptionsBuilder<T> BindConfiguration(string key) => this;
    }
}
