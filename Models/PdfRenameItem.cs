using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace PdfTitleRenamer.Models;

public sealed class PdfRenameItem : INotifyPropertyChanged
{
    private string _sourcePath;
    private bool _isSelected = true;
    private string _suggestedName = string.Empty;
    private double _confidence;
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
            }
        }
    }

    public string ConfidenceDisplay => Confidence <= 0 ? "—" : $"{Confidence:P0}";

    public string Reason
    {
        get => _reason;
        set => SetField(ref _reason, value);
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

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
