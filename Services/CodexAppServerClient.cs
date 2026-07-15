using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using PdfTitleRenamer.Models;

namespace PdfTitleRenamer.Services;

public sealed class CodexAppServerClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly Dictionary<string, long> _threadTokenTotals = new(StringComparer.Ordinal);
    private Process? _process;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private Task<string>? _stderrTask;
    private long _lastTurnTokens;
    private CodexRateLimitWindow? _primaryLimit;
    private CodexRateLimitWindow? _secondaryLimit;
    private CodexCreditsSnapshot? _credits;
    private CodexSpendControlSnapshot? _individualLimit;
    private string? _planType;
    private int _requestId;

    public CodexUsageSnapshot Usage { get; private set; } = CodexUsageSnapshot.Empty;

    public event Action<CodexUsageSnapshot>? UsageUpdated;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_process is not null)
        {
            return;
        }

        var startInfo = CodexProcess.CreateStartInfo(
            ["app-server", "--listen", "stdio://"],
            redirect: true);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            process.Start();
        }
        catch
        {
            process.Dispose();
            throw;
        }

        _process = process;
        _writer = process.StandardInput;
        _reader = process.StandardOutput;
        _stderrTask = process.StandardError.ReadToEndAsync();

        var initializeId = NextRequestId();
        await SendAsync(new
        {
            id = initializeId,
            method = "initialize",
            @params = new
            {
                clientInfo = new
                {
                    name = "pdf_title_renamer",
                    title = "PDF Title Renamer AI",
                    version = "0.1.5"
                }
            }
        }, cancellationToken);
        _ = await ReadResponseAsync(initializeId, cancellationToken);

        await SendAsync(new
        {
            method = "initialized",
            @params = new { }
        }, cancellationToken);

        await TryRefreshAccountRateLimitsAsync(cancellationToken);
    }

    public async Task<TitleSuggestion> SuggestTitleAsync(
        OcrDocument document,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            await StartAsync(cancellationToken);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    return await SuggestTitleOnceAsync(document, workingDirectory, cancellationToken);
                }
                catch (InvalidDataException ex) when (
                    attempt == 0 &&
                    string.Equals(ex.Message, "Codexの応答が空でした。", StringComparison.Ordinal))
                {
                    // A completed turn can rarely omit its final item. Retry once on a fresh thread.
                }
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task<TitleSuggestion> SuggestTitleOnceAsync(
        OcrDocument document,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var threadRequestId = NextRequestId();
        await SendAsync(new
        {
            id = threadRequestId,
            method = "thread/start",
            @params = new
            {
                cwd = Path.GetFullPath(workingDirectory),
                approvalPolicy = "never",
                sandbox = "read-only",
                ephemeral = true,
                personality = "pragmatic",
                developerInstructions =
                    "You are a deterministic document-title classifier. " +
                    "Do not call tools, inspect files, use the web, or infer from file names or metadata. " +
                    "Use only the supplied page images and OCR layout. Return only the requested JSON."
            }
        }, cancellationToken);

        using var threadResponse = await ReadResponseAsync(threadRequestId, cancellationToken);
        var threadId = threadResponse.RootElement
            .GetProperty("result")
            .GetProperty("thread")
            .GetProperty("id")
            .GetString()
            ?? throw new InvalidDataException("CodexからスレッドIDが返されませんでした。");

        var turnRequestId = NextRequestId();
        await SendAsync(new
        {
            id = turnRequestId,
            method = "turn/start",
            @params = new
            {
                threadId,
                input = BuildInputs(document),
                approvalPolicy = "never",
                sandboxPolicy = new { type = "readOnly", networkAccess = false },
                effort = "medium",
                summary = "none",
                outputSchema = CreateOutputSchema()
            }
        }, cancellationToken);

        return await ReadTitleResultAsync(turnRequestId, cancellationToken);
    }

    public static TitleSuggestion ParseSuggestion(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            throw new InvalidDataException("Codexの応答が空でした。");
        }

        var trimmed = responseText.Trim();
        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace < 0 || lastBrace <= firstBrace)
        {
            throw new InvalidDataException("Codexの応答をJSONとして解釈できませんでした。");
        }

        var dto = JsonSerializer.Deserialize<TitleSuggestionDto>(
            trimmed[firstBrace..(lastBrace + 1)],
            JsonOptions)
            ?? throw new InvalidDataException("Codexのタイトル候補を読み取れませんでした。");

        if (string.IsNullOrWhiteSpace(dto.Title))
        {
            throw new InvalidDataException("Codexからタイトルが返されませんでした。");
        }

        return new TitleSuggestion(
            dto.Title.Trim(),
            Math.Clamp(dto.Confidence, 0, 1),
            string.IsNullOrWhiteSpace(dto.Reason) ? "判断根拠なし" : dto.Reason.Trim());
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is null)
        {
            _operationLock.Dispose();
            return;
        }

        try
        {
            _process.StandardInput.Close();
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }

            await _process.WaitForExitAsync();
        }
        catch
        {
            // Process cleanup is best effort.
        }
        finally
        {
            _process.Dispose();
            _process = null;
            _writer = null;
            _reader = null;
            _operationLock.Dispose();
        }
    }

    private static object[] BuildInputs(OcrDocument document)
    {
        var inputs = new List<object>
        {
            new
            {
                type = "text",
                text = BuildPrompt(document)
            }
        };

        foreach (var page in document.Pages)
        {
            inputs.Add(new
            {
                type = "text",
                text = $"以下の画像はPDFの{page.PageNumber}ページ目です。"
            });
            inputs.Add(new
            {
                type = "localImage",
                path = Path.GetFullPath(page.ImagePath),
                detail = "original"
            });
        }

        return inputs.ToArray();
    }

    private static string BuildPrompt(OcrDocument document)
    {
        var pages = document.Pages.Select(page => new
        {
            page = page.PageNumber,
            width = Math.Round(page.Width, 1),
            height = Math.Round(page.Height, 1),
            lines = page.Lines
                .Where(line => !string.IsNullOrWhiteSpace(line.Text))
                .Take(300)
                .Select(line =>
                {
                    var words = line.Words;
                    var x = words.Count == 0 ? 0 : words.Min(word => word.X);
                    var y = words.Count == 0 ? 0 : words.Min(word => word.Y);
                    var right = words.Count == 0 ? 0 : words.Max(word => word.X + word.Width);
                    var bottom = words.Count == 0 ? 0 : words.Max(word => word.Y + word.Height);
                    return new
                    {
                        text = line.Text.Length <= 400 ? line.Text : line.Text[..400],
                        x = Math.Round(x, 1),
                        y = Math.Round(y, 1),
                        width = Math.Round(Math.Max(0, right - x), 1),
                        height = Math.Round(Math.Max(0, bottom - y), 1)
                    };
                })
        });

        var ocrJson = JsonSerializer.Serialize(pages);
        var prompt = new StringBuilder();
        prompt.AppendLine("このPDFの正式な文書タイトルを推定してください。");
        prompt.AppendLine("前提と制約:");
        prompt.AppendLine("- PDFのタイトルプロパティ、作成者、ファイル名などのメタデータは存在せず、判断にも使用してはいけません。");
        prompt.AppendLine("- 対象は先頭2ページだけです。OCR文字列、文字ブロックの座標・大きさ、添付されたページ画像を総合判断してください。");
        prompt.AppendLine("- ページ上部、中央配置、大きい文字、独立した見出しを重視し、会社名、日付、版番号、文書番号、ヘッダー、フッターは原則タイトルから除外してください。");
        prompt.AppendLine("- 主題に必要な副題は「主題 — 副題」の形で残して構いません。");
        prompt.AppendLine("- OCR誤認はページ画像と文脈から補正してください。推測できない固有名詞を創作しないでください。");
        prompt.AppendLine("- Windowsファイル名として自然で簡潔なタイトルにしてください。拡張子 .pdf は付けないでください。");
        prompt.AppendLine();
        prompt.AppendLine("OCRレイアウトJSON:");
        prompt.Append(ocrJson);
        return prompt.ToString();
    }

    private static object CreateOutputSchema() => new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "title", "confidence", "reason" },
        properties = new
        {
            title = new { type = "string", minLength = 1, maxLength = 160 },
            confidence = new { type = "number", minimum = 0, maximum = 1 },
            reason = new { type = "string", minLength = 1, maxLength = 300 }
        }
    };

    private async Task<TitleSuggestion> ReadTitleResultAsync(int requestId, CancellationToken cancellationToken)
    {
        var messages = new AgentMessageCollector();
        var requestAcknowledged = false;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));

        while (true)
        {
            using var message = await ReadMessageAsync(timeout.Token);
            var root = message.RootElement;

            if (TryGetResponseId(root, out var responseId) && responseId == requestId)
            {
                ThrowIfError(root);
                requestAcknowledged = true;
                continue;
            }

            if (!root.TryGetProperty("method", out var methodElement))
            {
                continue;
            }

            var method = methodElement.GetString();
            messages.Observe(root, method);

            if (method == "error")
            {
                throw new InvalidOperationException(GetNotificationError(root));
            }

            if (method != "turn/completed" || !root.TryGetProperty("params", out var completedParams))
            {
                continue;
            }

            if (!requestAcknowledged)
            {
                throw new InvalidDataException("Codexがturn/startを確認する前に処理が終了しました。");
            }

            var turn = completedParams.GetProperty("turn");
            var status = turn.GetProperty("status").GetString();
            if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                var error = turn.TryGetProperty("error", out var errorElement) &&
                            errorElement.ValueKind == JsonValueKind.Object &&
                            errorElement.TryGetProperty("message", out var errorMessage)
                    ? errorMessage.GetString()
                    : "Codexの処理が完了しませんでした。";
                throw new InvalidOperationException(error);
            }

            return ParseSuggestion(messages.GetFinalText(turn) ?? string.Empty);
        }
    }

    private async Task<JsonDocument> ReadResponseAsync(
        int requestId,
        CancellationToken cancellationToken,
        TimeSpan? timeoutDuration = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutDuration ?? TimeSpan.FromSeconds(45));

        while (true)
        {
            var message = await ReadMessageAsync(timeout.Token);
            if (!TryGetResponseId(message.RootElement, out var responseId) || responseId != requestId)
            {
                message.Dispose();
                continue;
            }

            ThrowIfError(message.RootElement);
            return message;
        }
    }

    private async Task SendAsync(object message, CancellationToken cancellationToken)
    {
        if (_writer is null)
        {
            throw new InvalidOperationException("Codex App Serverが起動していません。");
        }

        var json = JsonSerializer.Serialize(message);
        await _writer.WriteLineAsync(json.AsMemory(), cancellationToken);
        await _writer.FlushAsync(cancellationToken);
    }

    private async Task<JsonDocument> ReadMessageAsync(CancellationToken cancellationToken)
    {
        if (_reader is null || _process is null)
        {
            throw new InvalidOperationException("Codex App Serverが起動していません。");
        }

        while (true)
        {
            var line = await _reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                var stderr = _stderrTask is null ? string.Empty : await _stderrTask;
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(stderr)
                        ? "Codex App Serverが予期せず終了しました。"
                        : $"Codex App Serverが終了しました: {stderr.Trim()}");
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var message = JsonDocument.Parse(line);
                try
                {
                    ObserveUsageMessage(message.RootElement);
                }
                catch (Exception)
                {
                    // Optional usage telemetry must never break title analysis.
                }

                return message;
            }
            catch (JsonException)
            {
                // Stdout should be JSONL. Ignore an unexpected diagnostic line defensively.
            }
        }
    }

    private async Task TryRefreshAccountRateLimitsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var requestId = NextRequestId();
            await SendAsync(new
            {
                id = requestId,
                method = "account/rateLimits/read"
            }, cancellationToken);

            using var response = await ReadResponseAsync(
                requestId,
                cancellationToken,
                TimeSpan.FromSeconds(10));
            if (response.RootElement.TryGetProperty("result", out var result))
            {
                ApplyRateLimitsResponse(result);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Usage information is optional and must not block title analysis.
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Older CLI versions and some plans may not expose account usage.
        }
    }

    private void ObserveUsageMessage(JsonElement root)
    {
        if (!root.TryGetProperty("method", out var methodElement) ||
            !root.TryGetProperty("params", out var parameters))
        {
            return;
        }

        switch (methodElement.GetString())
        {
            case "thread/tokenUsage/updated":
                ApplyTokenUsage(parameters);
                break;
            case "account/rateLimits/updated":
                if (parameters.TryGetProperty("rateLimits", out var rateLimits))
                {
                    ApplyRateLimitSnapshot(rateLimits);
                }

                break;
        }
    }

    private void ApplyTokenUsage(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("threadId", out var threadIdElement) ||
            !parameters.TryGetProperty("tokenUsage", out var tokenUsage) ||
            !tokenUsage.TryGetProperty("total", out var total) ||
            !TryGetInt64(total, "totalTokens", out var totalTokens))
        {
            return;
        }

        var threadId = threadIdElement.GetString();
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return;
        }

        _threadTokenTotals[threadId] = Math.Max(0, totalTokens);
        if (tokenUsage.TryGetProperty("last", out var last) &&
            TryGetInt64(last, "totalTokens", out var lastTokens))
        {
            _lastTurnTokens = Math.Max(0, lastTokens);
        }

        PublishUsage();
    }

    private void ApplyRateLimitsResponse(JsonElement result)
    {
        JsonElement selected = default;
        if (result.TryGetProperty("rateLimitsByLimitId", out var byLimitId) &&
            byLimitId.ValueKind == JsonValueKind.Object)
        {
            if (!byLimitId.TryGetProperty("codex", out selected))
            {
                foreach (var property in byLimitId.EnumerateObject())
                {
                    selected = property.Value;
                    break;
                }
            }
        }

        if (selected.ValueKind != JsonValueKind.Object &&
            result.TryGetProperty("rateLimits", out var legacyRateLimits))
        {
            selected = legacyRateLimits;
        }

        if (selected.ValueKind == JsonValueKind.Object)
        {
            ApplyRateLimitSnapshot(selected);
        }
    }

    private void ApplyRateLimitSnapshot(JsonElement snapshot)
    {
        _primaryLimit = TryParseRateLimitWindow(snapshot, "primary") ?? _primaryLimit;
        _secondaryLimit = TryParseRateLimitWindow(snapshot, "secondary") ?? _secondaryLimit;
        _credits = TryParseCredits(snapshot) ?? _credits;
        _individualLimit = TryParseIndividualLimit(snapshot) ?? _individualLimit;
        if (snapshot.TryGetProperty("planType", out var planType) &&
            planType.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(planType.GetString()))
        {
            _planType = planType.GetString();
        }

        PublishUsage();
    }

    private void PublishUsage()
    {
        var sessionTotal = _threadTokenTotals.Values.Sum();
        var snapshot = new CodexUsageSnapshot(
            sessionTotal,
            _lastTurnTokens,
            _primaryLimit,
            _secondaryLimit,
            _credits,
            _individualLimit,
            _planType);
        if (snapshot == Usage)
        {
            return;
        }

        Usage = snapshot;
        UsageUpdated?.Invoke(snapshot);
    }

    private static CodexRateLimitWindow? TryParseRateLimitWindow(JsonElement snapshot, string propertyName)
    {
        if (!snapshot.TryGetProperty(propertyName, out var window) ||
            window.ValueKind != JsonValueKind.Object ||
            !TryGetDouble(window, "usedPercent", out var usedPercent))
        {
            return null;
        }

        int? duration = TryGetInt32(window, "windowDurationMins", out var durationValue)
            ? durationValue
            : null;
        return new CodexRateLimitWindow(
            Math.Clamp(usedPercent, 0, 100),
            duration,
            TryGetUnixTime(window, "resetsAt"));
    }

    private static CodexCreditsSnapshot? TryParseCredits(JsonElement snapshot)
    {
        if (!snapshot.TryGetProperty("credits", out var credits) ||
            credits.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var hasCredits = credits.TryGetProperty("hasCredits", out var hasCreditsElement) &&
                         hasCreditsElement.ValueKind == JsonValueKind.True;
        var unlimited = credits.TryGetProperty("unlimited", out var unlimitedElement) &&
                        unlimitedElement.ValueKind == JsonValueKind.True;
        var balance = credits.TryGetProperty("balance", out var balanceElement) &&
                      balanceElement.ValueKind == JsonValueKind.String
            ? balanceElement.GetString()
            : null;
        return new CodexCreditsSnapshot(hasCredits, unlimited, balance);
    }

    private static CodexSpendControlSnapshot? TryParseIndividualLimit(JsonElement snapshot)
    {
        if (!snapshot.TryGetProperty("individualLimit", out var limit) ||
            limit.ValueKind != JsonValueKind.Object ||
            !limit.TryGetProperty("limit", out var limitElement) ||
            !limit.TryGetProperty("used", out var usedElement) ||
            !TryGetDouble(limit, "remainingPercent", out var remainingPercent))
        {
            return null;
        }

        return new CodexSpendControlSnapshot(
            limitElement.GetString() ?? string.Empty,
            usedElement.GetString() ?? string.Empty,
            Math.Clamp(remainingPercent, 0, 100),
            TryGetUnixTime(limit, "resetsAt"));
    }

    private static bool TryGetInt64(JsonElement parent, string propertyName, out long value)
    {
        value = 0;
        return parent.TryGetProperty(propertyName, out var element) &&
               element.ValueKind == JsonValueKind.Number &&
               element.TryGetInt64(out value);
    }

    private static bool TryGetInt32(JsonElement parent, string propertyName, out int value)
    {
        value = 0;
        return parent.TryGetProperty(propertyName, out var element) &&
               element.ValueKind == JsonValueKind.Number &&
               element.TryGetInt32(out value);
    }

    private static bool TryGetDouble(JsonElement parent, string propertyName, out double value)
    {
        value = 0;
        return parent.TryGetProperty(propertyName, out var element) &&
               element.ValueKind == JsonValueKind.Number &&
               element.TryGetDouble(out value);
    }

    private static DateTimeOffset? TryGetUnixTime(JsonElement parent, string propertyName)
    {
        return TryGetInt64(parent, propertyName, out var unixTime)
            ? DateTimeOffset.FromUnixTimeSeconds(unixTime)
            : null;
    }

    private static string? FindLastAgentMessage(JsonElement turn)
    {
        if (!turn.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? result = null;
        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var type) &&
                type.GetString() == "agentMessage" &&
                item.TryGetProperty("text", out var text))
            {
                var candidate = text.GetString();
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    result = candidate;
                }
            }
        }

        return result;
    }

    private static string GetNotificationError(JsonElement root)
    {
        if (!root.TryGetProperty("params", out var parameters))
        {
            return "Codex App Serverでエラーが発生しました。";
        }

        if (parameters.TryGetProperty("error", out var error))
        {
            if (error.ValueKind == JsonValueKind.Object &&
                error.TryGetProperty("message", out var nestedMessage))
            {
                return nestedMessage.GetString() ?? "Codex App Serverでエラーが発生しました。";
            }

            if (error.ValueKind == JsonValueKind.String)
            {
                return error.GetString() ?? "Codex App Serverでエラーが発生しました。";
            }
        }

        return parameters.TryGetProperty("message", out var message)
            ? message.GetString() ?? "Codex App Serverでエラーが発生しました。"
            : "Codex App Serverでエラーが発生しました。";
    }

    private static bool TryGetResponseId(JsonElement root, out int id)
    {
        id = 0;
        return root.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out id);
    }

    private static void ThrowIfError(JsonElement root)
    {
        if (!root.TryGetProperty("error", out var error) || error.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        var message = error.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString()
            : error.GetRawText();
        throw new InvalidOperationException(message ?? "Codex App Serverでエラーが発生しました。");
    }

    private int NextRequestId() => Interlocked.Increment(ref _requestId);

    private sealed class AgentMessageCollector
    {
        private readonly Dictionary<string, StringBuilder> _streamedMessages = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _completedMessages = new(StringComparer.Ordinal);
        private readonly List<string> _messageOrder = new();

        public void Observe(JsonElement root, string? method)
        {
            if (!root.TryGetProperty("params", out var parameters))
            {
                return;
            }

            if (method == "item/agentMessage/delta" &&
                parameters.TryGetProperty("itemId", out var itemIdElement) &&
                parameters.TryGetProperty("delta", out var deltaElement))
            {
                var itemId = itemIdElement.GetString();
                var delta = deltaElement.GetString();
                if (!string.IsNullOrEmpty(itemId) && !string.IsNullOrEmpty(delta))
                {
                    GetStream(itemId).Append(delta);
                }

                return;
            }

            if (method != "item/completed" ||
                !parameters.TryGetProperty("item", out var item) ||
                !item.TryGetProperty("type", out var itemType) ||
                itemType.GetString() != "agentMessage" ||
                !item.TryGetProperty("id", out var completedIdElement) ||
                !item.TryGetProperty("text", out var textElement))
            {
                return;
            }

            var completedId = completedIdElement.GetString();
            var text = textElement.GetString();
            if (!string.IsNullOrEmpty(completedId) && !string.IsNullOrWhiteSpace(text))
            {
                EnsureMessage(completedId);
                _completedMessages[completedId] = text;
            }
        }

        public string? GetFinalText(JsonElement turn)
        {
            var turnMessage = FindLastAgentMessage(turn);
            if (!string.IsNullOrWhiteSpace(turnMessage))
            {
                return turnMessage;
            }

            for (var index = _messageOrder.Count - 1; index >= 0; index--)
            {
                var itemId = _messageOrder[index];
                if (_completedMessages.TryGetValue(itemId, out var completed) &&
                    !string.IsNullOrWhiteSpace(completed))
                {
                    return completed;
                }

                if (_streamedMessages.TryGetValue(itemId, out var streamed) && streamed.Length > 0)
                {
                    var text = streamed.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }

            return null;
        }

        private StringBuilder GetStream(string itemId)
        {
            EnsureMessage(itemId);
            return _streamedMessages[itemId];
        }

        private void EnsureMessage(string itemId)
        {
            if (_streamedMessages.ContainsKey(itemId))
            {
                return;
            }

            _streamedMessages.Add(itemId, new StringBuilder());
            _messageOrder.Add(itemId);
        }
    }

    private sealed class TitleSuggestionDto
    {
        public string Title { get; set; } = string.Empty;

        public double Confidence { get; set; }

        public string Reason { get; set; } = string.Empty;
    }
}
