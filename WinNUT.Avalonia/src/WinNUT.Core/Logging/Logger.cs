using System.Diagnostics;
using System.Globalization;
using WinNUT.Core.Models;

namespace WinNUT.Core.Logging;

/// <summary>
/// Central application logger. Writes to an in-memory ring buffer for on-screen display
/// (capped at <see cref="MaxDisplayedLogs"/>), an in-memory event history for crash reports
/// (capped at <see cref="MaxEvents"/>), and optionally a daily rolling log file.
/// </summary>
/// <remarks>
/// <see cref="LogLevel"/> is NOT a conventional severity order. The configured
/// <see cref="LogLevelValue"/> acts as an inclusion ceiling over the enum's declared order
/// (Notice=0 &lt; Warning=1 &lt; Error=2 &lt; Debug=3): a message is written to file only if its
/// own level's numeric value is &lt;= the configured ceiling's value. Debug is therefore the
/// "log everything" tier, Notice the quietest — this mirrors the original application exactly
/// and must not be "fixed" into a conventional Debug&lt;Info&lt;Warning&lt;Error scheme.
/// </remarks>
public sealed class Logger : IDisposable
{
    public const int MaxDisplayedLogs = 50;

    private readonly object _sync = new();
    private readonly string _logDirectory;
    private readonly Queue<string> _displayedLogs = new();
    private readonly List<string> _lastEvents = new();
    private long _displayCounter;
    private StreamWriter? _fileWriter;
    private DateTime _fileDate;
    private bool _isWritingToFile;

    public int MaxEvents { get; set; } = 200;
    public LogLevel LogLevelValue { get; set; }

    /// <summary>Culture used for timestamp formatting. Invariant in DEBUG builds for consistent bug reports.</summary>
    public IFormatProvider DateTimeFormat { get; set; } =
#if DEBUG
        CultureInfo.InvariantCulture;
#else
        CultureInfo.CurrentCulture;
#endif

    public bool IsWritingToFile
    {
        get => _isWritingToFile;
        set
        {
            if (_isWritingToFile == value) return;
            _isWritingToFile = value;
            if (value) InitializeLogFile();
            else TerminateLogFile();
        }
    }

    /// <summary>Newest-first is NOT applied here; oldest-first ring buffer. UI layers decide display order.</summary>
    public IReadOnlyCollection<string> DisplayedLogs => _displayedLogs;

    /// <summary>Raised with the newly added display line whenever a log entry with display text is recorded.</summary>
    public event Action<string>? DisplayedLogsLineAdded;

    /// <summary>Raised when the display ring buffer exceeds <see cref="MaxDisplayedLogs"/> and the oldest line is evicted.</summary>
    public event Action? DisplayedLogsTrimmed;

    public Logger(LogLevel initialLevel, string dataDirectory)
    {
        LogLevelValue = initialLevel;
        _logDirectory = Path.Combine(dataDirectory, "Logs");
    }

    /// <summary>A copy of the last <see cref="MaxEvents"/> internal log lines, oldest first. Safe to reverse by the caller.</summary>
    public IReadOnlyList<string> LastEvents
    {
        get { lock (_sync) return _lastEvents.ToList(); }
    }

    public void LogTracing(string message, LogLevel level, object? sender, string? logToDisplay = null)
    {
        var senderName = sender?.GetType().Name ?? "null";
        var line = $"[{DateTime.Now.ToString(DateTimeFormat)}] [{level}] [{senderName}] {message}";

        Trace.WriteLine(line);

        lock (_sync)
        {
            _lastEvents.Add(line);
            while (_lastEvents.Count > MaxEvents)
            {
                _lastEvents.RemoveAt(0);
            }

            if (_isWritingToFile && ShouldWriteToFile(level))
            {
                WriteToFile(line);
            }

            if (logToDisplay is not null)
            {
                _displayCounter++;
                var displayLine = $"[{_displayCounter}][{DateTime.Now.ToString(DateTimeFormat)}] {logToDisplay}";
                _displayedLogs.Enqueue(displayLine);

                while (_displayedLogs.Count > MaxDisplayedLogs)
                {
                    _displayedLogs.Dequeue();
                    DisplayedLogsTrimmed?.Invoke();
                }

                DisplayedLogsLineAdded?.Invoke(displayLine);
            }
        }
    }

    public void LogException(Exception ex, object? sender)
    {
        LogTracing($"Exception Type: {ex.GetType()}", LogLevel.Error, sender);
        LogTracing($"Source: {ex.Source}", LogLevel.Error, sender);
        LogTracing($"Message: {ex.Message}", LogLevel.Error, sender);
        LogTracing($"StackTrace: {ex.StackTrace}", LogLevel.Error, sender);

        if (ex.InnerException is not null)
        {
            LogException(ex.InnerException, ex);
        }

        LogTracing("Exception report complete.", LogLevel.Notice, sender);
    }

    /// <summary>
    /// Inclusion-by-ceiling check per the ordering documented on this class: a message at
    /// <paramref name="level"/> is written to file only if its numeric value does not exceed
    /// the configured <see cref="LogLevelValue"/>.
    /// </summary>
    private bool ShouldWriteToFile(LogLevel level) => (int)LogLevelValue >= (int)level;

    private void InitializeLogFile()
    {
        Directory.CreateDirectory(_logDirectory);
        OpenFileForToday();

        lock (_sync)
        {
            foreach (var historyLine in _lastEvents)
            {
                _fileWriter?.WriteLine(historyLine);
            }
        }

        _fileWriter?.WriteLine("==== Begin Live Log ====");
        _fileWriter?.Flush();
    }

    private void OpenFileForToday()
    {
        _fileDate = DateTime.Today;
        var path = Path.Combine(_logDirectory, $"WinNUT_{_fileDate:yyyy-MM-dd}.log");
        _fileWriter = new StreamWriter(path, append: true) { AutoFlush = true };
    }

    private void WriteToFile(string line)
    {
        if (_fileWriter is null) return;

        if (DateTime.Today != _fileDate)
        {
            _fileWriter.Dispose();
            OpenFileForToday();
        }

        _fileWriter.WriteLine(line);
    }

    private void TerminateLogFile()
    {
        _fileWriter?.Dispose();
        _fileWriter = null;
    }

    public string? CurrentLogFilePath => _fileWriter is null
        ? null
        : Path.Combine(_logDirectory, $"WinNUT_{_fileDate:yyyy-MM-dd}.log");

    public void DeleteLogFile()
    {
        if (!_isWritingToFile)
        {
            throw new InvalidOperationException("Cannot delete the log file while file logging is disabled.");
        }

        var path = CurrentLogFilePath;
        TerminateLogFile();
        if (path is not null && File.Exists(path))
        {
            File.Delete(path);
        }

        InitializeLogFile();
    }

    public void Dispose()
    {
        _fileWriter?.Dispose();
    }
}
