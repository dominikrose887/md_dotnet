using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace MdViewer.Models;

public sealed class DocumentTab : INotifyPropertyChanged
{
    private string? _filePath;
    private string _content = string.Empty;
    private string _savedContent = string.Empty;
    private bool _isActive;
    private double _verticalOffset;
    private int _caretOffset;

    public Guid Id { get; } = Guid.NewGuid();

    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (string.Equals(_filePath, value, StringComparison.OrdinalIgnoreCase)) return;
            _filePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(DisplayTitle));
        }
    }

    public string Content
    {
        get => _content;
        set
        {
            if (_content == value) return;
            _content = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(DisplayTitle));
        }
    }

    public string SavedContent
    {
        get => _savedContent;
        set
        {
            if (_savedContent == value) return;
            _savedContent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(DisplayTitle));
        }
    }

    public bool IsDirty => !string.Equals(Content, SavedContent, StringComparison.Ordinal);

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            OnPropertyChanged();
        }
    }

    public double VerticalOffset
    {
        get => _verticalOffset;
        set
        {
            if (Math.Abs(_verticalOffset - value) < 0.01) return;
            _verticalOffset = value;
            OnPropertyChanged();
        }
    }

    public int CaretOffset
    {
        get => _caretOffset;
        set
        {
            if (_caretOffset == value) return;
            _caretOffset = value;
            OnPropertyChanged();
        }
    }

    public string Title =>
        string.IsNullOrEmpty(FilePath) ? "Untitled" : Path.GetFileName(FilePath);

    public string DisplayTitle => IsDirty ? $"{Title} •" : Title;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void MarkSaved()
    {
        SavedContent = Content;
    }

    public void NotifyTitleChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(IsDirty));
    }
}
