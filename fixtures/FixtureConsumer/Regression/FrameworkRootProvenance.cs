using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder
{
    public interface IApplicationBuilder;
}

namespace Microsoft.AspNetCore.Mvc
{
    public abstract class ControllerBase;
}

namespace ConfigGap.FixtureConsumer.Regression
{
    public sealed class Startup
    {
        private readonly IConfiguration _configuration;

        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string? Read() => _configuration["StartupRoot"];

        public void Configure(Microsoft.AspNetCore.Builder.IApplicationBuilder app)
        {
            _ = _configuration;
            _ = app;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            _ = _configuration;
            _ = services;
        }
    }

    public sealed class FixtureController : Microsoft.AspNetCore.Mvc.ControllerBase
    {
        private readonly IConfiguration _configuration;

        public FixtureController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string? Read() => _configuration["ControllerRoot"];
    }

    public sealed class RegisteredReceiver
    {
        private readonly IConfiguration _configuration;

        public RegisteredReceiver(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string? Read() => _configuration["RegisteredRoot"];
    }

    public static class FrameworkRootRegistration
    {
        private static IConfigurationRoot Root { get; } = null!;

        public static void Register(IServiceCollection services)
        {
            services.AddScoped<RegisteredReceiver>();
            services.UseFixtureConfiguration(Root);
        }

        private static IServiceCollection UseFixtureConfiguration(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            _ = configuration["ReducedExtensionRoot"];
            return services;
        }
    }
}
