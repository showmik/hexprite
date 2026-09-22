using System;
using System.Windows.Input;

namespace Hexprite.Services
{
    /// <summary>
    /// Encapsulates a keyboard shortcut definition including action identifier, key combination,
    /// command dispatch, routing scope, and optional command parameter.
    /// </summary>
    public sealed class ShortcutDefinition
    {
        public string ActionId { get; init; } = string.Empty;
        public Key Key { get; init; }
        public ModifierKeys ModifierKeys { get; init; } = ModifierKeys.None;
        public ICommand Command { get; init; } = null!;
        public ShortcutScope Scope { get; init; } = ShortcutScope.Global;

        /// <summary>
        /// Explicit property alias for <see cref="Scope"/> to conform to API specifications.
        /// </summary>
        public ShortcutScope ShortcutScope
        {
            get => Scope;
            init => Scope = value;
        }

        public object? CommandParameter { get; init; }

        public ShortcutDefinition()
        {
        }

        public ShortcutDefinition(
            string actionId,
            Key key,
            ModifierKeys modifierKeys,
            ICommand command,
            ShortcutScope scope = ShortcutScope.Global,
            object? commandParameter = null)
        {
            ActionId = actionId ?? throw new ArgumentNullException(nameof(actionId));
            Key = key;
            ModifierKeys = modifierKeys;
            Command = command ?? throw new ArgumentNullException(nameof(command));
            Scope = scope;
            CommandParameter = commandParameter;
        }

        public override string ToString() =>
            $"[{Scope}] {ModifierKeys}+{Key} -> {ActionId}";
    }
}
