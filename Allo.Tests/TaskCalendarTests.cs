using System.Text;
using Allo.Shared.Lists;
using Allo.Shared.Models;

namespace Allo.Tests;

// The .ics handed to the phone's calendar. Worth pinning down because a malformed file
// fails silently in a calendar app, or lands the task on the wrong day.
public class TaskCalendarTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 19, 30, 0, TimeSpan.Zero);

    private static TaskEntry Task(string title = "call the plumber", DateOnly? dueOn = null,
        string? note = null) => new()
        {
            Id = new Guid("11111111-2222-3333-4444-555555555555"),
            TaskListId = TaskList.DefaultId, Title = title,
            DueOn = dueOn ?? new DateOnly(2026, 10, 1), Note = note,
        };

    private static string[] Lines(string ics) => ics.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void NoDueDate_MeansNoEvent() =>
        Assert.Null(TaskCalendar.Build(new TaskEntry { Title = "someday" }, Now));

    [Fact]
    public void ItIsAnAllDayEvent_EndingTheFollowingDay()
    {
        var lines = Lines(TaskCalendar.Build(Task(), Now)!);

        Assert.Contains("DTSTART;VALUE=DATE:20261001", lines);
        // DTEND is exclusive in iCalendar: a one-day event ends on the 2nd. Off by one
        // here and the task shows on the wrong date.
        Assert.Contains("DTEND;VALUE=DATE:20261002", lines);
        // No time component in the value: an all-day event is eight digits, nothing more.
        // (Checking the whole line would always pass — "DTSTART" itself contains a T.)
        foreach (var line in lines.Where(l => l.StartsWith("DTSTART") || l.StartsWith("DTEND")))
        {
            Assert.Matches(@"^DT(START|END);VALUE=DATE:\d{8}$", line);
        }
    }

    [Fact]
    public void TheEventIsKeyedOnTheTask_SoAddingItTwiceDoesNotDuplicate()
    {
        var first = TaskCalendar.Build(Task(), Now)!;
        var second = TaskCalendar.Build(Task(), Now.AddDays(3))!;

        Assert.Contains("UID:11111111-2222-3333-4444-555555555555@allo", Lines(first));
        Assert.Equal(
            Lines(first).Single(l => l.StartsWith("UID:")),
            Lines(second).Single(l => l.StartsWith("UID:")));
    }

    [Fact]
    public void ItLooksLikeAnICalendarFile()
    {
        var lines = Lines(TaskCalendar.Build(Task(), Now)!);

        Assert.Equal("BEGIN:VCALENDAR", lines[0]);
        Assert.Equal("END:VCALENDAR", lines[^1]);
        Assert.Contains("VERSION:2.0", lines);
        Assert.Contains("DTSTAMP:20260924T193000Z", lines);
        Assert.Contains("SUMMARY:call the plumber", lines);
        // Every line ends CRLF, including the last.
        Assert.EndsWith("\r\n", TaskCalendar.Build(Task(), Now)!);
    }

    // A comma or semicolon in a title would otherwise split the field and the calendar
    // would show a truncated event.
    [Theory]
    [InlineData("milk, bread and eggs", "SUMMARY:milk\\, bread and eggs")]
    [InlineData("call plumber; then电 sparky", "SUMMARY:call plumber\\; then电 sparky")]
    [InlineData("back\\slash", "SUMMARY:back\\\\slash")]
    public void DelimitersInTheTitle_AreEscaped(string title, string expected)
    {
        var lines = Lines(TaskCalendar.Build(Task(title), Now)!);

        Assert.Contains(expected, lines);
    }

    [Fact]
    public void ANoteBecomesTheDescription()
    {
        var lines = Lines(TaskCalendar.Build(Task(note: "ring after six"), Now)!);

        Assert.Contains("DESCRIPTION:ring after six", lines);
    }

    [Fact]
    public void NoNote_MeansNoDescriptionLine() =>
        Assert.DoesNotContain(Lines(TaskCalendar.Build(Task(), Now)!), l => l.StartsWith("DESCRIPTION"));

    // Titles can be 200 characters; strict parsers reject lines past 75 octets.
    [Fact]
    public void LongLines_AreFolded()
    {
        var ics = TaskCalendar.Build(Task(new string('a', 200)), Now)!;

        foreach (var line in Lines(ics))
        {
            Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, $"line of {line.Length} chars was not folded");
        }
        // Continuations are marked by a leading space, which is how they rejoin.
        Assert.Contains(Lines(ics), l => l.StartsWith(' '));
    }

    [Fact]
    public void FoldingNeverSplitsACharacterInHalf()
    {
        // Multi-byte characters: a naive byte-wise fold would cut one in two and corrupt it.
        var ics = TaskCalendar.Build(Task(string.Concat(Enumerable.Repeat("é", 100))), Now)!;

        Assert.Contains("é", ics);
        Assert.Equal(100, ics.Count(c => c == 'é'));
        foreach (var line in Lines(ics))
        {
            Assert.True(Encoding.UTF8.GetByteCount(line) <= 75);
        }
    }

    [Theory]
    [InlineData("call the plumber", "call-the-plumber.ics")]
    [InlineData("Mow the lawn!", "mow-the-lawn.ics")]
    [InlineData("!!!", "task.ics")]
    public void TheFileNameIsReadable(string title, string expected) =>
        Assert.Equal(expected, TaskCalendar.FileName(Task(title)));
}
