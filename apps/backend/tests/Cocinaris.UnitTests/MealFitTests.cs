using Cocinaris.Modules.Planning.Application;

namespace Cocinaris.UnitTests;

/// <summary>PLAN-0065: dishes must suit the meal. The names come from a real session where every dish was labeled a snack.</summary>
public sealed class MealFitTests
{
    private static bool Snack(string name, string format, int minutes) => MealFit.Default.Fits("snack", name, format, minutes);

    [Theory]
    [InlineData("Frango Crocante Agridoce com Pimenta e Mel", "pedaços empanados", 30)]
    [InlineData("Batata Rústica na Air Fryer com Páprica e Alho", "batata assada em gomos", 40)]
    [InlineData("Espetinhos de Carne com Pimenta e Mel", "espetinho", 45)]
    [InlineData("Batata-Doce Assada Agridoce com Pimenta e Mel", "assado em cubos", 50)]
    public void WhatThePersonAcceptedAsASnackPasses(string name, string format, int minutes) => Assert.True(Snack(name, format, minutes));

    [Theory]
    [InlineData("Pão de Queijo de Frigideira", "pão de queijo de frigideira", 25)]
    [InlineData("Bolinho de polvilho assado com pimenta", "bolinho assado", 50)]
    [InlineData("Pipoca Picante com Manteiga e Páprica", "petisco crocante", 10)]
    [InlineData("Tapioca Recheada de Carne-Seca com Queijo Coalho", "tapioca recheada", 30)]
    [InlineData("Wrap Integral de Tomate Apimentado", "sanduíche wrap", 35)]
    [InlineData("Pastel de Forno de Carne Moída Apimentada", "pastel assado", 55)]
    [InlineData("Bolinhos de Arroz Picantes", "bolinho frito", 45)]
    [InlineData("Cuscuz de Flocão com Molho Picante de Tomate", "cuscuz no vapor", 25)]
    [InlineData("Pasta de grão-de-bico com torradas", "patê", 15)]
    public void RealSnacksPass(string name, string format, int minutes) => Assert.True(Snack(name, format, minutes));

    [Theory]
    [InlineData("Espaguete Agridoce com Tomate, Mel e Pimenta", "massa em molho", 25)]
    [InlineData("Macarrão Picante com Tomate e Pimenta", "macarrão", 35)]
    [InlineData("Frango Agridoce com Pimentão e Cebola Caramelizada", "salteado em frigideira", 25)]
    [InlineData("Tigela de Carne Bovina Apimentada Estilo Jerk", "tigela", 50)]
    [InlineData("Escondidinho de Milho Cremoso", "assadeira cremosa", 45)]
    [InlineData("Escondidinho Individual de Frango com Purê", "escondidinho gratinado", 60)]
    public void FullMealsAreNotSnacks(string name, string format, int minutes) => Assert.False(Snack(name, format, minutes));

    [Fact]
    public void ALongDishIsNotASnack()
    {
        Assert.False(Snack("Batata Assada Recheada com Carne Picante", "batata recheada", 70));
        Assert.True(Snack("Batata Assada Recheada com Carne Picante", "batata recheada", 60));
    }

    [Fact]
    public void WordsMatchWholeAndWithoutAccentsInEveryLanguage()
    {
        Assert.False(Snack("MACARRÃO da casa", "x", 10));
        Assert.False(Snack("Beef stew", "x", 10));
        Assert.False(Snack("Espagueti rápido", "x", 10));
        // Never inside another word.
        Assert.True(Snack("Pratododia biscoito", "x", 10));
        Assert.True(Snack("Sopapo de banana", "x", 10));
    }

    [Theory]
    [InlineData("lunch")]
    [InlineData("dinner")]
    public void AMainMealIsNotASnack(string meal)
    {
        Assert.False(MealFit.Default.Fits(meal, "Pipoca de micro-ondas", "petisco", 5));
        Assert.True(MealFit.Default.Fits(meal, "Espaguete Agridoce", "massa em molho", 25));
        Assert.True(MealFit.Default.Fits(meal, "Feijoada leve", "ensopado", 180));
    }

    [Fact]
    public void BreakfastHasItsOwnRules()
    {
        Assert.False(MealFit.Default.Fits("breakfast", "Feijoada leve", "ensopado", 30));
        Assert.True(MealFit.Default.Fits("breakfast", "Tigela de açaí", "tigela", 10));
    }

    [Fact]
    public void AnUnknownMealIsNotJudged() => Assert.True(MealFit.Default.Fits("brunch", "Macarrão", "macarrão", 500));
}

/// <summary>PLAN-0065: three versions of one dish are one option, not three.</summary>
public sealed class DishSimilarityTests
{
    [Theory]
    [InlineData("Pão de Queijo de Frigideira", "Pão de queijo de frigideira no air fryer")]
    [InlineData("Pão de Queijo de Air Fryer com Molho Picante", "Pão de Queijo de Frigideira")]
    [InlineData("Escondidinho de Milho Cremoso", "Escondidinho de milho com carne moída apimentada")]
    [InlineData("Pastel de Forno de Carne Moída Apimentada", "Pastel de carne moída")]
    [InlineData("PÃO DE QUEIJO", "pao de queijo")]
    public void TheSameDishIsRecognized(string first, string second) => Assert.True(DishSimilarity.IsSameDish(first, second));

    [Theory]
    [InlineData("Pão de Queijo de Frigideira", "Bolinho de polvilho assado com pimenta")]
    [InlineData("Espetinho de Frango Picante com Pimentão", "Espetinhos de Carne com Pimenta e Mel")]
    [InlineData("Wrap Integral de Tomate Apimentado", "Wrap de Carne Moída Agridoce com Repolho Crocante")]
    [InlineData("Batata Rústica na Air Fryer com Páprica e Alho", "Batata-Doce Assada Agridoce com Pimenta e Mel")]
    public void DifferentDishesStayApart(string first, string second) => Assert.False(DishSimilarity.IsSameDish(first, second));

    [Fact]
    public void NamesMadeOnlyOfIgnoredWordsAreNotCompared() => Assert.False(DishSimilarity.IsSameDish("Assado picante", "Frito com molho"));
}
