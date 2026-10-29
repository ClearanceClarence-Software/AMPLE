using Ample.Services;

namespace Ample.App.ViewModels;

/// <summary>One line of the log.</summary>
/// <param name="Time">When the line was written, local time.</param>
/// <param name="Source">The service tag or area, e.g. "apache" or "install".</param>
/// <param name="Message">The text.</param>
/// <param name="Level">Its severity, which picks the color.</param>
public sealed record LogEntry(DateTime Time, string Source, string Message, LogLevel Level)
{
    /// <summary>"18:42:07" for the time column.</summary>
    public string TimeText => Time.ToString("HH:mm:ss");

    /// <summary>"18:42" for the activity list on the Overview.</summary>
    public string ShortTime => Time.ToString("HH:mm");

    /// <summary>The source in brackets, as the log shows it.</summary>
    public string SourceText => $"[{Source}]";

    /// <summary>The resource key of the brush for this line's text.</summary>
    public string BrushKey => Level switch
    {
        LogLevel.Ok => "OkTextBrush",
        LogLevel.Warn => "WarnTextBrush",
        LogLevel.Error => "ErrorBrush",
        LogLevel.Debug => "DebugBrush",
        _ => "TextSoftBrush",
    };

    /// <summary>The line as one string, for the log file and the clipboard.</summary>
    public string AsText => $"{Time:yyyy-MM-dd HH:mm:ss} [{Source}] {Message}";
}
