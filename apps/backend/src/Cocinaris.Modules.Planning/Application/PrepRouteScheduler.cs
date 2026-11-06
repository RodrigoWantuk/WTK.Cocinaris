using System.Text.Json;
using Cocinaris.Modules.Planning.Domain;
using Cocinaris.Modules.Recipes.Application;

namespace Cocinaris.Modules.Planning.Application;

/// <summary>One task of a preparation session.</summary>
/// <param name="Key">Identifier that is stable for this menu entry: the entry, then the task's own id.</param>
/// <param name="EntryId">Menu entry the task cooks.</param>
/// <param name="Kind"><c>preparation</c> or <c>stage</c>.</param>
/// <param name="Name">Name, as the recipe has it.</param>
/// <param name="Instructions">Instructions, as the recipe has them.</param>
/// <param name="SensoryCues">How to tell it is done.</param>
/// <param name="ActiveMinutes">Hands-on minutes, which keep the cook busy.</param>
/// <param name="PassiveMinutes">Minutes it runs alone after the hands-on part.</param>
/// <param name="DependsOn">Keys of the tasks that must finish first.</param>
public sealed record PrepRouteTask(string Key, Guid EntryId, string Kind, string Name, string Instructions, IReadOnlyList<string> SensoryCues, int ActiveMinutes, int PassiveMinutes, IReadOnlyList<string> DependsOn);

/// <summary>When one task runs, in minutes from the start of the session.</summary>
/// <param name="Key">Task.</param>
/// <param name="StartMinute">When it starts.</param>
/// <param name="ActiveEndMinute">When the cook is free again.</param>
/// <param name="EndMinute">When it has finished, passive part included.</param>
public sealed record PrepRouteSlot(string Key, int StartMinute, int ActiveEndMinute, int EndMinute);

/// <summary>A session's order of work.</summary>
/// <param name="Slots">Tasks in the order the cook starts them.</param>
/// <param name="TotalMinutes">When the last task finishes.</param>
/// <param name="CycleFallback">Whether the recipes' dependencies were circular, so the recipes run one after another.</param>
public sealed record PrepRouteSchedule(IReadOnlyList<PrepRouteSlot> Slots, int TotalMinutes, bool CycleFallback);

/// <summary>
/// Merges the session's recipes into one order of work for one cook (PLAN-0062 Slice 6). Pure and
/// deterministic, because a person who reloads the page must find the same route. Hands-on parts never
/// overlap; passive parts run in the background, which is where the session saves its time. Recipes have
/// no equipment per stage, so burner and oven conflicts are not detected, and the screen says so.
/// </summary>
public static class PrepRouteScheduler
{
    /// <summary>Orders the tasks. Ties follow the input's order, which is recipe order and then task order.</summary>
    /// <param name="tasks">Tasks in recipe order. Dependencies on unknown keys are ignored.</param>
    public static PrepRouteSchedule Schedule(IReadOnlyList<PrepRouteTask> tasks)
    {
        var known = tasks.Select(task => task.Key).ToHashSet(StringComparer.Ordinal);
        var dependencies = tasks.ToDictionary(
            task => task.Key,
            task => (IReadOnlyList<string>)task.DependsOn.Where(key => known.Contains(key) && key != task.Key).Distinct(StringComparer.Ordinal).ToList(),
            StringComparer.Ordinal);
        var cycle = HasCycle(tasks, dependencies);
        if (cycle)
        {
            // Model-written dependencies can be wrong. One after another is slow, but it is always doable.
            for (var index = 0; index < tasks.Count; index++)
            {
                dependencies[tasks[index].Key] = index == 0 ? [] : [tasks[index - 1].Key];
            }
        }

        var remaining = RemainingPaths(tasks, dependencies);
        var order = tasks.Select((task, index) => (task.Key, index)).ToDictionary(item => item.Key, item => item.index, StringComparer.Ordinal);
        var pending = tasks.ToList();
        var ends = new Dictionary<string, int>(StringComparer.Ordinal);
        var slots = new List<PrepRouteSlot>(tasks.Count);
        var time = 0;
        while (pending.Count > 0)
        {
            var ready = pending.Where(task => dependencies[task.Key].All(key => ends.TryGetValue(key, out var end) && end <= time)).ToList();
            if (ready.Count == 0)
            {
                // Nothing can start until something in the background finishes. Cycles were removed
                // above, so a later finish always exists.
                time = ends.Values.Where(end => end > time).Min();
                continue;
            }

            var next = ready.OrderByDescending(task => remaining[task.Key]).ThenBy(task => order[task.Key]).First();
            var activeEnd = time + next.ActiveMinutes;
            var finish = activeEnd + next.PassiveMinutes;
            slots.Add(new PrepRouteSlot(next.Key, time, activeEnd, finish));
            ends[next.Key] = finish;
            pending.Remove(next);
            time = activeEnd;
        }

        return new PrepRouteSchedule(slots, slots.Count == 0 ? 0 : slots.Max(slot => slot.EndMinute), cycle);
    }

    private static bool HasCycle(IReadOnlyList<PrepRouteTask> tasks, Dictionary<string, IReadOnlyList<string>> dependencies)
    {
        var left = tasks.ToDictionary(task => task.Key, task => dependencies[task.Key].Count, StringComparer.Ordinal);
        var dependents = Dependents(tasks, dependencies);
        var free = new Queue<string>(left.Where(item => item.Value == 0).Select(item => item.Key));
        var visited = 0;
        while (free.TryDequeue(out var key))
        {
            visited++;
            foreach (var dependent in dependents[key])
            {
                if (--left[dependent] == 0)
                {
                    free.Enqueue(dependent);
                }
            }
        }

        return visited != tasks.Count;
    }

    /// <summary>Minutes from each task's start to the end of the longest chain it leads, computed from the last tasks back.</summary>
    private static Dictionary<string, int> RemainingPaths(IReadOnlyList<PrepRouteTask> tasks, Dictionary<string, IReadOnlyList<string>> dependencies)
    {
        var dependents = Dependents(tasks, dependencies);
        var byKey = tasks.ToDictionary(task => task.Key, StringComparer.Ordinal);
        var left = tasks.ToDictionary(task => task.Key, task => dependents[task.Key].Count, StringComparer.Ordinal);
        var free = new Queue<string>(left.Where(item => item.Value == 0).Select(item => item.Key));
        var remaining = new Dictionary<string, int>(StringComparer.Ordinal);
        while (free.TryDequeue(out var key))
        {
            var task = byKey[key];
            remaining[key] = task.ActiveMinutes + task.PassiveMinutes + dependents[key].Select(item => remaining[item]).DefaultIfEmpty(0).Max();
            foreach (var dependency in dependencies[key])
            {
                if (--left[dependency] == 0)
                {
                    free.Enqueue(dependency);
                }
            }
        }

        return remaining;
    }

    private static Dictionary<string, List<string>> Dependents(IReadOnlyList<PrepRouteTask> tasks, Dictionary<string, IReadOnlyList<string>> dependencies)
    {
        var dependents = tasks.ToDictionary(task => task.Key, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var task in tasks)
        {
            foreach (var dependency in dependencies[task.Key])
            {
                dependents[dependency].Add(task.Key);
            }
        }

        return dependents;
    }
}

/// <summary>A dish of the session, as the route builder reads it.</summary>
/// <param name="EntryId">Menu entry.</param>
/// <param name="NormalizedRecipeJson">The revision this entry cooks.</param>
/// <param name="ProducesKey">Match key of what a base makes; null for a dish.</param>
/// <param name="IngredientKeys">Match keys of its ingredients.</param>
public sealed record PrepRouteRecipe(Guid EntryId, string NormalizedRecipeJson, string? ProducesKey, IReadOnlySet<string> IngredientKeys);

/// <summary>Turns a session's recipes into tasks with their dependencies (PLAN-0062 Slice 6).</summary>
public static class PrepRouteBuilder
{
    /// <summary>Reads every recipe's tasks. Malformed content yields no tasks instead of failing the page.</summary>
    /// <param name="recipes">Recipes in route order: bases first.</param>
    public static IReadOnlyList<PrepRouteTask> Build(IReadOnlyList<PrepRouteRecipe> recipes)
    {
        var read = recipes.Select(recipe => (Recipe: recipe, Tasks: ReadTasks(recipe))).ToList();
        var tasks = new List<PrepRouteTask>();
        foreach (var (recipe, own) in read)
        {
            // A dish waits for the base it uses, once that base's own tasks have all finished.
            var bases = read
                .Where(other => other.Recipe.EntryId != recipe.EntryId && other.Recipe.ProducesKey is { } key && recipe.IngredientKeys.Contains(key))
                .SelectMany(other => other.Tasks.Select(task => task.Key))
                .ToList();
            foreach (var task in own)
            {
                tasks.Add(task.Kind == "stage" && !task.DependsOn.Any(key => own.Any(item => item.Key == key && item.Kind == "stage"))
                    ? task with { DependsOn = [.. task.DependsOn, .. bases] }
                    : task);
            }
        }

        return tasks;
    }

    private static List<PrepRouteTask> ReadTasks(PrepRouteRecipe recipe)
    {
        try
        {
            using var document = JsonDocument.Parse(recipe.NormalizedRecipeJson);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("recipe", out var inner))
            {
                root = inner;
            }

            return root.ValueKind == JsonValueKind.Object ? ReadTasks(recipe.EntryId, root) : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<PrepRouteTask> ReadTasks(Guid entryId, JsonElement root)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        string Unique(string? id, string fallback)
        {
            var value = string.IsNullOrWhiteSpace(id) ? fallback : id;
            for (var attempt = 2; !used.Add(value); attempt++)
            {
                value = $"{fallback}-{attempt}";
            }

            return value;
        }

        string KeyOf(string id) => $"{entryId:n}:{id}";

        var preparations = new List<PrepRouteTask>();
        foreach (var (item, index) in Items(root, "preparations"))
        {
            var active = Minutes(item, "activeMinutes");
            // A task such as marinating needs its lead time before cooking, even if the recipe counts it nowhere else.
            var passive = Math.Max(Minutes(item, "passiveMinutes"), Minutes(item, "minimumLeadMinutes") - active);
            preparations.Add(new PrepRouteTask(KeyOf(Unique(Text(item, "taskId"), $"p{index}")), entryId, "preparation", Text(item, "name") ?? string.Empty, Text(item, "instructions") ?? string.Empty, [], active, passive, []));
        }

        var stageIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var stageRows = new List<(JsonElement Item, string Id, string Key)>();
        foreach (var (item, index) in Items(root, "stages"))
        {
            var source = Text(item, "stageId");
            var id = Unique(source, $"s{index}");
            stageIds.TryAdd(source ?? id, id);
            stageRows.Add((item, id, KeyOf(id)));
        }

        var extra = ReadDependencies(root);
        var stages = new List<PrepRouteTask>();
        for (var index = 0; index < stageRows.Count; index++)
        {
            var (item, id, key) = stageRows[index];
            var declared = Strings(item, "dependsOn")
                .Concat(extra.Where(pair => pair.Task == id).Select(pair => pair.DependsOn))
                .Select(value => stageIds.TryGetValue(value, out var mapped) ? KeyOf(mapped) : KeyOf(value))
                .Where(value => value != key)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            // Stages are written in cooking order. An empty list on a later stage often means the writer
            // left it out, not that the stage may start first, so only stated dependencies create parallel work.
            if (declared.Count == 0 && index > 0)
            {
                declared.Add(stageRows[index - 1].Key);
            }

            var dependsOn = declared.Any(value => stageRows.Any(row => row.Key == value))
                ? declared
                : [.. declared, .. preparations.Select(task => task.Key).Where(value => !declared.Contains(value))];
            stages.Add(new PrepRouteTask(key, entryId, "stage", Text(item, "name") ?? Text(item, "title") ?? string.Empty, Text(item, "instructions") ?? string.Empty, Strings(item, "sensoryCues"), Minutes(item, "activeMinutes"), Minutes(item, "passiveMinutes"), dependsOn));
        }

        return [.. preparations, .. stages];
    }

    private static List<(string Task, string DependsOn)> ReadDependencies(JsonElement root)
    {
        var pairs = new List<(string, string)>();
        foreach (var (item, _) in Items(root, "dependencies"))
        {
            if (Text(item, "fromId") is not { } from || Text(item, "toId") is not { } to)
            {
                continue;
            }

            // "from before to" means "to" waits; "after" and "requires" mean "from" waits.
            pairs.Add(Text(item, "relationship") == "before" ? (to, from) : (from, to));
        }

        return pairs;
    }

    private static IEnumerable<(JsonElement Item, int Index)> Items(JsonElement root, string name) =>
        root.TryGetProperty(name, out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).Select((item, index) => (item, index))
            : [];

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Minutes(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var minutes) ? Math.Max(0, minutes) : 0;

    private static List<string> Strings(JsonElement item, string name) =>
        item.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!).ToList()
            : [];
}

/// <summary>One ingredient of a dish in the session, already for the portions being cooked.</summary>
/// <param name="Name">Name.</param>
/// <param name="Quantity">Quantity.</param>
/// <param name="Unit"><c>g</c>, <c>ml</c>, or <c>unit</c>.</param>
/// <param name="Optional">Whether the dish works without it.</param>
public sealed record ShoppingPlanRouteIngredientView(string Name, decimal Quantity, string Unit, bool Optional);

/// <summary>One dish in the session.</summary>
/// <param name="EntryId">Menu entry.</param>
/// <param name="RecipeId">Recipe.</param>
/// <param name="Name">Name.</param>
/// <param name="Servings">Portions.</param>
/// <param name="IsComponent">Whether it is a base.</param>
/// <param name="ProducesName">What a base makes.</param>
/// <param name="Status"><c>planned</c>, <c>cooking</c>, or <c>cooked</c>.</param>
/// <param name="ExecutionId">Its latest cooking session, if any.</param>
/// <param name="Ingredients">Ingredients, scaled.</param>
public sealed record ShoppingPlanRouteRecipeView(Guid EntryId, Guid RecipeId, string Name, int Servings, bool IsComponent, string? ProducesName, string Status, Guid? ExecutionId, IReadOnlyList<ShoppingPlanRouteIngredientView> Ingredients);

/// <summary>One task on the route.</summary>
/// <param name="TaskKey">Task.</param>
/// <param name="EntryId">Dish it belongs to.</param>
/// <param name="Kind"><c>preparation</c> or <c>stage</c>.</param>
/// <param name="Name">Name.</param>
/// <param name="Instructions">Instructions.</param>
/// <param name="SensoryCues">How to tell it is done.</param>
/// <param name="ActiveMinutes">Hands-on minutes.</param>
/// <param name="PassiveMinutes">Minutes it runs alone.</param>
/// <param name="StartMinute">Start, from the beginning of the session.</param>
/// <param name="ActiveEndMinute">When the cook is free.</param>
/// <param name="EndMinute">When it has finished.</param>
/// <param name="DependsOn">Tasks that must finish first.</param>
public sealed record ShoppingPlanRouteTaskView(string TaskKey, Guid EntryId, string Kind, string Name, string Instructions, IReadOnlyList<string> SensoryCues, int ActiveMinutes, int PassiveMinutes, int StartMinute, int ActiveEndMinute, int EndMinute, IReadOnlyList<string> DependsOn);

/// <summary>A preparation session's route.</summary>
/// <param name="SessionId">Plan.</param>
/// <param name="MenuId">Its menu.</param>
/// <param name="PrepDate">Session day: the day dishes are cooked ahead, or the setup's day on older plans.</param>
/// <param name="TotalMinutes">When the last task finishes.</param>
/// <param name="BudgetMinutes">Time the person set aside, if any.</param>
/// <param name="OverBudget">Whether the route takes longer than that.</param>
/// <param name="Recipes">Dishes, bases first.</param>
/// <param name="Tasks">Tasks, in the order the cook starts them.</param>
/// <param name="Progress">What was done. Tasks of a dish already cooked always count as done.</param>
public sealed record ShoppingPlanRouteView(Guid SessionId, Guid MenuId, DateOnly? PrepDate, int TotalMinutes, int? BudgetMinutes, bool OverBudget, IReadOnlyList<ShoppingPlanRouteRecipeView> Recipes, IReadOnlyList<ShoppingPlanRouteTaskView> Tasks, ShoppingPlanRouteProgress Progress);

/// <summary>Where a preparation session can be opened from.</summary>
/// <param name="SessionId">The plan whose session is still to finish; null when there is none.</param>
public sealed record ShoppingPlanRouteLinkView(Guid? SessionId);

/// <summary>
/// A plan's preparation session (PLAN-0062 Slice 6). It exists only after the plan is accepted, because
/// cooking uses the pantry and a draft must never reserve anything. It reads the menu and changes nothing
/// except the session's own progress; cooking itself goes through recipe executions.
/// </summary>
public sealed class ShoppingPlanRouteService(
    Cocinaris.Modules.Identity.ICurrentUserAccessor currentUser,
    IShoppingPlanStore store,
    MenuPlanningApplicationService planning,
    IRecipeLibraryStore recipes,
    Cocinaris.Modules.Ingredients.IIngredientMatching matching,
    TimeProvider timeProvider)
{
    private const int MaxProgressItems = 1000;
    private const int RecentPlansToSearch = 5;
    private static readonly System.Diagnostics.Metrics.Meter Meter = new("Cocinaris.ShoppingPlanRoutes", "1.0.0");
    private static readonly System.Diagnostics.Metrics.Counter<long> CycleFallbacks = Meter.CreateCounter<long>("cocinaris.shopping_plan.route_cycle_fallback");

    /// <summary>Gets the route.</summary>
    /// <param name="sessionId">Plan.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanRouteView>> GetAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var session = await store.FindAsync(user.Id, sessionId, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        return !IsAccepted(session)
            ? NotAccepted()
            : RecipeApplicationResult<ShoppingPlanRouteView>.Succeeded(await BuildAsync(user.Id, session, cancellationToken) ?? throw new InvalidOperationException("A plan lost its menu."));
    }

    /// <summary>
    /// Replaces the session's progress. The browser always sends the whole state, so a retry is harmless.
    /// Keys no longer on the route are dropped, because the person may have changed the menu meanwhile.
    /// </summary>
    /// <param name="sessionId">Plan.</param>
    /// <param name="progress">Progress.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanRouteView>> SaveProgressAsync(Guid sessionId, ShoppingPlanRouteProgress? progress, CancellationToken cancellationToken)
    {
        if (progress?.Done is null || progress.Skipped is null || progress.Timers is null
            || progress.Done.Count + progress.Skipped.Count + progress.Timers.Count > MaxProgressItems)
        {
            return RecipeApplicationResult<ShoppingPlanRouteView>.Failure("validation_failed", "The progress is invalid.", new Dictionary<string, string[]> { ["progress"] = ["The progress is invalid."] });
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var session = await store.FindAsync(user.Id, sessionId, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        if (!IsAccepted(session))
        {
            return NotAccepted();
        }

        var route = await BuildAsync(user.Id, session, cancellationToken);
        if (route is null)
        {
            return NotFound();
        }

        var known = route.Tasks.Select(task => task.TaskKey).ToHashSet(StringComparer.Ordinal);
        session.RouteProgress = new ShoppingPlanRouteProgress(
            progress.Done.Where(known.Contains).Distinct(StringComparer.Ordinal).ToList(),
            progress.Skipped.Where(known.Contains).Distinct(StringComparer.Ordinal).ToList(),
            progress.Timers.Where(timer => known.Contains(timer.TaskKey)).DistinctBy(timer => timer.TaskKey, StringComparer.Ordinal).ToList());
        session.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveAsync(session, cancellationToken);
        return RecipeApplicationResult<ShoppingPlanRouteView>.Succeeded(route with { Progress = WithCooked(session.RouteProgress, route.Recipes, route.Tasks) });
    }

    /// <summary>Finds a session still to finish, for the menu page or for "Hoje".</summary>
    /// <param name="menuId">A menu, if asked from the menu page.</param>
    /// <param name="date">The owner's day, if asked from "Hoje".</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanRouteLinkView>> FindLinkAsync(Guid? menuId, DateOnly? date, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        IReadOnlyList<ShoppingPlanSession> candidates = menuId is { } id
            ? await store.FindFinishedByMenuAsync(user.Id, id, cancellationToken) is { } found ? [found] : []
            : await store.ListFinishedAsync(user.Id, RecentPlansToSearch, cancellationToken);
        foreach (var session in candidates)
        {
            var route = await BuildAsync(user.Id, session, cancellationToken);
            if (route is { PrepDate: { } day } && (date is null || day == date) && route.Recipes.Any(item => item.Status != "cooked"))
            {
                return RecipeApplicationResult<ShoppingPlanRouteLinkView>.Succeeded(new ShoppingPlanRouteLinkView(session.Id));
            }
        }

        return RecipeApplicationResult<ShoppingPlanRouteLinkView>.Succeeded(new ShoppingPlanRouteLinkView(null));
    }

    private async Task<ShoppingPlanRouteView?> BuildAsync(Guid ownerUserId, ShoppingPlanSession session, CancellationToken cancellationToken)
    {
        var menu = await planning.GetAsync(session.MenuPlanId, cancellationToken);
        if (menu.Value is not { } view)
        {
            return null;
        }

        var legacy = session.Setup.PrepDate is not null;
        var routeDay = view.Status == "accepted" ? RouteDay(session, view.Entries) : null;
        var entries = routeDay is not { } day
            ? []
            : view.Entries
                .Where(entry => entry.Status != "skipped" && (entry.CookOnDate ?? (legacy ? entry.PlannedDate : null)) == day)
                .OrderBy(entry => entry.Kind == "component" ? 0 : 1)
                .ThenBy(entry => entry.SuggestedOrder ?? int.MaxValue)
                .ToList();
        var contents = new List<(MenuEntryView Entry, RecipeRevisionContent Content, List<RecipeIngredient> Ingredients)>();
        foreach (var entry in entries)
        {
            // A deleted recipe cannot be cooked, so it has no place on the route.
            if (await recipes.FindRevisionAsync(ownerUserId, entry.RecipeId, entry.RevisionNumber, cancellationToken) is { } content)
            {
                contents.Add((entry, content, RecipeIngredientReader.Read(content.NormalizedRecipeJson).ToList()));
            }
        }

        var names = contents.SelectMany(item => item.Ingredients.Select(ingredient => ingredient.Name)).Concat(contents.Select(item => item.Entry.ProducesName).OfType<string>());
        var keys = await matching.KeysAsync(ownerUserId, names, cancellationToken);
        var tasks = PrepRouteBuilder.Build(contents.Select(item => new PrepRouteRecipe(
            item.Entry.EntryId,
            item.Content.NormalizedRecipeJson,
            item.Entry.ProducesName is { } made ? keys.Key(made) : null,
            item.Ingredients.Where(ingredient => !ingredient.Optional).Select(ingredient => keys.Key(ingredient.Name)).ToHashSet(StringComparer.Ordinal))).ToList());
        var schedule = PrepRouteScheduler.Schedule(tasks);
        if (schedule.CycleFallback)
        {
            CycleFallbacks.Add(1);
        }

        var byKey = tasks.ToDictionary(task => task.Key, StringComparer.Ordinal);
        var taskViews = schedule.Slots.Select(slot =>
        {
            var task = byKey[slot.Key];
            return new ShoppingPlanRouteTaskView(task.Key, task.EntryId, task.Kind, task.Name, task.Instructions, task.SensoryCues, task.ActiveMinutes, task.PassiveMinutes, slot.StartMinute, slot.ActiveEndMinute, slot.EndMinute, task.DependsOn);
        }).ToList();
        var recipeViews = contents.Select(item => new ShoppingPlanRouteRecipeView(
            item.Entry.EntryId,
            item.Entry.RecipeId,
            item.Entry.RecipeName,
            item.Entry.Servings,
            item.Entry.Kind == "component",
            item.Entry.ProducesName,
            item.Entry.Status,
            item.Entry.ExecutionId,
            item.Ingredients.Select(ingredient => new ShoppingPlanRouteIngredientView(
                ingredient.Name,
                RecipeIngredientReader.Scale(ingredient.Quantity, RecipeIngredientReader.ToLotUnit(ingredient.Unit), item.Entry.Servings / (decimal)Math.Max(1, item.Content.Servings)),
                ingredient.Unit,
                ingredient.Optional)).ToList())).ToList();
        var budget = session.Setup.PrepMinutes;
        return new ShoppingPlanRouteView(session.Id, session.MenuPlanId, routeDay, schedule.TotalMinutes, budget, budget is { } limit && schedule.TotalMinutes > limit, recipeViews, taskViews, WithCooked(session.RouteProgress, recipeViews, taskViews));
    }

    /// <summary>
    /// The day of the menu's preparation session. Dishes the person chose to cook ahead (PLAN-0065
    /// Slice 4) set it: the earliest such day with something still to cook, else the last one, so a
    /// finished session stays visible. A plan from before then that has none keeps the day it was
    /// asked for in its setup, with the dishes dated that day.
    /// </summary>
    private static DateOnly? RouteDay(ShoppingPlanSession session, IReadOnlyList<MenuEntryView> entries)
    {
        var ahead = entries.Where(entry => entry.CookOnDate is not null && entry.Status != "skipped").ToList();
        var open = ahead.Where(entry => entry.Status != "cooked").Select(entry => entry.CookOnDate!.Value).ToList();
        return open.Count > 0 ? open.Min()
            : session.Setup.PrepDate is { } asked ? asked
            : ahead.Count > 0 ? ahead.Max(entry => entry.CookOnDate!.Value)
            : null;
    }

    // A menu being changed after acceptance (PLAN-0065 Slice 4) is still in use.
    private static bool IsAccepted(ShoppingPlanSession session) => session.Status is ShoppingPlanStatus.Finished or ShoppingPlanStatus.Amending;

    private static ShoppingPlanRouteProgress WithCooked(ShoppingPlanRouteProgress? progress, IReadOnlyList<ShoppingPlanRouteRecipeView> recipeViews, IReadOnlyList<ShoppingPlanRouteTaskView> taskViews)
    {
        var cooked = recipeViews.Where(item => item.Status == "cooked").Select(item => item.EntryId).ToHashSet();
        var done = (progress?.Done ?? []).Concat(taskViews.Where(task => cooked.Contains(task.EntryId)).Select(task => task.TaskKey)).Distinct(StringComparer.Ordinal).ToList();
        return new ShoppingPlanRouteProgress(done, progress?.Skipped ?? [], progress?.Timers ?? []);
    }

    private static RecipeApplicationResult<ShoppingPlanRouteView> NotFound() =>
        RecipeApplicationResult<ShoppingPlanRouteView>.Failure("resource_not_found", "The plan was not found.");

    private static RecipeApplicationResult<ShoppingPlanRouteView> NotAccepted() =>
        RecipeApplicationResult<ShoppingPlanRouteView>.Failure("domain_rule_violated", "Accept the plan before starting its preparation.");
}
