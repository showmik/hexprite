using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Hexprite.Behaviors
{
    public static class NumericScrubBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(NumericScrubBehavior),
                new UIPropertyMetadata(false, OnIsEnabledChanged));

        public static readonly DependencyProperty ScrubStartedCommandProperty =
            DependencyProperty.RegisterAttached(
                "ScrubStartedCommand",
                typeof(ICommand),
                typeof(NumericScrubBehavior),
                new UIPropertyMetadata(null));

        public static ICommand GetScrubStartedCommand(DependencyObject obj)
        {
            return (ICommand)obj.GetValue(ScrubStartedCommandProperty);
        }

        public static void SetScrubStartedCommand(DependencyObject obj, ICommand value)
        {
            obj.SetValue(ScrubStartedCommandProperty, value);
        }

        public static readonly DependencyProperty ScrubEndedCommandProperty =
            DependencyProperty.RegisterAttached(
                "ScrubEndedCommand",
                typeof(ICommand),
                typeof(NumericScrubBehavior),
                new UIPropertyMetadata(null));

        public static ICommand GetScrubEndedCommand(DependencyObject obj)
        {
            return (ICommand)obj.GetValue(ScrubEndedCommandProperty);
        }

        public static void SetScrubEndedCommand(DependencyObject obj, ICommand value)
        {
            obj.SetValue(ScrubEndedCommandProperty, value);
        }

        // ── Keyboard Scrub Commands ──────────────────────────────────────
        // Arrow key increments are batched into a single undo entry.

        public static readonly DependencyProperty KeyboardScrubStartedCommandProperty =
            DependencyProperty.RegisterAttached(
                "KeyboardScrubStartedCommand",
                typeof(ICommand),
                typeof(NumericScrubBehavior),
                new UIPropertyMetadata(null));

        public static ICommand GetKeyboardScrubStartedCommand(DependencyObject obj)
        {
            return (ICommand)obj.GetValue(KeyboardScrubStartedCommandProperty);
        }

        public static void SetKeyboardScrubStartedCommand(DependencyObject obj, ICommand value)
        {
            obj.SetValue(KeyboardScrubStartedCommandProperty, value);
        }

        public static readonly DependencyProperty KeyboardScrubEndedCommandProperty =
            DependencyProperty.RegisterAttached(
                "KeyboardScrubEndedCommand",
                typeof(ICommand),
                typeof(NumericScrubBehavior),
                new UIPropertyMetadata(null));

        public static ICommand GetKeyboardScrubEndedCommand(DependencyObject obj)
        {
            return (ICommand)obj.GetValue(KeyboardScrubEndedCommandProperty);
        }

        public static void SetKeyboardScrubEndedCommand(DependencyObject obj, ICommand value)
        {
            obj.SetValue(KeyboardScrubEndedCommandProperty, value);
        }

        public static bool GetIsEnabled(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsEnabledProperty);
        }

        public static void SetIsEnabled(DependencyObject obj, bool value)
        {
            obj.SetValue(IsEnabledProperty, value);
        }

        public static readonly DependencyProperty MinValueProperty =
            DependencyProperty.RegisterAttached("MinValue", typeof(double), typeof(NumericScrubBehavior), new UIPropertyMetadata(-double.MaxValue));

        public static double GetMinValue(DependencyObject obj)
        {
            return (double)obj.GetValue(MinValueProperty);
        }

        public static void SetMinValue(DependencyObject obj, double value)
        {
            obj.SetValue(MinValueProperty, value);
        }

        public static readonly DependencyProperty MaxValueProperty =
            DependencyProperty.RegisterAttached("MaxValue", typeof(double), typeof(NumericScrubBehavior), new UIPropertyMetadata(double.MaxValue));

        public static double GetMaxValue(DependencyObject obj)
        {
            return (double)obj.GetValue(MaxValueProperty);
        }

        public static void SetMaxValue(DependencyObject obj, double value)
        {
            obj.SetValue(MaxValueProperty, value);
        }

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBox textBox)
            {
                if ((bool)e.NewValue)
                {
                    textBox.PreviewMouseLeftButtonDown += TextBox_PreviewMouseLeftButtonDown;
                    textBox.PreviewMouseMove += TextBox_PreviewMouseMove;
                    textBox.PreviewMouseLeftButtonUp += TextBox_PreviewMouseLeftButtonUp;
                    textBox.PreviewKeyDown += TextBox_PreviewKeyDown;
                    textBox.GotKeyboardFocus += TextBox_GotKeyboardFocus;
                    textBox.LostKeyboardFocus += TextBox_LostKeyboardFocus;
                    textBox.LostMouseCapture += TextBox_LostMouseCapture;
                    
                    if (!textBox.IsFocused)
                        textBox.Cursor = Cursors.SizeWE;
                }
                else
                {
                    textBox.PreviewMouseLeftButtonDown -= TextBox_PreviewMouseLeftButtonDown;
                    textBox.PreviewMouseMove -= TextBox_PreviewMouseMove;
                    textBox.PreviewMouseLeftButtonUp -= TextBox_PreviewMouseLeftButtonUp;
                    textBox.PreviewKeyDown -= TextBox_PreviewKeyDown;
                    textBox.GotKeyboardFocus -= TextBox_GotKeyboardFocus;
                    textBox.LostKeyboardFocus -= TextBox_LostKeyboardFocus;
                    textBox.LostMouseCapture -= TextBox_LostMouseCapture;
                    textBox.ClearValue(FrameworkElement.CursorProperty);
                }
            }
        }

        private static void TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox textBox)
                textBox.Cursor = Cursors.IBeam;
        }

        private static void TextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.Cursor = Cursors.SizeWE;
                // End any in-progress keyboard scrub when focus is lost
                if (_isKeyboardScrubActive)
                {
                    _isKeyboardScrubActive = false;
                    GetKeyboardScrubEndedCommand(textBox)?.Execute(null);
                }
            }
        }

        private static void TextBox_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_isDragging && sender is TextBox textBox)
            {
                _isDragging = false;
                if (_dragThresholdPassed)
                {
                    GetScrubEndedCommand(textBox)?.Execute(null);
                }
            }
        }

        private static Point _lastMousePos;
        private static bool _isDragging;
        private static double _accumulatedDelta;
        private static bool _dragThresholdPassed;
        private static bool _isInteger;
        private static bool _isKeyboardScrubActive;

        private static void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox textBox && !textBox.IsFocused)
            {
                _lastMousePos = e.GetPosition(textBox);
                _isDragging = true;
                _dragThresholdPassed = false;
                _accumulatedDelta = 0;
                
                string text = textBox.Text;
                _isInteger = !text.Contains('.', StringComparison.Ordinal) && !text.Contains(',', StringComparison.Ordinal);

                textBox.CaptureMouse();
                e.Handled = true; // Prevent focus on click down
            }
        }

        private static void TextBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && sender is TextBox textBox)
            {
                var currentPos = e.GetPosition(textBox);
                var deltaX = currentPos.X - _lastMousePos.X;
                _lastMousePos = currentPos;

                if (!_dragThresholdPassed)
                {
                    _accumulatedDelta += deltaX;
                    if (Math.Abs(_accumulatedDelta) > SystemParameters.MinimumHorizontalDragDistance)
                    {
                        _dragThresholdPassed = true;
                        _accumulatedDelta = 0; // Reset accumulation after threshold is passed
                        GetScrubStartedCommand(textBox)?.Execute(null);
                    }
                }

                if (_dragThresholdPassed)
                {
                    double speed = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) ? 10.0 : 1.0;
                    _accumulatedDelta += deltaX * speed * 0.5;

                    if (!double.TryParse(textBox.Text, CultureInfo.InvariantCulture, out double currentVal) &&
                        !double.TryParse(textBox.Text, CultureInfo.CurrentCulture, out currentVal))
                    {
                        currentVal = 0;
                    }

                    double min = GetMinValue(textBox);
                    double max = GetMaxValue(textBox);

                    if (_isInteger)
                    {
                        int steps = (int)_accumulatedDelta;
                        if (steps != 0)
                        {
                            double clampedVal = Math.Clamp(currentVal + steps, min, max);
                            textBox.Text = clampedVal.ToString(CultureInfo.InvariantCulture);
                            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                            
                            if (clampedVal == min || clampedVal == max)
                                _accumulatedDelta = 0;
                            else
                                _accumulatedDelta -= steps;
                        }
                    }
                    else
                    {
                        if (Math.Abs(_accumulatedDelta) > 0.01)
                        {
                            double clampedVal = Math.Clamp(currentVal + _accumulatedDelta, min, max);
                            textBox.Text = Math.Round(clampedVal, 2, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
                            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                            _accumulatedDelta = 0;
                        }
                    }
                }
            }
        }

        private static void TextBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging && sender is TextBox textBox)
            {
                _isDragging = false;
                textBox.ReleaseMouseCapture();

                if (!_dragThresholdPassed)
                {
                    // Focus and select all on click
                    textBox.Focus();
                    textBox.SelectAll();
                }
                else
                {
                    GetScrubEndedCommand(textBox)?.Execute(null);
                }
                
                e.Handled = true;
            }
        }

        private static void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is TextBox textBox && textBox.IsFocused)
            {
                if (e.Key == Key.Up || e.Key == Key.Down)
                {
                    // Start keyboard scrub on first arrow press to batch increments
                    if (!_isKeyboardScrubActive)
                    {
                        _isKeyboardScrubActive = true;
                        GetKeyboardScrubStartedCommand(textBox)?.Execute(null);
                    }

                    if (!double.TryParse(textBox.Text, CultureInfo.InvariantCulture, out double val) &&
                        !double.TryParse(textBox.Text, CultureInfo.CurrentCulture, out val))
                    {
                        val = 0;
                    }

                    double step = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) ? 10 : 1;
                    if (e.Key == Key.Down) step = -step;

                    val += step;
                    
                    double min = GetMinValue(textBox);
                    double max = GetMaxValue(textBox);
                    val = Math.Clamp(val, min, max);
                    
                    string text = textBox.Text;
                    bool isInt = !text.Contains('.', StringComparison.Ordinal) && !text.Contains(',', StringComparison.Ordinal);
                    
                    textBox.Text = isInt
                        ? Math.Round(val, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)
                        : Math.Round(val, 2, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
                    
                    var binding = textBox.GetBindingExpression(TextBox.TextProperty);
                    binding?.UpdateSource();
                    
                    // Select all text again after changing
                    textBox.SelectAll();
                    
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    // End keyboard scrub if active
                    if (_isKeyboardScrubActive)
                    {
                        _isKeyboardScrubActive = false;
                        GetKeyboardScrubEndedCommand(textBox)?.Execute(null);
                    }
                    textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                    textBox.SelectAll();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    // End keyboard scrub if active
                    if (_isKeyboardScrubActive)
                    {
                        _isKeyboardScrubActive = false;
                        GetKeyboardScrubEndedCommand(textBox)?.Execute(null);
                    }
                    textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
                    textBox.SelectAll();
                    e.Handled = true;
                }
                else if (e.Key == Key.Tab)
                {
                    // End keyboard scrub when tabbing away
                    if (_isKeyboardScrubActive)
                    {
                        _isKeyboardScrubActive = false;
                        GetKeyboardScrubEndedCommand(textBox)?.Execute(null);
                    }
                }
            }
        }
    }
}
