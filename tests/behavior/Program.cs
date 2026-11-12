using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using PdfTitleRenamer;
using PdfTitleRenamer.Models;
using PdfTitleRenamer.Services;

var root = Path.Combine(Path.GetTempPath(), "PdfRenamerChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); checks++; }
PdfRenameItem Add(string name, string suggestion)
{
    var path = Path.Combine(root, name + ".pdf");
    File.WriteAllText(path, name);
    return new PdfRenameItem(path) { SuggestedName = suggestion };
}
try
{
    var service = new FileRenameService();
    var a = Add("A", "B.pdf"); var b = Add("B", "A.pdf"); var c = Add("C", "B.pdf");
    var plans = service.BuildPlan([a, b, c]);
    Check(plans.Select(p => p.TargetPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3, "batch destinations are unique");
    Check(Path.GetFileName(plans[0].TargetPath) == "B (2).pdf" && Path.GetFileName(plans[1].TargetPath) == "A (2).pdf", "existing source names stay reserved");
    var result = service.Execute(plans);
    Check(result.Renamed == 3 && result.Errors.Count == 0, "colliding rename batch succeeds");
    Check(File.ReadAllText(a.SourcePath) == "A" && File.ReadAllText(b.SourcePath) == "B" && File.ReadAllText(c.SourcePath) == "C", "file contents preserved");
    Check(service.Execute(service.BuildPlan([a])).Unchanged == 1, "same-name operation is unchanged");
    var d = Add("D", "folder.pdf"); Directory.CreateDirectory(Path.Combine(root, "folder.pdf"));
    Check(Path.GetFileName(service.BuildPlan([d])[0].TargetPath) == "folder (2).pdf", "directory collisions get a suffix");
    var race = service.BuildPlan([d]); File.WriteAllText(race[0].TargetPath, "existing");
    Check(service.Execute(race).Errors.Count == 1 && File.ReadAllText(race[0].TargetPath) == "existing" && File.Exists(d.SourcePath), "target appearing after preview is never overwritten");
    var missing = new PdfRenameItem(Path.Combine(root, "missing.pdf")) { SuggestedName = "missing.pdf" };
    Check(service.Execute(service.BuildPlan([missing])).Errors.Count == 1, "missing unchanged source reports failure");
    Check(AnalysisProfile.All.SequenceEqual(new[] { AnalysisProfile.Light, AnalysisProfile.Standard, AnalysisProfile.Detailed }), "analysis profiles are ordered from light to detailed");
    Check(AnalysisProfile.Light.ReasoningEffort == "low" && AnalysisProfile.Standard.ReasoningEffort == "medium"
        && AnalysisProfile.Detailed.ReasoningEffort == "high", "profiles select increasing reasoning effort");
    var fixtureLines = Enumerable.Range(0, 400).Select(i => new OcrLineData("Body " + i,
        new[] { new OcrWordData("Body", 10, i * 2, 80, 10) })).ToList();
    fixtureLines.Add(new OcrLineData("Fictional document title", new[] { new OcrWordData("Title", 100, 900, 500, 60) }));
    using (var promptDocument = new OcrDocument(Path.Combine(root, "not-created"),
        Enumerable.Range(1, 5).Select(i => new OcrPageData(i, 1600, 1600, Path.Combine(root, $"fictional-{i}.png"), fixtureLines)).ToArray()))
    {
        var lengths = new List<int>();
        foreach (var profile in AnalysisProfile.All)
        {
            var input = typeof(CodexAppServerClient).GetMethod("BuildInputs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, new object[] { promptDocument, profile });
            using var payload = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(input));
            Check(payload.RootElement.EnumerateArray().Count(item => item.GetProperty("type").GetString() == "localImage") == profile.PageCount,
                profile.ReasoningEffort + " profile limits image count");
            var prompt = payload.RootElement[0].GetProperty("text").GetString()!;
            const string marker = "OCRレイアウトJSON:";
            using var ocrPayload = System.Text.Json.JsonDocument.Parse(prompt[(prompt.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..].Trim());
            Check(ocrPayload.RootElement.GetArrayLength() == profile.PageCount && ocrPayload.RootElement.EnumerateArray().All(page =>
                page.GetProperty("lines").GetArrayLength() == profile.MaximumOcrLines
                && page.GetProperty("lines").EnumerateArray().Any(line => line.GetProperty("text").GetString() == "Fictional document title")),
                profile.ReasoningEffort + " profile bounds OCR while retaining the large title near the bottom");
            lengths.Add(prompt.Length);
        }
        Check(lengths[0] < lengths[1] && lengths[1] < lengths[2], "lighter profiles produce smaller OCR prompts");
    }
    var lower = Add("lower", "LOWER.pdf");
    var casePlan = service.BuildPlan([lower]);
    Check(casePlan[0].IsCaseOnlyChange && !casePlan[0].IsUnchanged, "case-only change is planned as a rename");
    Check(service.Execute(casePlan).Renamed == 1 && Directory.EnumerateFiles(root).Select(Path.GetFileName).Contains("LOWER.pdf")
        && File.ReadAllText(lower.SourcePath) == "lower", "case-only rename is applied without losing content");
    Check(new PdfRenameItem("x") { Status = "解析失敗" }.StatusTone == "Danger"
        && new PdfRenameItem("x") { Status = "要確認（低確信）" }.StatusTone == "Warning"
        && new PdfRenameItem("x").StatusTone == "Neutral", "status chips map to tones");
    Check(new PdfRenameItem("x") { Confidence = 0.5 }.IsLowConfidence && !new PdfRenameItem("x").IsLowConfidence, "low-confidence flag ignores missing confidence");
    Check(FileRenameService.SanitizeTitle("CON.manual.pdf") == "_CON.manual", "reserved Windows names with extensions");
    Check(FileRenameService.SanitizeTitle("CONTEXT", 3) == "_CO", "truncation cannot create a reserved name");
    Check(FileRenameService.SanitizeTitle("売上：報告 / 2026.pdf") == "売上 報告 2026", "filename normalization");
    Check(FileRenameService.SanitizeTitle("abc😀def", 4) == "abc", "surrogate pairs are not split");
    Check(FileRenameService.SanitizeTitle(" . ") == "無題", "empty title fallback");
    d.IsSelected = false;
    Check(service.BuildPlan([d]).Count == 0, "unselected item ignored");
    d.IsSelected = true; d.SuggestedName = " ";
    Check(service.BuildPlan([d]).Count == 0, "blank candidate ignored");
    Check(new PdfRenameItem("x").ConfidenceDisplay == "—" && new PdfRenameItem("x") { Confidence = 0 }.ConfidenceDisplay != "—", "zero confidence differs from unavailable");
    var suggestion = CodexAppServerClient.ParseSuggestion("{\"title\":\"Example\",\"confidence\":0.6,\"reason\":\"page 1\"}");
    Check(suggestion.Title == "Example" && suggestion.Confidence == 0.6, "AI structured response parsing");

    {
        var fixture = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "sample.pdf");
        var ocr = new PdfOcrService();
        using var one = await ocr.ReadFirstPagesAsync(fixture, CancellationToken.None, 1);
        Check(one.Pages.Count == 1 && one.Pages.All(p => p.Width <= 2400 && p.Height <= 2400), "one-page OCR and image bounds");
        Check(one.Pages[0].Lines.Any(l => l.Text.Contains("TEST", StringComparison.OrdinalIgnoreCase)), "actual Windows OCR recognizes fixture title");
        using var light = await ocr.ReadFirstPagesAsync(fixture, CancellationToken.None, AnalysisProfile.Light.PageCount, AnalysisProfile.Light.MaximumImageDimension);
        Check(light.Pages.Count == 1 && light.Pages.All(p => p.Width <= 1600 && p.Height <= 1600)
            && light.Pages[0].Lines.Any(l => l.Text.Contains("TEST", StringComparison.OrdinalIgnoreCase)), "light profile renders smaller images and preserves OCR title recognition");
        using var five = await ocr.ReadFirstPagesAsync(fixture, CancellationToken.None, 5);
        Check(five.Pages.Count == 5 && five.Pages.All(p => p.Width <= 2400 && p.Height <= 2400), "five-page OCR range including tall portrait page");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { using var unused = await ocr.ReadFirstPagesAsync(fixture, cancelled.Token); throw new Exception("cancellation was ignored"); }
        catch (OperationCanceledException) { Check(true, "cancelled OCR stops"); }
        try { using var unused = await ocr.ReadFirstPagesAsync(fixture, CancellationToken.None, 6); throw new Exception("invalid range accepted"); }
        catch (ArgumentOutOfRangeException) { Check(true, "invalid page range rejected"); }
    }
    Exception? windowFailure = null;
    var uiThread = new Thread(() =>
    {
        try
        {
            var app = new App(); app.InitializeComponent();
            var license = new LicenseWindow();
            Check(((TextBox)license.FindName("AppLicenseTextBox")).Text.Contains("Permission is hereby granted"), "MIT license is embedded and visible");
            var licenseAssembly = typeof(LicenseWindow).Assembly;
            var licenseResources = licenseAssembly.GetManifestResourceNames().Where(name => name.StartsWith("PdfTitleRenamer.ThirdPartyLicenses/", StringComparison.Ordinal)).ToArray();
            Check(licenseResources.Length == 23, "all third-party license files are embedded");
            var licenseText = ((TextBox)license.FindName("ThirdPartyNoticesTextBox")).Text;
            Check(licenseResources.All(name =>
            {
                using var stream = licenseAssembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return licenseText.Contains(reader.ReadToEnd(), StringComparison.Ordinal);
            }), "every embedded third-party license is fully visible without companion files");
            var window = new MainWindow();
            window.Measure(new Size(1000, 760)); window.Arrange(new Rect(0, 0, 1000, 760)); window.UpdateLayout();
            var profilePicker = (ComboBox)window.FindName("AnalysisProfileComboBox");
            Check(ReferenceEquals(profilePicker.SelectedItem, AnalysisProfile.Light), "light analysis is selected by default");
            profilePicker.SelectedItem = AnalysisProfile.Detailed;
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            Check(((TextBlock)window.FindName("AnalysisProfileDescriptionText")).Text == AnalysisProfile.Detailed.Description, "profile description follows the selected mode");
            profilePicker.SelectedItem = AnalysisProfile.Light;
            Check(((Button)window.FindName("RenameButton")).IsEnabled == false, "empty UI disables rename");
            typeof(MainWindow).GetMethod("LoadFolder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, [root]);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            var row = window.Items[0];
            row.Confidence = 0.95;
            var editor = (TextBox)window.FindName("CandidateTextBox");
            editor.Text = "Edited title.pdf";
            editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Check(row.SuggestedName == "Edited title.pdf" && row.Status == "手動編集" && double.IsNaN(row.Confidence), "detail editor updates candidate and clears stale confidence");
            Check(((Button)window.FindName("RenameButton")).IsEnabled, "editing enables rename immediately");
            row.IsSelected = false;
            Check(!((Button)window.FindName("RenameButton")).IsEnabled, "unchecking candidate disables rename immediately");
            var originalCount = window.Items.Count;
            try { typeof(MainWindow).GetMethod("LoadFolder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, [Path.Combine(root, "not-present")]); }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is DirectoryNotFoundException) { }
            Check(window.Items.Count == originalCount && row.SuggestedName == "Edited title.pdf", "failed folder loading preserves edited candidates");
            var preview = new RenamePreviewWindow(plans);
            preview.Measure(new Size(960, 620)); preview.Arrange(new Rect(0, 0, 960, 620)); preview.UpdateLayout();
            Check(((DataGrid)preview.FindName("PreviewGrid")).Items.Count == 3, "preview XAML loads every plan");
            var many = new RenamePreviewWindow(Enumerable.Range(0, 15).Select(_ => plans[0]).ToArray());
            Check(((DataGrid)many.FindName("PreviewGrid")).Items.Count == 15, "preview is not limited to first eight rows");
            var unchanged = new RenamePreviewWindow(service.BuildPlan([a]));
            Check(!((Button)unchanged.FindName("ExecuteButton")).IsEnabled, "unchanged-only preview disables execute");
            var singlePath = Path.Combine(root, "single.PDF");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "sample.pdf"), singlePath);
            var siblingPath = a.SourcePath;
            var siblingBytes = File.ReadAllBytes(siblingPath);
            var loadFile = typeof(MainWindow).GetMethod("LoadFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            loadFile.Invoke(window, [singlePath]);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            Check(window.Items.Count == 1 && window.Items[0].SourcePath == singlePath && window.Items[0].IsSelected,
                "single-file mode loads only the selected PDF, including uppercase extension");
            Check(((TextBlock)window.FindName("SelectionModeText")).Text.Contains("1ファイル"), "single-file mode is visible");
            var targets = (PdfRenameItem[])typeof(MainWindow).GetMethod("GetAnalysisTargets", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null)!;
            Check(targets.Length == 1 && targets[0] == window.Items[0], "analysis targets only the chosen file");
            window.Items[0].SuggestedName = "Single renamed.pdf";
            var singleResult = service.Execute(service.BuildPlan(window.Items));
            Check(singleResult.Renamed == 1 && File.Exists(Path.Combine(root, "Single renamed.pdf")) && !File.Exists(singlePath), "single-file rename succeeds");
            Check(File.ReadAllBytes(siblingPath).SequenceEqual(siblingBytes), "single-file rename leaves sibling files untouched");
            Check(((TextBox)window.FindName("FolderTextBox")).Text == window.Items[0].SourcePath, "selected file path follows the rename");
            foreach (var invalidPath in new[] { Path.Combine(root, "missing.pdf"), Path.Combine(root, "wrong.txt") })
            {
                try { loadFile.Invoke(window, [invalidPath]); throw new Exception("invalid file accepted"); }
                catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is FileNotFoundException or InvalidDataException) { }
            }
            Check(window.Items.Count == 1 && window.Items[0].CurrentName == "Single renamed.pdf", "invalid file selection preserves the current list");
            var busy = typeof(MainWindow).GetMethod("SetBusy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            busy.Invoke(window, [true, "Testing"]);
            Check(!profilePicker.IsEnabled, "analysis profile cannot change during a running batch");
            Check(!((Button)window.FindName("ChooseFileButton")).IsEnabled && !((Button)window.FindName("ChooseFolderButton")).IsEnabled, "both selection methods are disabled during analysis");
            busy.Invoke(window, [false, "Ready"]);
            typeof(MainWindow).GetMethod("LoadFolder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, [root]);
            Check(window.Items.Count > 1 && ((TextBlock)window.FindName("SelectionModeText")).Text.Contains("フォルダ"), "switching back restores folder mode");
            app.Shutdown();
        }
        catch (Exception ex) { windowFailure = ex; }
    });
    uiThread.SetApartmentState(ApartmentState.STA); uiThread.Start(); uiThread.Join();
    if (windowFailure is not null) throw windowFailure;
    Console.WriteLine($"PASS {checks} checks");
}
finally { Directory.Delete(root, recursive: true); }
