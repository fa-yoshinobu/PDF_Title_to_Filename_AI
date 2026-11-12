using System.IO;
using System.Windows;
using PdfTitleRenamer.Services;

namespace PdfTitleRenamer;

public partial class RenamePreviewWindow : Window
{
    public RenamePreviewWindow(IReadOnlyList<RenamePlan> plans)
    {
        InitializeComponent();
        var rows = plans.Select(plan =>
        {
            var note = plan.IsUnchanged
                ? "変更なし"
                : Path.GetFileName(plan.TargetPath) != plan.Item.SuggestedName ? "名前を自動補正"
                : plan.IsCaseOnlyChange ? "大文字・小文字のみ変更" : "変更";
            return new
            {
                CurrentName = Path.GetFileName(plan.SourcePath),
                TargetName = Path.GetFileName(plan.TargetPath),
                Note = note,
                NoteTone = note switch
                {
                    "変更なし" => "Neutral",
                    "名前を自動補正" => "Warning",
                    _ => "Info"
                }
            };
        }).ToArray();
        var changes = rows.Count(row => row.Note != "変更なし");
        PreviewGrid.ItemsSource = rows;
        SummaryText.Text = $"対象 {plans.Count}件 / 変更 {changes}件 / 変更なし {plans.Count - changes}件";
        var review = plans.Count(plan => double.IsNaN(plan.Item.Confidence) || plan.Item.Confidence < 0.7 ||
            plan.Item.Status.Contains("旧候補", StringComparison.Ordinal));
        ReviewText.Text = review > 0 ? $"要確認：{review}件は低確信・手動入力・再解析前の候補です。名前を確認してください。" : "候補はAIによる推定です。元のPDFと照らし合わせて確認してください。";
        ReviewBanner.Tag = review > 0 ? "Warning" : "Info";
        ExecuteButton.Content = $"ファイル名を変更（{changes}件）";
        ExecuteButton.IsEnabled = changes > 0;
    }

    private void ExecuteButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
