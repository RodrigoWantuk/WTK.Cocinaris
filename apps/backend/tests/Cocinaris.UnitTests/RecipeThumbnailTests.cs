using Cocinaris.Modules.Ai.Images;
using Cocinaris.Modules.Recipes.Thumbnails;

namespace Cocinaris.UnitTests;

public sealed class RecipeThumbnailTests
{
    private const string Policy = "black-forest-labs/flux-3-image-klein\n768\n16:9\n1024x576";

    private const string Visual = """
        {"schemaVersion":"1","appearanceDescription":"Chicken in tomato sauce with white rice.","visibleComponents":["white rice","chicken breast in tomato sauce"],"dishFormat":"plated_main_with_sides","plating":"single plate","sauceAppearance":"red tomato sauce","textureAndDoneness":["fully cooked chicken"],"garnish":[],"dominantColors":["red","white"],"excludedElements":["cutlery","text"]}
        """;

    [Fact]
    public void EquivalentDescriptorsShareARenderKeyAndAnotherPolicyDoesNot()
    {
        var descriptor = RecipeThumbnailDescriptor.TryCreate(Visual, Policy)!;
        var reworded = RecipeThumbnailDescriptor.TryCreate(Visual.Replace("\"white rice\",\"chicken breast in tomato sauce\"", "\"Chicken breast in tomato sauce\",\"  WHITE rice\""), Policy)!;
        var otherDish = RecipeThumbnailDescriptor.TryCreate(Visual.Replace("white rice", "mashed potatoes"), Policy)!;
        var otherPolicy = RecipeThumbnailDescriptor.TryCreate(Visual, Policy.Replace("768", "1K"))!;

        Assert.Equal(descriptor.RenderKey, reworded.RenderKey);
        Assert.NotEqual(descriptor.RenderKey, otherDish.RenderKey);
        Assert.Equal(descriptor.VisualIdentityKey, otherPolicy.VisualIdentityKey);
        Assert.NotEqual(descriptor.RenderKey, otherPolicy.RenderKey);
        Assert.Equal(64, descriptor.RenderKey.Length);
        Assert.Contains("Visible components: white rice, chicken breast in tomato sauce", descriptor.Prompt);
        Assert.Contains("Do not add ingredients", descriptor.Prompt);
        Assert.Contains("real photograph of home-cooked food", descriptor.Prompt);
        Assert.Contains("3D-rendered", descriptor.Prompt);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"schemaVersion\":\"2\"}")]
    public void HandWrittenOrInvalidDescriptorsHaveNoImage(string json) =>
        Assert.Null(RecipeThumbnailDescriptor.TryCreate(json, Policy));

    [Fact]
    public void TheInspectorReadsTheRealTypeAndSizeAndRejectsEverythingElse()
    {
        var png = FakeImageProvider.BlankPng(1024, 576);
        Assert.Equal(new InspectedImage("image/png", "png", 1024, 576), ImageInspector.Inspect(png));

        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x02, 0x40, 0x04, 0x00, 0x03];
        Assert.Equal(new InspectedImage("image/jpeg", "jpg", 1024, 576), ImageInspector.Inspect(jpeg));

        Assert.Null(ImageInspector.Inspect("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8));
        Assert.Null(ImageInspector.Inspect(png.AsSpan(0, 10)));
    }
}
