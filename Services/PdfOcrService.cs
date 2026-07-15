using System.IO;
using PDFtoImage;
using PDFtoImage.Exceptions;
using PdfTitleRenamer.Models;
using SkiaSharp;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

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
            var renderedPages = await Task.Run(
                () => RenderFirstPages(pdfPath, temporaryDirectory, cancellationToken),
                cancellationToken);
            var pages = new List<OcrPageData>();

            foreach (var renderedPage in renderedPages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var imageFile = await StorageFile.GetFileFromPathAsync(renderedPage.ImagePath);
                using var imageStream = await imageFile.OpenReadAsync();
                var decoder = await BitmapDecoder.CreateAsync(imageStream);
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
                    renderedPage.PageNumber,
                    renderedPage.Width,
                    renderedPage.Height,
                    renderedPage.ImagePath,
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

    private static IReadOnlyList<RenderedPage> RenderFirstPages(
        string pdfPath,
        string temporaryDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            using var pdfStream = File.OpenRead(Path.GetFullPath(pdfPath));
            var pageSizes = Conversion.GetPageSizes(pdfStream, leaveOpen: true);
            if (pageSizes.Count == 0)
            {
                throw new InvalidDataException("ページがないPDFです。");
            }

            var renderedPages = new List<RenderedPage>();
            var pageCount = Math.Min(PagesToRead, pageSizes.Count);
            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pageSize = pageSizes[pageIndex];
                var scale = RenderScale;
                if (pageSize.Width * scale > MaximumRenderWidth)
                {
                    scale = MaximumRenderWidth / pageSize.Width;
                }

                var width = Math.Max(1, (int)Math.Round(pageSize.Width * scale));
                var height = Math.Max(1, (int)Math.Round(pageSize.Height * scale));
                var options = new RenderOptions(
                    Width: width,
                    Height: height,
                    BackgroundColor: SKColors.White);

                pdfStream.Position = 0;
                using var image = Conversion.ToImage(
                    pdfStream,
                    page: pageIndex,
                    leaveOpen: true,
                    options: options);
                cancellationToken.ThrowIfCancellationRequested();

                var imagePath = Path.Combine(temporaryDirectory, $"page-{pageIndex + 1}.png");
                using (var output = File.Create(imagePath))
                {
                    if (!image.Encode(output, SKEncodedImageFormat.Png, 100))
                    {
                        throw new InvalidDataException("PDFページをPNG画像へ変換できませんでした。");
                    }
                }

                renderedPages.Add(new RenderedPage(
                    pageIndex + 1,
                    checked((uint)image.Width),
                    checked((uint)image.Height),
                    imagePath));
            }

            return renderedPages;
        }
        catch (PdfPasswordProtectedException ex)
        {
            throw new InvalidDataException("パスワードが必要なPDFです。パスワード保護を解除してから再実行してください。", ex);
        }
        catch (PdfUnsupportedSecuritySchemeException ex)
        {
            throw new InvalidDataException("PDFiumが対応していないセキュリティ方式のPDFです。", ex);
        }
        catch (PdfInvalidFormatException ex)
        {
            throw new InvalidDataException("PDFの形式が不正または破損しています。", ex);
        }
        catch (PdfCannotOpenFileException ex)
        {
            throw new IOException("PDFファイルを開けませんでした。使用中またはアクセス権を確認してください。", ex);
        }
    }

    private sealed record RenderedPage(int PageNumber, uint Width, uint Height, string ImagePath);

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
