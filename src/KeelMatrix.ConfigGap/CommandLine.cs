namespace KeelMatrix.ConfigGap;

internal static class CommandLineParser
{
    public static CliParseResult Parse(string[] args)
    {
        if (args.Length == 0 || args.Any(argument => string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase) || argument == "-h"))
        {
            return new CliParseResult { Options = new CliOptions { ShowHelp = true } };
        }

        if (!string.Equals(args[0], "check", StringComparison.OrdinalIgnoreCase))
        {
            return Error("the required command is 'check'.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? solution = null;
        string? project = null;
        string? config = null;
        var format = OutputFormat.Text;

        for (var index = 1; index < args.Length; index++)
        {
            var argument = args[index];
            var separator = argument.IndexOf('=');
            var name = separator < 0 ? argument : argument[..separator];
            var inlineValue = separator < 0 ? null : argument[(separator + 1)..];
            string? value;
            switch (name)
            {
                case "--solution":
                    value = ReadValue(args, ref index, name, inlineValue, seen);
                    solution = value;
                    break;
                case "--project":
                    value = ReadValue(args, ref index, name, inlineValue, seen);
                    project = value;
                    break;
                case "--config":
                    value = ReadValue(args, ref index, name, inlineValue, seen);
                    config = value;
                    break;
                case "--format":
                    value = ReadValue(args, ref index, name, inlineValue, seen);
                    if (string.Equals(value, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        format = OutputFormat.Text;
                    }
                    else if (string.Equals(value, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        format = OutputFormat.Json;
                    }
                    else
                    {
                        return Error("--format must be 'text' or 'json'.");
                    }

                    break;
                default:
                    return Error($"unknown option '{name}'.");
            }
        }

        return new CliParseResult
        {
            Options = new CliOptions
            {
                SolutionPath = solution,
                ProjectPath = project,
                ConfigPath = config,
                Format = format
            }
        };
    }

    public static string Usage() => """
        ConfigGap checks statically used .NET configuration keys against declared repository surfaces.

        Usage:
          configgap check [--solution <path>] [--project <path>] [--config <path>] [--format text|json]

        Options:
          --solution <path>  Solution to analyze. If omitted, one solution is selected only when --project is not supplied.
          --project <path>   Project to analyze directly, or to select from the explicitly supplied solution.
          --config <path>    appsettings*.json/.env.example, or a version 1 ConfigGap surface file.
          --format text|json Human diagnostics (default) or the versioned local JSON report.

        Analysis:
          Literal IConfiguration reads require statically proven root provenance.
          Bounded ASP.NET Core Startup, controller, and registered-service activation is recognized.
          Otherwise, no visible same-compilation call site is CG900 unknown, not an assumed root.

        Exit codes:
          0  Complete trustworthy analysis with no blocking missing-key finding.
          1  Complete trustworthy analysis with one or more CG001 findings.
          2  Workspace, configuration, parse, option, or unsupported execution failure.
        """;

    private static string? ReadValue(
        string[] args,
        ref int index,
        string name,
        string? inlineValue,
        HashSet<string> seen)
    {
        if (!seen.Add(name))
        {
            throw new InvalidOperationException($"option '{name}' may only be specified once.");
        }

        var value = inlineValue;
        if (value is null)
        {
            if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith('-'))
            {
                throw new InvalidOperationException($"option '{name}' requires a value.");
            }

            value = args[index];
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"option '{name}' requires a non-empty value.");
        }

        return value;
    }

    private static CliParseResult Error(string message) => new() { Error = message };
}
