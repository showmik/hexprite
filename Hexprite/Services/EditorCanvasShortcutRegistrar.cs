using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Hexprite.Core;
using Hexprite.ViewModels;

namespace Hexprite.Services
{
    /// <summary>
    /// Registers default EditorCanvas scope shortcuts (tool selection, brush sizing,
    /// animation playback/navigation, onion skinning, and grid shifting) into <see cref="IHexpriteShortcutManager"/>.
    /// </summary>
    public static class EditorCanvasShortcutRegistrar
    {
        // ── Action Identifiers ─────────────────────────────────────────────
        public const string ActionCanvasBrushSizeDecrement = "Canvas.BrushSize.Decrement";
        public const string ActionCanvasBrushSizeIncrement = "Canvas.BrushSize.Increment";

        public const string ActionAnimationTogglePlayback = "Animation.TogglePlayback";
        public const string ActionAnimationPreviousFrame = "Animation.PreviousFrame";
        public const string ActionAnimationNextFrame = "Animation.NextFrame";
        public const string ActionAnimationFirstFrame = "Animation.FirstFrame";
        public const string ActionAnimationLastFrame = "Animation.LastFrame";
        public const string ActionAnimationToggleOnionSkin = "Animation.ToggleOnionSkin";
        public const string ActionAnimationAddFrame = "Animation.AddFrame";

        public const string ActionCanvasGridShiftUp = "Canvas.GridShift.Up";
        public const string ActionCanvasGridShiftDown = "Canvas.GridShift.Down";
        public const string ActionCanvasGridShiftLeft = "Canvas.GridShift.Left";
        public const string ActionCanvasGridShiftRight = "Canvas.GridShift.Right";

        public const string ActionCanvasDeleteSelection = "Canvas.DeleteSelection";
        public const string ActionCanvasDeleteSelectionBackspace = "Canvas.DeleteSelection.Backspace";
        public const string ActionDeleteSelection = ActionCanvasDeleteSelection;
        public const string ActionDeleteSelectionBackspace = ActionCanvasDeleteSelectionBackspace;

        public static void RegisterDefaultTools(IHexpriteShortcutManager manager, ShellViewModel shell)
        {
            RegisterDefaultShortcuts(manager, shell);
        }

        public static void RegisterDefaultTools(IHexpriteShortcutManager manager, ICommand selectToolCommand)
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(selectToolCommand);

            var toolShortcuts = new (Key Key, ModifierKeys Modifiers, string ToolName, string ActionId)[]
            {
                (Key.B, ModifierKeys.None, "Pencil", "Tool.Pencil"),
                (Key.E, ModifierKeys.None, "Eraser", "Tool.Eraser"),
                (Key.L, ModifierKeys.None, "Line", "Tool.Line"),
                (Key.R, ModifierKeys.None, "Rectangle", "Tool.Rectangle"),
                (Key.C, ModifierKeys.None, "Ellipse", "Tool.Ellipse"),
                (Key.F, ModifierKeys.None, "Fill", "Tool.Fill"),
                (Key.T, ModifierKeys.None, "Text", "Tool.Text"),
                (Key.V, ModifierKeys.None, "Move", "Tool.Move"),
                (Key.M, ModifierKeys.None, "Marquee", "Tool.Marquee"),
                (Key.Q, ModifierKeys.None, "Lasso", "Tool.Lasso"),
                (Key.W, ModifierKeys.None, "MagicWand", "Tool.MagicWand"),
                (Key.D, ModifierKeys.None, "Dither", "Tool.Dither"),
                (Key.R, ModifierKeys.Shift, "FilledRectangle", "Tool.FilledRectangle"),
                (Key.C, ModifierKeys.Shift, "FilledEllipse", "Tool.FilledEllipse"),
                (Key.G, ModifierKeys.None, "Gradient", "Tool.Gradient"),
                (Key.M, ModifierKeys.Shift, "EllipticalMarquee", "Tool.EllipticalMarquee"),
            };

            foreach (var (key, modifiers, toolName, actionId) in toolShortcuts)
            {
                if (manager.GetShortcut(actionId) == null)
                {
                    manager.Register(new ShortcutDefinition(
                        actionId: actionId,
                        key: key,
                        modifierKeys: modifiers,
                        command: selectToolCommand,
                        scope: ShortcutScope.EditorCanvas,
                        commandParameter: toolName
                    ));
                }
            }
        }

        public static void RegisterDefaultShortcuts(IHexpriteShortcutManager manager, ShellViewModel shell)
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(shell);

            manager.ActiveTextEditingPredicate ??= () => shell.ActiveDocument is MainViewModel mvm && mvm.IsTextEditing;
            manager.TimelineFocusPredicate ??= (element) =>
            {
                try
                {
                    if (element != null && HexpriteShortcutManager.IsTimelineElement(element))
                    {
                        return true;
                    }

                    var app = System.Windows.Application.Current;
                    if (app != null)
                    {
                        if (app.Dispatcher != null && !app.Dispatcher.CheckAccess())
                        {
                            return false;
                        }

                        if (app.MainWindow is MainWindow mw)
                        {
                            if (mw.Dispatcher != null && !mw.Dispatcher.CheckAccess())
                            {
                                return false;
                            }

                            return mw.TimelinePanel != null && mw.TimelinePanel.IsKeyboardFocusWithin && !HexpriteShortcutManager.IsControlTextInput(element);
                        }
                    }
                }
                catch
                {
                }

                return false;
            };

            // 1. Tool selection shortcuts
            RegisterDefaultTools(manager, shell.SelectToolCommand);

            // 2. Brush size shortcuts
            RegisterBrushShortcuts(manager, shell);

            // 3. Animation playback & frame navigation shortcuts
            RegisterAnimationShortcuts(manager, shell);

            // 4. Canvas grid shift shortcuts (Ctrl + Arrows)
            RegisterGridShiftShortcuts(manager, shell);

            // 5. Selection deletion shortcuts (Delete, Backspace)
            RegisterSelectionShortcuts(manager, shell);
        }

        public static void RegisterBrushShortcuts(IHexpriteShortcutManager manager, ShellViewModel shell)
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(shell);

            if (manager.GetShortcut("Canvas.BrushSize.Decrement") == null)
            {
                manager.Register(new ShortcutDefinition(
                    actionId: "Canvas.BrushSize.Decrement",
                    key: Key.OemOpenBrackets,
                    modifierKeys: ModifierKeys.None,
                    command: new RelayCommand(
                        () => { if (shell.ActiveDocument is MainViewModel mvm) mvm.BrushSize--; },
                        () => shell.ActiveDocument is MainViewModel),
                    scope: ShortcutScope.EditorCanvas
                ));
            }

            if (manager.GetShortcut("Canvas.BrushSize.Increment") == null)
            {
                manager.Register(new ShortcutDefinition(
                    actionId: "Canvas.BrushSize.Increment",
                    key: Key.OemCloseBrackets,
                    modifierKeys: ModifierKeys.None,
                    command: new RelayCommand(
                        () => { if (shell.ActiveDocument is MainViewModel mvm) mvm.BrushSize++; },
                        () => shell.ActiveDocument is MainViewModel),
                    scope: ShortcutScope.EditorCanvas
                ));
            }
        }

        public static void RegisterAnimationShortcuts(IHexpriteShortcutManager manager, ShellViewModel shell)
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(shell);

            // Space: Toggle Play/Pause
            if (manager.GetShortcut("Animation.TogglePlayback") == null)
            {
                manager.Register(new ShortcutDefinition(
                    actionId: "Animation.TogglePlayback",
                    key: Key.Space,
                    modifierKeys: ModifierKeys.None,
                    command: new RelayCommand(
                        () =>
                        {
                            if (shell.ActiveDocument is MainViewModel mvm && mvm.IsAnimationEnabled)
                            {
                                mvm.IsPlaying = !mvm.IsPlaying;
                            }
                        },
                        () => shell.ActiveDocument is MainViewModel mvm && mvm.IsAnimationEnabled),
                    scope: ShortcutScope.EditorCanvas
                ));
            }

            // OemComma (,): Previous Frame
            if (manager.GetShortcut("Animation.PreviousFrame") == null)
            {
                manager.Register(new ShortcutDefinition(
                    actionId: "Animation.PreviousFrame",
                    key: Key.OemComma,
                    modifierKeys: ModifierKeys.None,
                    command: new RelayCommand(
                        () =>
                        {
                            if (shell.ActiveDocument is MainViewModel mvm && mvm.PreviousFrameCommand.CanExecute(null))
                            {
                                mvm.PreviousFrameCommand.Execute(null);
                            }
                        },
                        () => shell.ActiveDocument is MainViewModel mvm && mvm.PreviousFrameCommand.CanExecute(null)),
                    scope: ShortcutScope.EditorCanvas
                ));
            }

            // OemPeriod (.): Next Frame
            if (manager.GetShortcut("Animation.NextFrame") == null)
            {
                manager.Register(new ShortcutDefinition(
                    actionId: "Animation.NextFrame",
                    key: Key.OemPeriod,
                    modifierKeys: ModifierKeys.None,
                    command: new RelayCommand(
                        () =>
                        {
                            if (shell.ActiveDocument is MainViewModel mvm && mvm.NextFrameCommand.CanExecute(null))
                            {
                                mvm.NextFrameCommand.Execute(null);
                            }
                        },
                        () => shell.ActiveDocument is MainViewModel mvm && mvm.NextFrameCommand.CanExecute(null)),
                    scope: ShortcutScope.EditorCanvas
                ));
            }

            // Home: First Frame
            if (manager.GetShortcut("Animation.FirstFrame") == null)
            {
                manager.Register(new ShortcutDefinition(
                    actionId: "Animation.FirstFrame",
                    key: Key.Home,
                    modifierKeys: ModifierKeys.None,
                    command: new RelayCommand(
                        () =>
                        {
                            if (shell.ActiveDocument is MainViewModel mvm && mvm.IsAnimationEnabled && mvm.SpriteState != null)
                            {
                                if (mvm.IsPlaying) mvm.IsPlaying = false;
                                mvm.SetActiveFrame(0);
                            }
                        },
                        () => shell.ActiveDocument is MainViewModel mvm && mvm.IsAnimationEnabled && mvm.SpriteState != null),
                    scope: ShortcutScope.EditorCanvas
                ));
            }

            // End: Last Frame
            if (manager.GetShortcut("Animation.LastFrame") == null)
            {
                manager.Register(new ShortcutDefinition(
                    actionId: "Animation.LastFrame",
                    key: Key.End,
                    modifierKeys: ModifierKeys.None,
                    command: new RelayCommand(
                        () =>
                        {
                            if (shell.ActiveDocument is MainViewModel mvm && mvm.IsAnimationEnabled && mvm.SpriteState != null)
                            {
                                if (mvm.IsPlaying) mvm.IsPlaying = false;
                                mvm.SetActiveFrame(mvm.SpriteState.Frames.Count - 1);
                            }
                        },
                        () => shell.ActiveDocument is MainViewModel mvm && mvm.IsAnimationEnabled && mvm.SpriteState != null),
                    scope: ShortcutScope.EditorCanvas
                ));
            }

            // O: Toggle Onion Skin
            if (manager.GetShortcut("Animation.ToggleOnionSkin") == null)
            {
                manager.Register(new ShortcutDefinition(
                    actionId: "Animation.ToggleOnionSkin",
                    key: Key.O,
                    modifierKeys: ModifierKeys.None,
                    command: new RelayCommand(
                        () =>
                        {
                            if (shell.ActiveDocument is MainViewModel mvm && mvm.ToggleOnionSkinCommand.CanExecute(null))
                            {
                                mvm.ToggleOnionSkinCommand.Execute(null);
                            }
                        },
                        () => shell.ActiveDocument is MainViewModel mvm && mvm.ToggleOnionSkinCommand.CanExecute(null)),
                    scope: ShortcutScope.EditorCanvas
                ));
            }

            // Alt+N: Add Frame
            if (manager.GetShortcut(ActionAnimationAddFrame) == null)
            {
                manager.Register(new ShortcutDefinition(
                    actionId: ActionAnimationAddFrame,
                    key: Key.N,
                    modifierKeys: ModifierKeys.Alt,
                    command: new RelayCommand(
                        () =>
                        {
                            if (shell.ActiveDocument is MainViewModel mvm && mvm.IsAnimationEnabled && mvm.AddFrameCommand.CanExecute(null))
                            {
                                mvm.AddFrameCommand.Execute(null);
                            }
                        },
                        () => shell.ActiveDocument is MainViewModel mvm && mvm.IsAnimationEnabled && mvm.AddFrameCommand.CanExecute(null)),
                    scope: ShortcutScope.EditorCanvas
                ));
            }
        }

        public static void RegisterGridShiftShortcuts(IHexpriteShortcutManager manager, ShellViewModel shell)
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(shell);

            var shifts = new (Key Key, int Dx, int Dy, string ActionId)[]
            {
                (Key.Up, 0, -1, "Canvas.GridShift.Up"),
                (Key.Down, 0, 1, "Canvas.GridShift.Down"),
                (Key.Left, -1, 0, "Canvas.GridShift.Left"),
                (Key.Right, 1, 0, "Canvas.GridShift.Right")
            };

            foreach (var (key, dx, dy, actionId) in shifts)
            {
                if (manager.GetShortcut(actionId) == null)
                {
                    manager.Register(new ShortcutDefinition(
                        actionId: actionId,
                        key: key,
                        modifierKeys: ModifierKeys.Control,
                        command: new RelayCommand(
                            () => { if (shell.ActiveDocument is MainViewModel mvm) mvm.ShiftGrid(dx, dy); },
                            () => shell.ActiveDocument is MainViewModel),
                        scope: ShortcutScope.EditorCanvas
                    ));
                }
            }
        }

        public static void RegisterSelectionShortcuts(IHexpriteShortcutManager manager, ShellViewModel shell)
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(shell);

            if (manager.GetShortcut(ActionCanvasDeleteSelection) == null)
            {
                manager.Register(new ShortcutDefinition(
                    actionId: ActionCanvasDeleteSelection,
                    key: Key.Delete,
                    modifierKeys: ModifierKeys.None,
                    command: new RelayCommand(
                        () =>
                        {
                            if (shell.ActiveDocument is MainViewModel mvm && mvm.DeleteSelectionCommand.CanExecute(null))
                            {
                                mvm.DeleteSelectionCommand.Execute(null);
                            }
                        },
                        () => shell.ActiveDocument is MainViewModel mvm && mvm.DeleteSelectionCommand.CanExecute(null)),
                    scope: ShortcutScope.EditorCanvas
                ));
            }

            if (manager.GetShortcut(ActionCanvasDeleteSelectionBackspace) == null)
            {
                manager.Register(new ShortcutDefinition(
                    actionId: ActionCanvasDeleteSelectionBackspace,
                    key: Key.Back,
                    modifierKeys: ModifierKeys.None,
                    command: new RelayCommand(
                        () =>
                        {
                            if (shell.ActiveDocument is MainViewModel mvm && mvm.DeleteSelectionCommand.CanExecute(null))
                            {
                                mvm.DeleteSelectionCommand.Execute(null);
                            }
                        },
                        () => shell.ActiveDocument is MainViewModel mvm && mvm.DeleteSelectionCommand.CanExecute(null)),
                    scope: ShortcutScope.EditorCanvas
                ));
            }

            var nudges = new (Key Key, ModifierKeys Modifiers, int Dx, int Dy, string ActionId)[]
            {
                (Key.Up, ModifierKeys.None, 0, -1, "Canvas.Selection.NudgeUp"),
                (Key.Down, ModifierKeys.None, 0, 1, "Canvas.Selection.NudgeDown"),
                (Key.Left, ModifierKeys.None, -1, 0, "Canvas.Selection.NudgeLeft"),
                (Key.Right, ModifierKeys.None, 1, 0, "Canvas.Selection.NudgeRight"),
                (Key.Up, ModifierKeys.Shift, 0, -10, "Canvas.Selection.NudgeUpFast"),
                (Key.Down, ModifierKeys.Shift, 0, 10, "Canvas.Selection.NudgeDownFast"),
                (Key.Left, ModifierKeys.Shift, -10, 0, "Canvas.Selection.NudgeLeftFast"),
                (Key.Right, ModifierKeys.Shift, 10, 0, "Canvas.Selection.NudgeRightFast"),
            };

            foreach (var (key, modifiers, dx, dy, actionId) in nudges)
            {
                if (manager.GetShortcut(actionId) == null)
                {
                    manager.Register(new ShortcutDefinition(
                        actionId: actionId,
                        key: key,
                        modifierKeys: modifiers,
                        command: new RelayCommand(
                            () =>
                            {
                                if (shell.ActiveDocument is MainViewModel mvm)
                                    mvm.NudgeSelection(dx, dy);
                            },
                            () => shell.ActiveDocument is MainViewModel mvm &&
                                  mvm.SelectionService.HasActiveSelection &&
                                  !mvm.IsTextEditing),
                        scope: ShortcutScope.EditorCanvas
                    ));
                }
            }
        }
    }
}
