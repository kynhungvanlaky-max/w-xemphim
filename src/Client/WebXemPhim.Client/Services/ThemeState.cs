using Microsoft.JSInterop;

namespace WebXemPhim.Client.Services;

public sealed class ThemeState(IJSRuntime js)
{
    private string _currentTheme = "dark";
    private bool _isInitialized;

    public string CurrentTheme => _currentTheme;
    public bool IsLight => string.Equals(_currentTheme, "light", StringComparison.OrdinalIgnoreCase);
    public bool IsDark => !IsLight;

    public event Action? OnChange;

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        try
        {
            var stored = await js.InvokeAsync<string>("cinemaTheme.getStoredTheme");
            _currentTheme = string.Equals(stored, "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
            _isInitialized = true;
            OnChange?.Invoke();
        }
        catch
        {
            _currentTheme = "dark";
            _isInitialized = true;
        }
    }

    public async Task SetThemeAsync(string theme)
    {
        var normalized = string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
        _currentTheme = normalized;
        OnChange?.Invoke();

        try
        {
            await js.InvokeVoidAsync("cinemaTheme.setTheme", normalized);
        }
        catch
        {
            // Ignore if prerendering or JS unavailable
        }
    }

    public async Task ToggleThemeAsync()
    {
        await SetThemeAsync(IsLight ? "dark" : "light");
    }
}
