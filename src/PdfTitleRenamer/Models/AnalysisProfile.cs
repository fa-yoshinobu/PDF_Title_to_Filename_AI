namespace PdfTitleRenamer.Models;

public sealed record AnalysisProfile
{
    private AnalysisProfile(string displayName, int pageCount, int imageLimit, int ocrLimit, string effort, string description)
    {
        DisplayName = displayName;
        PageCount = pageCount;
        MaximumImageDimension = imageLimit;
        MaximumOcrLines = ocrLimit;
        ReasoningEffort = effort;
        Description = description;
    }

    public string DisplayName { get; }
    public int PageCount { get; }
    public int MaximumImageDimension { get; }
    public int MaximumOcrLines { get; }
    public string ReasoningEffort { get; }
    public string Description { get; }

    public static AnalysisProfile Light { get; } = new("軽量（1ページ・節約）", 1, 1600, 60, "low",
        "先頭1ページ・画像長辺最大1600px・OCR最大60行／ページ・推論は軽め。まずはこちらで試し、読み取りが難しいPDFだけ標準・詳細へ切り替えてください。");
    public static AnalysisProfile Standard { get; } = new("標準（2ページ・バランス）", 2, 2400, 150, "medium",
        "先頭2ページ・画像長辺最大2400px・OCR最大150行／ページ・推論は標準。表紙と次ページを照らし合わせます。軽量より利用量が増える場合があります。");
    public static AnalysisProfile Detailed { get; } = new("詳細（5ページ・詳しく）", 5, 2400, 300, "high",
        "先頭5ページ・画像長辺最大2400px・OCR最大300行／ページ・推論は詳しく。タイトルが曖昧なPDF向けです。時間・利用量が増える場合があります。");

    public static IReadOnlyList<AnalysisProfile> All { get; } = Array.AsReadOnly(new[] { Light, Standard, Detailed });
}
