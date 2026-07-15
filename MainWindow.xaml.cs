using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using PdfTitleRenamer.Models;
using PdfTitleRenamer.Services;

namespace PdfTitleRenamer;

public partial class MainWindow : Window
{
    private readonly PdfOcrService _ocrService = new();
    private readonly CodexLoginService _loginService = new();
    private readonly FileRenameService _renameService = new();
    private CancellationTokenSource? _analysisCancellation;
    private bool _isBusy;
    private bool _isCodexLoggedIn;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    public ObservableCollection<PdfRenameItem> Items { get; } = new();

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshCodexStatusAsync();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _analysisCancellation?.Cancel();
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "ブラウザでChatGPTにログインしてください…");
        try
        {
            var status = _isCodexLoggedIn
                ? await _loginService.GetStatusAsync()
                : await _loginService.LoginAsync();
            ApplyCodexStatus(status);
            if (!status.IsLoggedIn)
            {
                MessageBox.Show(
                    status.Message,
                    "Codexログイン",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Codexログイン", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, Items.Count == 0 ? "フォルダを選択してください" : $"PDF {Items.Count}件");
        }
    }

    private void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "PDFが入っているフォルダを選択",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        LoadFolder(dialog.FolderName);
    }

    private void LicenseButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new LicenseWindow
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void LoadFolder(string folder)
    {
        Items.Clear();
        FolderTextBox.Text = folder;

        var paths = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        foreach (var path in paths)
        {
            Items.Add(new PdfRenameItem(path));
        }

        OperationProgress.Maximum = Math.Max(1, Items.Count);
        OperationProgress.Value = 0;
        OperationStatusText.Text = paths.Length == 0
            ? "フォルダ直下にPDFがありません"
            : $"PDF {paths.Length}件を読み込みました";
        UpdateButtonStates();
    }

    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        var targets = Items.Where(item => item.IsSelected).ToArray();
        if (targets.Length == 0)
        {
            MessageBox.Show("解析するPDFを選択してください。", "タイトル推定", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var loginStatus = await _loginService.GetStatusAsync();
        ApplyCodexStatus(loginStatus);
        if (!loginStatus.IsLoggedIn)
        {
            MessageBox.Show(
                "先に「ChatGPTにログイン」を実行してください。APIキーは不要です。",
                "Codex未ログイン",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _analysisCancellation = new CancellationTokenSource();
        var cancellationToken = _analysisCancellation.Token;
        ApplyCodexUsage(CodexUsageSnapshot.Empty);
        SetBusy(true, "Codex App Serverを起動しています…");
        OperationProgress.Maximum = targets.Length;
        OperationProgress.Value = 0;

        var completed = 0;
        var failed = 0;
        try
        {
            await using var codex = new CodexAppServerClient();
            codex.UsageUpdated += ApplyCodexUsage;
            await codex.StartAsync(cancellationToken);

            for (var index = 0; index < targets.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = targets[index];
                item.Status = "OCR中";
                item.Reason = string.Empty;
                OperationStatusText.Text = $"{index + 1}/{targets.Length}  OCR: {item.CurrentName}";

                try
                {
                    using var document = await _ocrService.ReadFirstPagesAsync(item.SourcePath, cancellationToken);
                    item.Status = "AI判定中";
                    OperationStatusText.Text = $"{index + 1}/{targets.Length}  AI判定: {item.CurrentName}";

                    var suggestion = await codex.SuggestTitleAsync(
                        document,
                        Path.GetDirectoryName(item.SourcePath)!,
                        cancellationToken);
                    var safeTitle = FileRenameService.SanitizeTitle(suggestion.Title);
                    item.SuggestedName = safeTitle + ".pdf";
                    item.Confidence = suggestion.Confidence;
                    item.Reason = suggestion.Reason;
                    item.Status = "確認待ち";
                    completed++;
                }
                catch (OperationCanceledException)
                {
                    item.Status = "中止";
                    throw;
                }
                catch (Exception ex)
                {
                    item.Status = "解析失敗";
                    item.Reason = ToUserMessage(ex);
                    failed++;
                }
                finally
                {
                    OperationProgress.Value = index + 1;
                }
            }

            OperationStatusText.Text = $"推定完了: {completed}件 / 失敗: {failed}件。候補を確認・編集してください。";
        }
        catch (OperationCanceledException)
        {
            OperationStatusText.Text = "タイトル推定を中止しました。";
        }
        catch (Exception ex)
        {
            OperationStatusText.Text = "Codexとの接続に失敗しました。";
            MessageBox.Show(ToUserMessage(ex), "タイトル推定", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _analysisCancellation.Dispose();
            _analysisCancellation = null;
            SetBusy(false, OperationStatusText.Text);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _analysisCancellation?.Cancel();
        CancelButton.IsEnabled = false;
        OperationStatusText.Text = "中止しています…";
    }

    private void RenameButton_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<RenamePlan> plans;
        try
        {
            plans = _renameService.BuildPlan(Items);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "リネーム準備", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (plans.Count == 0)
        {
            MessageBox.Show(
                "リネームする解析済みPDFを選択してください。",
                "一括リネーム",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var previewLines = plans.Take(8)
            .Select(plan => $"・{Path.GetFileName(plan.SourcePath)}\n  → {Path.GetFileName(plan.TargetPath)}");
        var preview = string.Join("\n", previewLines);
        if (plans.Count > 8)
        {
            preview += $"\nほか {plans.Count - 8}件";
        }

        var answer = MessageBox.Show(
            $"選択した{plans.Count}件を次の名前へ変更します。\n\n{preview}\n\n実行しますか？",
            "一括リネームの確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        var result = _renameService.Execute(plans);
        OperationStatusText.Text = $"リネーム完了: {result.Renamed}件 / 変更なし: {result.Unchanged}件 / 失敗: {result.Errors.Count}件";

        if (result.Errors.Count > 0)
        {
            MessageBox.Show(
                string.Join(Environment.NewLine, result.Errors.Take(12)),
                "一部のリネームに失敗しました",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        else
        {
            MessageBox.Show(
                $"{result.Renamed}件のPDFをリネームしました。",
                "一括リネーム完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        UpdateButtonStates();
    }

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items)
        {
            item.IsSelected = true;
        }
    }

    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items)
        {
            item.IsSelected = false;
        }
    }

    private async Task RefreshCodexStatusAsync()
    {
        var status = await _loginService.GetStatusAsync();
        ApplyCodexStatus(status);
    }

    private void ApplyCodexUsage(CodexUsageSnapshot usage)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ApplyCodexUsage(usage));
            return;
        }

        var limitText = usage.PrimaryLimit is null
            ? "利用枠 —"
            : $"利用枠 {usage.PrimaryLimit.UsedPercent:0.#}%使用";
        CodexUsageText.Text = $"{limitText} / 今回 {usage.SessionTotalTokens:N0} tokens";

        var details = new List<string>();
        if (usage.PrimaryLimit is not null)
        {
            details.Add(FormatRateLimit("主利用枠", usage.PrimaryLimit));
        }

        if (usage.SecondaryLimit is not null)
        {
            details.Add(FormatRateLimit("副利用枠", usage.SecondaryLimit));
        }

        details.Add($"今回の合計: {usage.SessionTotalTokens:N0} tokens");
        if (usage.LastTurnTokens > 0)
        {
            details.Add($"直近の判定: {usage.LastTurnTokens:N0} tokens");
        }

        if (usage.Credits is { Unlimited: true })
        {
            details.Add("クレジット: 無制限");
        }
        else if (!string.IsNullOrWhiteSpace(usage.Credits?.Balance))
        {
            details.Add($"クレジット残高: {usage.Credits.Balance}");
        }
        else if (usage.Credits is { HasCredits: true })
        {
            details.Add("クレジット: 利用可能（残高は非公開）");
        }
        else if (usage.Credits is { HasCredits: false })
        {
            details.Add("クレジット: なし");
        }

        if (usage.IndividualLimit is not null)
        {
            var resetText = usage.IndividualLimit.ResetsAt is null
                ? string.Empty
                : $"、{usage.IndividualLimit.ResetsAt.Value.ToLocalTime():M/d HH:mm}リセット";
            details.Add(
                $"個別上限: {usage.IndividualLimit.Used} / {usage.IndividualLimit.Limit}" +
                $"（残り{usage.IndividualLimit.RemainingPercent:0.#}%{resetText}）");
        }

        if (!string.IsNullOrWhiteSpace(usage.PlanType))
        {
            details.Add($"プラン: {usage.PlanType}");
        }

        if (usage.PrimaryLimit is null && usage.Credits is null)
        {
            details.Add("利用枠とクレジットは、契約プランから提供された場合だけ表示されます。");
        }

        CodexUsageToolTipText.Text = string.Join(Environment.NewLine, details);
    }

    private static string FormatRateLimit(string label, CodexRateLimitWindow limit)
    {
        var metadata = new List<string>();
        var windowText = limit.WindowDurationMinutes switch
        {
            >= 1440 when limit.WindowDurationMinutes % 1440 == 0 =>
                $"{limit.WindowDurationMinutes / 1440}日枠",
            >= 60 when limit.WindowDurationMinutes % 60 == 0 =>
                $"{limit.WindowDurationMinutes / 60}時間枠",
            > 0 => $"{limit.WindowDurationMinutes}分枠",
            _ => ""
        };
        if (!string.IsNullOrEmpty(windowText))
        {
            metadata.Add(windowText);
        }

        if (limit.ResetsAt is not null)
        {
            metadata.Add($"{limit.ResetsAt.Value.ToLocalTime():M/d HH:mm}リセット");
        }

        var suffix = metadata.Count == 0 ? string.Empty : $"（{string.Join("・", metadata)}）";
        return $"{label}: {limit.UsedPercent:0.#}%使用{suffix}";
    }

    private void ApplyCodexStatus(CodexLoginStatus status)
    {
        _isCodexLoggedIn = status.IsLoggedIn;
        CodexStatusText.Text = !status.IsAvailable
            ? "Codex CLIなし"
            : status.IsLoggedIn ? "Codex ログイン済み" : "Codex 未ログイン";
        CodexStatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            status.IsLoggedIn ? "#176B50" : status.IsAvailable ? "#8A5B12" : "#8B3341"));
        LoginButton.Content = status.IsLoggedIn ? "ログイン状態を更新" : "ChatGPTにログイン";
        UpdateButtonStates();
    }

    private void SetBusy(bool isBusy, string statusText)
    {
        _isBusy = isBusy;
        OperationStatusText.Text = statusText;
        CancelButton.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = isBusy;
        LoginButton.IsEnabled = !isBusy;
        PdfGrid.IsReadOnly = isBusy;
        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        AnalyzeButton.IsEnabled = !_isBusy && _isCodexLoggedIn && Items.Count > 0;
        RenameButton.IsEnabled = !_isBusy && Items.Any(item =>
            item.IsSelected && !string.IsNullOrWhiteSpace(item.SuggestedName));
    }

    private static string ToUserMessage(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return "処理を中止しました。";
        }

        var message = exception.Message;
        if (message.Contains("login", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            return "Codexへログインできていません。「ChatGPTにログイン」を実行してください。";
        }

        return message;
    }
}
