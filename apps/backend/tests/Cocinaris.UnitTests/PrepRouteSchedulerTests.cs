using Cocinaris.Modules.Planning.Application;

namespace Cocinaris.UnitTests;

/// <summary>PLAN-0062 Slice 6: merging a session's recipes into one order of work for one cook.</summary>
public sealed class PrepRouteSchedulerTests
{
    private static readonly Guid DishA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid DishB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private static PrepRouteTask Task(string key, int active, int passive, params string[] dependsOn) =>
        new(key, DishA, "stage", key, string.Empty, [], active, passive, dependsOn);

    private static PrepRouteSlot Slot(PrepRouteSchedule schedule, string key) => schedule.Slots.Single(slot => slot.Key == key);

    [Fact]
    public void PassiveWorkRunsInTheBackgroundWhileTheCookDoesSomethingElse()
    {
        var schedule = PrepRouteScheduler.Schedule([Task("feijao", 10, 60), Task("salada", 20, 0)]);

        // The longest chain goes first, and the salad starts as soon as the cook's hands are free.
        Assert.Equal(new PrepRouteSlot("feijao", 0, 10, 70), Slot(schedule, "feijao"));
        Assert.Equal(new PrepRouteSlot("salada", 10, 30, 30), Slot(schedule, "salada"));
        Assert.Equal(70, schedule.TotalMinutes);
    }

    [Fact]
    public void HandsOnPartsNeverOverlap()
    {
        var schedule = PrepRouteScheduler.Schedule([Task("a", 10, 5), Task("b", 15, 0), Task("c", 5, 30)]);

        var busy = schedule.Slots.OrderBy(slot => slot.StartMinute).ToList();
        for (var index = 1; index < busy.Count; index++)
        {
            Assert.True(busy[index].StartMinute >= busy[index - 1].ActiveEndMinute);
        }
    }

    [Fact]
    public void ATaskWithNoHandsOnPartDoesNotUseTheCook()
    {
        var schedule = PrepRouteScheduler.Schedule([Task("demolhar", 0, 30), Task("picar", 10, 0)]);

        Assert.Equal(0, Slot(schedule, "demolhar").StartMinute);
        Assert.Equal(0, Slot(schedule, "picar").StartMinute);
        Assert.Equal(30, schedule.TotalMinutes);
    }

    [Fact]
    public void ATaskWaitsForADependencyIncludingItsPassivePart()
    {
        var schedule = PrepRouteScheduler.Schedule([Task("assar", 5, 40), Task("rechear", 10, 0, "assar")]);

        Assert.Equal(45, Slot(schedule, "rechear").StartMinute);
        Assert.Equal(55, schedule.TotalMinutes);
    }

    [Fact]
    public void TheCookWaitsWhenNothingIsReady()
    {
        var schedule = PrepRouteScheduler.Schedule([Task("a", 5, 20), Task("b", 5, 0, "a"), Task("c", 5, 0, "b")]);

        Assert.Equal([0, 25, 30], schedule.Slots.Select(slot => slot.StartMinute));
    }

    [Fact]
    public void TiesFollowRecipeOrderAndTheResultIsDeterministic()
    {
        PrepRouteTask[] tasks = [Task("a", 10, 0), Task("b", 10, 0), Task("c", 10, 0)];

        var first = PrepRouteScheduler.Schedule(tasks);
        var second = PrepRouteScheduler.Schedule(tasks);

        Assert.Equal(["a", "b", "c"], first.Slots.Select(slot => slot.Key));
        Assert.Equal(first.Slots, second.Slots);
    }

    [Fact]
    public void CircularDependenciesFallBackToOneAfterAnother()
    {
        var schedule = PrepRouteScheduler.Schedule([Task("a", 10, 0, "b"), Task("b", 10, 0, "a"), Task("c", 5, 0)]);

        Assert.True(schedule.CycleFallback);
        Assert.Equal(["a", "b", "c"], schedule.Slots.Select(slot => slot.Key));
        Assert.Equal(25, schedule.TotalMinutes);
    }

    [Fact]
    public void UnknownAndSelfDependenciesAreIgnored()
    {
        var schedule = PrepRouteScheduler.Schedule([Task("a", 10, 0, "a", "nada")]);

        Assert.False(schedule.CycleFallback);
        Assert.Equal(0, schedule.Slots[0].StartMinute);
    }

    [Fact]
    public void AnEmptyRouteIsEmpty()
    {
        var schedule = PrepRouteScheduler.Schedule([]);

        Assert.Empty(schedule.Slots);
        Assert.Equal(0, schedule.TotalMinutes);
    }

    private static string Recipe(string preparations, string stages, string dependencies = "[]") =>
        $$$"""{"recipe":{"preparations":{{{preparations}}},"stages":{{{stages}}},"dependencies":{{{dependencies}}}}}""";

    private static string Stage(string id, int active, int passive, string dependsOn = "[]") =>
        $$$"""{"stageId":"{{{id}}}","name":"{{{id}}}","instructions":"Faça","activeMinutes":{{{active}}},"passiveMinutes":{{{passive}}},"dependsOn":{{{dependsOn}}},"sensoryCues":["pronto"]}""";

    private static PrepRouteRecipe Dish(Guid entry, string json, string? produces = null, params string[] keys) =>
        new(entry, json, produces, keys.ToHashSet(StringComparer.Ordinal));

    private static string Key(Guid entry, string id) => $"{entry:n}:{id}";

    [Fact]
    public void StagesWaitForTheirRecipesPreparations()
    {
        var json = Recipe("""[{"taskId":"marinar","name":"Marinar","instructions":"x","activeMinutes":5,"passiveMinutes":30,"minimumLeadMinutes":0}]""", $"[{Stage("assar", 10, 0)}]");

        var tasks = PrepRouteBuilder.Build([Dish(DishA, json)]);

        Assert.Equal([Key(DishA, "marinar")], tasks.Single(task => task.Kind == "stage").DependsOn);
    }

    [Fact]
    public void APreparationsLeadTimeCountsAsWaiting()
    {
        var json = Recipe("""[{"taskId":"marinar","name":"Marinar","instructions":"x","activeMinutes":5,"passiveMinutes":10,"minimumLeadMinutes":60}]""", $"[{Stage("assar", 10, 0)}]");

        var preparation = PrepRouteBuilder.Build([Dish(DishA, json)]).Single(task => task.Kind == "preparation");

        Assert.Equal(55, preparation.PassiveMinutes);
    }

    [Fact]
    public void ALaterStageWithNoStatedDependencyFollowsThePreviousOne()
    {
        var json = Recipe("[]", $"[{Stage("a", 5, 0)},{Stage("b", 5, 0)}]");

        var tasks = PrepRouteBuilder.Build([Dish(DishA, json)]);

        Assert.Empty(tasks[0].DependsOn);
        Assert.Equal([Key(DishA, "a")], tasks[1].DependsOn);
    }

    [Fact]
    public void StatedDependenciesAllowParallelStages()
    {
        var json = Recipe("[]", $"""[{Stage("base", 5, 0)},{Stage("molho", 5, 20, """["base"]""")},{Stage("massa", 5, 0, """["base"]""")}]""");

        var schedule = PrepRouteScheduler.Schedule(PrepRouteBuilder.Build([Dish(DishA, json)]));

        // The pasta does not wait for the sauce's twenty minutes.
        Assert.Equal(10, Slot(schedule, Key(DishA, "massa")).StartMinute);
    }

    [Theory]
    [InlineData("before", "a", "b", "b", "a")]
    [InlineData("after", "b", "a", "b", "a")]
    [InlineData("requires", "b", "a", "b", "a")]
    public void TheDependenciesListIsReadInBothDirections(string relationship, string from, string to, string waiting, string first)
    {
        var json = Recipe("[]", $"[{Stage("a", 5, 0)},{Stage("b", 5, 0, """["a"]""")}]", $$"""[{"fromId":"{{from}}","toId":"{{to}}","relationship":"{{relationship}}"}]""");

        var task = PrepRouteBuilder.Build([Dish(DishA, json)]).Single(item => item.Key == Key(DishA, waiting));

        Assert.Contains(Key(DishA, first), task.DependsOn);
    }

    [Fact]
    public void ADishWaitsForTheBaseItUsesAndNotForOneItDoesNot()
    {
        var baseJson = Recipe("[]", $"[{Stage("cozinhar", 10, 30)}]");
        var dishJson = Recipe("[]", $"[{Stage("montar", 10, 0)},{Stage("assar", 5, 0)}]");

        var uses = PrepRouteBuilder.Build([Dish(DishA, baseJson, "frango desfiado"), Dish(DishB, dishJson, null, "frango desfiado")]);
        var other = PrepRouteBuilder.Build([Dish(DishA, baseJson, "frango desfiado"), Dish(DishB, dishJson, null, "arroz")]);

        Assert.Contains(Key(DishA, "cozinhar"), uses.Single(task => task.Key == Key(DishB, "montar")).DependsOn);
        Assert.DoesNotContain(Key(DishA, "cozinhar"), other.Single(task => task.Key == Key(DishB, "montar")).DependsOn);
        Assert.Equal(40, Slot(PrepRouteScheduler.Schedule(uses), Key(DishB, "montar")).StartMinute);
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("[]")]
    [InlineData("""{"stages":"x"}""")]
    public void MalformedContentYieldsNoTasks(string json) =>
        Assert.Empty(PrepRouteBuilder.Build([Dish(DishA, json)]));

    [Fact]
    public void RepeatedStageIdsStayDistinct()
    {
        var json = Recipe("[]", $"[{Stage("a", 5, 0)},{Stage("a", 5, 0)}]");

        var tasks = PrepRouteBuilder.Build([Dish(DishA, json)]);

        Assert.Equal(2, tasks.Select(task => task.Key).Distinct().Count());
    }
}
