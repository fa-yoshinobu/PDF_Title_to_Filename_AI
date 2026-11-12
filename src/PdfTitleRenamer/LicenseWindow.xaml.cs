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
        var notices = new StringBuilder(ReadEmbeddedText("PdfTitleRenamer.THIRD-PARTY-NOTICES.txt"));
        const string licensePrefix = "PdfTitleRenamer.ThirdPartyLicenses/";
        foreach (var resource in Assembly.GetExecutingAssembly().GetManifestResourceNames()
                     .Where(name => name.StartsWith(licensePrefix, StringComparison.Ordinal))
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            notices.AppendLine().AppendLine().AppendLine(new string('=', 72));
            notices.AppendLine("ThirdPartyLicenses/" + resource[licensePrefix.Length..].Replace('\\', '/'));
            notices.AppendLine(new string('=', 72)).AppendLine(ReadEmbeddedText(resource));
        }
        ThirdPartyNoticesTextBox.Text = notices.ToString();
    }

    private static string ReadEmbeddedText(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return "EXEに埋め込まれたライセンス文書を読み込めませんでした。";
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        OpenWithShell(e.Uri.AbsoluteUri, "GitHubページを開けませんでした。");
        e.Handled = true;
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
