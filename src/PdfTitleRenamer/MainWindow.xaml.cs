using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Controls;
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
    private bool _isSingleFileSelection;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        UpdateButtonStates();
    }

    public ObservableCollection<PdfRenameItem> Items { get; } = new();

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Page images from a crashed run would otherwise stay in %TEMP% indefinitely.
        _ = Task.Run(() => PdfOcrService.DeleteStaleTemporaryDirectories(TimeSpan.FromHours(12)));
        await RefreshCodexStatusAsync();
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_isBusy && TryGetDroppedPath(e.Data, out _) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_isBusy || !TryGetDroppedPath(e.Data, out var path) || !ConfirmReplaceSelection()) return;
        try
        {
            if (Directory.Exists(path)) LoadFolder(path);
            else LoadFile(path);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "PDFを開けません", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private static bool TryGetDroppedPath(IDataObject data, out string path)
    {
        path = string.Empty;
        if (data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } paths) return false;
        path = paths[0];
        return Directory.Exists(path) ||
            (File.Exists(path) && string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase));
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _analysisCancellation?.Cancel();
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, _isCodexLoggedIn
            ? "ログイン状態を確認しています…"
            : $"ブラウザでChatGPTにログインしてください…（最大{CodexLoginService.LoginTimeout.TotalMinutes:0}分）");
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
            SetBusy(false, Items.Count == 0 ? "PDFファイルまたはフォルダを選択してください" : $"PDF {Items.Count}件");
        }
    }

    private void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        var dialog = new OpenFolderDialog
        {
            Title = "PDFが入っているフォルダを選択",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!ConfirmReplaceSelection()) return;
        try { LoadFolder(dialog.FolderName); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "フォルダを開けません", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ChooseFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        var dialog = new OpenFileDialog
        {
            Title = "解析・リネームするPDFを1ファイル選択",
            Filter = "PDFファイル (*.pdf)|*.pdf",
            Multiselect = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true || !ConfirmReplaceSelection()) return;
        try { LoadFile(dialog.FileName); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "PDFを開けません", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private bool ConfirmReplaceSelection()
    {
        CommitEdits();
        return !Items.Any(item => !string.IsNullOrWhiteSpace(item.SuggestedName)) ||
            MessageBox.Show(this, "対象を変更すると現在の候補と編集内容が一覧から消えます。変更しますか？",
                "対象の変更", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
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
        var paths = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        ReplaceItems(paths, folder, singleFile: false);
    }

    private void LoadFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(fullPath), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PDFファイルを選択してください。");
        if (!File.Exists(fullPath)) throw new FileNotFoundException("PDFファイルが見つかりません。", fullPath);
        ReplaceItems([fullPath], fullPath, singleFile: true);
    }

    private void ReplaceItems(string[] paths, string selectionPath, bool singleFile)
    {
        foreach (var item in Items) item.PropertyChanged -= Item_PropertyChanged;
        Items.Clear();
        FolderTextBox.Text = selectionPath;
        _isSingleFileSelection = singleFile;
        SelectionModeText.Text = singleFile ? "PDFを選ぶ（1ファイル）" : "PDFを選ぶ（フォルダ）";
        foreach (var path in paths)
        {
            var item = new PdfRenameItem(path);
            item.PropertyChanged += Item_PropertyChanged;
            Items.Add(item);
        }
        if (Items.Count > 0) PdfGrid.SelectedIndex = 0;

        OperationProgress.Maximum = Math.Max(1, Items.Count);
        OperationProgress.Value = 0;
        OperationStatusText.Text = paths.Length == 0
            ? "フォルダ直下にPDFがありません"
            : $"PDF {paths.Length}件を読み込みました";
        UpdateButtonStates();
    }

    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        CommitEdits();
        var targets = GetAnalysisTargets();
        var pagesToRead = int.Parse((string)((ComboBoxItem)PageCountComboBox.SelectedItem).Tag);
        if (ReanalyzeCheckBox.IsChecked == true && targets.Any(item => !string.IsNullOrWhiteSpace(item.SuggestedName)) &&
            MessageBox.Show(this, "選択したPDFの既存の候補・手動編集を新しい推定結果で置き換えます。続けますか？",
                "再解析の確認", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        if (targets.Length == 0)
        {
            MessageBox.Show("解析するPDFを選択してください。", "タイトル推定", MessageBoxButton.OK, MessageBoxImage.Information);
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
            var loginStatus = await _loginService.GetStatusAsync(cancellationToken);
            ApplyCodexStatus(loginStatus);
            cancellationToken.ThrowIfCancellationRequested();
            if (!loginStatus.IsLoggedIn)
                throw new InvalidOperationException("先に「ChatGPTにログイン」を実行してください。");
            await using var codex = new CodexAppServerClient();
            codex.UsageUpdated += ApplyCodexUsage;
            await codex.StartAsync(cancellationToken);

            for (var index = 0; index < targets.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = targets[index];
                item.Status = "OCR中";
                PdfGrid.ScrollIntoView(item);

                OperationStatusText.Text = $"{index + 1}/{targets.Length}  OCR: {item.CurrentName}";

                try
                {
                    using var document = await _ocrService.ReadFirstPagesAsync(item.SourcePath, cancellationToken, pagesToRead);
                    item.Status = "AI判定中";
                    OperationStatusText.Text = $"{index + 1}/{targets.Length}  AI判定: {item.CurrentName}";

                    // Run Codex in the temporary image folder, never in the user's PDF folder,
                    // so the read-only sandbox cannot see neighbouring documents.
                    var suggestion = await codex.SuggestTitleAsync(
                        document,
                        document.TemporaryDirectory,
                        cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    var safeTitle = FileRenameService.SanitizeTitle(suggestion.Title);
                    item.SuggestedName = safeTitle + ".pdf";
                    item.Confidence = suggestion.Confidence;
                    item.Reason = suggestion.Reason;
                    item.Status = suggestion.Confidence < 0.7 ? "要確認（低確信）" : "確認待ち";
                    completed++;
                }
                catch (OperationCanceledException)
                {
                    item.Status = string.IsNullOrWhiteSpace(item.SuggestedName) ? "中止" : "中止・旧候補";
                    throw;
                }
                catch (Exception ex)
                {
                    item.Status = string.IsNullOrWhiteSpace(item.SuggestedName) ? "解析失敗" : "失敗・旧候補";
                    item.Reason = ToUserMessage(ex) + (string.IsNullOrWhiteSpace(item.SuggestedName) ? "" : "（前回の候補を保持しています）");
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
        if (_isBusy) return;
        CommitEdits();
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

        var confirmation = new RenamePreviewWindow(plans) { Owner = this };
        if (confirmation.ShowDialog() != true) return;

        SetBusy(true, "ファイル名を変更しています…");
        RenameBatchResult result;
        try { result = _renameService.Execute(plans); }
        finally { SetBusy(false, OperationStatusText.Text); }
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
        try
        {
            var status = await _loginService.GetStatusAsync();
            ApplyCodexStatus(status);
        }
        catch (Exception ex)
        {
            CodexStatusText.Text = "ログイン確認失敗";
            OperationStatusText.Text = ToUserMessage(ex);
        }
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
        CancelButton.Visibility = isBusy && _analysisCancellation is not null ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = isBusy;
        LoginButton.IsEnabled = !isBusy;
        PdfGrid.IsReadOnly = isBusy;
        PdfGrid.IsEnabled = !isBusy;
        ChooseFolderButton.IsEnabled = !isBusy;
        ChooseFileButton.IsEnabled = !isBusy;
        SelectionActions.IsEnabled = !isBusy;
        PageCountComboBox.IsEnabled = !isBusy;
        ReanalyzeCheckBox.IsEnabled = !isBusy;
        UpdateButtonStates();
    }

    private PdfRenameItem[] GetAnalysisTargets() => Items.Where(item => item.IsSelected &&
        (ReanalyzeCheckBox.IsChecked == true || string.IsNullOrWhiteSpace(item.SuggestedName))).ToArray();

    private void CommitEdits()
    {
        PdfGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        PdfGrid.CommitEdit(DataGridEditingUnit.Row, true);
        CandidateTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isSingleFileSelection && e.PropertyName == nameof(PdfRenameItem.SourcePath) && sender is PdfRenameItem renamedItem)
            FolderTextBox.Text = renamedItem.SourcePath;
        if (!_isBusy && e.PropertyName == nameof(PdfRenameItem.SuggestedName) && sender is PdfRenameItem item)
        {
            item.Confidence = double.NaN;
            item.Reason = "手動で編集した候補です。元のPDFと照らし合わせて確認してください。";
            item.Status = string.IsNullOrWhiteSpace(item.SuggestedName) ? "候補なし" : "手動編集";
        }
        UpdateButtonStates();
    }
    private void AnalysisOptions_Changed(object sender, RoutedEventArgs e) => UpdateButtonStates();
    private void PdfGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtonStates();

    private void OpenPdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (PdfGrid.SelectedItem is not PdfRenameItem item) return;
        try { Process.Start(new ProcessStartInfo(item.SourcePath) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "PDFを開けません", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void UpdateButtonStates()
    {
        if (RenameButton is null) return; // XAML can raise selection events while initializing.
        var selected = Items.Count(item => item.IsSelected);
        var ready = Items.Count(item => item.IsSelected && !string.IsNullOrWhiteSpace(item.SuggestedName));
        var targets = GetAnalysisTargets().Length;
        AnalyzeButton.IsEnabled = !_isBusy && _isCodexLoggedIn && targets > 0;
        AnalyzeButton.Content = $"タイトルを推定（{targets}件）";
        RenameButton.IsEnabled = !_isBusy && ready > 0;
        RenameButton.Content = $"変更内容を確認（{ready}件）";
        SelectionSummaryText.Text = $"PDF {Items.Count}件 / 選択 {selected}件 / 候補あり {ready}件";
        EmptyListPanel.Visibility = Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CandidateTextBox.IsEnabled = !_isBusy && PdfGrid.SelectedItem is PdfRenameItem;
        OpenPdfButton.IsEnabled = !_isBusy && PdfGrid.SelectedItem is PdfRenameItem;
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
