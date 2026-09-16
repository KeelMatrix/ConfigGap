using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class ArrayOptions
{
    public static void Read(IConfiguration configuration)
    {
        var options = new ServerOptions();
        configuration.GetSection("Servers").Bind(options);
    }

    private sealed class ServerOptions
    {
        public List<ServerEndpoint> Items { get; } = [];
    }

    private sealed class ServerEndpoint
    {
        public string? Name { get; set; }
        public string? Endpoint { get; set; }
    }
}
