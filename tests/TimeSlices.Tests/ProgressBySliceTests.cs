using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TimeSlices.Web.Assessment;
using TimeSlices.Web.Collection;
using TimeSlices.Web.Data;

namespace TimeSlices.Tests;

/// <summary>Прохождение и сдача — по срезу, а не по тематическому срезу (ADR 0014, п. В).</summary>
public class ProgressBySliceTests : IDisposable
{
    // s в двух треках; n — только в «net», но его ребро подписано треком «start».
    private readonly TestContent _content = new TestContent().Track("start").Track("hw").Track("net")
        .Slice("s", ["start", "hw"], [])
        .Slice("deep", "hw", "s@hw")
        .Slice("n", "net", "s@start")
        .Questions("s", "q1");

    private readonly ProgressFixture _fx;
    private const string User = ProgressFixture.UserId;

    public ProgressBySliceTests()
    {
        _fx = new ProgressFixture(_content.Load());
    }

    public void Dispose()
    {
        _fx.Dispose();
        _content.Dispose();
    }

    [Fact]
    public async Task SliceDoneInOneTrack_IsDoneInTheOther()
    {
        await _fx.Service.MarkDoneAsync(User, new("s", "start"));

        Assert.Equal(SliceState.Done, (await _fx.StatusOf("s@start")).State);
        Assert.Equal(SliceState.Done, (await _fx.StatusOf("s@hw")).State);
        Assert.Single(_fx.Db.UserSliceStates);
    }

    [Fact]
    public async Task EdgeTrack_IsOnlyALabel()
    {
        // n ждёт s@start, deep — s@hw; срез пройден в треке hw — открываются оба.
        await _fx.Service.MarkDoneAsync(User, new("s", "hw"));

        Assert.Equal(SliceState.Available, (await _fx.StatusOf("n@net")).State);
        Assert.Equal(SliceState.Available, (await _fx.StatusOf("deep@hw")).State);
    }

    [Fact]
    public async Task Opening_InOtherTrack_DoesNotRollBackDoneSlice()
    {
        await _fx.Service.MarkDoneAsync(User, new("s", "start"));
        await _fx.Service.OpenAsync(User, new("s", "hw"));

        Assert.Equal(SliceState.Done, (await _fx.StatusOf("s@hw")).State);
    }

    [Fact]
    public async Task Repeat_InOneTrack_SeesAdvanceInAnotherTrack()
    {
        await _fx.Service.MarkDoneAsync(User, new("s", "hw"));
        await _fx.Service.OpenAsync(User, new("n", "net"));

        Assert.True(await _fx.Service.HasAdvancedAsync(User, new("s", "hw")));
        await _fx.Service.RepeatAsync(User, new("s", "hw"), resetFollowing: true);
        Assert.Equal(SliceState.Unavailable, (await _fx.StatusOf("n@net")).State);
    }

    [Fact]
    public async Task Attempt_IsOnePerSlice()
    {
        await _fx.Service.OpenAsync(User, new("s", "start"));
        Assert.Equal(SelfCheckOutcome.Ok, await _fx.SelfCheck.StartAsync(User, new("s", "start")));

        // Из другого трека — та же попытка, вторая не начинается.
        Assert.Equal(SelfCheckOutcome.Conflict, await _fx.SelfCheck.StartAsync(User, new("s", "hw")));
        var fromHw = await _fx.SelfCheck.LatestAsync(User, new("s", "hw"));
        var fromStart = await _fx.SelfCheck.LatestAsync(User, new("s", "start"));
        Assert.NotNull(fromHw);
        Assert.Equal(fromStart!.Id, fromHw.Id);
        Assert.Single(_fx.Db.Attempts);
    }
}

/// <summary>Миграция ProgressBySlice на базе со строками по двум трекам одного среза.</summary>
public class ProgressBySliceMigrationTests
{
    private const string Before = "20261019075233_SelfCheck";

    [Fact]
    public async Task Migration_MergesStatesBySlice_AndKeepsAttempts()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Before);

        await Exec(connection, """
            INSERT INTO "AspNetUsers" ("Id", "UserName", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
                VALUES ('u1', 'u1', 0, 0, 0, 0, 0), ('u2', 'u2', 0, 0, 0, 0, 0);
            INSERT INTO "UserSliceStates" ("UserId", "SliceId", "TrackId", "State", "IsRepeat", "UpdatedAt") VALUES
                ('u1', 'eniac', 'foundations', 'InProgress', 1, '2026-10-20 10:00:00'),
                ('u1', 'eniac', 'hardware',    'Done',       0, '2026-10-21 10:00:00'),
                ('u1', 'edsac', 'hardware',    'InProgress', 0, '2026-10-21 11:00:00'),
                ('u2', 'eniac', 'hardware',    'InProgress', 0, '2026-10-21 12:00:00'),
                ('u2', 'eniac', 'foundations', 'InProgress', 0, '2026-10-21 12:30:00');
            INSERT INTO "Attempts" ("UserId", "SliceId", "TrackId", "StartedAt", "Status") VALUES
                ('u1', 'eniac', 'foundations', '2026-10-20 10:00:00', 'Confirmed'),
                ('u1', 'eniac', 'hardware',    '2026-10-21 10:00:00', 'InProgress'),
                ('u1', 'eniac', 'foundations', '2026-10-21 11:00:00', 'Submitted');
            """);

        await migrator.MigrateAsync();

        var states = await db.UserSliceStates.AsNoTracking().OrderBy(s => s.UserId).ThenBy(s => s.SliceId).ToListAsync();
        Assert.Collection(states,
            s => Assert.Equal(("u1", "edsac", SliceState.InProgress, false), (s.UserId, s.SliceId, s.State, s.IsRepeat)),
            s => Assert.Equal(("u1", "eniac", SliceState.Done, true), (s.UserId, s.SliceId, s.State, s.IsRepeat)),
            s => Assert.Equal(("u2", "eniac", SliceState.InProgress, false), (s.UserId, s.SliceId, s.State, s.IsRepeat)));

        // Попытки не удалены; открытой осталась последняя.
        var attempts = await db.Attempts.AsNoTracking().OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AttemptStatus.Confirmed, AttemptStatus.Returned, AttemptStatus.Submitted],
            attempts.Select(a => a.Status));
        Assert.Contains("ADR 0014", attempts[1].ExaminerComment);
    }

    private static async Task Exec(SqliteConnection connection, string sql)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
