using Microsoft.EntityFrameworkCore;
using TimeSlices.Web.Collection;
using TimeSlices.Web.Data;

namespace TimeSlices.Web.Progress;

/// <summary>
/// Состояние среза для пользователя, как его видит обучаемый. Тематический срез в каждом треке
/// показывает состояние своего среза (ADR 0014, п. В).
/// </summary>
public readonly record struct SliceStatus(SliceState State, bool IsRepeat)
{
    /// <summary>Считается ли срез пройденным для его последующих (brief 5.3, вариант «ничего не менять»).</summary>
    public bool CountsAsDone => State == SliceState.Done || (State == SliceState.InProgress && IsRepeat);
}

/// <summary>
/// Переходы состояний срезов (brief 5.3, docs/spec/shell-spec.md). Учёт — по паре пользователь × срез
/// (ADR 0014, п. В): пройденный срез пройден во всех своих треках. Методы принимают тематический срез —
/// он нужен навигации («Далее» в пределах трека), но состояние берут только по его срезу.
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
        var bySlice = await GetSliceStatusesAsync(userId);
        return catalog.ThematicSlices.ToDictionary(k => k, k => bySlice[k.SliceId]);
    }

    /// <summary>Состояния срезов; трек ребра в prerequisites на подсчёт не влияет (ADR 0014, п. В.2).</summary>
    public async Task<IReadOnlyDictionary<string, SliceStatus>> GetSliceStatusesAsync(string userId)
    {
        var stored = (await db.UserSliceStates.AsNoTracking().Where(s => s.UserId == userId).ToListAsync())
            .ToDictionary(s => s.SliceId);

        var result = new Dictionary<string, SliceStatus>(StringComparer.Ordinal);
        foreach (var id in catalog.SliceOrder) // топологический порядок: предшественники уже посчитаны
        {
            var open = catalog.PrerequisiteSlicesOf(id).All(p => result[p].CountsAsDone);
            result[id] = !open
                ? new SliceStatus(SliceState.Unavailable, false)
                : stored.TryGetValue(id, out var row)
                    ? new SliceStatus(row.State, row.IsRepeat)
                    : new SliceStatus(SliceState.Available, false);
        }
        return result;
    }

    public async Task<SliceStatus> GetStatusAsync(string userId, ThematicSliceKey key) =>
        (await GetSliceStatusesAsync(userId))[key.SliceId];

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

    /// <summary>
    /// «Далее»: непосредственные последующие срезы, которые сейчас открыты, — по одному на срез.
    /// Если среди них есть срезы текущего трека, предлагаются только они; иначе — переход в другие треки.
    /// </summary>
    public async Task<IReadOnlyList<ThematicSliceKey>> GetNextAsync(string userId, ThematicSliceKey key)
    {
        var statuses = await GetSliceStatusesAsync(userId);
        var open = catalog.FollowingOf(key).Where(k => statuses[k.SliceId].State != SliceState.Unavailable).ToList();
        var inTrack = open.Where(k => k.TrackId == key.TrackId).ToList();
        return inTrack.Count > 0 ? inTrack : open;
    }

    /// <summary>Продвинулся ли пользователь дальше: начат или пройден хотя бы один последующий срез (в любом треке).</summary>
    public async Task<bool> HasAdvancedAsync(string userId, ThematicSliceKey key)
    {
        var statuses = await GetSliceStatusesAsync(userId);
        return catalog.SuccessorSlicesOf(key.SliceId).Any(id => statuses[id].State is SliceState.InProgress or SliceState.Done);
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
            var following = catalog.AllSuccessorSlicesOf(key.SliceId);
            var rows = await db.UserSliceStates.Where(s => s.UserId == userId).ToListAsync();
            db.UserSliceStates.RemoveRange(rows.Where(r => following.Contains(r.SliceId)));
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
            s => s.UserId == userId && s.SliceId == key.SliceId);
        if (row is null)
        {
            row = new UserSliceState { UserId = userId, SliceId = key.SliceId };
            db.UserSliceStates.Add(row);
        }
        row.State = state;
        row.IsRepeat = isRepeat;
        row.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
    }
}
