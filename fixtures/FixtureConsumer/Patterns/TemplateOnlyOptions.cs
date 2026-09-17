using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class TemplateOnlyOptions
{
    public static void Read(IConfiguration configuration)
    {
        configuration.GetSection("TEMPLATE_ONLY").Bind(new TemplateOnlySettings());
    }

    private sealed class TemplateOnlySettings
    {
        public string? Key { get; set; }
    }
}
