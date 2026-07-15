using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Navigation;

namespace PdfTitleRenamer;

public partial class LicenseWindow : Window
{
    private const string RepositoryUrl = "https://github.com/fa-yoshinobu/PDF_Title_to_Filename_AI";

    public LicenseWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var displayVersion = version is null
            ? "—"
            : $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
        VersionText.Text = $"Version {displayVersion}";
        VersionDetailText.Text = displayVersion;
        AppLicenseTextBox.Text = ReadEmbeddedText("PdfTitleRenamer.LICENSE");
        ThirdPartyNoticesTextBox.Text = ReadEmbeddedText("PdfTitleRenamer.THIRD-PARTY-NOTICES.txt");
    }

    private static string ReadEmbeddedText(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return "ライセンス文書を読み込めませんでした。配布フォルダー内の文書を確認してください。";
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        OpenWithShell(e.Uri.AbsoluteUri, "GitHubページを開けませんでした。");
        e.Handled = true;
    }

    private void OpenThirdPartyLicensesButton_Click(object sender, RoutedEventArgs e)
    {
        var licensesFolder = Path.Combine(AppContext.BaseDirectory, "ThirdPartyLicenses");
        if (Directory.Exists(licensesFolder))
        {
            OpenWithShell(licensesFolder, "第三者ライセンスフォルダーを開けませんでした。");
            return;
        }

        var noticesPath = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
        if (File.Exists(noticesPath))
        {
            OpenWithShell(noticesPath, "第三者ライセンス文書を開けませんでした。");
            return;
        }

        MessageBox.Show(
            "配布フォルダー内に第三者ライセンス文書が見つかりません。画面上の概要は引き続き確認できます。",
            "ライセンス文書",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void OpenWithShell(string target, string errorMessage)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"{errorMessage}\n\n{ex.Message}",
                "ライセンス情報",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
