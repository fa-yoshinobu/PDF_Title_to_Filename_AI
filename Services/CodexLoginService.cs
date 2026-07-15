using System.Diagnostics;
using System.IO;

namespace PdfTitleRenamer.Services;

public sealed record CodexLoginStatus(bool IsAvailable, bool IsLoggedIn, string Message);

public sealed class CodexLoginService
{
    public async Task<CodexLoginStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = CreateRedirectedStartInfo("login", "status")
            };

            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = (await outputTask).Trim();
            var error = (await errorTask).Trim();
            var message = string.Join(" ", new[] { output, error }.Where(value => !string.IsNullOrWhiteSpace(value)));

            return new CodexLoginStatus(
                IsAvailable: true,
                IsLoggedIn: process.ExitCode == 0,
                Message: string.IsNullOrWhiteSpace(message)
                    ? (process.ExitCode == 0 ? "ログイン済み" : "未ログイン")
                    : message);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return new CodexLoginStatus(false, false, "Codex CLIが見つかりません。PATHを確認してください。");
        }
    }

    public async Task<CodexLoginStatus> LoginAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = CreateRedirectedStartInfo("login")
            };

            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            _ = await outputTask;
            _ = await errorTask;
            return await GetStatusAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return new CodexLoginStatus(false, false, "Codex CLIを起動できませんでした。");
        }
    }

    private static ProcessStartInfo CreateRedirectedStartInfo(params string[] arguments)
    {
        return CodexProcess.CreateStartInfo(arguments, redirect: true);
    }
}
