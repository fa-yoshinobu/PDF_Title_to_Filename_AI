namespace PdfTitleRenamer.Models;

public sealed record CodexRateLimitWindow(
    double UsedPercent,
    int? WindowDurationMinutes,
    DateTimeOffset? ResetsAt);

public sealed record CodexCreditsSnapshot(
    bool HasCredits,
    bool Unlimited,
    string? Balance);

public sealed record CodexSpendControlSnapshot(
    string Limit,
    string Used,
    double RemainingPercent,
    DateTimeOffset? ResetsAt);

public sealed record CodexUsageSnapshot(
    long SessionTotalTokens,
    long LastTurnTokens,
    CodexRateLimitWindow? PrimaryLimit,
    CodexRateLimitWindow? SecondaryLimit,
    CodexCreditsSnapshot? Credits,
    CodexSpendControlSnapshot? IndividualLimit,
    string? PlanType)
{
    public static CodexUsageSnapshot Empty { get; } = new(
        0,
        0,
        null,
        null,
        null,
        null,
        null);
}
