using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace PersianRatesWidget.Models;

public sealed class MarketRate : INotifyPropertyChanged
{
    private string _priceText = "—";
    private string _changePercentText = "";
    private int _changeDirection;
    private long _rawTomanPrice;

    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string Unit { get; init; } = "تومان";

    public long RawTomanPrice
    {
        get => _rawTomanPrice;
        set
        {
            if (_rawTomanPrice == value) return;
            _rawTomanPrice = value;
            OnPropertyChanged();
        }
    }

    public string PriceText
    {
        get => _priceText;
        set
        {
            if (_priceText == value) return;
            _priceText = value;
            OnPropertyChanged();
        }
    }

    public string ChangePercentText
    {
        get => _changePercentText;
        set
        {
            if (_changePercentText == value) return;
            _changePercentText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasChange));
        }
    }

    public bool HasChange => !string.IsNullOrWhiteSpace(_changePercentText);

    public int ChangeDirection
    {
        get => _changeDirection;
        set
        {
            if (_changeDirection == value) return;
            _changeDirection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ChangeBrush));
            OnPropertyChanged(nameof(ChangeBackgroundBrush));
        }
    }

    public System.Windows.Media.Brush ChangeBrush => ChangeDirection switch
    {
        > 0 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0xDE, 0x80)),
        < 0 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71)),
        _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x94, 0xA3, 0xB8))
    };

    public System.Windows.Media.Brush ChangeBackgroundBrush => ChangeDirection switch
    {
        > 0 => new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x24, 0x22, 0xC5, 0x5E)),
        < 0 => new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x24, 0xEF, 0x44, 0x44)),
        _ => new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x18, 0x94, 0xA3, 0xB8))
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
