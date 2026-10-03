using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Hexprite.Views
{
    public partial class LoadingSpinner : UserControl
    {
        private RotateTransform? _transform;

        public LoadingSpinner()
        {
            InitializeComponent();
            _transform = new RotateTransform(0);
            SpinnerCanvas.RenderTransform = _transform;

            Loaded += (s, e) => UpdateAnimation();
            Unloaded += (s, e) => StopAnimation();
            IsVisibleChanged += (s, e) => UpdateAnimation();
        }

        private void UpdateAnimation()
        {
            if (IsLoaded && IsVisible)
            {
                StartAnimation();
            }
            else
            {
                StopAnimation();
            }
        }

        private void StartAnimation()
        {
            if (_transform == null) return;
            var anim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(0.9),
                RepeatBehavior = RepeatBehavior.Forever
            };
            _transform.BeginAnimation(RotateTransform.AngleProperty, anim);
        }

        private void StopAnimation()
        {
            _transform?.BeginAnimation(RotateTransform.AngleProperty, null);
        }
    }
}
