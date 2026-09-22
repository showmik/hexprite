using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Hexprite.Views;
using Xunit;

namespace Hexprite.Tests
{
    [Collection("WindowLayoutSettingsFile")]
    [Trait("Category", "Unit")]
    public class TimelinePanelTests
    {
        private static void RunOnStaThread(Action action)
        {
            WpfTestHelper.RunOnSta(action);
        }

        [Fact]
        public void HandleFrameListPreviewMouseWheel_WhenScrollable_ScrollsHorizontallyAndMarksHandled()
        {
            RunOnStaThread(() =>
            {
                var sv = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Width = 200,
                    Height = 100
                };
                var content = new Canvas { Width = 600, Height = 100 };
                sv.Content = content;

                sv.ApplyTemplate();
                sv.Measure(new Size(200, 100));
                sv.Arrange(new Rect(0, 0, 200, 100));
                sv.UpdateLayout();

                Assert.True(sv.ScrollableWidth > 0, "ScrollViewer must have ScrollableWidth > 0 for this test.");

                var mouseDevice = Mouse.PrimaryDevice;
                var eventArgs = new MouseWheelEventArgs(mouseDevice, Environment.TickCount, -120)
                {
                    RoutedEvent = UIElement.PreviewMouseWheelEvent,
                    Source = sv
                };

                TimelinePanel.HandleFrameListPreviewMouseWheel(sv, eventArgs);
                sv.UpdateLayout();

                Assert.True(eventArgs.Handled);
                Assert.Equal(120, sv.HorizontalOffset);

                // Scrolling up (positive delta) moves left
                var eventArgsUp = new MouseWheelEventArgs(mouseDevice, Environment.TickCount, 50)
                {
                    RoutedEvent = UIElement.PreviewMouseWheelEvent,
                    Source = sv
                };

                TimelinePanel.HandleFrameListPreviewMouseWheel(sv, eventArgsUp);
                sv.UpdateLayout();

                Assert.True(eventArgsUp.Handled);
                Assert.Equal(70, sv.HorizontalOffset);
            });
        }

        [Fact]
        public void HandleFrameListPreviewMouseWheel_WhenNotScrollable_DoesNotMarkHandled()
        {
            RunOnStaThread(() =>
            {
                var sv = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Width = 500,
                    Height = 100
                };
                var content = new Canvas { Width = 200, Height = 100 };
                sv.Content = content;

                sv.ApplyTemplate();
                sv.Measure(new Size(500, 100));
                sv.Arrange(new Rect(0, 0, 500, 100));
                sv.UpdateLayout();

                Assert.Equal(0, sv.ScrollableWidth);

                var mouseDevice = Mouse.PrimaryDevice;
                var eventArgs = new MouseWheelEventArgs(mouseDevice, Environment.TickCount, -120)
                {
                    RoutedEvent = UIElement.PreviewMouseWheelEvent,
                    Source = sv
                };

                TimelinePanel.HandleFrameListPreviewMouseWheel(sv, eventArgs);

                Assert.False(eventArgs.Handled);
                Assert.Equal(0, sv.HorizontalOffset);
            });
        }

        [Fact]
        public void BatchButton_Click_OpensContextMenu_WhenChecked()
        {
            RunOnStaThread(() =>
            {
                var button = new ToggleButton
                {
                    IsChecked = true,
                    ContextMenu = new ContextMenu()
                };

                TimelinePanel.HandleBatchButtonClick(button);

                Assert.True(button.ContextMenu.IsOpen);
                Assert.Same(button, button.ContextMenu.PlacementTarget);
            });
        }

        [Fact]
        public void BatchButton_Click_ClosesContextMenu_WhenUnchecked()
        {
            RunOnStaThread(() =>
            {
                var button = new ToggleButton
                {
                    IsChecked = false,
                    ContextMenu = new ContextMenu { IsOpen = true }
                };

                TimelinePanel.HandleBatchButtonClick(button);

                Assert.False(button.ContextMenu.IsOpen);
            });
        }

        [Fact]
        public void BatchContextMenu_Closed_ResetsIsChecked_ToFalse()
        {
            RunOnStaThread(() =>
            {
                var button = new ToggleButton { IsChecked = true };

                TimelinePanel.HandleBatchContextMenuClosed(button);

                Assert.False(button.IsChecked);
            });
        }

        [Fact]
        public void BatchContextMenu_Closed_HandlesNullButtonGracefully()
        {
            RunOnStaThread(() =>
            {
                TimelinePanel.HandleBatchContextMenuClosed(null);
            });
        }
    }
}
