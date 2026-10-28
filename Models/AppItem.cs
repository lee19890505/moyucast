using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MoyuCast.Models;

/// <summary>锁屏模式 App 列表里的一项</summary>
public class AppItem : INotifyPropertyChanged
{
    public string Package { get; set; } = "";
    public string Activity { get; set; } = "";
    public string DisplayName { get; set; } = "";

    private bool _isStarred;
    public bool IsStarred
    {
        get => _isStarred;
        set { _isStarred = value; Notify(nameof(IsStarred)); Notify(nameof(StarGlyph)); }
    }

    private string _iconPath;
    public string IconPath
    {
        get => _iconPath;
        set { _iconPath = value; Notify(nameof(IconPath)); Notify(nameof(IconSource)); Notify(nameof(HasIcon)); }
    }

    public bool HasIcon => !string.IsNullOrEmpty(_iconPath) && File.Exists(_iconPath);

    public ImageSource IconSource
    {
        get
        {
            if (!HasIcon) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(_iconPath);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }
    }

    public string StarGlyph => _isStarred ? "★" : "☆";

    public string AvatarText
    {
        get
        {
            var n = string.IsNullOrWhiteSpace(DisplayName) ? Package : DisplayName;
            return n.Length > 0 ? n.Substring(0, 1).ToUpperInvariant() : "?";
        }
    }

    public Brush AvatarBrush
    {
        get
        {
            var hash = 0;
            foreach (var ch in Package) hash = (hash * 31 + ch) & 0x7FFFFFFF;
            var colors = new[]
            {
                "#4FD1C5", "#0A84FF", "#FF9F0A", "#BF5AF2", "#30D158", "#FF453A", "#64D2FF", "#FFD60A"
            };
            return (Brush)new BrushConverter().ConvertFromString(colors[hash % colors.Length]);
        }
    }

    public override string ToString() => DisplayName;

    public event PropertyChangedEventHandler PropertyChanged;
    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
