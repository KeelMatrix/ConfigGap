using System.Linq;
using Microsoft.Extensions.Configuration;

namespace ConfigGap.FixtureConsumer.Patterns;

public static class RootGetChildren
{
    public static int Read(IConfiguration configuration)
    {
        return configuration.GetChildren().Count();
    }
}
