using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class RequiredOptionsBind
{
    public static void Read(IConfiguration configuration)
    {
        configuration.GetRequiredSection("RequiredOnly").Bind(new RequiredOptions());
    }

    private sealed class RequiredOptions
    {
        public string? Key { get; set; }
    }
}
