using System;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Hexprite.Controllers;
using Hexprite.ViewModels;

namespace Hexprite.Services
{
    /// <summary>
    /// Registers window-level, document-level, and application-level shortcuts (file operations,
    /// edit operations, view navigation/zoom, and global application utilities) into <see cref="IHexpriteShortcutManager"/>.
    /// Completes the Strangler Fig migration by replacing native XAML <c>Window.InputBindings</c>.
    /// </summary>
    public static class WindowShortcutRegistrar
    {
        // ── Action Identifiers ─────────────────────────────────────────────
        public const string ActionFileNew = "File.New";
        public const string ActionFileOpen = "File.Open";
        public const string ActionFileSave = "File.Save";
        public const string ActionFileSaveAs = "File.SaveAs";
        public const string ActionFileCloseTab = "File.CloseTab";

        public const string ActionEditUndo = "Edit.Undo";
        public const string ActionEditRedo = "Edit.Redo";
        public const string ActionEditRedoCtrlShiftZ = "Edit.Redo.CtrlShiftZ";
        public const string ActionEditInvert = "Edit.Invert";
        public const string ActionEditCopy = "Edit.Copy";
        public const string ActionEditCut = "Edit.Cut";
        public const string ActionEditPaste = "Edit.Paste";
        public const string ActionEditSelectAll = "Edit.SelectAll";
        public const string ActionEditDeselect = "Edit.Deselect";
        public const string ActionEditReselect = "Edit.Reselect";
        public const string ActionEditTransform = "Edit.Transform";
        public const string ActionEditMergeLayer = "Edit.MergeLayer";

        public const string ActionLayerAdd = "Layer.Add";
        public const string ActionLayerDuplicate = "Layer.Duplicate";
        public const string ActionLayerNewFromSelection = "Layer.NewFromSelection";
        public const string ActionLayerDelete = "Layer.Delete";
        public const string ActionLayerRename = "Layer.Rename";

        public const string ActionViewZoomInOemPlus = "View.ZoomIn.OemPlus";
        public const string ActionViewZoomInShiftOemPlus = "View.ZoomIn.ShiftOemPlus";
        public const string ActionViewZoomInAdd = "View.ZoomIn.Add";
        public const string ActionViewZoomOutOemMinus = "View.ZoomOut.OemMinus";
        public const string ActionViewZoomOutSubtract = "View.ZoomOut.Subtract";
        public const string ActionViewZoomResetD0 = "View.ZoomReset.D0";
        public const string ActionViewZoomResetNumPad0 = "View.ZoomReset.NumPad0";

        public const string ActionGlobalRefreshTheme = "Utility.RefreshTheme";
        public const string ActionGlobalOpenDocumentation = "Utility.OpenDocumentation";
        public const string ActionHelpKeyboardShortcuts = "Help.KeyboardShortcuts";
        public const string ActionHelpKeyboardShortcutsNumPad = "Help.KeyboardShortcuts.NumPad";

        /// <summary>
        /// Registers all 23 default window-level, document-level, and application-level shortcuts.
        /// </summary>
        public static void RegisterDefaultShortcuts(
            IHexpriteShortcutManager manager,
            ShellViewModel shell,
            ICommand? zoomInCommand = null,
            ICommand? zoomOutCommand = null,
            ICommand? zoomResetCommand = null)
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

            RegisterFileShortcuts(manager, shell);
            RegisterEditShortcuts(manager, shell);
            RegisterLayerShortcuts(manager, shell);
            RegisterViewShortcuts(manager, shell, zoomInCommand, zoomOutCommand, zoomResetCommand);
            RegisterGlobalUtilities(manager, shell);
        }

        /// <summary>
        /// Registers file operation shortcuts (New, Open, Save, Save As, Close Tab) under <see cref="ShortcutScope.Window"/>.
        /// </summary>
        public static void RegisterFileShortcuts(IHexpriteShortcutManager manager, ShellViewModel shell)
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(shell);

            var fileShortcuts = new (Key Key, ModifierKeys Modifiers, ICommand Command, string ActionId)[]
            {
                (Key.N, ModifierKeys.Control, shell.NewDocumentCommand, ActionFileNew),
                (Key.O, ModifierKeys.Control, shell.OpenCommand, ActionFileOpen),
                (Key.S, ModifierKeys.Control, shell.SaveCommand, ActionFileSave),
                (Key.S, ModifierKeys.Control | ModifierKeys.Shift, shell.SaveAsCommand, ActionFileSaveAs),
                (Key.W, ModifierKeys.Control, shell.CloseTabCommand, ActionFileCloseTab),
            };

            foreach (var (key, modifiers, command, actionId) in fileShortcuts)
            {
                if (manager.GetShortcut(actionId) == null)
                {
                    manager.Register(new ShortcutDefinition(
                        actionId: actionId,
                        key: key,
                        modifierKeys: modifiers,
                        command: command,
                        scope: ShortcutScope.Window));
                }
            }
        }

        /// <summary>
        /// Registers edit operation shortcuts (Undo, Redo, Copy, Cut, Paste, Select All, Deselect, Transform, Merge Layer, Invert)
        /// under <see cref="ShortcutScope.Window"/>, dynamically resolving against <see cref="ShellViewModel.ActiveDocument"/>.
        /// </summary>
        public static void RegisterEditShortcuts(IHexpriteShortcutManager manager, ShellViewModel shell)
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(shell);

            var editShortcuts = new (Key Key, ModifierKeys Modifiers, string CommandProperty, string ActionId)[]
            {
                (Key.Z, ModifierKeys.Control, nameof(MainViewModel.UndoCommand), ActionEditUndo),
                (Key.Y, ModifierKeys.Control, nameof(MainViewModel.RedoCommand), ActionEditRedo),
                (Key.Z, ModifierKeys.Control | ModifierKeys.Shift, nameof(MainViewModel.RedoCommand), ActionEditRedoCtrlShiftZ),
                (Key.I, ModifierKeys.Control, nameof(MainViewModel.InvertCommand), ActionEditInvert),
                (Key.C, ModifierKeys.Control, nameof(MainViewModel.CopySelectionCommand), ActionEditCopy),
                (Key.X, ModifierKeys.Control, nameof(MainViewModel.CutSelectionCommand), ActionEditCut),
                (Key.V, ModifierKeys.Control, nameof(MainViewModel.PasteCommand), ActionEditPaste),
                (Key.A, ModifierKeys.Control, nameof(MainViewModel.SelectAllCommand), ActionEditSelectAll),
                (Key.D, ModifierKeys.Control, nameof(MainViewModel.DeselectCommand), ActionEditDeselect),
                (Key.D, ModifierKeys.Control | ModifierKeys.Shift, nameof(MainViewModel.ReselectCommand), ActionEditReselect),
                (Key.T, ModifierKeys.Control, nameof(MainViewModel.BeginSelectionTransformCommand), ActionEditTransform),
                (Key.E, ModifierKeys.Control, nameof(MainViewModel.MergeLayerCommand), ActionEditMergeLayer),
            };

            foreach (var (key, modifiers, commandProp, actionId) in editShortcuts)
            {
                if (manager.GetShortcut(actionId) == null)
                {
                    manager.Register(new ShortcutDefinition(
                        actionId: actionId,
                        key: key,
                        modifierKeys: modifiers,
                        command: CreateActiveDocumentCommand(shell, commandProp),
                        scope: ShortcutScope.Window));
                }
            }
        }

        /// <summary>
        /// Registers layer management shortcuts (Add Layer, Duplicate Layer, Delete Layer) under <see cref="ShortcutScope.Window"/>,
        /// dynamically resolving against <see cref="ShellViewModel.ActiveDocument"/>.
        /// </summary>
        public static void RegisterLayerShortcuts(IHexpriteShortcutManager manager, ShellViewModel shell)
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(shell);

            var layerShortcuts = new (Key Key, ModifierKeys Modifiers, string CommandProperty, string ActionId)[]
            {
                (Key.N, ModifierKeys.Control | ModifierKeys.Shift, nameof(MainViewModel.AddLayerCommand), ActionLayerAdd),
                (Key.J, ModifierKeys.Control, nameof(MainViewModel.DuplicateLayerCommand), ActionLayerDuplicate),
                (Key.J, ModifierKeys.Control | ModifierKeys.Shift, nameof(MainViewModel.NewLayerFromSelectionCommand), ActionLayerNewFromSelection),
                (Key.Delete, ModifierKeys.Control | ModifierKeys.Shift, nameof(MainViewModel.DeleteLayerCommand), ActionLayerDelete),
                (Key.F2, ModifierKeys.None, nameof(MainViewModel.RenameLayerCommand), ActionLayerRename),
            };

            foreach (var (key, modifiers, commandProp, actionId) in layerShortcuts)
            {
                if (manager.GetShortcut(actionId) == null)
                {
                    manager.Register(new ShortcutDefinition(
                        actionId: actionId,
                        key: key,
                        modifierKeys: modifiers,
                        command: CreateActiveDocumentCommand(shell, commandProp),
                        scope: ShortcutScope.Window));
                }
            }
        }

        /// <summary>
        /// Registers view navigation and zoom shortcuts under <see cref="ShortcutScope.Window"/>.
        /// </summary>
        public static void RegisterViewShortcuts(
            IHexpriteShortcutManager manager,
            ShellViewModel shell,
            ICommand? zoomInCommand = null,
            ICommand? zoomOutCommand = null,
            ICommand? zoomResetCommand = null)
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(shell);

            bool hasCustomZoomIn = zoomInCommand != null;
            bool hasCustomZoomOut = zoomOutCommand != null;
            bool hasCustomZoomReset = zoomResetCommand != null;

            zoomInCommand ??= new RelayCommand(
                () =>
                {
                    try
                    {
                        var app = Application.Current;
                        if (app != null && (app.Dispatcher == null || app.Dispatcher.CheckAccess()))
                        {
                            if (app.MainWindow is MainWindow mw)
                            {
                                mw.ZoomPan.ApplyZoomCentered(ZoomPanController.ZoomFactor);
                                return;
                            }
                        }
                    }
                    catch
                    {
                    }

                    try
                    {
                        var target = Keyboard.FocusedElement;
                        if (target != null && NavigationCommands.IncreaseZoom.CanExecute(null, target))
                        {
                            NavigationCommands.IncreaseZoom.Execute(null, target);
                        }
                    }
                    catch
                    {
                    }
                },
                () => shell.ActiveDocument is MainViewModel);

            zoomOutCommand ??= new RelayCommand(
                () =>
                {
                    try
                    {
                        var app = Application.Current;
                        if (app != null && (app.Dispatcher == null || app.Dispatcher.CheckAccess()))
                        {
                            if (app.MainWindow is MainWindow mw)
                            {
                                mw.ZoomPan.ApplyZoomCentered(1.0 / ZoomPanController.ZoomFactor);
                                return;
                            }
                        }
                    }
                    catch
                    {
                    }

                    try
                    {
                        var target = Keyboard.FocusedElement;
                        if (target != null && NavigationCommands.DecreaseZoom.CanExecute(null, target))
                        {
                            NavigationCommands.DecreaseZoom.Execute(null, target);
                        }
                    }
                    catch
                    {
                    }
                },
                () => shell.ActiveDocument is MainViewModel);

            zoomResetCommand ??= new RelayCommand(
                () =>
                {
                    try
                    {
                        var app = Application.Current;
                        if (app != null && (app.Dispatcher == null || app.Dispatcher.CheckAccess()))
                        {
                            if (app.MainWindow is MainWindow mw)
                            {
                                mw.ZoomPan.ZoomReset();
                                return;
                            }
                        }
                    }
                    catch
                    {
                    }

                    try
                    {
                        var target = Keyboard.FocusedElement;
                        if (target != null && NavigationCommands.Zoom.CanExecute(null, target))
                        {
                            NavigationCommands.Zoom.Execute(null, target);
                        }
                    }
                    catch
                    {
                    }
                },
                () => shell.ActiveDocument is MainViewModel);

            var zoomShortcuts = new (Key Key, ModifierKeys Modifiers, ICommand Command, string ActionId, bool HasCustomCommand)[]
            {
                (Key.OemPlus, ModifierKeys.Control, zoomInCommand, ActionViewZoomInOemPlus, hasCustomZoomIn),
                (Key.OemPlus, ModifierKeys.Control | ModifierKeys.Shift, zoomInCommand, ActionViewZoomInShiftOemPlus, hasCustomZoomIn),
                (Key.Add, ModifierKeys.Control, zoomInCommand, ActionViewZoomInAdd, hasCustomZoomIn),
                (Key.OemMinus, ModifierKeys.Control, zoomOutCommand, ActionViewZoomOutOemMinus, hasCustomZoomOut),
                (Key.Subtract, ModifierKeys.Control, zoomOutCommand, ActionViewZoomOutSubtract, hasCustomZoomOut),
                (Key.D0, ModifierKeys.Control, zoomResetCommand, ActionViewZoomResetD0, hasCustomZoomReset),
                (Key.NumPad0, ModifierKeys.Control, zoomResetCommand, ActionViewZoomResetNumPad0, hasCustomZoomReset),
            };

            foreach (var (key, modifiers, command, actionId, hasCustom) in zoomShortcuts)
            {
                var existing = manager.GetShortcut(actionId);
                if (existing != null)
                {
                    if (hasCustom)
                    {
                        manager.Unregister(actionId);
                        manager.Register(new ShortcutDefinition(
                            actionId: actionId,
                            key: key,
                            modifierKeys: modifiers,
                            command: command,
                            scope: ShortcutScope.Window));
                    }
                }
                else
                {
                    manager.Register(new ShortcutDefinition(
                        actionId: actionId,
                        key: key,
                        modifierKeys: modifiers,
                        command: command,
                        scope: ShortcutScope.Window));
                }
            }
        }

        /// <summary>
        /// Registers global utility shortcuts (F5: Refresh Theme, F1: Open Documentation) under <see cref="ShortcutScope.Global"/>.
        /// </summary>
        public static void RegisterGlobalUtilities(IHexpriteShortcutManager manager, ShellViewModel shell)
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(shell);

            var utilities = new (Key Key, ModifierKeys Modifiers, ICommand Command, string ActionId, ShortcutScope Scope)[]
            {
                (Key.F5, ModifierKeys.None, shell.RefreshThemeCommand, ActionGlobalRefreshTheme, ShortcutScope.Window),
                (Key.F1, ModifierKeys.None, shell.OpenDocumentationCommand, ActionGlobalOpenDocumentation, ShortcutScope.Global),
                (Key.OemQuestion, ModifierKeys.Control, shell.OpenKeyboardShortcutsCommand, ActionHelpKeyboardShortcuts, ShortcutScope.Global),
                (Key.Divide, ModifierKeys.Control, shell.OpenKeyboardShortcutsCommand, ActionHelpKeyboardShortcutsNumPad, ShortcutScope.Global),
            };

            foreach (var (key, modifiers, command, actionId, scope) in utilities)
            {
                if (manager.GetShortcut(actionId) == null)
                {
                    manager.Register(new ShortcutDefinition(
                        actionId: actionId,
                        key: key,
                        modifierKeys: modifiers,
                        command: command,
                        scope: scope));
                }
            }
        }

        private static RelayCommand CreateActiveDocumentCommand(
            ShellViewModel shell,
            string commandPropertyName)
        {
            return new RelayCommand(
                () =>
                {
                    var doc = shell.ActiveDocument;
                    if (doc == null)
                    {
                        return;
                    }

                    if (doc is MainViewModel mvm && mvm.IsTextEditing &&
                        commandPropertyName != nameof(MainViewModel.UndoCommand) &&
                        commandPropertyName != nameof(MainViewModel.RedoCommand))
                    {
                        return;
                    }

                    var cmd = ResolveDocumentCommand(doc, commandPropertyName);
                    if (cmd != null && cmd.CanExecute(null))
                    {
                        cmd.Execute(null);
                    }
                },
                () =>
                {
                    var doc = shell.ActiveDocument;
                    if (doc == null)
                    {
                        return false;
                    }

                    if (doc is MainViewModel mvm && mvm.IsTextEditing &&
                        commandPropertyName != nameof(MainViewModel.UndoCommand) &&
                        commandPropertyName != nameof(MainViewModel.RedoCommand))
                    {
                        return false;
                    }

                    var cmd = ResolveDocumentCommand(doc, commandPropertyName);
                    return cmd != null && cmd.CanExecute(null);
                });
        }

        private static ICommand? ResolveDocumentCommand(IDocumentTab doc, string commandPropertyName)
        {
            if (doc is MainViewModel mvm)
            {
                return commandPropertyName switch
                {
                    nameof(MainViewModel.UndoCommand) => mvm.UndoCommand,
                    nameof(MainViewModel.RedoCommand) => mvm.RedoCommand,
                    nameof(MainViewModel.InvertCommand) => mvm.InvertCommand,
                    nameof(MainViewModel.CopySelectionCommand) => mvm.CopySelectionCommand,
                    nameof(MainViewModel.CutSelectionCommand) => mvm.CutSelectionCommand,
                    nameof(MainViewModel.PasteCommand) => mvm.PasteCommand,
                    nameof(MainViewModel.DeselectCommand) => mvm.DeselectCommand,
                    nameof(MainViewModel.ReselectCommand) => mvm.ReselectCommand,
                    nameof(MainViewModel.SelectAllCommand) => mvm.SelectAllCommand,
                    nameof(MainViewModel.BeginSelectionTransformCommand) => mvm.BeginSelectionTransformCommand,
                    nameof(MainViewModel.MergeLayerCommand) => mvm.MergeLayerCommand,
                    nameof(MainViewModel.AddLayerCommand) => mvm.AddLayerCommand,
                    nameof(MainViewModel.DuplicateLayerCommand) => mvm.DuplicateLayerCommand,
                    nameof(MainViewModel.NewLayerFromSelectionCommand) => mvm.NewLayerFromSelectionCommand,
                    nameof(MainViewModel.DeleteLayerCommand) => mvm.DeleteLayerCommand,
                    nameof(MainViewModel.RenameLayerCommand) => mvm.RenameLayerCommand,
                    _ => null
                };
            }

            if (doc is FontViewModel fvm)
            {
                return commandPropertyName switch
                {
                    nameof(FontViewModel.UndoCommand) => fvm.UndoCommand,
                    nameof(FontViewModel.RedoCommand) => fvm.RedoCommand,
                    _ => null
                };
            }

            if (doc is AssetPackViewModel apvm)
            {
                return commandPropertyName switch
                {
                    nameof(MainViewModel.UndoCommand) => apvm.UndoCommand,
                    nameof(MainViewModel.RedoCommand) => apvm.RedoCommand,
                    _ => null
                };
            }

            var prop = doc.GetType().GetProperty(commandPropertyName);
            if (prop != null && typeof(ICommand).IsAssignableFrom(prop.PropertyType))
            {
                return prop.GetValue(doc) as ICommand;
            }

            return null;
        }
    }
}
