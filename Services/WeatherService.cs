using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;

namespace NotchWin.Services;

public sealed record WeatherSnapshot(
    int TempC,
    int HighC,
    int LowC,
    string Condition,
    string City,
    string IconKey,
    bool IsDay
);

/// <summary>
/// Free, keyless weather via Open-Meteo (current + daily High/Low) + IP geolocation.
/// Polls once at startup and every 25 minutes (~0% CPU).
/// </summary>
public sealed class WeatherService : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private double? _lat;
    private double? _lon;
    private string _city = "Local";

    public WeatherSnapshot? Current { get; private set; }
    public event Action<WeatherSnapshot?>? Changed;

    public WeatherService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMinutes(25)
        };
        _timer.Tick += (_, _) => _ = RefreshAsync();
    }

    public void Start()
    {
        _timer.Start();
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        try
        {
            if (_lat == null || _lon == null)
            {
                using var locResp = await Http.GetAsync("https://ipwho.is/");
                if (locResp.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await locResp.Content.ReadAsStringAsync());
                    var root = doc.RootElement;
                    if (root.TryGetProperty("latitude", out var latEl) && root.TryGetProperty("longitude", out var lonEl))
                    {
                        _lat = latEl.GetDouble();
                        _lon = lonEl.GetDouble();
                        if (root.TryGetProperty("city", out var cityEl) && cityEl.ValueKind == JsonValueKind.String)
                            _city = cityEl.GetString() ?? "Local";
                    }
                }
            }

            if (_lat == null || _lon == null) return;

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string url = $"https://api.open-meteo.com/v1/forecast?latitude={_lat.Value.ToString(inv)}&longitude={_lon.Value.ToString(inv)}&current=temperature_2m,weather_code,is_day&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1";
            using var wResp = await Http.GetAsync(url);
            if (!wResp.IsSuccessStatusCode) return;

            using var wDoc = JsonDocument.Parse(await wResp.Content.ReadAsStringAsync());
            if (!wDoc.RootElement.TryGetProperty("current", out var cur)) return;

            int temp = (int)Math.Round(cur.GetProperty("temperature_2m").GetDouble());
            int code = cur.GetProperty("weather_code").GetInt32();
            bool isDay = cur.GetProperty("is_day").GetInt32() == 1;

            int high = temp + 2, low = temp - 3;
            if (wDoc.RootElement.TryGetProperty("daily", out var daily))
            {
                if (daily.TryGetProperty("temperature_2m_max", out var mx) && mx.GetArrayLength() > 0)
                    high = (int)Math.Round(mx[0].GetDouble());
                if (daily.TryGetProperty("temperature_2m_min", out var mn) && mn.GetArrayLength() > 0)
                    low = (int)Math.Round(mn[0].GetDouble());
            }

            var (cond, icon) = MapWmo(code, isDay);
            var snap = new WeatherSnapshot(temp, high, low, cond, _city, icon, isDay);
            Current = snap;
            _ = _dispatcher.BeginInvoke(() => Changed?.Invoke(snap));
        }
        catch { }
    }

    private static (string cond, string icon) MapWmo(int code, bool isDay) => code switch
    {
        0 => (isDay ? "Clear" : "Clear Night", isDay ? "sun" : "moon"),
        1 or 2 => ("Partly Cloudy", isDay ? "sun" : "cloud"),
        3 or 45 or 48 => ("Cloudy", "cloud"),
        51 or 53 or 55 or 61 or 63 or 65 or 80 or 81 or 82 => ("Rain", "rain"),
        71 or 73 or 75 or 77 or 85 or 86 => ("Snow", "snow"),
        95 or 96 or 99 => ("Storm", "storm"),
        _ => ("Fair", isDay ? "sun" : "cloud")
    };

    public void Dispose() => _timer.Stop();
}
