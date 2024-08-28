using System.Collections.Generic;
using System;
using System.Diagnostics.CodeAnalysis;

namespace ProximityVoiceChat.Log;

public sealed class LogLevel
{
    public static readonly LogLevel Trace = new LogLevel("Trace", 0);
    public static readonly LogLevel Debug = new LogLevel("Debug", 1);
    public static readonly LogLevel Info = new LogLevel("Info", 2);
    public static readonly LogLevel Warn = new LogLevel("Warn", 3);
    public static readonly LogLevel Error = new LogLevel("Error", 4);
    public static readonly LogLevel Fatal = new LogLevel("Fatal", 5);
    public static readonly LogLevel Off = new LogLevel("Off", 6);

    private static readonly IList<LogLevel> allLoggingLevels = new List<LogLevel>
        {
            Trace,
            Debug,
            Info,
            Warn,
            Error,
            Fatal
        }.AsReadOnly();
    public static IEnumerable<LogLevel> AllLoggingLevels => allLoggingLevels;

    private readonly string name;
    public string Name => this.name;

    private readonly int ordinal;
    public int Ordinal => this.ordinal;

    private LogLevel(string name, int ordinal)
    {
        this.name = name;
        this.ordinal = ordinal;
    }

    public static bool operator ==(LogLevel level1, LogLevel level2)
    {
        if (level1 is null)
        {
            return level2 is null;
        }

        if (level2 is null)
        {
            return false;
        }

        return level1.Ordinal == level2.Ordinal;
    }
    public static bool operator !=(LogLevel level1, LogLevel level2)
    {
        if (level1 is null)
        {
            return level2 is object;
        }

        if (level2 is null)
        {
            return true;
        }

        return level1.Ordinal != level2.Ordinal;
    }
    public static bool operator >(LogLevel level1, LogLevel level2)
    {
        ArgumentNullException.ThrowIfNull(level1);
        ArgumentNullException.ThrowIfNull(level2);

        return level1.Ordinal > level2.Ordinal;
    }
    public static bool operator >=(LogLevel level1, LogLevel level2)
    {
        ArgumentNullException.ThrowIfNull(level1);
        ArgumentNullException.ThrowIfNull(level2);

        return level1.Ordinal >= level2.Ordinal;
    }
    public static bool operator <(LogLevel level1, LogLevel level2)
    {
        ArgumentNullException.ThrowIfNull(level1);
        ArgumentNullException.ThrowIfNull(level2);

        return level1.Ordinal < level2.Ordinal;
    }
    public static bool operator <=(LogLevel level1, LogLevel level2)
    {
        ArgumentNullException.ThrowIfNull(level1);
        ArgumentNullException.ThrowIfNull(level2);

        return level1.Ordinal <= level2.Ordinal;
    }

    public override string ToString()
    {
        return this.Name;
    }

    public override int GetHashCode()
    {
        return Ordinal;
    }

    public override bool Equals([AllowNull] object obj)
    {
        if (obj is not LogLevel logLevel)
        {
            return false;
        }

        return Ordinal == logLevel.Ordinal;
    }
}
