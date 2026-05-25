namespace ThisCafeteria.Infrastructure.Configuration;

/// <summary>
/// Loads a repository-root <c>.env</c> into process environment variables for local development.
/// Production and CI should set variables directly; missing <c>.env</c> is not an error.
/// </summary>
public static class LocalDotEnvLoader
{
    public static void LoadIfPresent()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var envPath = Path.Combine(directory.FullName, ".env");
            if (File.Exists(envPath))
            {
                Apply(File.ReadAllLines(envPath));
                return;
            }

            directory = directory.Parent;
        }
    }

    private static void Apply(IEnumerable<string> lines)
    {
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            if (!string.IsNullOrEmpty(key))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
