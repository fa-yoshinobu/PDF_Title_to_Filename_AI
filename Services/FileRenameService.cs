using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using PdfTitleRenamer.Models;

namespace PdfTitleRenamer.Services;

public sealed record RenamePlan(PdfRenameItem Item, string SourcePath, string TargetPath);

public sealed record RenameBatchResult(int Renamed, int Unchanged, IReadOnlyList<string> Errors);

public sealed partial class FileRenameService
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public IReadOnlyList<RenamePlan> BuildPlan(IEnumerable<PdfRenameItem> items)
    {
        var selected = items
            .Where(item => item.IsSelected && !string.IsNullOrWhiteSpace(item.SuggestedName))
            .ToArray();
        var reservedByFolder = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var plans = new List<RenamePlan>(selected.Length);

        foreach (var item in selected)
        {
            var sourcePath = Path.GetFullPath(item.SourcePath);
            var folder = Path.GetDirectoryName(sourcePath)
                ?? throw new InvalidOperationException("PDFのフォルダを取得できません。");

            if (!reservedByFolder.TryGetValue(folder, out var reserved))
            {
                reserved = Directory.EnumerateFiles(folder)
                    .Select(Path.GetFileName)
                    .Where(name => name is not null)
                    .Cast<string>()
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                reservedByFolder.Add(folder, reserved);
            }

            reserved.Remove(Path.GetFileName(sourcePath));
            var baseTitle = SanitizeTitle(item.SuggestedName, GetMaximumBaseLength(folder));
            var candidate = baseTitle + ".pdf";
            var suffix = 2;

            while (reserved.Contains(candidate))
            {
                var suffixText = $" ({suffix++})";
                var truncated = TruncateText(baseTitle, Math.Max(1, GetMaximumBaseLength(folder) - suffixText.Length));
                candidate = truncated + suffixText + ".pdf";
            }

            reserved.Add(candidate);
            plans.Add(new RenamePlan(item, sourcePath, Path.Combine(folder, candidate)));
        }

        return plans;
    }

    public RenameBatchResult Execute(IReadOnlyList<RenamePlan> plans)
    {
        var renamed = 0;
        var unchanged = 0;
        var errors = new List<string>();

        foreach (var plan in plans)
        {
            try
            {
                if (string.Equals(plan.SourcePath, plan.TargetPath, StringComparison.OrdinalIgnoreCase))
                {
                    plan.Item.Status = "変更なし";
                    unchanged++;
                    continue;
                }

                if (!File.Exists(plan.SourcePath))
                {
                    throw new FileNotFoundException("元のPDFが見つかりません。", plan.SourcePath);
                }

                if (File.Exists(plan.TargetPath))
                {
                    throw new IOException("同名のファイルがすでに存在します。");
                }

                File.Move(plan.SourcePath, plan.TargetPath);
                plan.Item.SourcePath = plan.TargetPath;
                plan.Item.SuggestedName = Path.GetFileName(plan.TargetPath);
                plan.Item.Status = "リネーム済み";
                renamed++;
            }
            catch (Exception ex)
            {
                plan.Item.Status = "リネーム失敗";
                errors.Add($"{Path.GetFileName(plan.SourcePath)}: {ex.Message}");
            }
        }

        return new RenameBatchResult(renamed, unchanged, errors);
    }

    public static string SanitizeTitle(string title, int maximumLength = 140)
    {
        var value = title.Normalize(NormalizationForm.FormKC).Trim();
        if (value.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalidCharacters.Contains(character) || char.IsControl(character) ? ' ' : character);
        }

        value = WhitespaceRegex().Replace(builder.ToString(), " ").Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(value))
        {
            value = "無題";
        }

        if (ReservedNames.Contains(value))
        {
            value = "_" + value;
        }

        value = TruncateText(value, Math.Max(1, maximumLength)).Trim(' ', '.');
        return string.IsNullOrWhiteSpace(value) ? "無題" : value;
    }

    private static int GetMaximumBaseLength(string folder)
    {
        var remainingPathLength = 238 - Path.GetFullPath(folder).Length - 5;
        return Math.Clamp(remainingPathLength, 24, 140);
    }

    private static string TruncateText(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
