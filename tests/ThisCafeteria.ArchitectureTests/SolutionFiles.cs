using System.Text;

namespace ThisCafeteria.ArchitectureTests;

/// <summary>
/// Locates the repository on disk and serves its source files with comments stripped.
///
/// The boundary rules are checked against source text rather than compiled IL. That is a
/// deliberate trade: source scanning reports a real <c>path:line</c> a developer can jump to,
/// and it sees <c>.razor</c> files, which compile into generated types whose names no longer
/// resemble the component that produced them.
/// </summary>
internal static class SolutionFiles
{
    private static readonly Lazy<string> RepositoryRootValue = new(FindRepositoryRoot);

    internal static string RepositoryRoot => RepositoryRootValue.Value;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ThisCafeteria.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate ThisCafeteria.sln walking up from {AppContext.BaseDirectory}.");
    }

    /// <summary>
    /// Every C# and Razor source file under the given repo-relative directory, excluding build
    /// output. Paths come back repo-relative with forward slashes so they are stable across
    /// machines and match the entries in <see cref="KnownViolations"/>.
    /// </summary>
    internal static IReadOnlyList<string> SourceFilesUnder(string repoRelativeDirectory)
    {
        var absoluteDirectory = Path.Combine(RepositoryRoot, repoRelativeDirectory);

        if (!Directory.Exists(absoluteDirectory))
        {
            throw new DirectoryNotFoundException($"Expected source directory {absoluteDirectory}.");
        }

        return Directory
            .EnumerateFiles(absoluteDirectory, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            .Where(path => !IsBuildOutput(path))
            .Select(ToRepoRelativePath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsBuildOutput(string absolutePath)
    {
        var normalized = absolutePath.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.Ordinal)
            || normalized.Contains("/bin/", StringComparison.Ordinal);
    }

    internal static string ToRepoRelativePath(string absolutePath) =>
        Path.GetRelativePath(RepositoryRoot, absolutePath).Replace('\\', '/');

    /// <summary>
    /// The file's lines with comment text blanked out, so a rule never fires on prose. Several
    /// files in this repository discuss <c>AppDbContext</c> in explanatory comments without
    /// touching it - see <c>Web/Services/ProfileAvatarState.cs</c>, whose comment explains the
    /// very constraint these rules protect.
    /// </summary>
    /// <returns>One entry per line, 1-indexed by position + 1, with comments replaced by spaces.</returns>
    internal static IReadOnlyList<string> ReadCodeLines(string repoRelativePath)
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot, repoRelativePath));
        return StripComments(text).Split('\n');
    }

    /// <summary>
    /// Blanks C#-style comments while preserving line structure and string literals, so that a
    /// URL such as "https://example.com" inside a string is not mistaken for a line comment.
    /// Razor markup comments are left alone; the rules only look for C# type usage.
    /// </summary>
    private static string StripComments(string text)
    {
        var builder = new StringBuilder(text.Length);
        var index = 0;

        while (index < text.Length)
        {
            var current = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';

            if (current == '"' || current == '\'')
            {
                index = CopyLiteral(text, index, builder);
                continue;
            }

            if (current == '/' && next == '/')
            {
                while (index < text.Length && text[index] != '\n')
                {
                    builder.Append(' ');
                    index++;
                }

                continue;
            }

            if (current == '/' && next == '*')
            {
                while (index < text.Length && !(text[index] == '*' && index + 1 < text.Length && text[index + 1] == '/'))
                {
                    builder.Append(text[index] == '\n' ? '\n' : ' ');
                    index++;
                }

                // Blank the closing "*/" too, when present.
                for (var skipped = 0; skipped < 2 && index < text.Length; skipped++, index++)
                {
                    builder.Append(' ');
                }

                continue;
            }

            builder.Append(current);
            index++;
        }

        return builder.ToString();
    }

    /// <summary>Copies a string or character literal verbatim, returning the index just past it.</summary>
    private static int CopyLiteral(string text, int start, StringBuilder builder)
    {
        var quote = text[start];
        builder.Append(quote);
        var index = start + 1;

        while (index < text.Length)
        {
            var current = text[index];

            if (current == '\\' && index + 1 < text.Length)
            {
                builder.Append(current);
                builder.Append(text[index + 1]);
                index += 2;
                continue;
            }

            builder.Append(current);
            index++;

            if (current == quote || current == '\n')
            {
                break;
            }
        }

        return index;
    }
}
