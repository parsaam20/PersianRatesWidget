using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PersianRatesWidget.Models;
using PersianRatesWidget.Services;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace PersianRatesWidget;

public partial class MainWindow : Window
{
    private readonly RateService _rateService = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly WinForms.NotifyIcon _trayIcon;
    private bool _isFetching;
    private bool _isExiting;

    public ObservableCollection<MarketRate> Rates { get; } = new()
    {
        new MarketRate { Key = "usd", Title = "دلار آمریکا", Subtitle = "USD / بازار آزاد" },
        new MarketRate { Key = "usdt", Title = "تتر", Subtitle = "USDT / تتر" },
        new MarketRate { Key = "aed", Title = "درهم امارات", Subtitle = "AED / بازار آزاد" },
        new MarketRate { Key = "gold18", Title = "طلای ۱۸ عیار", Subtitle = "هر گرم (۷۵۰)" },
        new MarketRate { Key = "gold24", Title = "طلای ۲۴ عیار", Subtitle = "هر گرم (۹۹۹.۹)" }
    };

    public MainWindow()
    {
        InitializeComponent();
        RatesList.ItemsSource = Rates;

        _trayIcon = CreateTrayIcon();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(60)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshRatesAsync();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private WinForms.NotifyIcon CreateTrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.RightToLeft = WinForms.RightToLeft.Yes;
        menu.Items.Add("نمایش / مخفی کردن", null, (_, _) => ToggleVisibility());
        menu.Items.Add("به‌روزرسانی فوری", null, async (_, _) => await RefreshRatesAsync());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("خروج", null, (_, _) => ExitApplication());

        var icon = new WinForms.NotifyIcon
        {
            Icon = LoadEmbeddedTrayIcon(),
            Text = "نرخ لحظه‌ای بازار",
            Visible = true,
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => ToggleVisibility();
        return icon;
    }

    private static Drawing.Icon LoadEmbeddedTrayIcon()
    {
        try
        {
            var streamInfo = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/app.ico"));
            if (streamInfo is not null)
            {
                return new Drawing.Icon(streamInfo.Stream);
            }
        }
        catch
        {
            // Fallback to default system icon
        }

        return Drawing.SystemIcons.Application;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        PositionWidgetInCorner();

        // Load cached rates first so the widget is immediately informative even if offline
        var cached = await _rateService.LoadCachedRatesAsync();
        if (cached is not null)
        {
            ApplySnapshot(cached);
        }

        await RefreshRatesAsync();
        _refreshTimer.Start();
    }

    private void PositionWidgetInCorner()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + 16;
        Top = workArea.Bottom - ActualHeight - 16;
    }

    private async Task RefreshRatesAsync()
    {
        if (_isFetching) return;
        _isFetching = true;

        try
        {
            SetStatusIndicator(ColorState.Fetching);
            RefreshButton.IsEnabled = false;

            var result = await _rateService.FetchLatestRatesAsync();

            if (result.Success)
            {
                ApplySnapshot(result);
                ErrorBanner.Visibility = Visibility.Collapsed;
                SetStatusIndicator(ColorState.Online);

                var timeStr = RateService.ToPersianDigits(result.FetchedAt.ToLocalTime().ToString("HH:mm:ss"));
                LastUpdatedText.Text = $"آخرین بروزرسانی: {timeStr}";
            }
            else
            {
                SetStatusIndicator(ColorState.Offline);
                ErrorBannerText.Text = result.ErrorMessage;
                ErrorBanner.Visibility = Visibility.Visible;

                if (Rates.All(r => r.RawTomanPrice == 0))
                {
                    LastUpdatedText.Text = "حالت آفلاین";
                }
                else
                {
                    LastUpdatedText.Text = "حالت آفلاین (نمایش آخرین نرخ)";
                }
            }
        }
        finally
        {
            RefreshButton.IsEnabled = true;
            _isFetching = false;
        }
    }

    private void ApplySnapshot(RateFetchResult result)
    {
        foreach (var rate in Rates)
        {
            if (result.Items.TryGetValue(rate.Key, out var item))
            {
                rate.RawTomanPrice = item.TomanPrice;
                rate.PriceText = RateService.FormatToman(item.TomanPrice);
                rate.ChangeDirection = item.Direction;
                rate.ChangePercentText = RateService.FormatChange(item.ChangePercent, item.Direction);
            }
        }
    }

    private enum ColorState { Online, Fetching, Offline }

    private void SetStatusIndicator(ColorState state)
    {
        StatusDot.Fill = state switch
        {
            ColorState.Online => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E)),
            ColorState.Fetching => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B)),
            _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44))
        };
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshRatesAsync();
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        PinButton.Foreground = Topmost
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF8, 0xFA, 0xFC))
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x64, 0x74, 0x8B));
        PinButton.ToolTip = Topmost
            ? "حالت همیشه روی صفحه: فعال"
            : "حالت همیشه روی صفحه: غیرفعال";
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        ExitApplication();
    }

    private void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
            Activate();
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExiting)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void ExitApplication()
    {
        _isExiting = true;
        _refreshTimer.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        System.Windows.Application.Current.Shutdown();
    }
}
