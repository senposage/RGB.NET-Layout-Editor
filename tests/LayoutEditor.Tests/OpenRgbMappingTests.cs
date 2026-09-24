using LayoutEditor.UI.Services;
using OpenRGB.NET;
using Xunit;

namespace LayoutEditor.Tests;

public class OpenRgbMappingTests
{
    [Theory]
    [InlineData(0, "Mouse1")]
    [InlineData(1, "Mouse2")]
    public void MouseLedsUseRgbNetSequence(int index, string expected)
    {
        Assert.Equal(expected, OpenRgbService.GetSequentialRgbNetId(DeviceType.Mouse, index));
        Assert.Equal(index, OpenRgbService.GetSequentialLedIndex(DeviceType.Mouse, expected));
    }

    [Fact]
    public void MatrixIndexesAreRelativeToTheirZone()
    {
        Assert.Equal(7, OpenRgbService.TranslateZoneLedIndex(5, 2));
    }

    [Theory]
    [InlineData("Key: F1 LED 2", "Key: F1", 2)]
    [InlineData("Key: Caps Lock LED 2", "Key: Caps Lock", 2)]
    [InlineData("Key: Pause/Break LED 2", "Key: Pause/Break", 2)]
    public void SecondaryKeyLedsResolveToTheirPrimary(string name, string expectedPrimary, int expectedLayer)
    {
        Assert.True(OpenRgbService.TryGetSecondaryLed(name, out var primary, out var layer));
        Assert.Equal(expectedPrimary, primary);
        Assert.Equal(expectedLayer, layer);
    }
}
