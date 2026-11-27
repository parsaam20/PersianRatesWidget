using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace PersianRatesWidget.Services;

public sealed record RateSnapshotItem(
    string Key,
    long TomanPrice,
    double ChangePercent,
    int Direction
);

public sealed record RateFetchResult(
    bool Success,
    Dictionary<string, RateSnapshotItem> Items,
    string ErrorMessage,
    DateTimeOffset FetchedAt
);

public sealed class RateService
{
    private const string TgjuEndpoint = "https://call.tgju.org/ajax.json";
    private const string NobitexUsdtEndpoint = "https://apiv2.nobitex.ir/v3/orderbook/USDTIRT";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersianRatesWidget");

    private static readonly string CacheFilePath = Path.Combine(CacheDirectory, "rates_cache.json");

    static RateService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) PersianRatesWidget/1.0");
    }

    public async Task<RateFetchResult> FetchLatestRatesAsync(CancellationToken cancellationToken = default)
    {
        if (!NetworkInterface.GetIsNetworkAvailable())
        {
            return new RateFetchResult(
                false,
                new Dictionary<string, RateSnapshotItem>(),
                "اتصال اینترنت برقرار نیست",
                DateTimeOffset.Now);
        }

        try
        {
            using var response = await Http.GetAsync(TgjuEndpoint, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("current", out var current))
            {
                return new RateFetchResult(
                    false,
                    new Dictionary<string, RateSnapshotItem>(),
                    "فرمت پاسخ سرور نامعتبر است",
                    DateTimeOffset.Now);
            }

            var items = new Dictionary<string, RateSnapshotItem>(StringComparer.OrdinalIgnoreCase);

            TryExtractTgjuItem(current, "price_dollar_rl", "usd", items);
            TryExtractTgjuItem(current, "crypto-tether-irr", "usdt", items);
            TryExtractTgjuItem(current, "price_aed", "aed", items);
            TryExtractTgjuItem(current, "geram18", "gold18", items);
            TryExtractTgjuItem(current, "geram24", "gold24", items);

            // Fallback to Nobitex for Tether (USDT) if TGJU omits it
            if (!items.ContainsKey("usdt"))
            {
                var nobitexUsdt = await TryFetchNobitexUsdtAsync(cancellationToken);
                if (nobitexUsdt is not null)
                {
                    items["usdt"] = nobitexUsdt;
                }
            }

            if (items.Count == 0)
            {
                return new RateFetchResult(
                    false,
                    items,
                    "نرخی از سرور دریافت نشد",
                    DateTimeOffset.Now);
            }

            await SaveCacheAsync(items, cancellationToken);

            return new RateFetchResult(true, items, string.Empty, DateTimeOffset.Now);
        }
        catch (TaskCanceledException)
        {
            return new RateFetchResult(
                false,
                new Dictionary<string, RateSnapshotItem>(),
                "زمان انتظار اتصال به پایان رسید",
                DateTimeOffset.Now);
        }
        catch (HttpRequestException)
        {
            return new RateFetchResult(
                false,
                new Dictionary<string, RateSnapshotItem>(),
                "خطا در برقراری ارتباط با سرور",
                DateTimeOffset.Now);
        }
        catch (JsonException)
        {
            return new RateFetchResult(
                false,
                new Dictionary<string, RateSnapshotItem>(),
                "خطا در پردازش داده‌های دریافتی",
                DateTimeOffset.Now);
        }
        catch (Exception)
        {
            return new RateFetchResult(
                false,
                new Dictionary<string, RateSnapshotItem>(),
                "خطای غیرمنتظره در دریافت نرخ‌ها",
                DateTimeOffset.Now);
        }
    }

    private static void TryExtractTgjuItem(
        JsonElement current,
        string tgjuKey,
        string localKey,
        Dictionary<string, RateSnapshotItem> target)
    {
        if (!current.TryGetProperty(tgjuKey, out var node) || node.ValueKind != JsonValueKind.Object)
            return;

        if (!node.TryGetProperty("p", out var priceProp))
            return;

        var rawPriceStr = priceProp.GetString()?.Replace(",", "").Trim();
        if (!double.TryParse(rawPriceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var rialPrice) ||
            rialPrice <= 0)
        {
            return;
        }

        // Convert Iranian Rial to Toman
        long tomanPrice = (long)Math.Round(rialPrice / 10.0, MidpointRounding.AwayFromZero);

        double changePct = 0;
        if (node.TryGetProperty("dp", out var dpProp))
        {
            var dpStr = dpProp.ValueKind == JsonValueKind.String
                ? dpProp.GetString()
                : dpProp.GetRawText();
            double.TryParse(dpStr?.Replace("%", "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out changePct);
        }

        int direction = 0;
        if (node.TryGetProperty("dt", out var dtProp))
        {
            var dt = dtProp.GetString()?.ToLowerInvariant();
            if (dt == "high") direction = 1;
            else if (dt == "low") direction = -1;
        }
        else if (changePct > 0) direction = 1;
        else if (changePct < 0) direction = -1;

        if (direction < 0 && changePct > 0)
            changePct = -changePct;

        target[localKey] = new RateSnapshotItem(localKey, tomanPrice, changePct, direction);
    }

    private static async Task<RateSnapshotItem?> TryFetchNobitexUsdtAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var resp = await Http.GetAsync(NobitexUsdtEndpoint, cancellationToken);
            if (!resp.IsSuccessStatusCode) return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (doc.RootElement.TryGetProperty("lastTradePrice", out var priceProp))
            {
                var raw = priceProp.GetString()?.Trim();
                if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var rial) && rial > 0)
                {
                    long toman = (long)Math.Round(rial / 10.0, MidpointRounding.AwayFromZero);
                    return new RateSnapshotItem("usdt", toman, 0, 0);
                }
            }
        }
        catch
        {
            // Ignore fallback failure; primary error handling covers it
        }

        return null;
    }

    public async Task<RateFetchResult?> LoadCachedRatesAsync()
    {
        try
        {
            if (!File.Exists(CacheFilePath)) return null;
            var json = await File.ReadAllTextAsync(CacheFilePath);
            var cached = JsonSerializer.Deserialize<CachedPayload>(json);
            if (cached is null || cached.Items.Count == 0) return null;

            return new RateFetchResult(false, cached.Items, "نمایش آخرین نرخ ذخیره‌شده", cached.SavedAt);
        }
        catch
        {
            return null;
        }
    }

    private static async Task SaveCacheAsync(
        Dictionary<string, RateSnapshotItem> items,
        CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var payload = new CachedPayload(DateTimeOffset.Now, items);
            var json = JsonSerializer.Serialize(payload);
            await File.WriteAllTextAsync(CacheFilePath, json, cancellationToken);
        }
        catch
        {
            // Non-fatal if local cache write fails
        }
    }

    private sealed record CachedPayload(
        DateTimeOffset SavedAt,
        Dictionary<string, RateSnapshotItem> Items);

    public static string ToPersianDigits(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return input
            .Replace('0', '۰')
            .Replace('1', '۱')
            .Replace('2', '۲')
            .Replace('3', '۳')
            .Replace('4', '۴')
            .Replace('5', '۵')
            .Replace('6', '۶')
            .Replace('7', '۷')
            .Replace('8', '۸')
            .Replace('9', '۹');
    }

    public static string FormatToman(long toman)
    {
        var formatted = toman.ToString("N0", CultureInfo.InvariantCulture);
        return ToPersianDigits(formatted);
    }

    public static string FormatChange(double changePct, int direction)
    {
        if (Math.Abs(changePct) < 0.005)
            return ToPersianDigits("0.0٪");

        var sign = direction > 0 ? "+" : (direction < 0 ? "-" : "");
        var absVal = Math.Abs(changePct).ToString("0.0", CultureInfo.InvariantCulture);
        return ToPersianDigits($"{sign}{absVal}٪");
    }
}
