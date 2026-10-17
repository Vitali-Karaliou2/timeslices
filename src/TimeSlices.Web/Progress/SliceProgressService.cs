using Microsoft.EntityFrameworkCore;
using TimeSlices.Web.Collection;
using TimeSlices.Web.Data;

namespace TimeSlices.Web.Progress;

/// <summary>Состояние тематического среза для пользователя, как его видит обучаемый.</summary>
public readonly record struct SliceStatus(SliceState State, bool IsRepeat)
{
    /// <summary>Считается ли срез пройденным для его последующих (brief 5.3, вариант «ничего не менять»).</summary>
    public bool CountsAsDone => State == SliceState.Done || (State == SliceState.InProgress && IsRepeat);
}

/// <summary>
/// Переходы состояний тематических срезов (brief 5.3, docs/spec/shell-spec.md).
/// Хранятся только InProgress и Done; Unavailable/Available вычисляются по графу.
/// </summary>
public class SliceProgressService(ApplicationDbContext db, ContentCatalog catalog, TimeProvider clock)
{
    /// <summary>
    /// Состояния всех тематических срезов. Граф главнее сохранённых строк: если хотя бы один
    /// предшественник не пройден, срез недоступен, что бы ни было записано в БД.
    /// </summary>
    public async Task<IReadOnlyDictionary<ThematicSliceKey, SliceStatus>> GetStatusesAsync(string userId)
    {
        var stored = (await db.UserSliceStates.AsNoTracking().Where(s => s.UserId == userId).ToListAsync())
            .ToDictionary(s => new ThematicSliceKey(s.SliceId, s.TrackId));

        var result = new Dictionary<ThematicSliceKey, SliceStatus>();
        foreach (var key in catalog.ThematicSlices) // топологический порядок: предшественники уже посчитаны
        {
            var open = catalog.PrerequisitesOf(key).All(p => result[p].CountsAsDone);
            result[key] = !open
                ? new SliceStatus(SliceState.Unavailable, false)
                : stored.TryGetValue(key, out var row)
                    ? new SliceStatus(row.State, row.IsRepeat)
                    : new SliceStatus(SliceState.Available, false);
        }
        return result;
    }

    public async Task<SliceStatus> GetStatusAsync(string userId, ThematicSliceKey key) =>
        (await GetStatusesAsync(userId))[key];

    /// <summary>Открытие доступного среза переводит его в «в процессе».</summary>
    public async Task<SliceStatus> OpenAsync(string userId, ThematicSliceKey key)
    {
        var status = await GetStatusAsync(userId, key);
        if (status.State != SliceState.Available)
            return status;
        await SaveAsync(userId, key, SliceState.InProgress, isRepeat: false);
        return new SliceStatus(SliceState.InProgress, false);
    }

    /// <summary>«Готово»: доступный или начатый срез становится пройденным. Недоступный — нет.</summary>
    public async Task<bool> MarkDoneAsync(string userId, ThematicSliceKey key)
    {
        var status = await GetStatusAsync(userId, key);
        if (status.State == SliceState.Unavailable)
            return false;
        if (status.State != SliceState.Done)
            await SaveAsync(userId, key, SliceState.Done, isRepeat: false);
        return true;
    }

    /// <summary>«Далее»: непосредственные последующие тематические срезы, которые сейчас открыты.</summary>
    public async Task<IReadOnlyList<ThematicSliceKey>> GetNextAsync(string userId, ThematicSliceKey key)
    {
        var statuses = await GetStatusesAsync(userId);
        return catalog.SuccessorsOf(key).Where(k => statuses[k].State != SliceState.Unavailable).ToList();
    }

    /// <summary>Продвинулся ли пользователь дальше: начат или пройден хотя бы один последующий срез.</summary>
    public async Task<bool> HasAdvancedAsync(string userId, ThematicSliceKey key)
    {
        var statuses = await GetStatusesAsync(userId);
        return catalog.SuccessorsOf(key).Any(k => statuses[k].State is SliceState.InProgress or SliceState.Done);
    }

    /// <summary>
    /// Повторное прохождение пройденного среза (brief 5.3).
    /// Если дальше не продвигался — срез просто «в процессе», <paramref name="resetFollowing"/> не важен.
    /// Иначе: resetFollowing = true — все последующие (транзитивно) снова недоступны;
    /// false — ничего не менять: срез «в процессе» с пометкой «повторно», последующие пройдены.
    /// </summary>
    public async Task<bool> RepeatAsync(string userId, ThematicSliceKey key, bool resetFollowing)
    {
        var status = await GetStatusAsync(userId, key);
        if (status.State != SliceState.Done)
            return false;

        if (!await HasAdvancedAsync(userId, key))
        {
            await SaveAsync(userId, key, SliceState.InProgress, isRepeat: false);
        }
        else if (resetFollowing)
        {
            var following = catalog.AllSuccessorsOf(key);
            var rows = await db.UserSliceStates.Where(s => s.UserId == userId).ToListAsync();
            db.UserSliceStates.RemoveRange(rows.Where(r => following.Contains(new ThematicSliceKey(r.SliceId, r.TrackId))));
            await SaveAsync(userId, key, SliceState.InProgress, isRepeat: false);
        }
        else
        {
            await SaveAsync(userId, key, SliceState.InProgress, isRepeat: true);
        }
        return true;
    }

    private async Task SaveAsync(string userId, ThematicSliceKey key, SliceState state, bool isRepeat)
    {
        try
        {
            await UpsertAsync(userId, key, state, isRepeat);
        }
        catch (DbUpdateException)
        {
            // Двойное нажатие: параллельный запрос успел вставить ту же строку (уникальный индекс).
            // Сбрасываем отслеживание и повторяем — теперь это обновление существующей строки.
            db.ChangeTracker.Clear();
            await UpsertAsync(userId, key, state, isRepeat);
        }
    }

    private async Task UpsertAsync(string userId, ThematicSliceKey key, SliceState state, bool isRepeat)
    {
        var row = await db.UserSliceStates.SingleOrDefaultAsync(
            s => s.UserId == userId && s.SliceId == key.SliceId && s.TrackId == key.TrackId);
        if (row is null)
        {
            row = new UserSliceState { UserId = userId, SliceId = key.SliceId, TrackId = key.TrackId };
            db.UserSliceStates.Add(row);
        }
        row.State = state;
        row.IsRepeat = isRepeat;
        row.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
    }
}
