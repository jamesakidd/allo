using Allo.Shared.Lists;
using Allo.Shared.Models;
using Allo.Shared.Sync;

namespace Allo.Tests;

// What the tasks screen shows, without a browser.
public class TaskViewTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 9, 24);

    private readonly LocalStore _store = new(new InMemoryStorage(), TimeProvider.System);
    private readonly TaskActions _actions;

    public TaskViewTests()
    {
        _actions = new TaskActions(_store, TimeProvider.System);
        _store.ApplyPullAsync(new SyncPullResponse
        {
            Cursor = 1,
            Rows = new SyncRows
            {
                TaskLists = [new TaskList { Id = TaskList.DefaultId, Name = "Tasks", Sequence = 1 }],
            },
        }).GetAwaiter().GetResult();
    }

    private Task<TaskEntry> AddAsync(string title, Priority priority = Priority.Normal, DateOnly? dueOn = null) =>
        _actions.AddAsync(title, TaskList.DefaultId, User, priority, dueOn);

    private TaskSections Build() => TaskView.Build(_store, TaskList.DefaultId, Today);

    [Fact]
    public async Task GroupsRunHighestPriorityFirst()
    {
        await AddAsync("low one", Priority.Low);
        await AddAsync("high one", Priority.High);
        await AddAsync("normal one");

        Assert.Equal([Priority.High, Priority.Normal, Priority.Low],
            Build().ToDo.Select(g => g.Priority));
    }

    [Fact]
    public async Task AnEmptyPriority_IsNotAGroup()
    {
        await AddAsync("only a high one", Priority.High);

        Assert.Equal([Priority.High], Build().ToDo.Select(g => g.Priority));
    }

    // Priority decides the groups and nothing else: inside one, due date leads.
    [Fact]
    public async Task InsideAGroup_SoonestDueComesFirst_AndUndatedComesLast()
    {
        await AddAsync("no date");
        await AddAsync("next week", dueOn: Today.AddDays(7));
        await AddAsync("tomorrow", dueOn: Today.AddDays(1));

        var group = Assert.Single(Build().ToDo);
        Assert.Equal(["tomorrow", "next week", "no date"], group.Tasks.Select(t => t.Title));
    }

    [Fact]
    public async Task SameDueDate_FallsBackToTitle()
    {
        await AddAsync("beta", dueOn: Today);
        await AddAsync("Alpha", dueOn: Today);

        Assert.Equal(["Alpha", "beta"], Assert.Single(Build().ToDo).Tasks.Select(t => t.Title));
    }

    // A low-priority task due yesterday still sits below a high-priority one due next year.
    [Fact]
    public async Task ADueDateNeverOutranksPriority()
    {
        await AddAsync("overdue but minor", Priority.Low, Today.AddDays(-3));
        await AddAsync("important, no rush", Priority.High, Today.AddDays(300));

        var groups = Build().ToDo;
        Assert.Equal("important, no rush", groups[0].Tasks.Single().Title);
        Assert.Equal("overdue but minor", groups[1].Tasks.Single().Title);
    }

    [Fact]
    public async Task DoneTasksLeaveTheGroups_AndCollectAtTheBottom()
    {
        var task = await AddAsync("mow the lawn");
        await AddAsync("still to do");

        await _actions.SetDoneAsync(task, true, User);

        var sections = Build();
        Assert.Equal("still to do", Assert.Single(sections.ToDo).Tasks.Single().Title);
        Assert.Equal("mow the lawn", Assert.Single(sections.Done).Title);
        Assert.Equal(1, sections.DoneCount);
    }

    [Fact]
    public async Task UntickingSendsItBackToItsGroup()
    {
        var task = await AddAsync("mow the lawn", Priority.High);
        await _actions.SetDoneAsync(task, true, User);

        await _actions.SetDoneAsync(task, false, User);

        var sections = Build();
        Assert.Empty(sections.Done);
        Assert.Equal(Priority.High, Assert.Single(sections.ToDo).Priority);
    }

    [Fact]
    public async Task ClearingDone_LeavesTheRestAlone()
    {
        var done = await AddAsync("finished");
        await AddAsync("unfinished");
        await _actions.SetDoneAsync(done, true, User);

        var cleared = await _actions.ClearDoneAsync(TaskList.DefaultId);

        Assert.Equal(1, cleared);
        var sections = Build();
        Assert.Empty(sections.Done);
        Assert.Equal("unfinished", Assert.Single(sections.ToDo).Tasks.Single().Title);
    }

    // The guardrail from the plan: no due date can never read as late.
    [Fact]
    public async Task ATaskWithNoDueDate_IsNeverOverdue()
    {
        var task = await AddAsync("someday");

        Assert.False(TaskView.IsOverdue(task, Today));
        Assert.Null(TaskView.DueLabel(task, Today));
    }

    [Fact]
    public async Task ADoneTask_IsNeverOverdue()
    {
        var task = await AddAsync("late but finished", dueOn: Today.AddDays(-5));
        await _actions.SetDoneAsync(task, true, User);

        Assert.False(TaskView.IsOverdue(task, Today));
    }

    [Theory]
    [InlineData(-5, "Overdue · Sep 19")]
    [InlineData(-1, "Yesterday")]
    [InlineData(0, "Today")]
    [InlineData(1, "Tomorrow")]
    [InlineData(3, "Sunday")]
    [InlineData(30, "Oct 24")]
    public async Task DueLabel_ReadsLikeAPersonWouldSayIt(int offsetDays, string expected)
    {
        var task = await AddAsync("something", dueOn: Today.AddDays(offsetDays));

        Assert.Equal(expected, TaskView.DueLabel(task, Today));
    }

    [Fact]
    public async Task TasksBelongToTheirOwnList()
    {
        var other = await _actions.CreateListAsync("Garden");
        await AddAsync("in the default list");
        await _actions.AddAsync("prune the apple tree", other.Id, User);

        Assert.Equal("in the default list",
            Assert.Single(Build().ToDo).Tasks.Single().Title);
        Assert.Equal("prune the apple tree",
            Assert.Single(TaskView.Build(_store, other.Id, Today).ToDo).Tasks.Single().Title);
    }

    [Fact]
    public async Task MovingATask_TakesItOffOneListAndOntoTheOther()
    {
        var garden = await _actions.CreateListAsync("Garden");
        var task = await AddAsync("prune the apple tree", Priority.High);

        Assert.True(await _actions.MoveAsync(task, garden.Id));

        Assert.True(Build().IsEmpty);
        var there = Assert.Single(TaskView.Build(_store, garden.Id, Today).ToDo);
        Assert.Equal(Priority.High, there.Priority);
        Assert.Equal("prune the apple tree", there.Tasks.Single().Title);
    }

    // What Undo does: move it straight back.
    [Fact]
    public async Task MovingItBack_PutsItWhereItWas()
    {
        var garden = await _actions.CreateListAsync("Garden");
        var task = await AddAsync("prune the apple tree");
        await _actions.MoveAsync(task, garden.Id);

        Assert.True(await _actions.MoveAsync(task, TaskList.DefaultId));

        Assert.Equal("prune the apple tree", Assert.Single(Build().ToDo).Tasks.Single().Title);
    }

    // Undo after the original list was deleted elsewhere must not resurrect it as a target.
    [Fact]
    public async Task MovingOntoAListThatIsGone_DoesNothing()
    {
        var garden = await _actions.CreateListAsync("Garden");
        var task = await AddAsync("prune the apple tree");
        await _actions.DeleteListAsync(garden);

        Assert.False(await _actions.MoveAsync(task, garden.Id));
        Assert.Equal(TaskList.DefaultId, task.TaskListId);
    }

    [Fact]
    public async Task MovingSeveral_MovesThemAll_AndRemembersWhereEachCameFrom()
    {
        var garden = await _actions.CreateListAsync("Garden");
        var a = await AddAsync("prune the apple tree", Priority.High);
        var b = await AddAsync("rake the leaves");
        var stays = await AddAsync("fix the gate");

        var moves = await _actions.MoveManyAsync([a, b], garden.Id);

        Assert.Equal([a.Id, b.Id], moves.Select(m => m.TaskId));
        Assert.All(moves, m => Assert.Equal(TaskList.DefaultId, m.FromListId));
        Assert.Equal(["fix the gate"], Build().ToDo.SelectMany(g => g.Tasks).Select(t => t.Title));
        Assert.Equal(2, TaskView.Tasks(_store, garden.Id).Count());
        // Nothing else about them changed.
        Assert.Equal(Priority.High, a.Priority);
        Assert.Equal(stays.TaskListId, TaskList.DefaultId);
    }

    // A task already on the target isn't a move, so Undo mustn't try to send it "back".
    [Fact]
    public async Task MovingSeveral_SkipsOnesAlreadyThere()
    {
        var garden = await _actions.CreateListAsync("Garden");
        var already = await _actions.AddAsync("already in the garden", garden.Id, User);
        var moving = await AddAsync("rake the leaves");

        var moves = await _actions.MoveManyAsync([already, moving], garden.Id);

        Assert.Equal([moving.Id], moves.Select(m => m.TaskId));
    }

    // What the batch Undo does: each one straight back to the list it came from.
    [Fact]
    public async Task UndoingABatch_PutsEachBackWhereItWas()
    {
        var garden = await _actions.CreateListAsync("Garden");
        var house = await _actions.CreateListAsync("House");
        var fromTasks = await AddAsync("from Tasks");
        var fromHouse = await _actions.AddAsync("from House", house.Id, User);

        var moves = await _actions.MoveManyAsync([fromTasks, fromHouse], garden.Id);
        foreach (var move in moves)
        {
            await _actions.MoveAsync(_store.Tasks.Single(t => t.Id == move.TaskId), move.FromListId);
        }

        Assert.Equal(TaskList.DefaultId, fromTasks.TaskListId);
        Assert.Equal(house.Id, fromHouse.TaskListId);
        Assert.Empty(TaskView.Tasks(_store, garden.Id));
    }

    [Fact]
    public async Task TheLastListCannotBeDeleted()
    {
        var only = TaskView.Lists(_store).Single();

        Assert.False(await _actions.DeleteListAsync(only));
        Assert.Single(TaskView.Lists(_store));
    }
}
