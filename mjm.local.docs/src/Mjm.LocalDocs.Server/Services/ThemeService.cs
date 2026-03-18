namespace Mjm.LocalDocs.Server.Services;

/// <summary>
/// Manages the application theme state (light/dark mode).
/// Registered as Scoped to maintain state per-circuit in Blazor Server.
/// </summary>
public sealed class ThemeService
{
    /// <summary>
    /// Gets a value indicating whether dark mode is active.
    /// </summary>
    public bool IsDarkMode { get; private set; }

    /// <summary>
    /// Raised when the theme changes.
    /// </summary>
    public event Action? OnChange;

    /// <summary>
    /// Sets the dark mode state and notifies subscribers.
    /// </summary>
    /// <param name="value">True for dark mode, false for light mode.</param>
    public void SetDarkMode(bool value)
    {
        if (IsDarkMode == value) return;
        IsDarkMode = value;
        OnChange?.Invoke();
    }

    /// <summary>
    /// Toggles between light and dark mode.
    /// </summary>
    public void ToggleDarkMode() => SetDarkMode(!IsDarkMode);
}
