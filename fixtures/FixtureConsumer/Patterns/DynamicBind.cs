using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class DynamicBind
{
    public static void Read(IConfiguration configuration, string sectionName)
    {
        configuration.GetSection(sectionName).Bind(new Settings());
    }

    private sealed class Settings
    {
        public string? Value { get; set; }
    }
}
