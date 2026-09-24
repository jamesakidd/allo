using System.Text.Json;
using Allo.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Allo.Tests;

public class PriorityTests : IDisposable
{
    private readonly TestDatabase _database = new();

    [Theory]
    [InlineData(Priority.High, "high")]
    [InlineData(Priority.Normal, "normal")]
    [InlineData(Priority.Low, "low")]
    public void Json_UsesCode_BothWays(Priority priority, string code)
    {
        var json = JsonSerializer.Serialize(priority);

        Assert.Equal($"\"{code}\"", json);
        Assert.Equal(priority, JsonSerializer.Deserialize<Priority>(json));
    }

    [Fact]
    public void EveryPriority_HasACode_ThatRoundTrips()
    {
        Assert.Equal(Enum.GetValues<Priority>().Length, Priorities.All.Count);
        foreach (var priority in Enum.GetValues<Priority>())
        {
            Assert.Equal(priority, Priorities.FromCode(priority.ToCode()));
        }
    }

    [Fact]
    public void AnUnsetPriority_IsNormal() => Assert.Equal(Priority.Normal, default(Priority));

    // Rank is deliberately separate from declaration order, so the enum can gain a member
    // in the middle without silently reordering anyone's screen.
    [Fact]
    public void RankOrdersHighestFirst_WhateverTheDeclarationOrder()
    {
        Assert.Equal([Priority.High, Priority.Normal, Priority.Low], Priorities.All);
        Assert.True(Priority.High.Rank() < Priority.Normal.Rank());
        Assert.True(Priority.Normal.Rank() < Priority.Low.Rank());
    }

    // Stored as the code, never the integer, so adding a level later never renumbers rows
    // that already exist.
    [Fact]
    public async Task PriorityAndDueDate_AreStoredReadably()
    {
        var user = new User { Id = Guid.NewGuid(), DisplayName = "Sam" };
        var task = new TaskEntry
        {
            Id = Guid.NewGuid(), TaskListId = TaskList.DefaultId, Title = "call the plumber",
            Priority = Priority.High, DueOn = new DateOnly(2026, 10, 1), AddedBy = user.Id,
        };
        await using (var db = _database.CreateContext())
        {
            db.AddRange(user, task);
            await db.SaveChangesAsync();
        }

        await using var read = _database.CreateContext();
        var stored = await read.TaskEntries.SingleAsync(t => t.Id == task.Id);
        var raw = await read.Database
            .SqlQuery<string>($"SELECT Priority || ' ' || DueOn AS Value FROM TaskEntries WHERE Id = {task.Id}")
            .ToListAsync();

        Assert.Equal(Priority.High, stored.Priority);
        Assert.Equal(new DateOnly(2026, 10, 1), stored.DueOn);
        // A date with no time and no offset: nothing here can shift a due date across midnight.
        Assert.Equal("high 2026-10-01", raw.Single());
    }

    public void Dispose() => _database.Dispose();
}
