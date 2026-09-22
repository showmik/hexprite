using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Rendering;
using Hexprite.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Hexprite.ViewModels
{
    public partial class MainViewModel
    {
        // ── Commands ──────────────────────────────────────────────────────
        public IRelayCommand UndoCommand { get; }
        public IRelayCommand RedoCommand { get; }
        public bool CanUndo => _historyService.CanUndo && !IsProcessing;
        public bool CanRedo => _historyService.CanRedo && !IsProcessing;
        public IRelayCommand ClearCommand { get; }
        public IRelayCommand InvertCommand { get; }
        public IRelayCommand OutlineCommand { get; }
        public IRelayCommand DeleteSelectionCommand { get; }
        public IRelayCommand CopyExportedCodeCommand { get; }
        public IRelayCommand CopyExportedSketchCommand { get; }
        public IAsyncRelayCommand ExportArduinoSketchFolderCommand { get; }
        public IRelayCommand GenerateCodeCommand { get; }
        public IRelayCommand ExportImageCommand { get; }
        public IRelayCommand CopySelectionCommand { get; }
        public IRelayCommand CutSelectionCommand { get; }
        public IRelayCommand PasteCommand { get; }
        public IRelayCommand DeselectCommand { get; }
        public IRelayCommand ReselectCommand { get; }
        public IRelayCommand SelectAllCommand { get; }
        public IRelayCommand NewLayerFromSelectionCommand { get; }
        public IRelayCommand<string> SelectToolCommand { get; }
        public IRelayCommand IncreasePreviewScaleCommand { get; }
        public IRelayCommand DecreasePreviewScaleCommand { get; }
        public IRelayCommand AddLayerCommand { get; }
        public IRelayCommand DeleteLayerCommand { get; }
        public IRelayCommand DuplicateLayerCommand { get; }
        public IRelayCommand MergeLayerCommand { get; }
        public IRelayCommand MoveLayerUpCommand { get; }
        public IRelayCommand MoveLayerDownCommand { get; }
        public IRelayCommand RenameLayerCommand { get; }
        public IRelayCommand<FrameItemViewModel?> IncreaseFrameDelayCommand { get; }
        public IRelayCommand<FrameItemViewModel?> DecreaseFrameDelayCommand { get; }
        public IRelayCommand<string> SetFrameDelayCommand { get; }
        public IAsyncRelayCommand RotateCanvasCWCommand { get; }
        public IAsyncRelayCommand RotateCanvasCCWCommand { get; }
        public IAsyncRelayCommand RotateCanvas180Command { get; }
        public IAsyncRelayCommand FlipCanvasHorizontalCommand { get; }
        public IAsyncRelayCommand FlipCanvasVerticalCommand { get; }
        public IRelayCommand FlipSelectionHorizontalCommand { get; }
        public IRelayCommand FlipSelectionVerticalCommand { get; }
        public IRelayCommand BeginSelectionTransformCommand { get; }
        public IRelayCommand ResetSymmetryCommand { get; }
        public IRelayCommand ResetBrushCommand { get; }

    }
}
