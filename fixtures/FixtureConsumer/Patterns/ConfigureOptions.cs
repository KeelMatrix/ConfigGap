using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class ConfigureOptions
{
    public static void Read(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ConfigureOptionsShape>(configuration.GetSection("Configure"));
    }

    private sealed class ConfigureOptionsShape
    {
        public string? Value { get; set; }
    }
}
