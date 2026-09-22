using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace Hexprite.Services
{
    /// <summary>
    /// Implements the Strangler Fig keyboard shortcut subsystem.
    /// Provides parallel non-destructive shortcut routing, duplicate registration validation,
    /// and text input focus suppression.
    /// </summary>
    public sealed class HexpriteShortcutManager : IHexpriteShortcutManager
    {
        public const string TracerBulletActionId = "Debug.TracerBullet.F11";

        private readonly Dictionary<(ShortcutScope Scope, Key Key, ModifierKeys Modifiers), ShortcutDefinition> _shortcutsByScopeAndKey = new();
        private readonly Dictionary<string, ShortcutDefinition> _shortcutsByActionId = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();
        private System.Windows.Threading.Dispatcher? _dispatcher;
        private bool _isInitialized;

        public event EventHandler? TracerBulletExecuted;

        public bool IsTracerBulletExecuted { get; private set; }

        public ModifierKeys? ModifierKeysOverride { get; set; }

        public Func<bool>? ActiveTextEditingPredicate { get; set; }

        public Func<IInputElement?, bool>? TimelineFocusPredicate { get; set; }

        public IReadOnlyCollection<ShortcutDefinition> Shortcuts
        {
            get
            {
                lock (_lock)
                {
                    return _shortcutsByActionId.Values.ToList().AsReadOnly();
                }
            }
        }

        public HexpriteShortcutManager() : this(registerTracerBullet: true)
        {
        }

        public HexpriteShortcutManager(bool registerTracerBullet)
        {
            if (registerTracerBullet)
            {
                RegisterTracerBullet();
            }

            Validate();
        }

        public HexpriteShortcutManager(IEnumerable<ShortcutDefinition> initialShortcuts, bool registerTracerBullet = true)
        {
            if (registerTracerBullet)
            {
                RegisterTracerBullet();
            }

            if (initialShortcuts != null)
            {
                Register(initialShortcuts);
            }

            Validate();
        }

        /// <summary>
        /// Registers the default harmless tracer bullet shortcut (F11 in Global scope).
        /// </summary>
        public void RegisterTracerBullet()
        {
            lock (_lock)
            {
                if (_shortcutsByActionId.ContainsKey(TracerBulletActionId))
                {
                    return;
                }

                Register(new ShortcutDefinition(
                    actionId: TracerBulletActionId,
                    key: Key.F11,
                    modifierKeys: ModifierKeys.None,
                    command: new RelayCommand(() =>
                    {
                        IsTracerBulletExecuted = true;
                        TracerBulletExecuted?.Invoke(this, EventArgs.Empty);
                        Log.Information("Tracer bullet shortcut executed: F11");
                    }),
                    scope: ShortcutScope.Global
                ));
            }
        }

        public void Register(ShortcutDefinition shortcut)
        {
            ArgumentNullException.ThrowIfNull(shortcut);

            if (string.IsNullOrWhiteSpace(shortcut.ActionId))
            {
                throw new ArgumentException("Shortcut ActionId cannot be null or whitespace.", nameof(shortcut));
            }

            if (shortcut.Command == null)
            {
                throw new ArgumentNullException(nameof(shortcut), "Shortcut Command cannot be null.");
            }

            lock (_lock)
            {
                var lookupKey = (shortcut.Scope, shortcut.Key, shortcut.ModifierKeys);
                if (_shortcutsByScopeAndKey.TryGetValue(lookupKey, out var existing))
                {
                    throw new InvalidOperationException(
                        $"Duplicate shortcut registration for Key={shortcut.Key}, Modifiers={shortcut.ModifierKeys} in Scope={shortcut.Scope}. Existing action: '{existing.ActionId}', Attempted action: '{shortcut.ActionId}'.");
                }

                if (_shortcutsByActionId.TryGetValue(shortcut.ActionId, out _))
                {
                    throw new InvalidOperationException(
                        $"Shortcut with ActionId '{shortcut.ActionId}' is already registered.");
                }

                _shortcutsByScopeAndKey[lookupKey] = shortcut;
                _shortcutsByActionId[shortcut.ActionId] = shortcut;
            }
        }

        public void Register(IEnumerable<ShortcutDefinition> shortcuts)
        {
            ArgumentNullException.ThrowIfNull(shortcuts);

            lock (_lock)
            {
                foreach (var shortcut in shortcuts)
                {
                    Register(shortcut);
                }
            }
        }

        public bool Unregister(string actionId)
        {
            ArgumentNullException.ThrowIfNull(actionId);

            lock (_lock)
            {
                if (_shortcutsByActionId.Remove(actionId, out var shortcut))
                {
                    _shortcutsByScopeAndKey.Remove((shortcut.Scope, shortcut.Key, shortcut.ModifierKeys));
                    return true;
                }

                return false;
            }
        }

        public ShortcutDefinition? GetShortcut(string actionId)
        {
            ArgumentNullException.ThrowIfNull(actionId);

            lock (_lock)
            {
                _shortcutsByActionId.TryGetValue(actionId, out var shortcut);
                return shortcut;
            }
        }

        public ShortcutDefinition? FindMatch(Key key, ModifierKeys modifiers, ShortcutScope scope)
        {
            lock (_lock)
            {
                _shortcutsByScopeAndKey.TryGetValue((scope, key, modifiers), out var shortcut);
                return shortcut;
            }
        }

        public void Validate()
        {
            lock (_lock)
            {
                var seen = new HashSet<(ShortcutScope, Key, ModifierKeys)>();
                foreach (var shortcut in _shortcutsByActionId.Values)
                {
                    var tuple = (shortcut.Scope, shortcut.Key, shortcut.ModifierKeys);
                    if (!seen.Add(tuple))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate shortcut validation failed: Key={shortcut.Key}, Modifiers={shortcut.ModifierKeys}, Scope={shortcut.Scope} for ActionId='{shortcut.ActionId}'.");
                    }
                }
            }
        }

        public void Initialize()
        {
            lock (_lock)
            {
                if (_isInitialized)
                {
                    return;
                }

                _isInitialized = true;
                Validate();

                try
                {
                    _dispatcher = System.Windows.Threading.Dispatcher.FromThread(System.Threading.Thread.CurrentThread)
                                  ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
                    InputManager.Current.PreProcessInput += OnPreProcessInput;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not attach to InputManager.Current.PreProcessInput (likely non-WPF STA test context)");
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (!_isInitialized)
                {
                    return;
                }

                _isInitialized = false;

                try
                {
                    if (_dispatcher != null && !_dispatcher.CheckAccess())
                    {
                        _dispatcher.Invoke(() =>
                        {
                            try { InputManager.Current.PreProcessInput -= OnPreProcessInput; } catch { }
                        });
                    }
                    else
                    {
                        InputManager.Current.PreProcessInput -= OnPreProcessInput;
                    }
                }
                catch
                {
                    // Best-effort cleanup during shutdown
                }
            }
        }

        public static bool IsControlTextInput(IInputElement? element)
        {
            if (element == null)
            {
                return false;
            }

            if (element is TextBoxBase || element is PasswordBox)
            {
                return true;
            }

            if (element is ComboBox comboBox && comboBox.IsEditable)
            {
                return true;
            }

            if (element is DependencyObject depObj)
            {
                DependencyObject? current = depObj;
                int depth = 0;
                while (current != null && depth < 50)
                {
                    depth++;
                    if (current is TextBoxBase || current is PasswordBox)
                    {
                        return true;
                    }

                    if (current is ComboBox cb && cb.IsEditable)
                    {
                        return true;
                    }

                    DependencyObject? parent = null;
                    if (current is Visual or System.Windows.Media.Media3D.Visual3D)
                    {
                        try
                        {
                            parent = VisualTreeHelper.GetParent(current);
                        }
                        catch
                        {
                            // Element is detached or not in the visual tree
                        }
                    }

                    if (parent == null && current is FrameworkElement fe)
                    {
                        parent = fe.Parent;
                    }

                    if (parent == null && current is FrameworkContentElement fce)
                    {
                        parent = fce.Parent;
                    }

                    parent ??= LogicalTreeHelper.GetParent(current);
                    if (parent == current)
                    {
                        break;
                    }

                    current = parent;
                }
            }

            return false;
        }

        public bool IsActiveTextInput(IInputElement? element)
        {
            if (IsControlTextInput(element))
            {
                return true;
            }

            if (ActiveTextEditingPredicate != null && ActiveTextEditingPredicate())
            {
                if (!IsInSecondaryWindow(element))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsTimelineElement(IInputElement? element)
        {
            if (element == null) return false;
            try
            {
                if (element is DependencyObject depObj)
                {
                    if (depObj.Dispatcher != null && !depObj.Dispatcher.CheckAccess())
                    {
                        return false;
                    }

                    DependencyObject? current = depObj;
                    int depth = 0;
                    while (current != null && depth < 50)
                    {
                        depth++;
                        if (current is Hexprite.Views.TimelinePanel)
                        {
                            return true;
                        }

                        DependencyObject? parent = null;
                        if (current is Visual or System.Windows.Media.Media3D.Visual3D)
                        {
                            try
                            {
                                parent = VisualTreeHelper.GetParent(current);
                            }
                            catch
                            {
                            }
                        }

                        if (parent == null && current is FrameworkElement fe)
                        {
                            parent = fe.Parent;
                        }

                        if (parent == null && current is FrameworkContentElement fce)
                        {
                            parent = fce.Parent;
                        }

                        parent ??= LogicalTreeHelper.GetParent(current);
                        if (parent == current)
                        {
                            break;
                        }

                        current = parent;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        public bool IsTimelineFocused(IInputElement? element)
        {
            if (TimelineFocusPredicate != null && TimelineFocusPredicate(element))
            {
                return true;
            }

            return IsTimelineElement(element);
        }

        private static Window? GetOwningWindow(DependencyObject? dep)
        {
            if (dep == null) return null;
            if (dep.Dispatcher != null && !dep.Dispatcher.CheckAccess())
            {
                return null;
            }

            try
            {
                if (dep is Window w) return w;
                var window = Window.GetWindow(dep);
                if (window != null) return window;

                DependencyObject? current = dep;
                int depth = 0;
                while (current != null && depth < 50)
                {
                    depth++;
                    if (current is ContextMenu cm && cm.PlacementTarget != null)
                    {
                        return GetOwningWindow(cm.PlacementTarget);
                    }
                    if (current is Popup popup && popup.PlacementTarget != null)
                    {
                        return GetOwningWindow(popup.PlacementTarget);
                    }

                    DependencyObject? parent = null;
                    if (current is Visual or System.Windows.Media.Media3D.Visual3D)
                    {
                        try
                        {
                            parent = VisualTreeHelper.GetParent(current);
                        }
                        catch
                        {
                        }
                    }

                    if (parent == null && current is FrameworkElement fe)
                    {
                        parent = fe.Parent;
                    }

                    if (parent == null && current is FrameworkContentElement fce)
                    {
                        parent = fce.Parent;
                    }

                    parent ??= LogicalTreeHelper.GetParent(current);
                    if (parent == current)
                    {
                        break;
                    }

                    if (parent is Window pw)
                    {
                        return pw;
                    }

                    current = parent;
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool IsInSecondaryWindow(IInputElement? element)
        {
            try
            {
                var app = Application.Current;
                if (app == null)
                {
                    return false;
                }

                if (app.Dispatcher != null && !app.Dispatcher.CheckAccess())
                {
                    return false;
                }

                var mainWindow = app.MainWindow;

                if (element is DependencyObject dep)
                {
                    var window = GetOwningWindow(dep);
                    if (window != null && mainWindow != null)
                    {
                        return window != mainWindow;
                    }
                }

                if (mainWindow != null && !mainWindow.IsActive && app.Windows != null)
                {
                    foreach (Window w in app.Windows)
                    {
                        if (w != mainWindow && w.IsActive)
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool IsStandardTextEditingKey(Key key, ModifierKeys modifiers)
        {
            if (modifiers == ModifierKeys.None)
            {
                return key is Key.Delete or Key.Back;
            }

            if (modifiers == ModifierKeys.Control)
            {
                return key is Key.A or Key.C or Key.V or Key.X or Key.Z or Key.Y or Key.Delete or Key.Back;
            }

            if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                return key is Key.Z;
            }

            return false;
        }

        public bool ProcessKey(Key key, ModifierKeys modifiers, IInputElement? focusedElement = null)
        {
            if (key == Key.None || key == Key.ImeProcessed || key == Key.DeadCharProcessed)
            {
                return false;
            }

            bool isControlText = IsControlTextInput(focusedElement);
            bool isCanvasText = !isControlText && ActiveTextEditingPredicate != null && ActiveTextEditingPredicate() && !IsInSecondaryWindow(focusedElement);
            bool suppressCanvas = isControlText || isCanvasText;
            bool isSecondaryWindow = IsInSecondaryWindow(focusedElement);
            bool isTimelineFocused = !isControlText && IsTimelineFocused(focusedElement);

            lock (_lock)
            {
                // 1. EditorCanvas scope (bypassed if active text input has focus or if inside secondary window)
                if (!suppressCanvas && !isSecondaryWindow && _shortcutsByScopeAndKey.TryGetValue((ShortcutScope.EditorCanvas, key, modifiers), out var canvasShortcut))
                {
                    // If timeline is focused, bypass canvas selection deletion so timeline frame deletion receives Delete/Backspace
                    if (isTimelineFocused && (canvasShortcut.ActionId == EditorCanvasShortcutRegistrar.ActionCanvasDeleteSelection ||
                                              canvasShortcut.ActionId == EditorCanvasShortcutRegistrar.ActionCanvasDeleteSelectionBackspace))
                    {
                        // bypass
                    }
                    else if (canvasShortcut.Command != null && canvasShortcut.Command.CanExecute(canvasShortcut.CommandParameter))
                    {
                        canvasShortcut.Command.Execute(canvasShortcut.CommandParameter);
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }

                // 2. Window scope (bypassed if inside secondary window, and bypassed for standard text editing if active text input has focus)
                if (!isSecondaryWindow && _shortcutsByScopeAndKey.TryGetValue((ShortcutScope.Window, key, modifiers), out var windowShortcut))
                {
                    // If timeline is focused, bypass canvas Select All so timeline can select all frames
                    if (isTimelineFocused && windowShortcut.ActionId == WindowShortcutRegistrar.ActionEditSelectAll)
                    {
                        return false;
                    }

                    if (isControlText && (windowShortcut.ActionId == WindowShortcutRegistrar.ActionLayerRename || IsStandardTextEditingKey(key, modifiers)))
                    {
                        return false;
                    }

                    if (isCanvasText && (windowShortcut.ActionId == WindowShortcutRegistrar.ActionLayerRename || (IsStandardTextEditingKey(key, modifiers) && key is not Key.Z and not Key.Y)))
                    {
                        return false;
                    }

                    if (windowShortcut.Command != null && windowShortcut.Command.CanExecute(windowShortcut.CommandParameter))
                    {
                        windowShortcut.Command.Execute(windowShortcut.CommandParameter);
                        return true;
                    }

                    return false;
                }

                // 3. Global scope (application-wide)
                if (_shortcutsByScopeAndKey.TryGetValue((ShortcutScope.Global, key, modifiers), out var globalShortcut))
                {
                    if (globalShortcut.Command != null && globalShortcut.Command.CanExecute(globalShortcut.CommandParameter))
                    {
                        globalShortcut.Command.Execute(globalShortcut.CommandParameter);
                        return true;
                    }

                    return false;
                }
            }

            return false;
        }

        public bool ProcessInput(KeyEventArgs keyArgs, IInputElement? focusedElementOverride = null, ModifierKeys? modifiersOverride = null)
        {
            ArgumentNullException.ThrowIfNull(keyArgs);

            if (keyArgs.Handled)
            {
                return false;
            }

            Key key = keyArgs.Key == Key.System ? keyArgs.SystemKey : keyArgs.Key;
            if (key == Key.None || key == Key.ImeProcessed || key == Key.DeadCharProcessed)
            {
                return false;
            }

            ModifierKeys modifiers = modifiersOverride ?? ModifierKeysOverride ?? ModifierKeys.None;
            if (modifiersOverride == null && ModifierKeysOverride == null)
            {
                try
                {
                    modifiers = keyArgs.KeyboardDevice?.Modifiers ?? Keyboard.Modifiers;
                }
                catch
                {
                    modifiers = ModifierKeys.None;
                }
            }

            if (keyArgs.Key == Key.System && key != Key.F10)
            {
                modifiers |= ModifierKeys.Alt;
            }

            IInputElement? focusedElement = focusedElementOverride;
            if (focusedElement == null)
            {
                try
                {
                    focusedElement = Keyboard.FocusedElement;
                }
                catch
                {
                    focusedElement = null;
                }

                focusedElement ??= (keyArgs.OriginalSource as IInputElement) ?? (keyArgs.Source as IInputElement);
            }

            if (ProcessKey(key, modifiers, focusedElement))
            {
                keyArgs.Handled = true;
                return true;
            }

            return false;
        }

        private void OnPreProcessInput(object sender, PreProcessInputEventArgs e)
        {
            if (e.StagingItem.Input is KeyEventArgs keyArgs)
            {
                if (keyArgs.RoutedEvent == Keyboard.PreviewKeyDownEvent || keyArgs.RoutedEvent == Keyboard.KeyDownEvent || keyArgs.RoutedEvent == null)
                {
                    ProcessInput(keyArgs);
                }
            }
        }
    }
}
