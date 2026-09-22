using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace Hexprite.Services
{
    /// <summary>
    /// Service contract for the Strangler Fig keyboard shortcut subsystem.
    /// Provides parallel non-destructive routing, duplicate validation, and text focus suppression.
    /// </summary>
    public interface IHexpriteShortcutManager : IDisposable
    {
        /// <summary>
        /// Gets all currently registered shortcut definitions.
        /// </summary>
        IReadOnlyCollection<ShortcutDefinition> Shortcuts { get; }

        /// <summary>
        /// Occurs when the tracer bullet shortcut is triggered.
        /// </summary>
        event EventHandler? TracerBulletExecuted;

        /// <summary>
        /// Gets whether the tracer bullet shortcut has been executed.
        /// </summary>
        bool IsTracerBulletExecuted { get; }

        /// <summary>
        /// Optional override for modifier keys evaluation. When set, overrides hardware/system modifier checks.
        /// Primarily used for deterministic testing and input simulation.
        /// </summary>
        ModifierKeys? ModifierKeysOverride { get; set; }

        /// <summary>
        /// Optional predicate to determine if canvas text editing mode is active
        /// (e.g. <c>MainViewModel.IsTextEditing</c>). When true, canvas tool shortcuts and
        /// standard text-editing shortcuts are suppressed so keystrokes reach the text tool.
        /// </summary>
        Func<bool>? ActiveTextEditingPredicate { get; set; }

        /// <summary>
        /// Optional predicate to determine if a timeline element has keyboard focus.
        /// When true, canvas selection deletion (Delete/Back) and window Select All (Ctrl+A)
        /// shortcuts are bypassed so timeline frame selection and deletion can handle the input.
        /// </summary>
        Func<IInputElement?, bool>? TimelineFocusPredicate { get; set; }

        /// <summary>
        /// Registers a shortcut definition. Throws <see cref="InvalidOperationException"/> if
        /// another shortcut with the same Key and ModifierKeys is already registered in the same ShortcutScope.
        /// </summary>
        void Register(ShortcutDefinition shortcut);

        /// <summary>
        /// Registers multiple shortcut definitions in order.
        /// </summary>
        void Register(IEnumerable<ShortcutDefinition> shortcuts);

        /// <summary>
        /// Unregisters a shortcut definition by its ActionId.
        /// </summary>
        bool Unregister(string actionId);

        /// <summary>
        /// Retrieves a registered shortcut by its ActionId, or null if not registered.
        /// </summary>
        ShortcutDefinition? GetShortcut(string actionId);

        /// <summary>
        /// Finds a matching shortcut for the given key, modifiers, and scope.
        /// </summary>
        ShortcutDefinition? FindMatch(Key key, ModifierKeys modifiers, ShortcutScope scope);

        /// <summary>
        /// Validates that no duplicate shortcuts exist within any ShortcutScope.
        /// Throws <see cref="InvalidOperationException"/> on duplicate detection.
        /// </summary>
        void Validate();

        /// <summary>
        /// Initializes the shortcut manager and hooks into the WPF input processing pipeline.
        /// </summary>
        void Initialize();

        /// <summary>
        /// Evaluates a key and modifiers against registered shortcuts, taking into account focus suppression.
        /// Returns true if a command was executed; false otherwise.
        /// </summary>
        bool ProcessKey(Key key, ModifierKeys modifiers, IInputElement? focusedElement = null);

        /// <summary>
        /// Processes a WPF <see cref="KeyEventArgs"/>, setting <see cref="KeyEventArgs.Handled"/> to true
        /// if and only if a registered shortcut command was successfully executed.
        /// </summary>
        bool ProcessInput(KeyEventArgs keyArgs, IInputElement? focusedElementOverride = null, ModifierKeys? modifiersOverride = null);

        /// <summary>
        /// Determines whether the given element is an active text input control
        /// (TextBoxBase, TextBox, RichTextBox, or editable ComboBox).
        /// </summary>
        bool IsActiveTextInput(IInputElement? element);
    }
}
