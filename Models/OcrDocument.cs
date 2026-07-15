using System.IO;

namespace PdfTitleRenamer.Models;

public sealed record OcrWordData(string Text, double X, double Y, double Width, double Height);

public sealed record OcrLineData(string Text, IReadOnlyList<OcrWordData> Words);

public sealed record OcrPageData(
    int PageNumber,
    double Width,
    double Height,
    string ImagePath,
    IReadOnlyList<OcrLineData> Lines);

public sealed class OcrDocument : IDisposable
{
    public OcrDocument(string temporaryDirectory, IReadOnlyList<OcrPageData> pages)
    {
        TemporaryDirectory = temporaryDirectory;
        Pages = pages;
    }

    public string TemporaryDirectory { get; }

    public IReadOnlyList<OcrPageData> Pages { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(TemporaryDirectory))
            {
                Directory.Delete(TemporaryDirectory, recursive: true);
            }
        }
        catch
        {
            // Temporary images are cleaned on a best-effort basis.
        }
    }
}

public sealed record TitleSuggestion(string Title, double Confidence, string Reason);
