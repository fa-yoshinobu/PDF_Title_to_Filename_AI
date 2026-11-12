using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace PdfTitleRenamer.Models;

public sealed class PdfRenameItem : INotifyPropertyChanged
{
    private string _sourcePath;
    private bool _isSelected = true;
    private string _suggestedName = string.Empty;
    private double _confidence = double.NaN;
    private string _reason = string.Empty;
    private string _status = "未解析";

    public PdfRenameItem(string sourcePath)
    {
        _sourcePath = sourcePath;
    }

    public string SourcePath
    {
        get => _sourcePath;
        set
        {
            if (SetField(ref _sourcePath, value))
            {
                OnPropertyChanged(nameof(CurrentName));
            }
        }
    }

    public string CurrentName => Path.GetFileName(SourcePath);

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public string SuggestedName
    {
        get => _suggestedName;
        set => SetField(ref _suggestedName, value);
    }

    public double Confidence
    {
        get => _confidence;
        set
        {
            if (SetField(ref _confidence, value))
            {
                OnPropertyChanged(nameof(ConfidenceDisplay));
                OnPropertyChanged(nameof(ConfidencePercent));
                OnPropertyChanged(nameof(IsLowConfidence));
            }
        }
    }

    public string ConfidenceDisplay => double.IsNaN(Confidence) ? "—" : $"{Confidence:P0}";

    public double ConfidencePercent => double.IsNaN(Confidence) ? 0 : Math.Clamp(Confidence, 0, 1) * 100;

    public bool IsLowConfidence => !double.IsNaN(Confidence) && Confidence < 0.7;

    public string Reason
    {
        get => _reason;
        set => SetField(ref _reason, value);
    }

    public string Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusTone));
            }
        }
    }

    /// <summary>Color group used by the status chip: Success, Warning, Danger, Info or Neutral.</summary>
    public string StatusTone => Status switch
    {
        "確認待ち" or "リネーム済み" => "Success",
        "要確認（低確信）" or "手動編集" or "候補なし" or "失敗・旧候補" or "中止・旧候補" => "Warning",
        "解析失敗" or "リネーム失敗" => "Danger",
        "OCR中" or "AI判定中" => "Info",
        _ => "Neutral"
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
