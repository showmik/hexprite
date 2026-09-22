namespace Hexprite.Services
{
    /// <summary>
    /// Manages application theme switching and persistence.
    /// </summary>
    public interface IThemeService
    {
        /// <summary>Gets the name of the currently applied theme (e.g. "Dark", "Light", "Dim", "Flipper").</summary>
        string CurrentTheme { get; }

        /// <summary>Loads the persisted theme (if any) and applies it immediately.</summary>
        void Initialize();

        /// <summary>
        /// Switches the active theme by swapping the color ResourceDictionary.
        /// </summary>
        /// <param name="themeName">"Dark", "Light", "Dim", or "Flipper"</param>
        void ApplyTheme(string themeName);

        /// <summary>Raised after the theme has been switched.</summary>
        event System.EventHandler? ThemeChanged;
    }
}
