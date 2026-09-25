namespace JevWf.Workflows.Jev;

// The OpenRouter key: the OPENROUTER_API_KEY environment variable, or the same line in a .env
// file found in the given directory or any parent (.env is git-ignored).
public static class JevApiKey
{
    public const string Name = "OPENROUTER_API_KEY";

    public static string? Find(string startDirectory)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(Name);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment;

        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, ".env");
            if (!File.Exists(file))
                continue;

            foreach (var line in File.ReadLines(file))
            {
                var separator = line.IndexOf('=');
                if (separator > 0 && line[..separator].Trim() == Name)
                {
                    var value = line[(separator + 1)..].Trim().Trim('"', '\'');
                    return value.Length > 0 ? value : null;
                }
            }
        }

        return null;
    }
}
