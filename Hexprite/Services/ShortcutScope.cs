namespace Hexprite.Services
{
    /// <summary>
    /// Defines the operational scope for a keyboard shortcut definition.
    /// </summary>
    public enum ShortcutScope
    {
        /// <summary>
        /// Global scope: active application-wide regardless of focused element.
        /// </summary>
        Global,

        /// <summary>
        /// EditorCanvas scope: canvas editing shortcuts (e.g. tool selection, drawing keys)
        /// that are bypassed when keyboard focus is on an active text input.
        /// </summary>
        EditorCanvas,

        /// <summary>
        /// Window scope: active across the active window.
        /// </summary>
        Window
    }
}
