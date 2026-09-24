using System.Text;
using Allo.Shared.Models;

namespace Allo.Shared.Lists;

// Turns a task with a due date into an iCalendar file the phone can hand to whatever
// calendar app it has. A generated file rather than a Google Calendar link: it works with
// no network, and it assumes nothing about whose calendar anyone keeps.
public static class TaskCalendar
{
    public static string FileName(TaskEntry task)
    {
        var safe = new string([.. task.Title.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')])
            .Trim('-');
        while (safe.Contains("--"))
        {
            safe = safe.Replace("--", "-");
        }
        return (safe.Length == 0 ? "task" : safe[..Math.Min(safe.Length, 40)]) + ".ics";
    }

    // Null when there is no due date: there is no event to make without one.
    public static string? Build(TaskEntry task, DateTimeOffset now)
    {
        if (task.DueOn is not { } due)
        {
            return null;
        }

        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//Allo//Tasks//EN",
            "CALSCALE:GREGORIAN",
            "BEGIN:VEVENT",
            // Keyed on the task, so adding the same task twice updates the event in
            // calendars that honour UID rather than making a second one.
            $"UID:{task.Id}@allo",
            $"DTSTAMP:{now.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}",
            // An all-day event. DTEND is exclusive in iCalendar, so a one-day event ends
            // the following day — off by one here and the task lands on the wrong date.
            $"DTSTART;VALUE=DATE:{due:yyyyMMdd}",
            $"DTEND;VALUE=DATE:{due.AddDays(1):yyyyMMdd}",
            $"SUMMARY:{Escape(task.Title)}",
        };
        if (!string.IsNullOrWhiteSpace(task.Note))
        {
            lines.Add($"DESCRIPTION:{Escape(task.Note)}");
        }
        lines.Add("END:VEVENT");
        lines.Add("END:VCALENDAR");

        // CRLF between lines and a trailing one, as the format requires.
        return string.Concat(lines.Select(l => Fold(l) + "\r\n"));
    }

    // RFC 5545: backslash, semicolon and comma are delimiters, and newlines are written
    // as an escape. A title with a comma in it would otherwise split the field.
    private static string Escape(string value) => value
        .Replace("\\", "\\\\")
        .Replace(";", "\\;")
        .Replace(",", "\\,")
        .Replace("\r\n", "\\n")
        .Replace("\n", "\\n")
        .Replace("\r", "\\n");

    // Long lines are folded at 75 octets with a leading space on each continuation.
    // A 200-character title makes a line well past that, and strict parsers reject it.
    private static string Fold(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) <= 75)
        {
            return line;
        }

        var folded = new StringBuilder();
        var bytes = 0;
        // The first line may use all 75 octets; every continuation spends one on its
        // leading space, so it has 74 left for content.
        var limit = 75;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (bytes + size > limit)
            {
                folded.Append("\r\n ");
                bytes = 0;
                limit = 74;
            }
            folded.Append(rune);
            bytes += size;
        }
        return folded.ToString();
    }
}
