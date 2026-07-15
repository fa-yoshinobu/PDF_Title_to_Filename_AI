using System.Diagnostics;
using System.IO;
using System.Text;

namespace PdfTitleRenamer.Services;

internal static class CodexProcess
{
    public static ProcessStartInfo CreateStartInfo(IEnumerable<string> arguments, bool redirect)
    {
        var executable = ResolveExecutable();
        ProcessStartInfo startInfo;

        if (executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
            executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            startInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(BuildCommandLine(executable, arguments));
        }
        else
        {
            startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        startInfo.RedirectStandardInput = redirect;
        startInfo.RedirectStandardOutput = redirect;
        startInfo.RedirectStandardError = redirect;
        if (redirect)
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            startInfo.StandardInputEncoding = utf8;
            startInfo.StandardOutputEncoding = utf8;
            startInfo.StandardErrorEncoding = utf8;
        }

        startInfo.CreateNoWindow = true;
        return startInfo;
    }

    private static string ResolveExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return Path.GetFullPath(configured);
        }

        var names = OperatingSystem.IsWindows()
            ? new[] { "codex.exe", "codex.cmd" }
            : new[] { "codex" };
        var pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var directory in pathDirectories)
        {
            foreach (var name in names)
            {
                try
                {
                    var candidate = Path.Combine(directory.Trim('"'), name);
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
                catch
                {
                    // Ignore malformed PATH entries.
                }
            }
        }

        throw new FileNotFoundException(
            "Codex CLIが見つかりません。Codex CLIをインストールし、PATHを設定してください。");
    }

    private static string BuildCommandLine(string executable, IEnumerable<string> arguments)
    {
        var parts = new List<string> { QuoteForCmd(executable) };
        parts.AddRange(arguments.Select(QuoteForCmd));
        return string.Join(" ", parts);
    }

    private static string QuoteForCmd(string value)
    {
        if (value.IndexOfAny([' ', '\t', '"', '&', '|', '<', '>', '^']) < 0)
        {
            return value;
        }

        return '"' + value.Replace("\"", "\"\"") + '"';
    }
}
