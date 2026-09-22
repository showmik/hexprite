using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hexprite.Core;
using Hexprite.ViewModels.Flipper;

namespace Hexprite.Views
{
    public partial class FlipperMatrixSimulatorWindow : Window
    {
        public FlipperSimulatorViewModel ViewModel => (FlipperSimulatorViewModel)DataContext;

        public FlipperPlaybackMode PlaybackMode => ViewModel.PlaybackMode;
        public int SequenceIndex => ViewModel.SequenceIndex;
        public CandidateAnimationVm? SelectedCandidate => ViewModel.SelectedCandidate;

        private bool _isDraggingBubble;

        public FlipperMatrixSimulatorWindow(FlipperSimulatorViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
            InitializeWindow();
        }

        public FlipperMatrixSimulatorWindow(
            IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> animations,
            string packName = "AssetPack")
            : this(new FlipperSimulatorViewModel(animations, packName))
        {
        }

        private void InitializeWindow()
        {
            // Sync controls for headless STA test support
            LstCandidates.SelectionChanged += (s, e) =>
            {
                if (LstCandidates.SelectedItem is CandidateAnimationVm cand)
                {
                    ViewModel.SelectedCandidate = cand;
                }
            };

            SliderLevel.ValueChanged += (s, e) =>
            {
                ViewModel.Level = (int)SliderLevel.Value;
            };

            SliderMood.ValueChanged += (s, e) =>
            {
                ViewModel.Mood = (int)SliderMood.Value;
            };

            // Immediate initial sync for headless tests
            TxtManifestInfo.Text = ViewModel.ManifestInfo;
            TxtCycleDetails.Text = ViewModel.CycleDetails;
            TxtCurrentAnimTitle.Text = ViewModel.CurrentAnimTitle;
            TxtCurrentAnimDetails.Text = ViewModel.CurrentAnimDetails;
            TxtFrameCounter.Text = ViewModel.FrameCounterText;
            TxtStateBadge.Text = ViewModel.StateBadgeText;

            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            ViewModel.RequestClose += OnViewModelRequestClose;

            Closed += (s, e) =>
            {
                ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
                ViewModel.RequestClose -= OnViewModelRequestClose;
                ViewModel.Dispose();
            };
        }

        private void OnViewModelRequestClose(object? sender, EventArgs e)
        {
            Close();
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ViewModel.ManifestInfo):
                    TxtManifestInfo.Text = ViewModel.ManifestInfo;
                    break;
                case nameof(ViewModel.CycleDetails):
                    TxtCycleDetails.Text = ViewModel.CycleDetails;
                    break;
                case nameof(ViewModel.CurrentAnimTitle):
                    TxtCurrentAnimTitle.Text = ViewModel.CurrentAnimTitle;
                    break;
                case nameof(ViewModel.CurrentAnimDetails):
                    TxtCurrentAnimDetails.Text = ViewModel.CurrentAnimDetails;
                    break;
                case nameof(ViewModel.FrameCounterText):
                    TxtFrameCounter.Text = ViewModel.FrameCounterText;
                    break;
                case nameof(ViewModel.StateBadgeText):
                    TxtStateBadge.Text = ViewModel.StateBadgeText;
                    break;
            }
        }

        public void UpdateCandidates() => ViewModel.UpdateCandidates();
        public void ResetAnimationToPassive() => ViewModel.ResetAnimationToPassive();
        public void AdvanceFrame() => ViewModel.AdvanceFrame();
        public void StepBack() => ViewModel.StepBack();
        public void TriggerActive() => ViewModel.TriggerActive();
        public void RenderCurrentFrame() => ViewModel.RenderCurrentFrame();

        public static int[] GetPassiveSequence(SpriteState sprite) =>
            FlipperSimulatorViewModel.GetPassiveSequence(sprite);

        public static int[] GetActiveSequence(SpriteState sprite) =>
            FlipperSimulatorViewModel.GetActiveSequence(sprite);

        private void SpeedChip_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.IsChecked == true && DataContext is FlipperSimulatorViewModel vm)
            {
                double mult = rb.Content?.ToString() switch
                {
                    "0.5x" => 0.5,
                    "2x" => 2.0,
                    _ => 1.0
                };
                vm.SetSpeedMultiplier(mult);
            }
        }

        private void ImgFlipperScreen_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel.ShowSpeechBubble && sender is Image img)
            {
                _isDraggingBubble = true;
                img.CaptureMouse();
                var pos = e.GetPosition(img);
                ViewModel.UpdateBubblePositionFromPoint(pos.X, pos.Y, img.ActualWidth, img.ActualHeight);
            }
        }

        private void ImgFlipperScreen_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingBubble && sender is Image img)
            {
                var pos = e.GetPosition(img);
                ViewModel.UpdateBubblePositionFromPoint(pos.X, pos.Y, img.ActualWidth, img.ActualHeight);
            }
        }

        private void ImgFlipperScreen_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingBubble && sender is Image img)
            {
                _isDraggingBubble = false;
                img.ReleaseMouseCapture();
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox)
            {
                base.OnPreviewKeyDown(e);
                return;
            }

            switch (e.Key)
            {
                case Key.Space:
                    ViewModel.TogglePlayPause();
                    e.Handled = true;
                    break;
                case Key.Up:
                    ViewModel.HandleDpadPress("UP");
                    e.Handled = true;
                    break;
                case Key.Down:
                    ViewModel.HandleDpadPress("DOWN");
                    e.Handled = true;
                    break;
                case Key.Left:
                    ViewModel.HandleDpadPress("LEFT");
                    e.Handled = true;
                    break;
                case Key.Right:
                    ViewModel.HandleDpadPress("RIGHT");
                    e.Handled = true;
                    break;
                case Key.R:
                    ViewModel.ResetAnimationToPassive();
                    e.Handled = true;
                    break;
                case Key.A:
                case Key.Enter:
                    ViewModel.HandleDpadPress("OK");
                    e.Handled = true;
                    break;
                case Key.Back:
                case Key.B:
                    ViewModel.HandleDpadPress("BACK");
                    e.Handled = true;
                    break;
                case Key.D:
                    ViewModel.ShowDesktopHud = !ViewModel.ShowDesktopHud;
                    e.Handled = true;
                    break;
                case Key.G:
                    ViewModel.ShowLcdGrid = !ViewModel.ShowLcdGrid;
                    e.Handled = true;
                    break;
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    break;
            }

            base.OnPreviewKeyDown(e);
        }

        private void CaptionClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
