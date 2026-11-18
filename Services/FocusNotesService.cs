using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace NotchWin.Services;

public sealed class NoteItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.Now;
}

/// <summary>
/// Focus / Pomodoro timer + Quick Notes & Checklist with local JSON persistence.
/// Timer only ticks while actively running (0% idle CPU).
/// </summary>
public sealed class FocusNotesService : IDisposable
{
    private static readonly string NotesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Pill", "notes.json");

    private readonly DispatcherTimer _timer;
    private readonly List<NoteItem> _notes = new();
    private DateTime _deadlineUtc;

    public int PresetMinutes { get; private set; } = 25;
    public TimeSpan Duration { get; private set; } = TimeSpan.FromMinutes(25);
    public TimeSpan Remaining { get; private set; } = TimeSpan.FromMinutes(25);
    public bool IsRunning { get; private set; }
    public bool IsFinished { get; private set; }
    public double Progress => Duration.TotalSeconds > 0 ? Math.Clamp(1.0 - Remaining.TotalSeconds / Duration.TotalSeconds, 0, 1) : 0;

    public IReadOnlyList<NoteItem> Notes => _notes;

    public event Action? TimerChanged;
    public event Action? TimerCompleted;
    public event Action? NotesChanged;

    public FocusNotesService(Dispatcher dispatcher)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _timer.Tick += OnTick;
        LoadNotes();
    }

    public void SelectPreset(int minutes)
    {
        PresetMinutes = Math.Clamp(minutes, 1, 180);
        IsRunning = false;
        IsFinished = false;
        _timer.Stop();
        Duration = TimeSpan.FromMinutes(PresetMinutes);
        Remaining = Duration;
        TimerChanged?.Invoke();
    }

    public void AdjustMinutes(int delta)
    {
        int next = Math.Clamp(PresetMinutes + delta, 5, 120);
        SelectPreset(next);
    }

    public void ToggleStartPause()
    {
        if (IsRunning)
        {
            Remaining = MaxZero(_deadlineUtc - DateTime.UtcNow);
            IsRunning = false;
            _timer.Stop();
        }
        else
        {
            if (Remaining.TotalSeconds <= 1 || IsFinished)
            {
                Duration = TimeSpan.FromMinutes(PresetMinutes);
                Remaining = Duration;
                IsFinished = false;
            }
            _deadlineUtc = DateTime.UtcNow + Remaining;
            IsRunning = true;
            _timer.Start();
        }
        TimerChanged?.Invoke();
    }

    public void Reset()
    {
        IsRunning = false;
        IsFinished = false;
        _timer.Stop();
        Duration = TimeSpan.FromMinutes(PresetMinutes);
        Remaining = Duration;
        TimerChanged?.Invoke();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        Remaining = MaxZero(_deadlineUtc - DateTime.UtcNow);
        if (Remaining <= TimeSpan.Zero)
        {
            IsRunning = false;
            IsFinished = true;
            _timer.Stop();
            TimerChanged?.Invoke();
            TimerCompleted?.Invoke();
            return;
        }
        TimerChanged?.Invoke();
    }

    private static TimeSpan MaxZero(TimeSpan ts) => ts < TimeSpan.Zero ? TimeSpan.Zero : ts;

    public void AddNote(string text)
    {
        string clean = text.Trim();
        if (string.IsNullOrEmpty(clean)) return;
        _notes.Insert(0, new NoteItem { Text = clean });
        SaveNotes();
        NotesChanged?.Invoke();
    }

    public void ToggleNote(string id)
    {
        var n = _notes.FirstOrDefault(x => x.Id == id);
        if (n == null) return;
        n.Done = !n.Done;
        SaveNotes();
        NotesChanged?.Invoke();
    }

    public void DeleteNote(string id)
    {
        if (_notes.RemoveAll(x => x.Id == id) > 0)
        {
            SaveNotes();
            NotesChanged?.Invoke();
        }
    }

    private void SaveNotes()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(NotesPath)!);
            File.WriteAllText(NotesPath, JsonSerializer.Serialize(_notes));
        }
        catch { }
    }

    private void LoadNotes()
    {
        try
        {
            if (!File.Exists(NotesPath)) return;
            var loaded = JsonSerializer.Deserialize<List<NoteItem>>(File.ReadAllText(NotesPath));
            if (loaded != null) _notes.AddRange(loaded);
        }
        catch { }
    }

    public void Dispose() => _timer.Stop();
}
