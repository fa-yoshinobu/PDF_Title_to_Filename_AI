using System.IO;
using PdfTitleRenamer.Models;
using Windows.Data.Pdf;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PdfTitleRenamer.Services;

public sealed class PdfOcrService
{
    private const int PagesToRead = 2;
    private const double RenderScale = 2.4;
    private const uint MaximumRenderWidth = 2400;

    public async Task<OcrDocument> ReadFirstPagesAsync(string pdfPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);

        var engine = CreateOcrEngine();
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "PdfTitleRenamer",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(pdfPath));
            var pdf = await PdfDocument.LoadFromFileAsync(storageFile);
            if (pdf.PageCount == 0)
            {
                throw new InvalidDataException("ページがないPDFです。");
            }

            var pages = new List<OcrPageData>();
            var pageCount = Math.Min(PagesToRead, checked((int)pdf.PageCount));

            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var page = pdf.GetPage((uint)pageIndex);
                using var rendered = new InMemoryRandomAccessStream();

                var scale = RenderScale;
                if (page.Size.Width * scale > MaximumRenderWidth)
                {
                    scale = MaximumRenderWidth / page.Size.Width;
                }

                var renderOptions = new PdfPageRenderOptions
                {
                    DestinationWidth = Math.Max(1, (uint)Math.Round(page.Size.Width * scale)),
                    DestinationHeight = Math.Max(1, (uint)Math.Round(page.Size.Height * scale)),
                    BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255)
                };

                await page.RenderToStreamAsync(rendered, renderOptions);
                cancellationToken.ThrowIfCancellationRequested();

                var imagePath = Path.Combine(temporaryDirectory, $"page-{pageIndex + 1}.png");
                rendered.Seek(0);
                using (var input = rendered.GetInputStreamAt(0))
                using (var dataReader = new DataReader(input))
                {
                    var byteCount = checked((uint)rendered.Size);
                    await dataReader.LoadAsync(byteCount);
                    var imageBytes = new byte[byteCount];
                    dataReader.ReadBytes(imageBytes);
                    await File.WriteAllBytesAsync(imagePath, imageBytes, cancellationToken);
                }

                rendered.Seek(0);
                var decoder = await BitmapDecoder.CreateAsync(rendered);
                using var bitmap = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied);
                var result = await engine.RecognizeAsync(bitmap);

                var lines = result.Lines
                    .Select(line => new OcrLineData(
                        line.Text,
                        line.Words.Select(word => new OcrWordData(
                            word.Text,
                            word.BoundingRect.X,
                            word.BoundingRect.Y,
                            word.BoundingRect.Width,
                            word.BoundingRect.Height)).ToArray()))
                    .ToArray();

                pages.Add(new OcrPageData(
                    pageIndex + 1,
                    renderOptions.DestinationWidth,
                    renderOptions.DestinationHeight,
                    imagePath,
                    lines));
            }

            return new OcrDocument(temporaryDirectory, pages);
        }
        catch
        {
            TryDeleteDirectory(temporaryDirectory);
            throw;
        }
    }

    private static OcrEngine CreateOcrEngine()
    {
        try
        {
            var japanese = new Language("ja-JP");
            if (OcrEngine.IsLanguageSupported(japanese))
            {
                return OcrEngine.TryCreateFromLanguage(japanese)
                    ?? throw new InvalidOperationException("日本語OCRを初期化できませんでした。");
            }
        }
        catch
        {
            // Fall through to the user's installed OCR languages.
        }

        return OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException(
                "Windows OCRを初期化できません。Windowsの言語設定で日本語のOCR機能を追加してください。");
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort only.
        }
    }
}
