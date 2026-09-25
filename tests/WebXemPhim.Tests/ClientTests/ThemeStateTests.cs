using System.IO;
using Microsoft.JSInterop;
using Moq;
using WebXemPhim.Client.Components.Icons;
using WebXemPhim.Client.Services;

namespace WebXemPhim.Tests.ClientTests;

public sealed class ThemeStateTests
{
    private static string ReadClientFile(params string[] segments)
    {
        var parts = new List<string> { AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client" };
        parts.AddRange(segments);
        return File.ReadAllText(Path.Combine(parts.ToArray()));
    }

    [Fact]
    public async Task ThemeStateDefaultsToDarkAndTogglesCleanly()
    {
        var jsMock = new Mock<IJSRuntime>();
        var theme = new ThemeState(jsMock.Object);

        Assert.Equal("dark", theme.CurrentTheme);
        Assert.False(theme.IsLight);
        Assert.True(theme.IsDark);

        var changedCount = 0;
        theme.OnChange += () => changedCount++;

        await theme.SetThemeAsync("light");
        Assert.Equal("light", theme.CurrentTheme);
        Assert.True(theme.IsLight);
        Assert.False(theme.IsDark);
        Assert.Equal(1, changedCount);

        await theme.ToggleThemeAsync();
        Assert.Equal("dark", theme.CurrentTheme);
        Assert.False(theme.IsLight);
        Assert.True(theme.IsDark);
        Assert.Equal(2, changedCount);

        await theme.ToggleThemeAsync();
        Assert.Equal("light", theme.CurrentTheme);
        Assert.True(theme.IsLight);
        Assert.Equal(3, changedCount);
    }

    [Fact]
    public async Task ThemeStateInitializesFromStoredTheme()
    {
        var jsMock = new Mock<IJSRuntime>();
        jsMock.Setup(js => js.InvokeAsync<string>("cinemaTheme.getStoredTheme", It.IsAny<object[]?>()))
              .ReturnsAsync("light");

        var theme = new ThemeState(jsMock.Object);
        await theme.InitializeAsync();

        Assert.Equal("light", theme.CurrentTheme);
        Assert.True(theme.IsLight);
    }

    [Fact]
    public void LightThemeCssDeclaresPureWhiteTokensAndComponents()
    {
        var css = ReadClientFile("wwwroot", "css", "light-theme.css");

        Assert.Contains("data-theme=\"light\"", css);
        Assert.Contains("--bg: #ffffff;", css);
        Assert.Contains("--surface: #ffffff;", css);
        Assert.Contains("--text: #0f172a;", css);
        Assert.Contains(".btn-theme-toggle", css);
    }

    [Fact]
    public void AppLinksLightThemeAndThemeManager()
    {
        var app = ReadClientFile("Components", "App.razor");

        Assert.Contains("css/light-theme.css", app);
        Assert.Contains("js/theme-manager.js", app);
    }

    [Fact]
    public void NavMenuAndProfileSettingsIncludeThemeToggles()
    {
        var nav = ReadClientFile("Components", "Layout", "NavMenu.razor");
        var settings = ReadClientFile("Components", "Pages", "ProfileSettings.razor");

        Assert.Contains("btn-theme-toggle", nav);
        Assert.Contains("CinemaIconName.Moon", nav);
        Assert.Contains("CinemaIconName.Sun", nav);
        Assert.Contains("ToggleTheme", nav);

        Assert.Contains("Giao diện hiển thị", settings);
        Assert.Contains("Chế độ Tối (Obsidian)", settings);
        Assert.Contains("Chế độ Sáng (Pure White)", settings);
    }

    [Fact]
    public void CinemaIconSupportsSunAndMoon()
    {
        var icon = ReadClientFile("Components", "Icons", "CinemaIcon.razor");
        var iconName = ReadClientFile("Components", "Icons", "CinemaIconName.cs");

        Assert.Contains("case CinemaIconName.Moon:", icon);
        Assert.Contains("case CinemaIconName.Sun:", icon);
        Assert.Contains("Moon,", iconName);
        Assert.Contains("Sun,", iconName);
    }
}
