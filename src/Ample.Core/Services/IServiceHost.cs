using Ample.Core;
using Ample.Stack;

namespace Ample.Services;

/// <summary>Severity of a log line; it sets the line's color in the log.</summary>
public enum LogLevel
{
    /// <summary>A normal progress message.</summary>
    Info,

    /// <summary>A success, such as a clean shutdown.</summary>
    Ok,

    /// <summary>Something the user may want to act on, while the operation carried on.</summary>
    Warn,

    /// <summary>A failure.</summary>
    Error,

    /// <summary>Detail shown only when "Show debug information" is on.</summary>
    Debug,
}

/// <summary>What a service driver can ask of AMPLE.</summary>
public interface IServiceHost
{
    /// <summary>The folders of the stack being managed.</summary>
    AmplePaths Paths { get; }

    /// <summary>AMPLE's live settings; edits apply without a restart.</summary>
    AppSettings Settings { get; }

    /// <summary>What is installed in the stack, including the generated root password.</summary>
    StackState Stack { get; }

    /// <summary>Adds a line to the log, and to the log file when logging to a file is on.</summary>
    /// <param name="source">Service tag or area shown before the message; it also picks its color.</param>
    /// <param name="message">The text of the line, shown to the user as written.</param>
    /// <param name="level">Sets the message color; <see cref="LogLevel.Debug"/> lines are written regardless.</param>
    void Write(string source, string message, LogLevel level = LogLevel.Info);

    /// <summary>Adds a <see cref="LogLevel.Debug"/> line, only when "Show debug information" is on.</summary>
    /// <param name="source">Service tag or area shown before the message.</param>
    /// <param name="message">The text of the line.</param>
    void Debug(string source, string message);

    /// <summary>Copies the last lines of a log file into the log as errors, or a warning when it has none.</summary>
    /// <param name="source">Service tag the copied lines are written under.</param>
    /// <param name="path">Full path of the log file; a missing or unreadable file counts as having no lines.</param>
    /// <param name="lines">Maximum number of non-empty lines to copy, taken from the end of the file.</param>
    void TailLog(string source, string path, int lines);
}
