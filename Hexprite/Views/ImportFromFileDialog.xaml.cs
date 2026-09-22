using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Microsoft.Win32;

namespace Hexprite.Views
{
    public class SelectableSprite : Hexprite.Services.DetectedSprite, INotifyPropertyChanged
    {
        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        private WriteableBitmap? _previewImage;
        public WriteableBitmap? PreviewImage
        {
            get => _previewImage;
            set
            {
                if (_previewImage != value)
                {
                    _previewImage = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewImage)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class ImportFromFileDialog : Window, INotifyPropertyChanged
    {
        public (string FilePath, List<DetectedSprite> SelectedSprites)? Result { get; private set; }
        
        public ObservableCollection<SelectableSprite> Sprites { get; } = [];
        
        // Property for binding
        public string SelectedFilePath
        {
            get => (string)GetValue(SelectedFilePathProperty);
            set => SetValue(SelectedFilePathProperty, value);
        }

        public static readonly DependencyProperty SelectedFilePathProperty =
            DependencyProperty.Register("SelectedFilePath", typeof(string), typeof(ImportFromFileDialog), new PropertyMetadata(string.Empty));

        private bool? _isAllSelected = true;
        public bool? IsAllSelected
        {
            get => _isAllSelected;
            set
            {
                if (_isAllSelected != value)
                {
                    _isAllSelected = value;
                    OnPropertyChanged(nameof(IsAllSelected));
                    if (value.HasValue)
                    {
                        SetAllSelected(value.Value);
                    }
                }
            }
        }

        private bool _hasNoSprites;
        public bool HasNoSprites
        {
            get => _hasNoSprites;
            private set
            {
                if (_hasNoSprites != value)
                {
                    _hasNoSprites = value;
                    OnPropertyChanged(nameof(HasNoSprites));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private readonly IFileImportExportService _importExportService;
        private readonly ICodeGeneratorService _codeGen;
        private bool _isUpdatingAllSelected;

        public ImportFromFileDialog(IFileImportExportService importExportService, ICodeGeneratorService codeGen, string? initialFilePath = null)
        {
            _importExportService = importExportService;
            _codeGen = codeGen;
            InitializeComponent();
            DataContext = this;

            if (!string.IsNullOrEmpty(initialFilePath))
            {
                SelectedFilePath = initialFilePath;
                ScanFile();
            }
        }

        private void CaptionClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BrowseBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Source Files (*.c;*.h;*.hpp;*.cpp;*.ino;*.py)|*.c;*.h;*.hpp;*.cpp;*.ino;*.py|All Files (*.*)|*.*",
                Title = "Select Source File",
            };

            if (dlg.ShowDialog() == true)
            {
                SelectedFilePath = dlg.FileName;
                ScanFile();
            }
        }

        private void ScanFile()
        {
            foreach (var s in Sprites)
                s.PropertyChanged -= Sprite_PropertyChanged;
            Sprites.Clear();

            try
            {
                var detected = _importExportService.ExtractSpritesFromFile(SelectedFilePath);
                foreach (var s in detected)
                {
                    var sprite = new SelectableSprite
                    {
                        Name = s.Name,
                        Width = s.Width,
                        Height = s.Height,
                        CodeSnippet = s.CodeSnippet,
                        Format = s.Format,
                        IsSelected = true,
                    };

                    // Generate preview thumbnail
                    try
                    {
                        sprite.PreviewImage = RenderPreview(s);
                    }
                    catch { /* preview is optional, don't block import */ }

                    sprite.PropertyChanged += Sprite_PropertyChanged;
                    Sprites.Add(sprite);
                }

                HasNoSprites = Sprites.Count == 0;
                UpdateSelectionState();

                if (HasNoSprites)
                {
                    MessageDialog.Show("No valid arrays/sprites detected in this file.", "Scan Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                HasNoSprites = true;
                UpdateSelectionState();
                MessageDialog.Show($"Error scanning file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Sprite_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectableSprite.IsSelected))
            {
                UpdateSelectionState();
            }
        }

        private void SetAllSelected(bool isSelected)
        {
            _isUpdatingAllSelected = true;
            try
            {
                foreach (var s in Sprites)
                {
                    s.IsSelected = isSelected;
                }
            }
            finally
            {
                _isUpdatingAllSelected = false;
            }
            UpdateSelectionState();
        }

        private void UpdateSelectionState()
        {
            if (_isUpdatingAllSelected) return;

            bool hasAny = Sprites.Any(s => s.IsSelected);
            bool allSelected = Sprites.Count > 0 && Sprites.All(s => s.IsSelected);
            bool noneSelected = Sprites.Count == 0 || Sprites.All(s => !s.IsSelected);

            _isAllSelected = allSelected ? true : (noneSelected ? false : null);
            OnPropertyChanged(nameof(IsAllSelected));

            if (BtnImport != null)
                BtnImport.IsEnabled = hasAny;
        }

        private WriteableBitmap? RenderPreview(DetectedSprite sprite)
        {
            if (sprite.Width <= 0 || sprite.Height <= 0 || sprite.Width > 512 || sprite.Height > 512)
                return null;

            var state = new SpriteState(sprite.Width, sprite.Height);
            MainViewModel.ParseCodeToState(_codeGen, sprite.Format, sprite.CodeSnippet, state);

            var bmp = new WriteableBitmap(sprite.Width, sprite.Height, 96, 96, PixelFormats.Bgra32, palette: null);
            var pixels = new byte[sprite.Width * sprite.Height * 4];

            for (int i = 0; i < state.Pixels.Length; i++)
            {
                int offset = i * 4;
                if (state.Pixels[i])
                {
                    // White pixel on dark background
                    pixels[offset]     = 255; // B
                    pixels[offset + 1] = 255; // G
                    pixels[offset + 2] = 255; // R
                    pixels[offset + 3] = 255; // A
                }
                else
                {
                    // Dark background
                    pixels[offset]     = 30;  // B
                    pixels[offset + 1] = 30;  // G
                    pixels[offset + 2] = 30;  // R
                    pixels[offset + 3] = 255; // A
                }
            }

            bmp.WritePixels(new Int32Rect(0, 0, sprite.Width, sprite.Height), pixels, sprite.Width * 4, 0);
            bmp.Freeze();
            return bmp;
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var selected = Sprites.Where(s => s.IsSelected).Cast<DetectedSprite>().ToList();
            if (selected.Count == 0)
            {
                MessageDialog.Show("Please select at least one sprite to import.");
                return;
            }

            Result = (SelectedFilePath, selected);
            DialogResult = true;
            Close();
        }
        
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}