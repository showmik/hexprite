using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Hexprite.ViewModels.Flipper;
using Hexprite.Views;
using Moq;
using Xunit;

namespace Hexprite.Tests.ViewModels
{
    [Trait("Category", "Unit")]
    public class FlipperMemoryLeakLifecycleTests
    {
        private static void FlushDispatcher()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new DispatcherOperationCallback(f =>
            {
                ((DispatcherFrame)f).Continue = false;
                return null;
            }), frame);
            Dispatcher.PushFrame(frame);
        }

        private static void ForceGarbageCollection()
        {
#pragma warning disable S1215 // Intentional GC collection in memory leak lifecycle tests
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
#pragma warning restore S1215
        }

        #region 1. FlipperScheduleMatrixViewModel

        [Fact]
        public void FlipperScheduleMatrixViewModel_WhenDisposed_IsGarbageCollected()
        {
            WeakReference weakVm = null!;

            [MethodImpl(MethodImplOptions.NoInlining)]
            void CreateAndDispose()
            {
                var vm = new FlipperScheduleMatrixViewModel();
                weakVm = new WeakReference(vm);

                // Add undo state and inspect cells
                vm.InspectCell(1, 0);
                vm.Undo();
                vm.Redo();

                vm.Dispose();
            }

            CreateAndDispose();
            ForceGarbageCollection();

            Assert.False(weakVm.IsAlive, "FlipperScheduleMatrixViewModel instance remained rooted after Dispose().");
        }

        #endregion

        #region 2. AssetPackEditorPanel

        [Fact]
        public void AssetPackEditorPanel_WhenUnloaded_DetachesAndAllowsViewModelCollection()
        {
            WeakReference weakPanel = null!;
            WeakReference weakVm = null!;

            WpfTestHelper.RunOnSta(() =>
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                void CreateAndUnload()
                {
                    var vm = new FlipperScheduleMatrixViewModel();
                    var panel = new AssetPackEditorPanel
                    {
                        DataContext = vm
                    };

                    weakVm = new WeakReference(vm);
                    weakPanel = new WeakReference(panel);

                    // Simulate loaded & unload
                    panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                    panel.RedrawMatrix();

                    panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                    panel.DataContext = null;
                    vm.Dispose();
                }

                CreateAndUnload();
                FlushDispatcher();
            });

            ForceGarbageCollection();

            Assert.False(weakVm.IsAlive, "FlipperScheduleMatrixViewModel remained rooted after AssetPackEditorPanel unloaded.");
            Assert.False(weakPanel.IsAlive, "AssetPackEditorPanel remained rooted after detachment.");
        }

        #endregion

        #region 3. FlipperSimulatorViewModel

        [Fact]
        public void FlipperSimulatorViewModel_WhenDisposed_IsGarbageCollected()
        {
            WeakReference weakVm = null!;

            [MethodImpl(MethodImplOptions.NoInlining)]
            void CreateAndDispose()
            {
                var sprite = new SpriteState(128, 64);
                var entry = new FlipperManifestEntry
                {
                    Name = "TestDolphin",
                    MinLevel = 1,
                    MaxLevel = 30,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = 1
                };

                var list = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
                {
                    ("TestDolphin", sprite, entry)
                };

                var vm = new FlipperSimulatorViewModel(list, "TestPack");
                weakVm = new WeakReference(vm);

                vm.AdvanceFrame();
                vm.RollRng();
                vm.Dispose();
            }

            CreateAndDispose();
            ForceGarbageCollection();

            Assert.False(weakVm.IsAlive, "FlipperSimulatorViewModel instance remained rooted after Dispose().");
        }

        #endregion

        #region 4. FlipperMediaSlicerViewModel

        [Fact]
        public void FlipperMediaSlicerViewModel_WhenDisposed_IsGarbageCollected()
        {
            WeakReference weakVm = null!;

            [MethodImpl(MethodImplOptions.NoInlining)]
            void CreateAndDispose()
            {
                var sprite = new SpriteState(128, 64);
                var mockExport = new Mock<IFlipperExportService>();
                var mockTabs = new Mock<IWorkspaceTabService>();
                var mockDialog = new Mock<IDialogService>();

                var vm = new FlipperMediaSlicerViewModel(mockExport.Object, mockTabs.Object, mockDialog.Object, sprite);
                weakVm = new WeakReference(vm);

                vm.AdvanceFrame();
                vm.Dispose();
            }

            CreateAndDispose();
            ForceGarbageCollection();

            Assert.False(weakVm.IsAlive, "FlipperMediaSlicerViewModel instance remained rooted after Dispose().");
        }

        #endregion

        #region 6. FlipperScreenMirrorViewModel

        [Fact]
        public void FlipperScreenMirrorViewModel_WhenDisposed_IsGarbageCollected()
        {
            WeakReference weakVm = null!;

            [MethodImpl(MethodImplOptions.NoInlining)]
            void CreateAndDispose()
            {
                var mockStream = new Mock<IFlipperScreenStreamService>();
                var mockTabs = new Mock<IWorkspaceTabService>();
                var mockDialog = new Mock<IDialogService>();

                var vm = new FlipperScreenMirrorViewModel(mockStream.Object, mockTabs.Object, mockDialog.Object);
                weakVm = new WeakReference(vm);

                vm.Dispose();
            }

            CreateAndDispose();
            ForceGarbageCollection();

            Assert.False(weakVm.IsAlive, "FlipperScreenMirrorViewModel instance remained rooted after Dispose().");
        }

        #endregion

        #region 7. FlipperDeployViewModel

        [Fact]
        public void FlipperDeployViewModel_WhenDisposed_IsGarbageCollected()
        {
            WeakReference weakVm = null!;

            [MethodImpl(MethodImplOptions.NoInlining)]
            void CreateAndDispose()
            {
                var mockDeployer = new Mock<IFlipperUsbDeployer>();
                var mockDialog = new Mock<IDialogService>();
                var files = new List<(string RelativePath, byte[] Data)>
                {
                    ("manifest.txt", [0x01, 0x02])
                };

                var vm = new FlipperDeployViewModel(mockDeployer.Object, files, "TestPack", mockDialog.Object);
                weakVm = new WeakReference(vm);

                vm.Dispose();
            }

            CreateAndDispose();
            ForceGarbageCollection();

            Assert.False(weakVm.IsAlive, "FlipperDeployViewModel instance remained rooted after Dispose().");
        }

        #endregion

        #region 8. FlipperExportViewModel

        [Fact]
        public void FlipperExportViewModel_WhenDisposed_IsGarbageCollected()
        {
            WeakReference weakVm = null!;

            [MethodImpl(MethodImplOptions.NoInlining)]
            void CreateAndDispose()
            {
                var sprite = new SpriteState(128, 64);
                var mockExport = new Mock<IFlipperExportService>();
                var mockWin = new Mock<IFlipperWindowManager>();
                var mockDialog = new Mock<IDialogService>();

                var vm = new FlipperExportViewModel(sprite, mockExport.Object, mockWin.Object, mockDialog.Object);
                weakVm = new WeakReference(vm);

                vm.AnimationName = "TestAnim";
                vm.Dispose();
            }

            CreateAndDispose();
            ForceGarbageCollection();

            Assert.False(weakVm.IsAlive, "FlipperExportViewModel instance remained rooted after Dispose().");
        }

        #endregion

        #region 9. AssetPackViewModel

        [Fact]
        public void AssetPackViewModel_WhenClosedAndDisposed_IsGarbageCollected()
        {
            WeakReference weakDoc = null!;
            WeakReference weakMatrixVm = null!;

            [MethodImpl(MethodImplOptions.NoInlining)]
            void CreateAndDispose()
            {
                var doc = new AssetPackViewModel();
                weakDoc = new WeakReference(doc);
                weakMatrixVm = new WeakReference(doc.MatrixViewModel);

                doc.IsActive = true;
                doc.MatrixViewModel.InspectCell(1, 0);

                doc.IsActive = false;
                doc.Dispose();
            }

            CreateAndDispose();
            ForceGarbageCollection();

            Assert.False(weakDoc.IsAlive, "AssetPackViewModel instance remained rooted after tab closure/Dispose().");
            Assert.False(weakMatrixVm.IsAlive, "FlipperScheduleMatrixViewModel instance remained rooted after tab closure/Dispose().");
        }

        #endregion

        #region 10. Window Lifecycle Tests

        [Fact]
        public void FlipperMatrixSimulatorWindow_WhenClosed_IsGarbageCollected()
        {
            WeakReference weakWin = null!;
            WeakReference weakVm = null!;

            WpfTestHelper.RunOnSta(() =>
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                void CreateAndCloseWindow()
                {
                    var sprite = new SpriteState(128, 64);
                    var entry = new FlipperManifestEntry
                    {
                        Name = "TestDolphin",
                        MinLevel = 1,
                        MaxLevel = 30,
                        MinButthurt = 0,
                        MaxButthurt = 14,
                        Weight = 1
                    };

                    var list = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
                    {
                        ("TestDolphin", sprite, entry)
                    };

                    var vm = new FlipperSimulatorViewModel(list, "TestPack");
                    var win = new FlipperMatrixSimulatorWindow(vm);

                    weakVm = new WeakReference(vm);
                    weakWin = new WeakReference(win);

                    win.Close();
                }

                CreateAndCloseWindow();
                FlushDispatcher();
            });

            ForceGarbageCollection();

            Assert.False(weakWin.IsAlive, "FlipperMatrixSimulatorWindow was not collected after Close().");
            Assert.False(weakVm.IsAlive, "FlipperSimulatorViewModel was not collected after window Close().");
        }

        [Fact]
        public void FlipperMediaSlicerWindow_WhenClosed_IsGarbageCollected()
        {
            WeakReference weakWin = null!;
            WeakReference weakVm = null!;

            WpfTestHelper.RunOnSta(() =>
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                void CreateAndCloseWindow()
                {
                    var sprite = new SpriteState(128, 64);
                    var mockExport = new Mock<IFlipperExportService>();
                    var mockTabs = new Mock<IWorkspaceTabService>();
                    var mockDialog = new Mock<IDialogService>();

                    var vm = new FlipperMediaSlicerViewModel(mockExport.Object, mockTabs.Object, mockDialog.Object, sprite);
                    var win = new FlipperMediaSlicerWindow(vm);

                    weakVm = new WeakReference(vm);
                    weakWin = new WeakReference(win);

                    win.Close();
                }

                CreateAndCloseWindow();
                FlushDispatcher();
            });

            ForceGarbageCollection();

            Assert.False(weakWin.IsAlive, "FlipperMediaSlicerWindow was not collected after Close().");
            Assert.False(weakVm.IsAlive, "FlipperMediaSlicerViewModel was not collected after window Close().");
        }

        [Fact]
        public void FlipperScreenMirrorWindow_WhenClosed_IsGarbageCollected()
        {
            WeakReference weakWin = null!;
            WeakReference weakVm = null!;

            WpfTestHelper.RunOnSta(() =>
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                void CreateAndCloseWindow()
                {
                    var mockStream = new Mock<IFlipperScreenStreamService>();
                    var mockTabs = new Mock<IWorkspaceTabService>();
                    var mockDialog = new Mock<IDialogService>();

                    var vm = new FlipperScreenMirrorViewModel(mockStream.Object, mockTabs.Object, mockDialog.Object);
                    var win = new FlipperScreenMirrorWindow(vm);

                    weakVm = new WeakReference(vm);
                    weakWin = new WeakReference(win);

                    win.Close();
                }

                CreateAndCloseWindow();
                FlushDispatcher();
            });

            ForceGarbageCollection();

            Assert.False(weakWin.IsAlive, "FlipperScreenMirrorWindow was not collected after Close().");
            Assert.False(weakVm.IsAlive, "FlipperScreenMirrorViewModel was not collected after window Close().");
        }

        [Fact]
        public void FlipperDeployWindow_WhenClosed_IsGarbageCollected()
        {
            WeakReference weakWin = null!;
            WeakReference weakVm = null!;

            WpfTestHelper.RunOnSta(() =>
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                void CreateAndCloseWindow()
                {
                    var mockDeployer = new Mock<IFlipperUsbDeployer>();
                    var mockDialog = new Mock<IDialogService>();
                    var files = new List<(string RelativePath, byte[] Data)>
                    {
                        ("manifest.txt", [0x01, 0x02])
                    };

                    var vm = new FlipperDeployViewModel(mockDeployer.Object, files, "TestPack", mockDialog.Object);
                    var win = new FlipperDeployWindow(vm);

                    weakVm = new WeakReference(vm);
                    weakWin = new WeakReference(win);

                    win.Close();
                }

                CreateAndCloseWindow();
                FlushDispatcher();
            });

            ForceGarbageCollection();

            Assert.False(weakWin.IsAlive, "FlipperDeployWindow was not collected after Close().");
            Assert.False(weakVm.IsAlive, "FlipperDeployViewModel was not collected after window Close().");
        }

        [Fact]
        public void FlipperExportDialog_WhenClosed_IsGarbageCollected()
        {
            WeakReference weakWin = null!;
            WeakReference weakVm = null!;

            WpfTestHelper.RunOnSta(() =>
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                void CreateAndCloseDialog()
                {
                    var sprite = new SpriteState(128, 64);
                    var mockExport = new Mock<IFlipperExportService>();
                    var mockWin = new Mock<IFlipperWindowManager>();
                    var mockDialog = new Mock<IDialogService>();

                    var vm = new FlipperExportViewModel(sprite, mockExport.Object, mockWin.Object, mockDialog.Object);
                    var dialog = new FlipperExportDialog(vm);

                    weakVm = new WeakReference(vm);
                    weakWin = new WeakReference(dialog);

                    dialog.Close();
                }

                CreateAndCloseDialog();
                FlushDispatcher();
            });

            ForceGarbageCollection();

            Assert.False(weakWin.IsAlive, "FlipperExportDialog was not collected after Close().");
            Assert.False(weakVm.IsAlive, "FlipperExportViewModel was not collected after dialog Close().");
        }

        #endregion
    }
}
