using System;
using Hexprite.Core;
using Hexprite.ViewModels;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FontViewModelTests
    {
        public FontViewModelTests()
        {
            WpfTestHelper.EnsureApplication();
        }

        [Fact]
        public void Constructor_InitializesWithDefaultDocument()
        {
            var vm = new FontViewModel();

            Assert.NotNull(vm.Document);
            Assert.Equal(8, vm.Document.MaxCellWidth);
            Assert.Equal(8, vm.Document.CellHeight);
            Assert.True(vm.IsNewlyCreated);
        }

        [Fact]
        public void SetDocument_ResetsUndoStackAndSetsProperties()
        {
            var vm = new FontViewModel();
            var newDoc = FontDocument.CreateNew(16, 16);
            newDoc.FontName = "TestFont";

            // Act
            vm.Document = newDoc;

            // Assert
            Assert.False(vm.IsNewlyCreated);
            Assert.False(vm.IsDirty);
            Assert.Equal(newDoc, vm.Document);
        }

        [Fact]
        public void UndoRedo_WorksCorrectlyForGlyphChanges()
        {
            var vm = new FontViewModel();
            vm.Document.ActiveGlyphIndex = 65;
            
            // Trigger a save state
            vm.ScrubStartedCommand.Execute(null);
            
            // Modify glyph
            vm.Document.Glyphs[65].Pixels[0] = true;
            
            vm.ScrubEndedCommand.Execute(null);
            
            Assert.True(vm.Document.Glyphs[65].Pixels[0]);

            // Undo
            vm.UndoCommand.Execute(null);
            Assert.False(vm.Document.Glyphs[65].Pixels[0]);

            // Redo
            vm.RedoCommand.Execute(null);
            Assert.True(vm.Document.Glyphs[65].Pixels[0]);
        }
    }
}
