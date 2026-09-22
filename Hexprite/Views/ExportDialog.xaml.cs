using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hexprite.Core;

namespace Hexprite.Views
{
    public partial class ExportDialog : Window
    {
        public ImageExportSettings Result { get; private set; }

        private readonly SpriteState _spriteState;
        private readonly bool _isInitializing = true;

        public ExportDialog(ImageExportSettings initialSettings, SpriteState spriteState)
        {
            InitializeComponent();
            _spriteState = spriteState;
            Result = initialSettings.Clone();

            LoadSettings(Result);
            _isInitializing = false;
            UpdateContextualState();
            UpdatePreviewText();
        }

        private void LoadSettings(ImageExportSettings settings)
        {
            // Format / Tab
            if (settings.Format == ImageExportFormat.Gif)
                FormatTabControl.SelectedIndex = 1;
            else if (settings.Format == ImageExportFormat.PngSequence)
                FormatTabControl.SelectedIndex = 2;
            else
                FormatTabControl.SelectedIndex = 0;

            if (settings.Format == ImageExportFormat.Bmp)
                RbBmp.IsChecked = true;
            else
                RbPng.IsChecked = true;

            // Scale
            SetScaleRadio("ImgScale", settings.Scale);
            SetScaleRadio("GifScale", settings.Scale);
            SetScaleRadio("SeqScale", settings.Scale);

            // Colors
            if (settings.ColorMode == ExportColorMode.DisplayPreview)
            {
                RbColorsDisplay.IsChecked = true;
                RbGifColorsDisplay.IsChecked = true;
                RbSeqColorsDisplay.IsChecked = true;
            }
            else
            {
                RbColorsEditor.IsChecked = true;
                RbGifColorsEditor.IsChecked = true;
                RbSeqColorsEditor.IsChecked = true;
            }

            // Image Options
            ChkImgGrid.IsChecked = settings.ShowGrid;
            ChkImgSpritesheet.IsChecked = settings.ExportAllFramesAsSpritesheet;
            ChkImgSeparateFrames.IsChecked = settings.ExportAllFramesAsSeparateFiles;

            // Gif Options
            TxtGifFps.Text = settings.GifFps.ToString(CultureInfo.InvariantCulture);
            RbGifLoopInf.IsChecked = settings.GifLoopInfinite;
            RbGifLoopCount.IsChecked = !settings.GifLoopInfinite;
            TxtGifLoopCount.Text = settings.GifLoopCount.ToString(CultureInfo.InvariantCulture);
            ChkGifDither.IsChecked = settings.GifEnableDithering;
            ChkGifAllFrames.IsChecked = settings.GifExportAllFrames;
            ChkGifTransparent.IsChecked = settings.GifTransparentBackground;
            ChkGifDeltaOpt.IsChecked = settings.GifEnableDeltaOptimization;
        }

        private void SetScaleRadio(string groupName, int scale)
        {
            var scaleStr = scale.ToString(CultureInfo.InvariantCulture);
            var radios = FindLogicalChildren<RadioButton>(this).Where(r => r.GroupName == groupName).ToList();
            var rb = radios.FirstOrDefault(r => r.Tag?.ToString() == scaleStr);
            
            if (rb != null)
            {
                rb.IsChecked = true;
            }
            else
            {
                // Find the custom radio button and text box for this group
                RadioButton? customRb = null;
                TextBox? customTxt = null;

                if (groupName == "ImgScale") { customRb = RbImgScaleCustom; customTxt = TxtImgScaleCustom; }
                else if (groupName == "GifScale") { customRb = RbGifScaleCustom; customTxt = TxtGifScaleCustom; }
                else if (groupName == "SeqScale") { customRb = RbSeqScaleCustom; customTxt = TxtSeqScaleCustom; }

                if (customRb != null && customTxt != null)
                {
                    customRb.IsChecked = true;
                    customTxt.Text = scale.ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        private int GetSelectedScale(string groupName)
        {
            var radios = FindLogicalChildren<RadioButton>(this).Where(r => r.GroupName == groupName);
            var rb = radios.FirstOrDefault(r => r.IsChecked == true);
            if (rb != null)
            {
                if (int.TryParse(rb.Tag?.ToString(), out int scale))
                    return scale;

                // If no tag, it's the custom one
                TextBox? customTxt = null;
                if (groupName == "ImgScale") customTxt = TxtImgScaleCustom;
                else if (groupName == "GifScale") customTxt = TxtGifScaleCustom;
                else if (groupName == "SeqScale") customTxt = TxtSeqScaleCustom;

                if (customTxt != null && int.TryParse(customTxt.Text, out int customScale))
                    return Math.Max(1, customScale);
            }
            return 4;
        }

        private void CustomScale_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;
            UpdatePreviewText();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private void TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                tb.SelectAll();
            }
        }

        private void CaptionClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            DialogResult = true;
        }

        private void SaveSettings()
        {
            var tabTag = (FormatTabControl.SelectedItem as TabItem)?.Tag?.ToString();
            
            if (tabTag == "Image")
            {
                Result.Format = RbBmp.IsChecked == true ? ImageExportFormat.Bmp : ImageExportFormat.Png;
                Result.Scale = GetSelectedScale("ImgScale");
                Result.ColorMode = RbColorsDisplay.IsChecked == true ? ExportColorMode.DisplayPreview : ExportColorMode.EditorColors;
                Result.ShowGrid = ChkImgGrid.IsChecked == true;
                Result.ExportAllFramesAsSpritesheet = ChkImgSpritesheet.IsChecked == true;
                Result.ExportAllFramesAsSeparateFiles = ChkImgSeparateFrames.IsChecked == true;
            }
            else if (tabTag == "Gif")
            {
                Result.Format = ImageExportFormat.Gif;
                Result.Scale = GetSelectedScale("GifScale");
                Result.ColorMode = RbGifColorsDisplay.IsChecked == true ? ExportColorMode.DisplayPreview : ExportColorMode.EditorColors;
                
                if (int.TryParse(TxtGifFps.Text, out int fps)) Result.GifFps = Math.Clamp(fps, 1, 60);
                Result.GifLoopInfinite = RbGifLoopInf.IsChecked == true;
                if (int.TryParse(TxtGifLoopCount.Text, out int count)) Result.GifLoopCount = Math.Clamp(count, 1, 255);
                Result.GifEnableDithering = ChkGifDither.IsChecked == true;
                Result.GifExportAllFrames = ChkGifAllFrames.IsChecked == true;
                Result.GifTransparentBackground = ChkGifTransparent.IsChecked == true;
                Result.GifEnableDeltaOptimization = ChkGifDeltaOpt.IsChecked == true;
            }
            else // Sequence
            {
                Result.Format = ImageExportFormat.PngSequence;
                Result.Scale = GetSelectedScale("SeqScale");
                Result.ColorMode = RbSeqColorsDisplay.IsChecked == true ? ExportColorMode.DisplayPreview : ExportColorMode.EditorColors;
            }
        }

        private void Scale_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            if (sender == RbImgScaleCustom && TxtImgScaleCustom != null)
            {
                TxtImgScaleCustom.Focus();
                TxtImgScaleCustom.SelectAll();
            }
            else if (sender == RbGifScaleCustom && TxtGifScaleCustom != null)
            {
                TxtGifScaleCustom.Focus();
                TxtGifScaleCustom.SelectAll();
            }
            else if (sender == RbSeqScaleCustom && TxtSeqScaleCustom != null)
            {
                TxtSeqScaleCustom.Focus();
                TxtSeqScaleCustom.SelectAll();
            }

            UpdateContextualState();
            UpdatePreviewText();
        }

        private void Loop_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            if (sender == RbGifLoopCount && TxtGifLoopCount != null)
            {
                TxtGifLoopCount.Focus();
                TxtGifLoopCount.SelectAll();
            }
        }

        private void ColorMode_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            UpdateContextualState();
            UpdatePreviewText();
        }

        private void Format_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            UpdatePreviewText();
        }

        private void Options_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            // Mutually exclusive image frame exports
            if (sender == ChkImgSpritesheet && ChkImgSpritesheet.IsChecked == true)
            {
                ChkImgSeparateFrames.IsChecked = false;
            }
            else if (sender == ChkImgSeparateFrames && ChkImgSeparateFrames.IsChecked == true)
            {
                ChkImgSpritesheet.IsChecked = false;
            }

            UpdatePreviewText();
        }

        private void UpdateContextualState()
        {
            if (_spriteState == null) return;

            int frames = _spriteState.Frames.Count;
            if (frames <= 1)
            {
                if (ChkImgSpritesheet != null)
                {
                    ChkImgSpritesheet.IsEnabled = false;
                    ChkImgSpritesheet.ToolTip = "Document contains only a single frame.";
                }
                if (ChkImgSeparateFrames != null)
                {
                    ChkImgSeparateFrames.IsEnabled = false;
                    ChkImgSeparateFrames.ToolTip = "Document contains only a single frame.";
                }
                if (ChkGifAllFrames != null)
                {
                    ChkGifAllFrames.IsEnabled = false;
                    ChkGifAllFrames.ToolTip = "Document contains only a single frame.";
                }
            }

            if (ChkGifDither != null)
            {
                bool isDisplay = RbGifColorsDisplay.IsChecked == true;
                ChkGifDither.IsEnabled = isDisplay;
                ChkGifDither.ToolTip = isDisplay
                    ? "Applies Floyd-Steinberg error diffusion dithering to display simulation colors."
                    : "Dithering requires Display preview color mode.";
            }

            int imgScale = GetSelectedScale("ImgScale");
            if (ChkImgGrid != null)
            {
                ChkImgGrid.IsEnabled = imgScale >= 2;
                ChkImgGrid.ToolTip = imgScale >= 2
                    ? "Renders subtle pixel grid lines overlay."
                    : "Grid overlay requires a scale of 2x or higher.";
            }
        }

        private void UpdatePreviewText()
        {
            if (_spriteState == null) return;

            int w = _spriteState.Width;
            int h = _spriteState.Height;
            int frames = _spriteState.Frames.Count;

            int imgScale = GetSelectedScale("ImgScale");
            string imgFormatName = RbBmp.IsChecked == true ? "BMP" : "PNG";

            if (ChkImgSpritesheet.IsChecked == true && frames > 1)
            {
                TxtImgResult.Text = string.Create(CultureInfo.InvariantCulture, $"Result: {w * imgScale * frames} × {h * imgScale} px • Spritesheet ({frames} frames)");
            }
            else if (ChkImgSeparateFrames.IsChecked == true && frames > 1)
            {
                TxtImgResult.Text = string.Create(CultureInfo.InvariantCulture, $"Result: {w * imgScale} × {h * imgScale} px • {frames} separate {imgFormatName} files");
            }
            else
            {
                TxtImgResult.Text = string.Create(CultureInfo.InvariantCulture, $"Result: {w * imgScale} × {h * imgScale} px • Single {imgFormatName} image");
            }

            int gifScale = GetSelectedScale("GifScale");
            int gifFrames = ChkGifAllFrames.IsChecked == true ? frames : 1;
            int fps = int.TryParse(TxtGifFps.Text, out int parsedFps) ? Math.Clamp(parsedFps, 1, 60) : 12;
            double durationSec = (double)gifFrames / fps;
            TxtGifResult.Text = string.Create(CultureInfo.InvariantCulture, $"Result: {w * gifScale} × {h * gifScale} px • {gifFrames} frame{(gifFrames > 1 ? "s" : "")} @ {fps} fps ({durationSec:F2}s)");

            int seqScale = GetSelectedScale("SeqScale");
            TxtSeqResult.Text = string.Create(CultureInfo.InvariantCulture, $"Result: {w * seqScale} × {h * seqScale} px per frame • {frames} PNG files total");

            UpdateContextualState();
        }

        private static System.Collections.Generic.IEnumerable<T> FindLogicalChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                foreach (object rawChild in LogicalTreeHelper.GetChildren(depObj))
                {
                    if (rawChild is DependencyObject child)
                    {
                        if (child is T t)
                            yield return t;

                        foreach (T childOfChild in FindLogicalChildren<T>(child))
                        {
                            yield return childOfChild;
                        }
                    }
                }
            }
        }
    }
}
