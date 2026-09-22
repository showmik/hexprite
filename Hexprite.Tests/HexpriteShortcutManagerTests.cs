using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Hexprite.Views;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hexprite.Tests;

[Collection("WindowLayoutSettingsFile")]
[Trait("Category", "Unit")]
public class HexpriteShortcutManagerTests
{
    private sealed class TestPresentationSource : PresentationSource
    {
        protected override CompositionTarget? GetCompositionTargetCore() => null;
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
    }

    #region R1 & R2: Registration & Duplicate Validation

    [Fact]
    public void Register_DuplicateSameKeyAndModifiersInSameScope_ThrowsInvalidOperationException()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var cmd1 = new RelayCommand(() => { });
        var cmd2 = new RelayCommand(() => { });

        var def1 = new ShortcutDefinition("Tool.Pencil", Key.B, ModifierKeys.None, cmd1, ShortcutScope.EditorCanvas);
        var def2 = new ShortcutDefinition("Tool.Brush", Key.B, ModifierKeys.None, cmd2, ShortcutScope.EditorCanvas);

        manager.Register(def1);
        var ex = Assert.Throws<InvalidOperationException>(() => manager.Register(def2));
        Assert.Contains("Duplicate shortcut registration", ex.Message);
    }

    [Fact]
    public void Register_SameKeyDifferentModifiersInSameScope_IsPermitted()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var cmd1 = new RelayCommand(() => { });
        var cmd2 = new RelayCommand(() => { });

        var def1 = new ShortcutDefinition("Tool.Pencil", Key.B, ModifierKeys.None, cmd1, ShortcutScope.EditorCanvas);
        var def2 = new ShortcutDefinition("Tool.PencilAlt", Key.B, ModifierKeys.Control, cmd2, ShortcutScope.EditorCanvas);

        manager.Register(def1);
        manager.Register(def2);

        Assert.Equal(2, manager.Shortcuts.Count);
    }

    [Fact]
    public void Register_SameKeyAcrossDifferentScopes_IsPermitted()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var cmd1 = new RelayCommand(() => { });
        var cmd2 = new RelayCommand(() => { });
        var cmd3 = new RelayCommand(() => { });

        var canvasShortcut = new ShortcutDefinition("Action.Canvas", Key.B, ModifierKeys.None, cmd1, ShortcutScope.EditorCanvas);
        var windowShortcut = new ShortcutDefinition("Action.Window", Key.B, ModifierKeys.None, cmd2, ShortcutScope.Window);
        var globalShortcut = new ShortcutDefinition("Action.Global", Key.B, ModifierKeys.None, cmd3, ShortcutScope.Global);

        manager.Register(canvasShortcut);
        manager.Register(windowShortcut);
        manager.Register(globalShortcut);

        Assert.Equal(3, manager.Shortcuts.Count);
        Assert.NotNull(manager.FindMatch(Key.B, ModifierKeys.None, ShortcutScope.EditorCanvas));
        Assert.NotNull(manager.FindMatch(Key.B, ModifierKeys.None, ShortcutScope.Window));
        Assert.NotNull(manager.FindMatch(Key.B, ModifierKeys.None, ShortcutScope.Global));
    }

    [Fact]
    public void Register_DuplicateActionId_ThrowsInvalidOperationException()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var cmd1 = new RelayCommand(() => { });
        var cmd2 = new RelayCommand(() => { });

        var def1 = new ShortcutDefinition("SameAction", Key.A, ModifierKeys.Control, cmd1, ShortcutScope.Window);
        var def2 = new ShortcutDefinition("SameAction", Key.B, ModifierKeys.Control, cmd2, ShortcutScope.Window);

        manager.Register(def1);
        var ex = Assert.Throws<InvalidOperationException>(() => manager.Register(def2));
        Assert.Contains("already registered", ex.Message);
    }

    [Fact]
    public void Unregister_RemovesShortcutAndAllowsReRegistration()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var cmd = new RelayCommand(() => { });
        var def = new ShortcutDefinition("Tool.Pencil", Key.B, ModifierKeys.None, cmd, ShortcutScope.EditorCanvas);

        manager.Register(def);
        Assert.True(manager.Unregister("Tool.Pencil"));
        Assert.Null(manager.GetShortcut("Tool.Pencil"));
        Assert.Null(manager.FindMatch(Key.B, ModifierKeys.None, ShortcutScope.EditorCanvas));

        // Now registering again should not throw
        manager.Register(def);
        Assert.NotNull(manager.GetShortcut("Tool.Pencil"));
    }

    [Fact]
    public void Validate_WithNoDuplicates_DoesNotThrow()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        manager.Register(new ShortcutDefinition("Action1", Key.A, ModifierKeys.None, new RelayCommand(() => { }), ShortcutScope.Global));
        manager.Register(new ShortcutDefinition("Action2", Key.B, ModifierKeys.None, new RelayCommand(() => { }), ShortcutScope.EditorCanvas));

        manager.Validate();
    }

    #endregion

    #region R3: PreProcessInput & Focus Suppression

    [Fact]
    public void ProcessKey_EditorCanvasScope_WhenFocusOnTextBox_BypassesShortcut()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            bool executed = false;
            manager.Register(new ShortcutDefinition("Tool.Pencil", Key.B, ModifierKeys.None,
                new RelayCommand(() => executed = true), ShortcutScope.EditorCanvas));

            var textBox = new TextBox();
            bool result = manager.ProcessKey(Key.B, ModifierKeys.None, focusedElement: textBox);

            Assert.False(result);
            Assert.False(executed);
        });
    }

    [Fact]
    public void ProcessKey_EditorCanvasScope_WhenFocusOnRichTextBox_BypassesShortcut()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            bool executed = false;
            manager.Register(new ShortcutDefinition("Tool.Eraser", Key.E, ModifierKeys.None,
                new RelayCommand(() => executed = true), ShortcutScope.EditorCanvas));

            var richTextBox = new RichTextBox();
            bool result = manager.ProcessKey(Key.E, ModifierKeys.None, focusedElement: richTextBox);

            Assert.False(result);
            Assert.False(executed);
        });
    }

    [Fact]
    public void ProcessKey_EditorCanvasScope_WhenFocusOnEditableComboBox_BypassesShortcut()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            bool executed = false;
            manager.Register(new ShortcutDefinition("Tool.Line", Key.L, ModifierKeys.None,
                new RelayCommand(() => executed = true), ShortcutScope.EditorCanvas));

            var comboBox = new ComboBox { IsEditable = true };
            bool result = manager.ProcessKey(Key.L, ModifierKeys.None, focusedElement: comboBox);

            Assert.False(result);
            Assert.False(executed);
        });
    }

    [Fact]
    public void ProcessKey_EditorCanvasScope_WhenFocusOnNonEditableComboBox_ExecutesShortcut()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            bool executed = false;
            manager.Register(new ShortcutDefinition("Tool.Line", Key.L, ModifierKeys.None,
                new RelayCommand(() => executed = true), ShortcutScope.EditorCanvas));

            var comboBox = new ComboBox { IsEditable = false };
            bool result = manager.ProcessKey(Key.L, ModifierKeys.None, focusedElement: comboBox);

            Assert.True(result);
            Assert.True(executed);
        });
    }

    [Fact]
    public void ProcessKey_EditorCanvasScope_WhenFocusIsNull_ExecutesShortcut()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        bool executed = false;
        manager.Register(new ShortcutDefinition("Tool.Pencil", Key.B, ModifierKeys.None,
            new RelayCommand(() => executed = true), ShortcutScope.EditorCanvas));

        bool result = manager.ProcessKey(Key.B, ModifierKeys.None, focusedElement: null);

        Assert.True(result);
        Assert.True(executed);
    }

    [Fact]
    public void ProcessKey_GlobalScope_WhenFocusOnTextBox_ExecutesShortcut()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            bool executed = false;
            manager.Register(new ShortcutDefinition("App.Help", Key.F1, ModifierKeys.None,
                new RelayCommand(() => executed = true), ShortcutScope.Global));

            var textBox = new TextBox();
            bool result = manager.ProcessKey(Key.F1, ModifierKeys.None, focusedElement: textBox);

            Assert.True(result);
            Assert.True(executed);
        });
    }

    [Fact]
    public void ProcessKey_ScopePrecedence_EditorCanvasPreferredOverGlobalUnlessSuppressed()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            string executedTarget = string.Empty;

            manager.Register(new ShortcutDefinition("Canvas.Action", Key.B, ModifierKeys.None,
                new RelayCommand(() => executedTarget = "Canvas"), ShortcutScope.EditorCanvas));
            manager.Register(new ShortcutDefinition("Global.Action", Key.B, ModifierKeys.None,
                new RelayCommand(() => executedTarget = "Global"), ShortcutScope.Global));

            // Case A: No text focus -> EditorCanvas takes precedence
            manager.ProcessKey(Key.B, ModifierKeys.None, focusedElement: null);
            Assert.Equal("Canvas", executedTarget);

            // Case B: In active text input -> EditorCanvas is bypassed, falls through to Global
            executedTarget = string.Empty;
            var textBox = new TextBox();
            manager.ProcessKey(Key.B, ModifierKeys.None, focusedElement: textBox);
            Assert.Equal("Global", executedTarget);
        });
    }

    #endregion

    #region R3: Fall-Through & Unhandled Events

    [Fact]
    public void ProcessInput_UnregisteredKey_LeavesHandledFalseAndReturnsFalse()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            var source = new TestPresentationSource();
            var keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Z)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };

            bool handled = manager.ProcessInput(keyArgs);

            Assert.False(handled);
            Assert.False(keyArgs.Handled);
        });
    }

    [Fact]
    public void ProcessInput_WhenCommandCannotExecute_LeavesHandledFalseAndReturnsFalse()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            bool executed = false;
            var disabledCommand = new RelayCommand(
                () => executed = true,
                () => false // CanExecute returns false
            );

            manager.Register(new ShortcutDefinition("DisabledAction", Key.K, ModifierKeys.Control,
                disabledCommand, ShortcutScope.Global));

            var source = new TestPresentationSource();
            var keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.K)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };

            bool handled = manager.ProcessInput(keyArgs);

            Assert.False(handled);
            Assert.False(keyArgs.Handled);
            Assert.False(executed);
        });
    }

    [Fact]
    public void ProcessInput_AlreadyHandledEvent_ExitsImmediately()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: true);
            var source = new TestPresentationSource();
            var keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.F11)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = true
            };

            bool handled = manager.ProcessInput(keyArgs);

            Assert.False(handled);
            Assert.False(manager.IsTracerBulletExecuted);
        });
    }

    [Fact]
    public void ProcessInput_BypassedCanvasShortcutInTextBox_LeavesHandledFalse()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            bool executed = false;
            manager.Register(new ShortcutDefinition("Tool.Pencil", Key.B, ModifierKeys.None,
                new RelayCommand(() => executed = true), ShortcutScope.EditorCanvas));

            var textBox = new TextBox();
            var source = new TestPresentationSource();
            var keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.B)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };

            bool handled = manager.ProcessInput(keyArgs, focusedElementOverride: textBox);

            Assert.False(handled);
            Assert.False(keyArgs.Handled);
            Assert.False(executed);
        });
    }

    #endregion

    #region R4: Tracer Bullet & DI Wiring

    [Fact]
    public void TracerBullet_RegisteredByDefault_InGlobalScope()
    {
        var manager = new HexpriteShortcutManager();

        var tracer = manager.GetShortcut(HexpriteShortcutManager.TracerBulletActionId);
        Assert.NotNull(tracer);
        Assert.Equal(Key.F11, tracer.Key);
        Assert.Equal(ModifierKeys.None, tracer.ModifierKeys);
        Assert.Equal(ShortcutScope.Global, tracer.Scope);
    }

    [Fact]
    public void TracerBullet_PressingF11InGlobalScope_TriggersCommandAndSetsHandledTrue()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager();
            bool eventFired = false;
            manager.TracerBulletExecuted += (s, e) => eventFired = true;

            var source = new TestPresentationSource();
            var keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.F11)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };

            bool handled = manager.ProcessInput(keyArgs, modifiersOverride: ModifierKeys.None);

            Assert.True(handled);
            Assert.True(keyArgs.Handled);
            Assert.True(manager.IsTracerBulletExecuted);
            Assert.True(eventFired);
        });
    }

    [Fact]
    public void TracerBullet_PressingF11WhileFocusOnTextBox_StillTriggersAndSetsHandledTrue()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager();
            var textBox = new TextBox();

            var source = new TestPresentationSource();
            var keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.F11)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };

            bool handled = manager.ProcessInput(keyArgs, focusedElementOverride: textBox, modifiersOverride: ModifierKeys.None);

            Assert.True(handled);
            Assert.True(keyArgs.Handled);
            Assert.True(manager.IsTracerBulletExecuted);
        });
    }

    [Fact]
    public void DiRegistration_ResolvesSingletonIHexpriteShortcutManager()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHexpriteShortcutManager, HexpriteShortcutManager>();
        var provider = services.BuildServiceProvider();

        var instance1 = provider.GetRequiredService<IHexpriteShortcutManager>();
        var instance2 = provider.GetRequiredService<IHexpriteShortcutManager>();

        Assert.NotNull(instance1);
        Assert.Same(instance1, instance2);

        // Verify tracer bullet is present
        Assert.NotNull(instance1.GetShortcut(HexpriteShortcutManager.TracerBulletActionId));
    }

    #endregion

    #region Adversarial & Edge Case Tests

    [Fact]
    public void Constructor_WithDuplicateInitialShortcuts_ThrowsInvalidOperationException()
    {
        var cmd = new RelayCommand(() => { });
        var def1 = new ShortcutDefinition("Action1", Key.A, ModifierKeys.Control, cmd, ShortcutScope.Global);
        var def2 = new ShortcutDefinition("Action2", Key.A, ModifierKeys.Control, cmd, ShortcutScope.Global);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new HexpriteShortcutManager(new[] { def1, def2 }, registerTracerBullet: false));

        Assert.Contains("Duplicate shortcut", ex.Message);
    }

    [Fact]
    public void Register_NullOrWhitespaceActionId_ThrowsArgumentException()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var def = new ShortcutDefinition("", Key.A, ModifierKeys.None, new RelayCommand(() => { }), ShortcutScope.Global);

        Assert.Throws<ArgumentException>(() => manager.Register(def));
    }

    [Fact]
    public void Register_NullCommand_ThrowsArgumentNullException()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var def = new ShortcutDefinition
        {
            ActionId = "Action.Valid",
            Key = Key.A,
            ModifierKeys = ModifierKeys.None,
            Command = null!,
            Scope = ShortcutScope.Global
        };

        Assert.Throws<ArgumentNullException>(() => manager.Register(def));
    }

    [Fact]
    public void ProcessInput_SystemKeyWithAlt_ExecutesCorrectly()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false)
            {
                ModifierKeysOverride = ModifierKeys.None
            };
            bool executed = false;
            manager.Register(new ShortcutDefinition(
                "App.Close", Key.F4, ModifierKeys.Alt,
                new RelayCommand(() => executed = true),
                ShortcutScope.Window));

            var source = new TestPresentationSource();
            // In WPF, Alt+Key results in Key.System with SystemKey set to the pressed key
            var keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.System)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };

            // In WPF, SystemKey returns _realKey when _key is Key.System
            var realKeyField = typeof(KeyEventArgs).GetField("_realKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            realKeyField?.SetValue(keyArgs, Key.F4);

            bool handled = manager.ProcessInput(keyArgs);

            Assert.True(handled);
            Assert.True(keyArgs.Handled);
            Assert.True(executed);
        });
    }

    [Fact]
    public void ProcessInput_ImeAndDeadCharKeys_BypassedImmediately()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: true);
            var source = new TestPresentationSource();

            var imeArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.ImeProcessed)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };

            Assert.False(manager.ProcessInput(imeArgs));
            Assert.False(imeArgs.Handled);

            // DeadCharProcessed is verified via ProcessKey directly
            Assert.False(manager.ProcessKey(Key.DeadCharProcessed, ModifierKeys.None));
            Assert.False(manager.ProcessKey(Key.ImeProcessed, ModifierKeys.None));
            Assert.False(manager.ProcessKey(Key.None, ModifierKeys.None));
        });
    }

    [Fact]
    public void ProcessKey_WindowScope_WhenFocusOnTextBox_ExecutesShortcut()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            bool executed = false;
            manager.Register(new ShortcutDefinition("Window.Escape", Key.Escape, ModifierKeys.None,
                new RelayCommand(() => executed = true), ShortcutScope.Window));

            var textBox = new TextBox();
            bool result = manager.ProcessKey(Key.Escape, ModifierKeys.None, focusedElement: textBox);

            Assert.True(result);
            Assert.True(executed);
        });
    }

    [Fact]
    public void ProcessKey_CommandParameter_PassedToCanExecuteAndExecute()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        object? receivedCanExecuteParam = null;
        object? receivedExecuteParam = null;

        var cmd = new RelayCommand<string>(
            p => receivedExecuteParam = p,
            p => { receivedCanExecuteParam = p; return true; });

        manager.Register(new ShortcutDefinition(
            "Parameterized.Action",
            Key.P,
            ModifierKeys.Control,
            cmd,
            ShortcutScope.Global,
            commandParameter: "CustomPayload"));

        bool result = manager.ProcessKey(Key.P, ModifierKeys.Control);

        Assert.True(result);
        Assert.Equal("CustomPayload", receivedCanExecuteParam);
        Assert.Equal("CustomPayload", receivedExecuteParam);
    }

    private sealed class ContainerTextBox : TextBox
    {
        public void AttachChild(FrameworkElement child) => AddLogicalChild(child);
    }

    private sealed class ContainerComboBox : ComboBox
    {
        public void AttachChild(FrameworkElement child) => AddLogicalChild(child);
    }

    [Fact]
    public void IsActiveTextInput_NestedElementInsideTextBox_ReturnsTrue()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            var textBox = new ContainerTextBox();
            var innerBorder = new Border();
            textBox.AttachChild(innerBorder);

            Assert.True(manager.IsActiveTextInput(innerBorder));
        });
    }

    [Fact]
    public void IsActiveTextInput_NestedElementInsideEditableComboBox_ReturnsTrue()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            var comboBox = new ContainerComboBox { IsEditable = true };
            var innerBorder = new Border();
            comboBox.AttachChild(innerBorder);

            Assert.True(manager.IsActiveTextInput(innerBorder));
        });
    }

    [Fact]
    public void IsActiveTextInput_NestedElementInsideNonEditableComboBox_ReturnsFalse()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            var comboBox = new ContainerComboBox { IsEditable = false };
            var innerBorder = new Border();
            comboBox.AttachChild(innerBorder);

            Assert.False(manager.IsActiveTextInput(innerBorder));
        });
    }

    [Fact]
    public void IsActiveTextInput_DeepFlowDocumentInsideRichTextBox_ReturnsTrue()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            var richTextBox = new RichTextBox();
            var run = new System.Windows.Documents.Run("sample text");
            var paragraph = new System.Windows.Documents.Paragraph(run);
            richTextBox.Document.Blocks.Add(paragraph);

            Assert.True(manager.IsActiveTextInput(run));
        });
    }

    [Fact]
    public void PreProcessInput_EndToEndHook_WithInputManager_ExecutesTracerBullet()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            using var manager = new HexpriteShortcutManager
            {
                ModifierKeysOverride = ModifierKeys.None
            };
            manager.Initialize();

            var source = new TestPresentationSource();
            var keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.F11)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };

            // Dispatch directly through WPF InputManager.Current
            InputManager.Current.ProcessInput(keyArgs);

            Assert.True(keyArgs.Handled);
            Assert.True(manager.IsTracerBulletExecuted);
        });
    }

    [Fact]
    public void Dispose_UnhooksFromInputManager_Cleanly()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager();
            try
            {
                manager.Initialize();
                manager.Dispose();

                var source = new TestPresentationSource();
                var keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.F11)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent,
                    Handled = false
                };

                InputManager.Current.ProcessInput(keyArgs);

                // Since it was disposed and unhooked, the tracer bullet should not execute through PreProcessInput
                Assert.False(manager.IsTracerBulletExecuted);
            }
            finally
            {
                manager.Dispose();
            }
        });
    }

    [Fact]
    public void Unregister_NonExistentAction_ReturnsFalse()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        Assert.False(manager.Unregister("NonExistentAction"));
    }

    [Fact]
    public void ProcessInput_WithExplicitModifiersOverride_OverridesDeviceModifiers()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            bool executed = false;
            manager.Register(new ShortcutDefinition(
                "Custom.Action", Key.C, ModifierKeys.Control | ModifierKeys.Shift,
                new RelayCommand(() => executed = true),
                ShortcutScope.Global));

            var source = new TestPresentationSource();
            var keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.C)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };

            // Call ProcessInput passing explicit modifiersOverride
            bool handled = manager.ProcessInput(keyArgs, modifiersOverride: ModifierKeys.Control | ModifierKeys.Shift);

            Assert.True(handled);
            Assert.True(keyArgs.Handled);
            Assert.True(executed);
        });
    }

    [Fact]
    public void ModifierKeysOverride_Property_OverridesHardwareModifierEvaluation()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false)
            {
                ModifierKeysOverride = ModifierKeys.Control
            };
            bool executed = false;
            manager.Register(new ShortcutDefinition(
                "Save.Action", Key.S, ModifierKeys.Control,
                new RelayCommand(() => executed = true),
                ShortcutScope.Global));

            var source = new TestPresentationSource();
            var keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.S)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };

            bool handled = manager.ProcessInput(keyArgs);

            Assert.True(handled);
            Assert.True(keyArgs.Handled);
            Assert.True(executed);
        });
    }

    #endregion

    #region EditorCanvas Tool Shortcut Migration Tests

    [Fact]
    public void RegisterDefaultTools_RegistersAll16ToolShortcutsWithoutDuplicateExceptions()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        string? selectedTool = null;
        var toolCmd = new RelayCommand<string>(tool => selectedTool = tool);

        EditorCanvasShortcutRegistrar.RegisterDefaultTools(manager, toolCmd);

        Assert.Equal(16, manager.Shortcuts.Count);
        Assert.NotNull(manager.FindMatch(Key.B, ModifierKeys.None, ShortcutScope.EditorCanvas));
        Assert.NotNull(manager.FindMatch(Key.E, ModifierKeys.None, ShortcutScope.EditorCanvas));
        Assert.NotNull(manager.FindMatch(Key.R, ModifierKeys.None, ShortcutScope.EditorCanvas));
        Assert.NotNull(manager.FindMatch(Key.R, ModifierKeys.Shift, ShortcutScope.EditorCanvas));
        Assert.NotNull(manager.FindMatch(Key.C, ModifierKeys.Shift, ShortcutScope.EditorCanvas));
        Assert.NotNull(manager.FindMatch(Key.G, ModifierKeys.None, ShortcutScope.EditorCanvas));
        Assert.NotNull(manager.FindMatch(Key.M, ModifierKeys.Shift, ShortcutScope.EditorCanvas));
    }

    [Theory]
    [InlineData(Key.B, ModifierKeys.None, "Pencil")]
    [InlineData(Key.E, ModifierKeys.None, "Eraser")]
    [InlineData(Key.L, ModifierKeys.None, "Line")]
    [InlineData(Key.R, ModifierKeys.None, "Rectangle")]
    [InlineData(Key.C, ModifierKeys.None, "Ellipse")]
    [InlineData(Key.F, ModifierKeys.None, "Fill")]
    [InlineData(Key.T, ModifierKeys.None, "Text")]
    [InlineData(Key.V, ModifierKeys.None, "Move")]
    [InlineData(Key.M, ModifierKeys.None, "Marquee")]
    [InlineData(Key.Q, ModifierKeys.None, "Lasso")]
    [InlineData(Key.W, ModifierKeys.None, "MagicWand")]
    [InlineData(Key.D, ModifierKeys.None, "Dither")]
    [InlineData(Key.R, ModifierKeys.Shift, "FilledRectangle")]
    [InlineData(Key.C, ModifierKeys.Shift, "FilledEllipse")]
    [InlineData(Key.G, ModifierKeys.None, "Gradient")]
    [InlineData(Key.M, ModifierKeys.Shift, "EllipticalMarquee")]
    public void ProcessKey_ToolShortcut_ExecutesSelectToolCommandWithCorrectParameter(Key key, ModifierKeys modifiers, string expectedTool)
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        string? executedTool = null;
        var toolCmd = new RelayCommand<string>(tool => executedTool = tool);

        EditorCanvasShortcutRegistrar.RegisterDefaultTools(manager, toolCmd);

        bool handled = manager.ProcessKey(key, modifiers);

        Assert.True(handled);
        Assert.Equal(expectedTool, executedTool);
    }

    [Fact]
    public void ProcessInput_ToolShortcut_WhenTextBoxFocused_IsSuppressed()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            string? executedTool = null;
            var toolCmd = new RelayCommand<string>(tool => executedTool = tool);
            EditorCanvasShortcutRegistrar.RegisterDefaultTools(manager, toolCmd);

            var textBox = new TextBox();
            var source = new TestPresentationSource();
            var keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.B)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };

            bool handled = manager.ProcessInput(keyArgs, focusedElementOverride: textBox);

            Assert.False(handled);
            Assert.False(keyArgs.Handled);
            Assert.Null(executedTool);
        });
    }

    [Fact]
    public void ProcessInput_ToolShortcut_WhenEditableComboBoxFocused_IsSuppressed()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            string? executedTool = null;
            var toolCmd = new RelayCommand<string>(tool => executedTool = tool);
            EditorCanvasShortcutRegistrar.RegisterDefaultTools(manager, toolCmd);

            var comboBox = new ComboBox { IsEditable = true };
            var source = new TestPresentationSource();
            var keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.R)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };

            bool handled = manager.ProcessInput(keyArgs, focusedElementOverride: comboBox);

            Assert.False(handled);
            Assert.False(keyArgs.Handled);
            Assert.Null(executedTool);
        });
    }

    [Fact]
    public void ProcessInput_ToolShortcut_WhenCanvasFocused_ExecutesAndMarksHandled()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            string? executedTool = null;
            var toolCmd = new RelayCommand<string>(tool => executedTool = tool);
            EditorCanvasShortcutRegistrar.RegisterDefaultTools(manager, toolCmd);

            var canvas = new Canvas();
            var source = new TestPresentationSource();
            var keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.B)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };

            bool handled = manager.ProcessInput(keyArgs, focusedElementOverride: canvas);

            Assert.True(handled);
            Assert.True(keyArgs.Handled);
            Assert.Equal("Pencil", executedTool);
        });
    }

    [Fact]
    public void RegisterDefaultShortcuts_RegistersAllToolBrushAnimationAndGridShortcutsWithoutCollision()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();

        EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        Assert.Equal(39, manager.Shortcuts.Count);
        Assert.NotNull(manager.GetShortcut("Canvas.BrushSize.Decrement"));
        Assert.NotNull(manager.GetShortcut("Canvas.BrushSize.Increment"));
        Assert.NotNull(manager.GetShortcut("Animation.TogglePlayback"));
        Assert.NotNull(manager.GetShortcut("Animation.PreviousFrame"));
        Assert.NotNull(manager.GetShortcut("Animation.NextFrame"));
        Assert.NotNull(manager.GetShortcut("Animation.FirstFrame"));
        Assert.NotNull(manager.GetShortcut("Animation.LastFrame"));
        Assert.NotNull(manager.GetShortcut("Animation.ToggleOnionSkin"));
        Assert.NotNull(manager.GetShortcut(EditorCanvasShortcutRegistrar.ActionAnimationAddFrame));
        Assert.NotNull(manager.GetShortcut("Canvas.GridShift.Up"));
        Assert.NotNull(manager.GetShortcut("Canvas.GridShift.Down"));
        Assert.NotNull(manager.GetShortcut("Canvas.GridShift.Left"));
        Assert.NotNull(manager.GetShortcut("Canvas.GridShift.Right"));
        Assert.NotNull(manager.GetShortcut(EditorCanvasShortcutRegistrar.ActionCanvasDeleteSelection));
        Assert.NotNull(manager.GetShortcut(EditorCanvasShortcutRegistrar.ActionCanvasDeleteSelectionBackspace));
        Assert.NotNull(manager.GetShortcut("Canvas.Selection.NudgeUp"));
        Assert.NotNull(manager.GetShortcut("Canvas.Selection.NudgeUpFast"));
    }

    [Fact]
    public void ProcessKey_BrushShortcuts_AdjustsBrushSizeOnActiveDocument()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("128x64");
        var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);
        mvm.BrushSize = 5;

        EditorCanvasShortcutRegistrar.RegisterBrushShortcuts(manager, shell);

        bool decrementHandled = manager.ProcessKey(Key.OemOpenBrackets, ModifierKeys.None);
        Assert.True(decrementHandled);
        Assert.Equal(4, mvm.BrushSize);

        bool incrementHandled = manager.ProcessKey(Key.OemCloseBrackets, ModifierKeys.None);
        Assert.True(incrementHandled);
        Assert.Equal(5, mvm.BrushSize);
    }

    [Fact]
    public void ProcessKey_GridShiftShortcuts_ShiftsGridOnActiveDocument()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("128x64");

        EditorCanvasShortcutRegistrar.RegisterGridShiftShortcuts(manager, shell);

        bool handled = manager.ProcessKey(Key.Down, ModifierKeys.Control);
        Assert.True(handled);
    }

    [Fact]
    public void ProcessInput_AnimationAndBrushShortcuts_SuppressedWhenTextBoxFocused()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
            shell.NewDocumentCommand.Execute("128x64");
            var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);
            mvm.BrushSize = 5;

            EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

            var textBox = new TextBox();
            var source = new TestPresentationSource();
            var keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.OemOpenBrackets)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };

            bool handled = manager.ProcessInput(keyArgs, focusedElementOverride: textBox);

            Assert.False(handled);
            Assert.False(keyArgs.Handled);
            Assert.Equal(5, mvm.BrushSize);
        });
    }

    #endregion

    #region Window & Document Shortcut Migration Tests

    [Fact]
    public void RegisterDefaultShortcuts_RegistersAll30WindowAndDocumentShortcutsWithoutCollision()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();

        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        Assert.Equal(33, manager.Shortcuts.Count);

        // 5 File shortcuts
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionFileNew));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionFileOpen));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionFileSave));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionFileSaveAs));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionFileCloseTab));

        // 12 Edit shortcuts
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionEditUndo));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionEditRedo));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionEditRedoCtrlShiftZ));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionEditInvert));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionEditCopy));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionEditCut));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionEditPaste));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionEditSelectAll));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionEditDeselect));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionEditReselect));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionEditTransform));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionEditMergeLayer));

        // 5 Layer shortcuts
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionLayerAdd));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionLayerDuplicate));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionLayerNewFromSelection));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionLayerDelete));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionLayerRename));

        // 7 View / Zoom shortcuts
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionViewZoomInOemPlus));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionViewZoomInShiftOemPlus));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionViewZoomInAdd));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionViewZoomOutOemMinus));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionViewZoomOutSubtract));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionViewZoomResetD0));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionViewZoomResetNumPad0));

        // 4 Global/Window utility shortcuts
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionGlobalRefreshTheme));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionGlobalOpenDocumentation));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionHelpKeyboardShortcuts));
        Assert.NotNull(manager.GetShortcut(WindowShortcutRegistrar.ActionHelpKeyboardShortcutsNumPad));

        // Validate scopes
        Assert.Equal(ShortcutScope.Window, manager.GetShortcut(WindowShortcutRegistrar.ActionGlobalRefreshTheme)!.Scope);
        Assert.Equal(ShortcutScope.Global, manager.GetShortcut(WindowShortcutRegistrar.ActionGlobalOpenDocumentation)!.Scope);
        Assert.Equal(ShortcutScope.Global, manager.GetShortcut(WindowShortcutRegistrar.ActionHelpKeyboardShortcuts)!.Scope);
        Assert.Equal(ShortcutScope.Global, manager.GetShortcut(WindowShortcutRegistrar.ActionHelpKeyboardShortcutsNumPad)!.Scope);
        Assert.Equal(ShortcutScope.Window, manager.GetShortcut(WindowShortcutRegistrar.ActionFileNew)!.Scope);
        Assert.Equal(ShortcutScope.Window, manager.GetShortcut(WindowShortcutRegistrar.ActionEditUndo)!.Scope);
        Assert.Equal(ShortcutScope.Window, manager.GetShortcut(WindowShortcutRegistrar.ActionViewZoomInOemPlus)!.Scope);
        Assert.Equal(ShortcutScope.Window, manager.GetShortcut(WindowShortcutRegistrar.ActionLayerAdd)!.Scope);
        Assert.Equal(ShortcutScope.Window, manager.GetShortcut(WindowShortcutRegistrar.ActionLayerRename)!.Scope);

        // Validation against duplicate registration succeeds
        manager.Validate();
    }

    [Fact]
    public void RegisterDefaultShortcuts_CoexistsWithEditorCanvasAndTracerBulletWithoutCollision()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: true);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();

        EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);
        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        // 1 tracer bullet + 39 canvas shortcuts + 33 window/global shortcuts = 73
        Assert.Equal(73, manager.Shortcuts.Count);
        manager.Validate();
    }

    [Fact]
    public void ProcessKey_FileShortcuts_ExecutesExpectedShellCommands()
    {
        var mockDialog = new Moq.Mock<IDialogService>();
        mockDialog.Setup(d => d.ShowNewDocumentDialog())
            .Returns((64, 64, ColorMode.Monochrome, DocumentMode.Sprite));

        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel(dialogService: mockDialog.Object);
        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        // Ctrl+N creates a document
        Assert.Empty(shell.OpenDocuments);
        bool newHandled = manager.ProcessKey(Key.N, ModifierKeys.Control);
        Assert.True(newHandled);
        Assert.Single(shell.OpenDocuments);

        // Ctrl+W closes the active document
        bool closeHandled = manager.ProcessKey(Key.W, ModifierKeys.Control);
        Assert.True(closeHandled);
        Assert.Empty(shell.OpenDocuments);
    }

    [Fact]
    public void ProcessKey_DocumentScopedCommands_WhenNoDocumentOpen_CannotExecute()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        Assert.Null(shell.ActiveDocument);

        Assert.False(manager.ProcessKey(Key.Z, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.Y, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(manager.ProcessKey(Key.I, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.C, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.X, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.V, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.A, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.D, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.T, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.E, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.N, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(manager.ProcessKey(Key.J, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.Delete, ModifierKeys.Control | ModifierKeys.Shift));
    }

    [Fact]
    public void ProcessKey_SelectAllAndDeselect_ExecutesOnActiveDocument()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("64x64");
        var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        Assert.False(mvm.SelectionService.HasActiveSelection);

        // Ctrl+A -> Select All
        bool selectAllHandled = manager.ProcessKey(Key.A, ModifierKeys.Control);
        Assert.True(selectAllHandled);
        Assert.True(mvm.SelectionService.HasActiveSelection);

        // Ctrl+D -> Deselect
        bool deselectHandled = manager.ProcessKey(Key.D, ModifierKeys.Control);
        Assert.True(deselectHandled);
        Assert.False(mvm.SelectionService.HasActiveSelection);
    }

    [Fact]
    public void ProcessKey_DynamicTabSwitching_RoutesToCurrentlyActiveDocument()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("32x32");
        var doc1 = Assert.IsType<MainViewModel>(shell.ActiveDocument);

        shell.NewDocumentCommand.Execute("64x64");
        var doc2 = Assert.IsType<MainViewModel>(shell.ActiveDocument);

        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        // Active document is doc2
        Assert.Equal(doc2, shell.ActiveDocument);

        // Select all on doc2
        manager.ProcessKey(Key.A, ModifierKeys.Control);
        Assert.True(doc2.SelectionService.HasActiveSelection);
        Assert.False(doc1.SelectionService.HasActiveSelection);

        // Switch active document to doc1
        shell.ActiveDocument = doc1;
        Assert.Equal(doc1, shell.ActiveDocument);

        // Select all on doc1
        manager.ProcessKey(Key.A, ModifierKeys.Control);
        Assert.True(doc1.SelectionService.HasActiveSelection);

        // Deselect on doc1
        manager.ProcessKey(Key.D, ModifierKeys.Control);
        Assert.False(doc1.SelectionService.HasActiveSelection);
        // doc2 still has its active selection
        Assert.True(doc2.SelectionService.HasActiveSelection);

        // Switch back to doc2 and deselect
        shell.ActiveDocument = doc2;
        manager.ProcessKey(Key.D, ModifierKeys.Control);
        Assert.False(doc2.SelectionService.HasActiveSelection);
    }

    [Fact]
    public void ProcessKey_UndoAndRedo_DispatchesToActiveDocument()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("32x32");
        var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        // Initially no undo history
        Assert.False(manager.ProcessKey(Key.Z, ModifierKeys.Control));

        // Push state for undo
        mvm.SaveStateForUndo();
        Assert.True(mvm.CanUndo);

        // Ctrl+Z -> Undo
        bool undoHandled = manager.ProcessKey(Key.Z, ModifierKeys.Control);
        Assert.True(undoHandled);
        Assert.True(mvm.CanRedo);

        // Ctrl+Y -> Redo
        bool redoHandled = manager.ProcessKey(Key.Y, ModifierKeys.Control);
        Assert.True(redoHandled);
    }

    [Fact]
    public void ProcessKey_ActiveDocument_FontViewModel_RoutesUndoAndRedo()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        var fvm = new FontViewModel();
        shell.OpenDocuments.Add(fvm);
        shell.ActiveDocument = fvm;

        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        // FontViewModel UndoCommand CanExecute is always true
        bool undoHandled = manager.ProcessKey(Key.Z, ModifierKeys.Control);
        Assert.True(undoHandled);

        bool redoHandled = manager.ProcessKey(Key.Y, ModifierKeys.Control);
        Assert.True(redoHandled);
    }

    [Fact]
    public void ProcessKey_ZoomShortcuts_RouteToProvidedZoomCommands()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("32x32");

        int zoomInCount = 0;
        int zoomOutCount = 0;
        int zoomResetCount = 0;

        var zoomInCmd = new RelayCommand(() => zoomInCount++, () => shell.HasOpenDocument);
        var zoomOutCmd = new RelayCommand(() => zoomOutCount++, () => shell.HasOpenDocument);
        var zoomResetCmd = new RelayCommand(() => zoomResetCount++, () => shell.HasOpenDocument);

        WindowShortcutRegistrar.RegisterDefaultShortcuts(
            manager, shell, zoomInCmd, zoomOutCmd, zoomResetCmd);

        // 3 Zoom in shortcuts
        Assert.True(manager.ProcessKey(Key.OemPlus, ModifierKeys.Control));
        Assert.Equal(1, zoomInCount);

        Assert.True(manager.ProcessKey(Key.OemPlus, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.Equal(2, zoomInCount);

        Assert.True(manager.ProcessKey(Key.Add, ModifierKeys.Control));
        Assert.Equal(3, zoomInCount);

        // 2 Zoom out shortcuts
        Assert.True(manager.ProcessKey(Key.OemMinus, ModifierKeys.Control));
        Assert.Equal(1, zoomOutCount);

        Assert.True(manager.ProcessKey(Key.Subtract, ModifierKeys.Control));
        Assert.Equal(2, zoomOutCount);

        // 2 Zoom reset shortcuts
        Assert.True(manager.ProcessKey(Key.D0, ModifierKeys.Control));
        Assert.Equal(1, zoomResetCount);

        Assert.True(manager.ProcessKey(Key.NumPad0, ModifierKeys.Control));
        Assert.Equal(2, zoomResetCount);
    }

    [Fact]
    public void ProcessKey_DefaultZoomShortcuts_RespectsHasOpenDocument()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();

        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        var zoomInDef = manager.GetShortcut(WindowShortcutRegistrar.ActionViewZoomInOemPlus);
        Assert.NotNull(zoomInDef);

        // No open document -> CanExecute is false
        Assert.False(zoomInDef.Command.CanExecute(null));
        Assert.False(manager.ProcessKey(Key.OemPlus, ModifierKeys.Control));

        // Open a document -> CanExecute is true
        shell.NewDocumentCommand.Execute("32x32");
        Assert.True(zoomInDef.Command.CanExecute(null));
    }

    [Fact]
    public void ProcessKey_GlobalUtilities_ExecutesRefreshThemeAndOpenDocumentation()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();

        bool themeRefreshed = false;
        shell.ThemeChanged += (s, e) => themeRefreshed = true;

        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        // F5 -> RefreshTheme
        bool f5Handled = manager.ProcessKey(Key.F5, ModifierKeys.None);
        Assert.True(f5Handled);
        Assert.True(themeRefreshed);

        // F1 -> OpenDocumentation
        var f1Def = manager.GetShortcut(WindowShortcutRegistrar.ActionGlobalOpenDocumentation);
        Assert.NotNull(f1Def);
        Assert.Equal(ShortcutScope.Global, f1Def.Scope);
        Assert.True(f1Def.Command.CanExecute(null));
    }

    [Fact]
    public void ProcessKey_GlobalUtilities_ExecutesKeyboardShortcutsCheatSheet()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var mockDialog = new Moq.Mock<IDialogService>();
        bool dialogOpened = false;
        mockDialog.Setup(d => d.ShowKeyboardShortcutsDialog()).Callback(() => dialogOpened = true);

        var shell = E2E.E2ETestHelper.CreateTestShellViewModel(dialogService: mockDialog.Object);
        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        // Ctrl + / (Key.OemQuestion) -> OpenKeyboardShortcutsCommand
        bool slashHandled = manager.ProcessKey(Key.OemQuestion, ModifierKeys.Control);
        Assert.True(slashHandled);
        Assert.True(dialogOpened);

        dialogOpened = false;
        // Ctrl + / (Key.Divide on numpad) -> OpenKeyboardShortcutsCommand
        bool numPadSlashHandled = manager.ProcessKey(Key.Divide, ModifierKeys.Control);
        Assert.True(numPadSlashHandled);
        Assert.True(dialogOpened);
    }

    [Fact]
    public void ProcessKey_EditOperations_ClipboardAndLayerCommands_RouteCorrectly()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("32x32");
        var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        // Select all to enable copy and cut
        manager.ProcessKey(Key.A, ModifierKeys.Control);
        Assert.True(mvm.SelectionService.HasActiveSelection);

        // Ctrl+C -> Copy
        Assert.True(manager.ProcessKey(Key.C, ModifierKeys.Control));

        // Ctrl+X -> Cut
        Assert.True(manager.ProcessKey(Key.X, ModifierKeys.Control));

        // Ctrl+V -> Paste
        Assert.True(manager.ProcessKey(Key.V, ModifierKeys.Control));

        // Ctrl+T -> Transform (when active selection exists)
        Assert.True(manager.ProcessKey(Key.T, ModifierKeys.Control));

        // Add a second layer to enable MergeLayer
        mvm.AddLayerCommand.Execute(null);
        Assert.True(mvm.CanMergeLayers);

        // Ctrl+E -> MergeLayer
        Assert.True(manager.ProcessKey(Key.E, ModifierKeys.Control));
    }

    [Fact]
    public void ProcessKey_EditOperations_ReselectAndNewLayerFromSelection_RouteCorrectly()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("32x32");
        var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

        EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);
        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        // Draw pixel so selection is not empty
        mvm.SpriteState.Pixels[5 * 32 + 5] = true;

        mvm.SelectAllCommand.Execute(null);
        Assert.True(mvm.SelectionService.HasActiveSelection);

        // Deselect with Ctrl+D
        Assert.True(manager.ProcessKey(Key.D, ModifierKeys.Control));
        Assert.False(mvm.SelectionService.HasActiveSelection);

        // Reselect with Ctrl+Shift+D
        Assert.True(manager.ProcessKey(Key.D, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.True(mvm.SelectionService.HasActiveSelection);

        // New layer from selection with Ctrl+Shift+J
        int layerCount = mvm.SpriteState.Layers.Count;
        Assert.True(manager.ProcessKey(Key.J, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.Equal(layerCount + 1, mvm.SpriteState.Layers.Count);

        // Nudge selection with arrow keys on canvas
        int minX = mvm.SelectionService.MinX;
        Assert.True(manager.ProcessKey(Key.Right, ModifierKeys.None));
        Assert.Equal(minX + 1, mvm.SelectionService.MinX);
    }

    [Fact]
    public void ProcessKey_WindowScope_WhenFocusOnTextBox_SuppressesStandardTextEditingShortcuts()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
            shell.NewDocumentCommand.Execute("32x32");
            var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

            WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

            var textBox = new System.Windows.Controls.TextBox { Text = "Test content" };

            // Standard text editing shortcuts must NOT be intercepted by Window scope,
            // allowing the TextBox to process them natively
            Assert.False(manager.ProcessKey(Key.A, ModifierKeys.Control, focusedElement: textBox));
            Assert.False(manager.ProcessKey(Key.C, ModifierKeys.Control, focusedElement: textBox));
            Assert.False(manager.ProcessKey(Key.V, ModifierKeys.Control, focusedElement: textBox));
            Assert.False(manager.ProcessKey(Key.X, ModifierKeys.Control, focusedElement: textBox));
            Assert.False(manager.ProcessKey(Key.Z, ModifierKeys.Control, focusedElement: textBox));
            Assert.False(manager.ProcessKey(Key.Y, ModifierKeys.Control, focusedElement: textBox));

            // Selection on canvas was not modified by the bypassed Ctrl+A
            Assert.False(mvm.SelectionService.HasActiveSelection);

            // Non-text-editing Window shortcuts (Save, New, CloseTab) still execute when focused on TextBox
            Assert.True(manager.ProcessKey(Key.S, ModifierKeys.Control, focusedElement: textBox));
            Assert.True(manager.ProcessKey(Key.W, ModifierKeys.Control, focusedElement: textBox));
        });
    }

    [Fact]
    public void ProcessKey_WindowScope_WhenFocusInSecondaryWindow_BypassesWindowAndCanvasShortcuts()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
            shell.NewDocumentCommand.Execute("32x32");

            WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);
            EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

            var mainWin = new Window();
            var secondaryWin = new Window();
            var secondaryButton = new System.Windows.Controls.Button();
            secondaryWin.Content = secondaryButton;

            var prevMainWindow = Application.Current?.MainWindow;
            try
            {
                if (Application.Current != null)
                {
                    Application.Current.MainWindow = mainWin;
                }

                // In secondary window, Window scope shortcuts (e.g. Save, CloseTab) are bypassed
                Assert.False(manager.ProcessKey(Key.S, ModifierKeys.Control, focusedElement: secondaryButton));
                Assert.False(manager.ProcessKey(Key.W, ModifierKeys.Control, focusedElement: secondaryButton));

                // Canvas scope shortcuts (e.g. Pencil tool B) are also bypassed
                Assert.False(manager.ProcessKey(Key.B, ModifierKeys.None, focusedElement: secondaryButton));

                // Global utilities (F1) still execute application-wide, while Window-scoped F5 is bypassed
                Assert.True(manager.ProcessKey(Key.F1, ModifierKeys.None, focusedElement: secondaryButton));
                Assert.False(manager.ProcessKey(Key.F5, ModifierKeys.None, focusedElement: secondaryButton));
            }
            finally
            {
                if (Application.Current != null)
                {
                    Application.Current.MainWindow = prevMainWindow;
                }
            }
        });
    }

    [Fact]
    public void ProcessKey_CanvasTextEditing_SuppressesToolsAndEditCommands_AllowsUndo()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("32x32");
        var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);
        EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        // Enter canvas text editing mode
        mvm.IsTextEditing = true;
        mvm.CurrentTool = Hexprite.Core.ToolMode.Text;

        // 1. Single letter tool shortcuts (e.g. B for Pencil, E for Eraser) must be suppressed so user can type letters
        bool pencilHandled = manager.ProcessKey(Key.B, ModifierKeys.None);
        Assert.False(pencilHandled);
        Assert.Equal(Hexprite.Core.ToolMode.Text, mvm.CurrentTool);

        bool eraserHandled = manager.ProcessKey(Key.E, ModifierKeys.None);
        Assert.False(eraserHandled);
        Assert.Equal(Hexprite.Core.ToolMode.Text, mvm.CurrentTool);

        // 2. Standard text editing keys (Ctrl+V, Ctrl+A, Ctrl+C, Ctrl+X) are bypassed in Window scope so text tool handles them
        Assert.False(manager.ProcessKey(Key.V, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.A, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.C, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.X, ModifierKeys.Control));

        // 3. Destructive document edit commands (SelectAll, Deselect, Transform, MergeLayer) CanExecute must be false
        Assert.False(manager.ProcessKey(Key.D, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.T, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.E, ModifierKeys.Control));

        // 4. Ctrl+Z (Undo) is allowed and cancels text editing
        mvm.SaveStateForUndo();
        Assert.True(mvm.CanUndo);
        bool undoHandled = manager.ProcessKey(Key.Z, ModifierKeys.Control);
        Assert.True(undoHandled);
        Assert.False(mvm.IsTextEditing);
    }

    [Fact]
    public void ProcessInput_SecondaryWindow_WhenFocusedElementNull_FallsBackToKeyArgsSource()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
            shell.NewDocumentCommand.Execute("32x32");

            WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

            var mainWin = new Window();
            var secondaryWin = new Window();
            var prevMainWindow = Application.Current?.MainWindow;

            try
            {
                if (Application.Current != null)
                {
                    Application.Current.MainWindow = mainWin;
                }

                // Simulate key event originating from secondaryWin where Keyboard.FocusedElement is null
                var source = new TestPresentationSource();
                var keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.S)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent,
                    Handled = false,
                    Source = secondaryWin
                };

                bool handled = manager.ProcessInput(keyArgs, focusedElementOverride: null, modifiersOverride: ModifierKeys.Control);

                // Because keyArgs.Source is secondaryWin, window shortcuts targeting MainWindow must NOT execute
                Assert.False(handled);
                Assert.False(keyArgs.Handled);
            }
            finally
            {
                if (Application.Current != null)
                {
                    Application.Current.MainWindow = prevMainWindow;
                }
            }
        });
    }

    [Fact]
    public void MainWindow_InputBindings_AreCompletelyRemoved()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            if (Application.Current != null && Application.Current.Resources.MergedDictionaries.Count == 0)
            {
                Application.Current.Resources.MergedDictionaries.Add(
                    new ResourceDictionary { Source = new Uri("/Hexprite;component/Themes/Dim.xaml", UriKind.RelativeOrAbsolute) });
                Application.Current.Resources.MergedDictionaries.Add(
                    new ResourceDictionary { Source = new Uri("/Hexprite;component/Themes/Styles.xaml", UriKind.RelativeOrAbsolute) });
            }

            var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
            var mainWindow = new MainWindow(shell);

            Assert.NotNull(mainWindow);
            Assert.Empty(mainWindow.InputBindings);
        });
    }

    [Fact]
    public void ProcessKey_ActiveDocument_AssetPackViewModel_RoutesUndoAndRedo()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        var apvm = new AssetPackViewModel();
        shell.OpenDocuments.Add(apvm);
        shell.ActiveDocument = apvm;

        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        // Before changes, CanUndo is false
        Assert.False(apvm.MatrixViewModel.CanUndo);
        Assert.False(manager.ProcessKey(Key.Z, ModifierKeys.Control));

        // Push state so CanUndo is true
        apvm.MatrixViewModel.AutoBalance();
        Assert.True(apvm.MatrixViewModel.CanUndo);

        // Ctrl+Z -> Undo
        bool undoHandled = manager.ProcessKey(Key.Z, ModifierKeys.Control);
        Assert.True(undoHandled);
        Assert.True(apvm.MatrixViewModel.CanRedo);

        // Ctrl+Y -> Redo
        bool redoHandled = manager.ProcessKey(Key.Y, ModifierKeys.Control);
        Assert.True(redoHandled);

        // Canvas-specific commands return false on AssetPackViewModel
        Assert.False(manager.ProcessKey(Key.A, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.C, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.V, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.X, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.D, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.T, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.E, ModifierKeys.Control));
    }

    [Fact]
    public void IsInSecondaryWindow_WhenFocusedElementNullAndSecondaryWindowActive_BypassesWindowShortcuts()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
            shell.NewDocumentCommand.Execute("32x32");

            WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

            var mainWin = new Window();
            var secondaryWin = new Window();
            var prevMainWindow = Application.Current?.MainWindow;

            try
            {
                if (Application.Current != null)
                {
                    Application.Current.MainWindow = mainWin;
                }

                // Simulate state where secondaryWin is active and focusedElement is null
                // (e.g. clicking non-focusable border / background of a dialog)
                mainWin.Visibility = Visibility.Hidden;
                secondaryWin.Show();
                secondaryWin.Activate();

                // When secondary window is active and focusedElement is null,
                // MainWindow window-scoped shortcuts must be bypassed
                bool handled = manager.ProcessKey(Key.S, ModifierKeys.Control, focusedElement: null);
                Assert.False(handled);

                bool closeHandled = manager.ProcessKey(Key.W, ModifierKeys.Control, focusedElement: null);
                Assert.False(closeHandled);

                // Global utilities still execute
                bool f1Handled = manager.ProcessKey(Key.F1, ModifierKeys.None, focusedElement: null);
                Assert.True(f1Handled);
            }
            finally
            {
                secondaryWin.Close();
                mainWin.Close();
                if (Application.Current != null)
                {
                    Application.Current.MainWindow = prevMainWindow;
                }
            }
        });
    }

    [Fact]
    public void CreateActiveDocumentCommand_Execute_GuardedAgainstCanvasTextEditing()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("32x32");
        var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        mvm.IsTextEditing = true;
        mvm.CurrentTool = Hexprite.Core.ToolMode.Text;

        var selectAllDef = manager.GetShortcut(WindowShortcutRegistrar.ActionEditSelectAll);
        Assert.NotNull(selectAllDef);

        // CanExecute returns false during canvas text editing
        Assert.False(selectAllDef.Command.CanExecute(null));

        // Even if Execute is called directly, text editing must not be corrupted
        selectAllDef.Command.Execute(null);
        Assert.False(mvm.SelectionService.HasActiveSelection);
        Assert.True(mvm.IsTextEditing);
    }

    #endregion

    #region Refined Shortcuts: Tools, Selection Deletion, Redo, Invert, Layer Management

    [Fact]
    public void ProcessKey_CanvasSelectionDeletion_DeleteAndBackspace_DeletesSelectionOnActiveDocument()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("32x32");
        var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

        EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        // 1. Delete key with active pixel selection
        mvm.SpriteState.Pixels[0] = true;
        mvm.SelectAllCommand.Execute(null);
        Assert.True(mvm.SelectionService.HasActiveSelection);

        bool deleteHandled = manager.ProcessKey(Key.Delete, ModifierKeys.None);
        Assert.True(deleteHandled);
        Assert.False(mvm.SelectionService.HasActiveSelection);
        Assert.False(mvm.SpriteState.Pixels[0]);

        // 2. Backspace key with active pixel selection
        mvm.SpriteState.Pixels[1] = true;
        mvm.SelectAllCommand.Execute(null);
        Assert.True(mvm.SelectionService.HasActiveSelection);

        bool backHandled = manager.ProcessKey(Key.Back, ModifierKeys.None);
        Assert.True(backHandled);
        Assert.False(mvm.SelectionService.HasActiveSelection);
        Assert.False(mvm.SpriteState.Pixels[1]);
    }

    [Fact]
    public void ProcessKey_CanvasSelectionDeletion_WhenNoActiveSelection_ReturnsFalse()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("32x32");
        var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

        EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        Assert.False(mvm.SelectionService.HasActiveSelection);

        Assert.False(manager.ProcessKey(Key.Delete, ModifierKeys.None));
        Assert.False(manager.ProcessKey(Key.Back, ModifierKeys.None));
    }

    [Fact]
    public void ProcessInput_CanvasSelectionDeletion_WhenTextBoxOrComboBoxFocused_IsSuppressed()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
            shell.NewDocumentCommand.Execute("32x32");
            var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

            EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);
            WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

            mvm.SelectAllCommand.Execute(null);
            Assert.True(mvm.SelectionService.HasActiveSelection);

            var textBox = new TextBox { Text = "#FFFFFF" };
            var source = new TestPresentationSource();

            // Pressing Delete inside TextBox must not delete canvas selection
            var delArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Delete)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };
            bool delHandled = manager.ProcessInput(delArgs, focusedElementOverride: textBox, modifiersOverride: ModifierKeys.None);
            Assert.False(delHandled);
            Assert.False(delArgs.Handled);
            Assert.True(mvm.SelectionService.HasActiveSelection);

            // Pressing Backspace inside TextBox must not delete canvas selection
            var backArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Back)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };
            bool backHandled = manager.ProcessInput(backArgs, focusedElementOverride: textBox, modifiersOverride: ModifierKeys.None);
            Assert.False(backHandled);
            Assert.False(backArgs.Handled);
            Assert.True(mvm.SelectionService.HasActiveSelection);

            // Pressing Delete inside editable ComboBox must not delete canvas selection
            var comboBox = new ComboBox { IsEditable = true };
            var cbDelArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Delete)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };
            bool cbDelHandled = manager.ProcessInput(cbDelArgs, focusedElementOverride: comboBox, modifiersOverride: ModifierKeys.None);
            Assert.False(cbDelHandled);
            Assert.False(cbDelArgs.Handled);
            Assert.True(mvm.SelectionService.HasActiveSelection);

            // Suppressed Redo (Ctrl+Shift+Z) inside TextBox
            var redoArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Z)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };
            bool redoHandled = manager.ProcessInput(redoArgs, focusedElementOverride: textBox, modifiersOverride: ModifierKeys.Control | ModifierKeys.Shift);
            Assert.False(redoHandled);
            Assert.False(redoArgs.Handled);
        });
    }

    [Fact]
    public void ProcessInput_CanvasSelectionDeletion_WhenCanvasFocused_ExecutesAndMarksHandled()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
            shell.NewDocumentCommand.Execute("32x32");
            var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

            EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

            mvm.SelectAllCommand.Execute(null);
            Assert.True(mvm.SelectionService.HasActiveSelection);

            var canvas = new Canvas();
            var source = new TestPresentationSource();
            var keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Delete)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };

            bool handled = manager.ProcessInput(keyArgs, focusedElementOverride: canvas, modifiersOverride: ModifierKeys.None);

            Assert.True(handled);
            Assert.True(keyArgs.Handled);
            Assert.False(mvm.SelectionService.HasActiveSelection);
        });
    }

    [Fact]
    public void ProcessKey_CanvasTextEditing_SuppressesNewToolAndSelectionShortcuts()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("32x32");
        var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

        EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);
        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        mvm.SelectAllCommand.Execute(null);
        Assert.True(mvm.SelectionService.HasActiveSelection);

        mvm.IsTextEditing = true;
        mvm.CurrentTool = Hexprite.Core.ToolMode.Text;

        // Tool shortcuts G and Shift+M must be suppressed so typing text works
        Assert.False(manager.ProcessKey(Key.G, ModifierKeys.None));
        Assert.False(manager.ProcessKey(Key.M, ModifierKeys.Shift));
        Assert.Equal(Hexprite.Core.ToolMode.Text, mvm.CurrentTool);

        // Delete and Backspace must be suppressed from deleting canvas selection
        Assert.False(manager.ProcessKey(Key.Delete, ModifierKeys.None));
        Assert.False(manager.ProcessKey(Key.Back, ModifierKeys.None));
        Assert.True(mvm.SelectionService.HasActiveSelection);

        // Destructive edit commands Invert and Layer commands are blocked during text editing
        Assert.False(manager.ProcessKey(Key.I, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.N, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(manager.ProcessKey(Key.J, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.Delete, ModifierKeys.Control | ModifierKeys.Shift));
    }

    [Fact]
    public void ProcessKey_UniversalRedo_CtrlShiftZ_DispatchesToActiveDocument()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("32x32");
        var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        // MainViewModel: Save state, undo with Ctrl+Z, redo with Ctrl+Shift+Z
        mvm.SaveStateForUndo();
        Assert.True(mvm.CanUndo);
        Assert.True(manager.ProcessKey(Key.Z, ModifierKeys.Control));
        Assert.True(mvm.CanRedo);

        bool redoHandled = manager.ProcessKey(Key.Z, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.True(redoHandled);
        Assert.False(mvm.CanRedo);

        // FontViewModel: Redo via Ctrl+Shift+Z
        var fvm = new FontViewModel();
        shell.OpenDocuments.Add(fvm);
        shell.ActiveDocument = fvm;
        Assert.True(manager.ProcessKey(Key.Z, ModifierKeys.Control | ModifierKeys.Shift));

        // AssetPackViewModel: Redo via Ctrl+Shift+Z
        var apvm = new AssetPackViewModel();
        shell.OpenDocuments.Add(apvm);
        shell.ActiveDocument = apvm;

        apvm.MatrixViewModel.AutoBalance();
        Assert.True(apvm.MatrixViewModel.CanUndo);
        Assert.True(manager.ProcessKey(Key.Z, ModifierKeys.Control));
        Assert.True(apvm.MatrixViewModel.CanRedo);

        bool apvmRedoHandled = manager.ProcessKey(Key.Z, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.True(apvmRedoHandled);
    }

    [Fact]
    public void ProcessKey_InvertCommand_CtrlI_ExecutesOnActiveDocument()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("32x32");
        var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        // Active document pixel initially false
        Assert.False(mvm.SpriteState.Pixels[0]);

        // Ctrl+I inverts canvas
        bool handled = manager.ProcessKey(Key.I, ModifierKeys.Control);
        Assert.True(handled);
        Assert.True(mvm.SpriteState.Pixels[0]);

        // Non-canvas tab (FontViewModel) cannot execute Invert
        var fvm = new FontViewModel();
        shell.OpenDocuments.Add(fvm);
        shell.ActiveDocument = fvm;

        Assert.False(manager.ProcessKey(Key.I, ModifierKeys.Control));
    }

    [Fact]
    public void ProcessKey_LayerManagement_AddDuplicateDelete_ExecutesOnActiveDocument()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("32x32");
        var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        // Initially 1 layer
        Assert.Single(mvm.SpriteState.Layers);

        // Ctrl+Shift+Delete (Delete Layer) cannot execute when only 1 layer exists
        bool deleteOnlyLayer = manager.ProcessKey(Key.Delete, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.False(deleteOnlyLayer);
        Assert.Single(mvm.SpriteState.Layers);

        // Ctrl+Shift+N: Add Layer
        bool addHandled = manager.ProcessKey(Key.N, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.True(addHandled);
        Assert.Equal(2, mvm.SpriteState.Layers.Count);

        // Ctrl+J: Duplicate Layer
        bool duplicateHandled = manager.ProcessKey(Key.J, ModifierKeys.Control);
        Assert.True(duplicateHandled);
        Assert.Equal(3, mvm.SpriteState.Layers.Count);

        // Ctrl+Shift+Delete: Delete Layer
        bool deleteHandled = manager.ProcessKey(Key.Delete, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.True(deleteHandled);
        Assert.Equal(2, mvm.SpriteState.Layers.Count);
    }

    [Fact]
    public void ProcessKey_LayerManagement_WhenNonMainViewModel_CannotExecute()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        var fvm = new FontViewModel();
        shell.OpenDocuments.Add(fvm);
        shell.ActiveDocument = fvm;

        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        Assert.False(manager.ProcessKey(Key.N, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(manager.ProcessKey(Key.J, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.Delete, ModifierKeys.Control | ModifierKeys.Shift));
    }

    [Fact]
    public void ProcessKey_RefinedShortcuts_BypassedInSecondaryWindow()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
            shell.NewDocumentCommand.Execute("32x32");

            EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);
            WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

            var mainWin = new Window();
            var secondaryWin = new Window();
            var secondaryButton = new Button();
            secondaryWin.Content = secondaryButton;

            var prevMainWindow = Application.Current?.MainWindow;
            try
            {
                if (Application.Current != null)
                {
                    Application.Current.MainWindow = mainWin;
                }

                // In secondary window, all editor canvas and window shortcuts are bypassed
                Assert.False(manager.ProcessKey(Key.G, ModifierKeys.None, focusedElement: secondaryButton));
                Assert.False(manager.ProcessKey(Key.M, ModifierKeys.Shift, focusedElement: secondaryButton));
                Assert.False(manager.ProcessKey(Key.Delete, ModifierKeys.None, focusedElement: secondaryButton));
                Assert.False(manager.ProcessKey(Key.Back, ModifierKeys.None, focusedElement: secondaryButton));
                Assert.False(manager.ProcessKey(Key.Z, ModifierKeys.Control | ModifierKeys.Shift, focusedElement: secondaryButton));
                Assert.False(manager.ProcessKey(Key.I, ModifierKeys.Control, focusedElement: secondaryButton));
                Assert.False(manager.ProcessKey(Key.N, ModifierKeys.Control | ModifierKeys.Shift, focusedElement: secondaryButton));
                Assert.False(manager.ProcessKey(Key.J, ModifierKeys.Control, focusedElement: secondaryButton));
                Assert.False(manager.ProcessKey(Key.Delete, ModifierKeys.Control | ModifierKeys.Shift, focusedElement: secondaryButton));
            }
            finally
            {
                if (Application.Current != null)
                {
                    Application.Current.MainWindow = prevMainWindow;
                }
            }
        });
    }

    [Fact]
    public void ProcessKey_WhenLayerModificationProhibited_SelectionDeletionAndInvertDoNotExecute()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("32x32");
        var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

        EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);
        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        mvm.SpriteState.Pixels[0] = true;
        mvm.SelectAllCommand.Execute(null);
        Assert.True(mvm.SelectionService.HasActiveSelection);

        // 1. When active layer is locked, modification is prohibited
        mvm.SpriteState.Layers[mvm.SpriteState.ActiveLayerIndex].IsLocked = true;
        Assert.True(mvm.IsActiveLayerLocked);
        Assert.False(mvm.CanModifyActiveLayer);

        Assert.False(manager.ProcessKey(Key.Delete, ModifierKeys.None));
        Assert.False(manager.ProcessKey(Key.Back, ModifierKeys.None));
        Assert.False(manager.ProcessKey(Key.I, ModifierKeys.Control));
        Assert.True(mvm.SelectionService.HasActiveSelection);
        Assert.True(mvm.SpriteState.Pixels[0]);

        // 2. Unlock, then hide active layer — modification is prohibited
        mvm.SpriteState.Layers[mvm.SpriteState.ActiveLayerIndex].IsLocked = false;
        mvm.SpriteState.Layers[mvm.SpriteState.ActiveLayerIndex].IsVisible = false;
        Assert.False(mvm.IsActiveLayerVisible);
        Assert.False(mvm.CanModifyActiveLayer);

        Assert.False(manager.ProcessKey(Key.Delete, ModifierKeys.None));
        Assert.False(manager.ProcessKey(Key.Back, ModifierKeys.None));
        Assert.False(manager.ProcessKey(Key.I, ModifierKeys.Control));
        Assert.True(mvm.SelectionService.HasActiveSelection);
        Assert.True(mvm.SpriteState.Pixels[0]);

        // 3. Make visible again — operations should now succeed
        mvm.SpriteState.Layers[mvm.SpriteState.ActiveLayerIndex].IsVisible = true;
        Assert.True(mvm.CanModifyActiveLayer);

        Assert.True(manager.ProcessKey(Key.Delete, ModifierKeys.None));
        Assert.False(mvm.SelectionService.HasActiveSelection);
        Assert.False(mvm.SpriteState.Pixels[0]);
    }

    [Fact]
    public void ProcessKey_UniversalRedo_CtrlShiftZ_WhenNoRedoAvailable_ReturnsFalse()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("32x32");
        var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        Assert.False(mvm.CanRedo);
        Assert.False(manager.ProcessKey(Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
    }

    [Fact]
    public void RegisterDefaultShortcuts_CalledMultipleTimes_IsIdempotentWithoutCollision()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();

        // First registration
        EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);
        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        // Second registration must not throw duplicate key or action ID exception
        var exCanvas = Record.Exception(() => EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell));
        Assert.Null(exCanvas);

        var exWindow = Record.Exception(() => WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell));
        Assert.Null(exWindow);

        manager.Validate();
    }

    [Fact]
    public void ProcessInput_CanvasSelectionDeletion_WhenPasswordBoxFocused_IsSuppressed()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
            shell.NewDocumentCommand.Execute("32x32");
            var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

            EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

            mvm.SelectAllCommand.Execute(null);
            Assert.True(mvm.SelectionService.HasActiveSelection);

            var passwordBox = new PasswordBox();
            var source = new TestPresentationSource();

            var delArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Delete)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };
            bool delHandled = manager.ProcessInput(delArgs, focusedElementOverride: passwordBox, modifiersOverride: ModifierKeys.None);
            Assert.False(delHandled);
            Assert.False(delArgs.Handled);
            Assert.True(mvm.SelectionService.HasActiveSelection);

            var backArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Back)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };
            bool backHandled = manager.ProcessInput(backArgs, focusedElementOverride: passwordBox, modifiersOverride: ModifierKeys.None);
            Assert.False(backHandled);
            Assert.False(backArgs.Handled);
            Assert.True(mvm.SelectionService.HasActiveSelection);
        });
    }

    [Fact]
    public void ProcessKey_LayerManagement_PlainDeleteWithoutModifiers_DoesNotDeleteLayer()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("32x32");
        var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

        EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);
        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        // Add a layer so we have 2 layers
        mvm.AddLayerCommand.Execute(null);
        Assert.Equal(2, mvm.SpriteState.Layers.Count);
        Assert.True(mvm.CanDeleteSelectedLayers);

        // Without an active canvas selection, plain Delete must return false and NOT delete the layer
        Assert.False(mvm.SelectionService.HasActiveSelection);
        bool deleteHandled = manager.ProcessKey(Key.Delete, ModifierKeys.None);
        Assert.False(deleteHandled);
        Assert.Equal(2, mvm.SpriteState.Layers.Count);

        // Now with active selection on canvas, plain Delete deletes canvas selection, NOT the layer
        mvm.SelectAllCommand.Execute(null);
        Assert.True(mvm.SelectionService.HasActiveSelection);

        bool canvasDeleteHandled = manager.ProcessKey(Key.Delete, ModifierKeys.None);
        Assert.True(canvasDeleteHandled);
        Assert.False(mvm.SelectionService.HasActiveSelection);
        Assert.Equal(2, mvm.SpriteState.Layers.Count); // Layer count still 2!

        // Standard layer deletion requires Ctrl+Shift+Delete
        bool layerDeleteHandled = manager.ProcessKey(Key.Delete, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.True(layerDeleteHandled);
        Assert.Single(mvm.SpriteState.Layers); // Layer count is now 1
    }

    private static void EnsureThemeResourcesLoaded()
    {
        if (Application.Current != null && Application.Current.Resources.MergedDictionaries.Count == 0)
        {
            Application.Current.Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = new Uri("/Hexprite;component/Themes/Dim.xaml", UriKind.RelativeOrAbsolute) });
            Application.Current.Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = new Uri("/Hexprite;component/Themes/Styles.xaml", UriKind.RelativeOrAbsolute) });
        }
    }

    [Fact]
    public void LayersPanel_LayerListPreviewKeyDown_DoesNotDeleteLayerOnPlainDelete()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            EnsureThemeResourcesLoaded();
            var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
            shell.NewDocumentCommand.Execute("32x32");
            var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

            mvm.AddLayerCommand.Execute(null);
            Assert.Equal(2, mvm.SpriteState.Layers.Count);

            var panel = new LayersPanel
            {
                DataContext = mvm
            };

            var source = new TestPresentationSource();
            var delArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Delete)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };

            // Raise PreviewKeyDown on the LayerList inside panel
            panel.LayerList.RaiseEvent(delArgs);

            // Layer count must remain 2 (plain Delete must NOT delete the layer)
            Assert.Equal(2, mvm.SpriteState.Layers.Count);
            Assert.False(delArgs.Handled);
        });
    }

    [Fact]
    public void LayersPanel_LayerListPreviewKeyDown_WhenSourceIsTextBox_DoesNotInterceptKeys()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            EnsureThemeResourcesLoaded();
            var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
            shell.NewDocumentCommand.Execute("32x32");
            var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

            mvm.AddLayerCommand.Execute(null);
            Assert.Equal(2, mvm.SpriteState.Layers.Count);

            var panel = new LayersPanel
            {
                DataContext = mvm
            };

            // Detach ItemsSource so we can directly add a TextBox item to the ListBox visual tree
            panel.LayerList.ItemsSource = null;
            var renameTextBox = new TextBox { Text = "Layer 2" };
            panel.LayerList.Items.Add(renameTextBox);

            var source = new TestPresentationSource();
            var delArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Delete)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Source = renameTextBox,
                Handled = false
            };

            // Raise PreviewKeyDown on the renameTextBox (tunnels through LayerList)
            renameTextBox.RaiseEvent(delArgs);

            // Layer count must remain 2, event must not be marked handled by LayerList
            Assert.Equal(2, mvm.SpriteState.Layers.Count);
            Assert.False(delArgs.Handled);
        });
    }

    [Fact]
    public void EditorCanvasShortcutRegistrar_WithoutWindowRegistrar_CanvasTextEditingSuppressesShortcuts()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("32x32");
        var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

        // ONLY register canvas shortcuts (without calling WindowShortcutRegistrar)
        EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        Assert.NotNull(manager.ActiveTextEditingPredicate);

        mvm.SelectAllCommand.Execute(null);
        Assert.True(mvm.SelectionService.HasActiveSelection);

        mvm.IsTextEditing = true;
        mvm.CurrentTool = Hexprite.Core.ToolMode.Text;

        // When text editing, tool switching and selection deletion must be suppressed
        Assert.False(manager.ProcessKey(Key.G, ModifierKeys.None));
        Assert.False(manager.ProcessKey(Key.M, ModifierKeys.Shift));
        Assert.Equal(Hexprite.Core.ToolMode.Text, mvm.CurrentTool);

        Assert.False(manager.ProcessKey(Key.Delete, ModifierKeys.None));
        Assert.False(manager.ProcessKey(Key.Back, ModifierKeys.None));
        Assert.True(mvm.SelectionService.HasActiveSelection);
    }

    [Fact]
    public void ProcessKey_NewCanvasDialog_AllShortcutsBypassedAndNotHijacked()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            EnsureThemeResourcesLoaded();
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
            shell.NewDocumentCommand.Execute("32x32");
            var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

            EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);
            WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

            var mainWindow = new Window();
            var prevMainWindow = Application.Current?.MainWindow;
            try
            {
                if (Application.Current != null)
                {
                    Application.Current.MainWindow = mainWindow;
                }

                var dialog = new NewCanvasDialog();

                // When typing in TxtWidth in NewCanvasDialog, no canvas or window shortcuts may be hijacked
                Assert.False(manager.ProcessKey(Key.G, ModifierKeys.None, focusedElement: dialog.TxtWidth));
                Assert.False(manager.ProcessKey(Key.M, ModifierKeys.Shift, focusedElement: dialog.TxtWidth));
                Assert.False(manager.ProcessKey(Key.Delete, ModifierKeys.None, focusedElement: dialog.TxtWidth));
                Assert.False(manager.ProcessKey(Key.Back, ModifierKeys.None, focusedElement: dialog.TxtWidth));
                Assert.False(manager.ProcessKey(Key.Z, ModifierKeys.Control | ModifierKeys.Shift, focusedElement: dialog.TxtWidth));
                Assert.False(manager.ProcessKey(Key.I, ModifierKeys.Control, focusedElement: dialog.TxtWidth));
                Assert.False(manager.ProcessKey(Key.N, ModifierKeys.Control | ModifierKeys.Shift, focusedElement: dialog.TxtWidth));
                Assert.False(manager.ProcessKey(Key.J, ModifierKeys.Control, focusedElement: dialog.TxtWidth));
                Assert.False(manager.ProcessKey(Key.Delete, ModifierKeys.Control | ModifierKeys.Shift, focusedElement: dialog.TxtWidth));

                dialog.Close();
            }
            finally
            {
                if (Application.Current != null)
                {
                    Application.Current.MainWindow = prevMainWindow;
                }
            }
        });
    }

    [Fact]
    public void ProcessKey_WhenActiveDocumentIsNull_DocumentAndLayerShortcutsReturnFalseWithoutThrowing()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        Assert.Null(shell.ActiveDocument);

        EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);
        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        // Tool selection without active document does not throw
        var exG = Record.Exception(() => manager.ProcessKey(Key.G, ModifierKeys.None));
        Assert.Null(exG);
        var exM = Record.Exception(() => manager.ProcessKey(Key.M, ModifierKeys.Shift));
        Assert.Null(exM);

        // All document, selection, and layer shortcuts must return false gracefully when no active document exists
        Assert.False(manager.ProcessKey(Key.Delete, ModifierKeys.None));
        Assert.False(manager.ProcessKey(Key.Back, ModifierKeys.None));
        Assert.False(manager.ProcessKey(Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(manager.ProcessKey(Key.I, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.N, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(manager.ProcessKey(Key.J, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.Delete, ModifierKeys.Control | ModifierKeys.Shift));
    }

    [Fact]
    public void ProcessInput_CanvasSelectionDeletion_WhenRichTextBoxFocused_IsSuppressed()
    {
        WpfTestHelper.RunOnSta(() =>
        {
            var manager = new HexpriteShortcutManager(registerTracerBullet: false);
            var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
            shell.NewDocumentCommand.Execute("32x32");
            var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

            EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

            mvm.SelectAllCommand.Execute(null);
            Assert.True(mvm.SelectionService.HasActiveSelection);

            var richTextBox = new RichTextBox();
            var source = new TestPresentationSource();

            var delArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Delete)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };
            bool delHandled = manager.ProcessInput(delArgs, focusedElementOverride: richTextBox, modifiersOverride: ModifierKeys.None);
            Assert.False(delHandled);
            Assert.False(delArgs.Handled);
            Assert.True(mvm.SelectionService.HasActiveSelection);

            var backArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Back)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Handled = false
            };
            bool backHandled = manager.ProcessInput(backArgs, focusedElementOverride: richTextBox, modifiersOverride: ModifierKeys.None);
            Assert.False(backHandled);
            Assert.False(backArgs.Handled);
            Assert.True(mvm.SelectionService.HasActiveSelection);
        });
    }

    #endregion

    #region Hardening & Refinement Tests

    [Fact]
    public void ProcessKey_TimelineFocus_BypassesCanvasDeleteSelectionAndWindowSelectAll()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("64x64");
        var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

        // Enable animation and create canvas selection
        mvm.IsAnimationEnabled = true;
        Assert.NotNull(mvm.SpriteState);
        mvm.SpriteState.Pixels[0] = true;
        mvm.SelectAllCommand.Execute(null);
        Assert.True(mvm.SelectionService.HasActiveSelection);

        EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);
        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        // When timeline has focus:
        bool timelineFocused = true;
        manager.TimelineFocusPredicate = _ => timelineFocused;

        // 1. Delete and Back are bypassed so timeline frame deletion receives them
        bool delHandled = manager.ProcessKey(Key.Delete, ModifierKeys.None);
        Assert.False(delHandled);
        Assert.True(mvm.SelectionService.HasActiveSelection);

        bool backHandled = manager.ProcessKey(Key.Back, ModifierKeys.None);
        Assert.False(backHandled);
        Assert.True(mvm.SelectionService.HasActiveSelection);

        // 2. Ctrl+A is bypassed so timeline can select all frames
        bool selectAllHandled = manager.ProcessKey(Key.A, ModifierKeys.Control);
        Assert.False(selectAllHandled);

        // 3. Other shortcuts (like Space for animation playback or Ctrl+N for new doc) continue to work
        bool spaceHandled = manager.ProcessKey(Key.Space, ModifierKeys.None);
        Assert.True(spaceHandled);

        bool newDocHandled = manager.ProcessKey(Key.N, ModifierKeys.Control);
        Assert.True(newDocHandled);

        // When timeline does NOT have focus:
        timelineFocused = false;

        // Delete deletes canvas selection
        delHandled = manager.ProcessKey(Key.Delete, ModifierKeys.None);
        Assert.True(delHandled);
        Assert.False(mvm.SelectionService.HasActiveSelection);

        // Ctrl+A selects all pixels on canvas
        selectAllHandled = manager.ProcessKey(Key.A, ModifierKeys.Control);
        Assert.True(selectAllHandled);
        Assert.True(mvm.SelectionService.HasActiveSelection);
    }

    [Fact]
    public void ProcessKey_ZoomCommands_DisabledWhenFontEditorActive_EnabledWhenMainViewModelActive()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("64x64");

        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        // When MainViewModel is active:
        Assert.IsType<MainViewModel>(shell.ActiveDocument);
        Assert.True(manager.ProcessKey(Key.OemPlus, ModifierKeys.Control));
        Assert.True(manager.ProcessKey(Key.OemMinus, ModifierKeys.Control));
        Assert.True(manager.ProcessKey(Key.D0, ModifierKeys.Control));

        // When FontViewModel is active:
        var fvm = new FontViewModel();
        shell.OpenDocuments.Add(fvm);
        shell.ActiveDocument = fvm;

        Assert.False(manager.ProcessKey(Key.OemPlus, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.OemMinus, ModifierKeys.Control));
        Assert.False(manager.ProcessKey(Key.D0, ModifierKeys.Control));
    }

    [Fact]
    public void ProcessKey_AltN_ExecutesAddFrameCommandOnMainViewModel()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("64x64");
        var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

        mvm.IsAnimationEnabled = true;
        Assert.NotNull(mvm.SpriteState);
        int initialFrames = mvm.SpriteState.Frames.Count;

        EditorCanvasShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        bool handled = manager.ProcessKey(Key.N, ModifierKeys.Alt);
        Assert.True(handled);
        Assert.Equal(initialFrames + 1, mvm.SpriteState.Frames.Count);
    }

    [Fact]
    public void ProcessKey_F2_ExecutesRenameLayerCommand_SuppressedInTextInput()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();
        shell.NewDocumentCommand.Execute("64x64");
        var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        Assert.False(mvm.Layers[0].IsRenaming);

        // Normal execution
        bool handled = manager.ProcessKey(Key.F2, ModifierKeys.None);
        Assert.True(handled);
        Assert.True(mvm.Layers[0].IsRenaming);

        // Reset renaming
        mvm.Layers[0].IsRenaming = false;

        // STA test for focus suppression inside TextBox
        WpfTestHelper.EnsureApplication();
        WpfTestHelper.RunOnSta(() =>
        {
            var textBox = new TextBox();
            bool textInputHandled = manager.ProcessKey(Key.F2, ModifierKeys.None, focusedElement: textBox);
            Assert.False(textInputHandled);
            Assert.False(mvm.Layers[0].IsRenaming);
        });
    }

    [Fact]
    public void ProcessKey_F5_RefreshTheme_SuppressedInSecondaryWindow()
    {
        var manager = new HexpriteShortcutManager(registerTracerBullet: false);
        var shell = E2E.E2ETestHelper.CreateTestShellViewModel();

        WindowShortcutRegistrar.RegisterDefaultShortcuts(manager, shell);

        bool themeRefreshed = false;
        shell.ThemeChanged += (s, e) => themeRefreshed = true;

        WpfTestHelper.EnsureApplication();
        WpfTestHelper.RunOnSta(() =>
        {
            var mainWin = new Window();
            var secondaryWin = new Window();
            var prevMainWindow = Application.Current?.MainWindow;
            if (Application.Current != null)
            {
                Application.Current.MainWindow = mainWin;
            }

            secondaryWin.Show();

            try
            {
                var border = new Border();
                secondaryWin.Content = border;

                // Inside secondary window, F5 should be suppressed so secondary windows can handle F5
                bool handled = manager.ProcessKey(Key.F5, ModifierKeys.None, focusedElement: border);
                Assert.False(handled);
                Assert.False(themeRefreshed);

                // When focused on main window, F5 executes
                bool mainHandled = manager.ProcessKey(Key.F5, ModifierKeys.None, focusedElement: mainWin);
                Assert.True(mainHandled);
                Assert.True(themeRefreshed);
            }
            finally
            {
                secondaryWin.Close();
                if (Application.Current != null)
                {
                    Application.Current.MainWindow = prevMainWindow;
                }
            }
        });
    }

    #endregion
}

