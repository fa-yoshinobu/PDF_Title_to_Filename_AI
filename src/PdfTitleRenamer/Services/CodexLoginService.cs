using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace PdfTitleRenamer.Services;

public sealed record CodexLoginStatus(bool IsAvailable, bool IsLoggedIn, string Message);

public sealed class CodexLoginService
{
    public static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(30);

    public async Task<CodexLoginStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await RunAsync(["login", "status"], StatusTimeout, cancellationToken);
            var message = string.Join(" ", new[] { result.Output, result.Error }.Where(value => !string.IsNullOrWhiteSpace(value)));

            return new CodexLoginStatus(
                IsAvailable: true,
                IsLoggedIn: result.ExitCode == 0,
                Message: string.IsNullOrWhiteSpace(message)
                    ? (result.ExitCode == 0 ? "ログイン済み" : "未ログイン")
                    : message);
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            return new CodexLoginStatus(false, false, "Codex CLIが見つかりません。PATHを確認してください。");
        }
        catch (TimeoutException)
        {
            return new CodexLoginStatus(true, false, "Codexのログイン状態を確認できませんでした（応答がありません）。");
        }
    }

    public async Task<CodexLoginStatus> LoginAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await RunAsync(["login"], LoginTimeout, cancellationToken);
            return await GetStatusAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            return new CodexLoginStatus(false, false, "Codex CLIを起動できませんでした。");
        }
        catch (TimeoutException)
        {
            return new CodexLoginStatus(
                true,
                false,
                $"{LoginTimeout.TotalMinutes:0}分以内にログインが完了しなかったため中断しました。もう一度お試しください。");
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string[] arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = CodexProcess.CreateStartInfo(arguments, redirect: true)
        };

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            // Never leave a hidden codex.exe running after the app gives up on it.
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException();
        }

        return (process.ExitCode, (await outputTask).Trim(), (await errorTask).Trim());
    }
}
