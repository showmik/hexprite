using Hexprite.Services;

namespace Hexprite.ViewModels
{
    public partial class MainViewModel
    {
        private bool? _isLinkedSourceExpandedOverride;

        public bool IsDisplayPreviewExpanded
        {
            get => UserPreferencesService.Get().IsDisplayPreviewExpanded;
            set
            {
                if (UserPreferencesService.Get().IsDisplayPreviewExpanded != value)
                {
                    UserPreferencesService.Update(p => p.IsDisplayPreviewExpanded = value);
                    OnPropertyChanged();
                }
            }
        }

        public bool IsLinkedSourceExpanded
        {
            get
            {
                if (_isLinkedSourceExpandedOverride.HasValue)
                    return _isLinkedSourceExpandedOverride.Value;

                if (IsLinked)
                    return true;

                return UserPreferencesService.Get().IsLinkedSourceExpanded;
            }
            set
            {
                if (_isLinkedSourceExpandedOverride != value)
                {
                    _isLinkedSourceExpandedOverride = value;
                    if (!IsLinked)
                    {
                        UserPreferencesService.Update(p => p.IsLinkedSourceExpanded = value);
                    }
                    OnPropertyChanged();
                }
            }
        }

        public bool IsHardwarePreviewExpanded
        {
            get => UserPreferencesService.Get().IsHardwarePreviewExpanded;
            set
            {
                if (UserPreferencesService.Get().IsHardwarePreviewExpanded != value)
                {
                    UserPreferencesService.Update(p => p.IsHardwarePreviewExpanded = value);
                    OnPropertyChanged();
                }
            }
        }

        public bool IsCodeGenerationExpanded
        {
            get => UserPreferencesService.Get().IsCodeGenerationExpanded;
            set
            {
                if (UserPreferencesService.Get().IsCodeGenerationExpanded != value)
                {
                    UserPreferencesService.Update(p => p.IsCodeGenerationExpanded = value);
                    OnPropertyChanged();
                }
            }
        }

        public bool IsImportExpanded
        {
            get => UserPreferencesService.Get().IsImportExpanded;
            set
            {
                if (UserPreferencesService.Get().IsImportExpanded != value)
                {
                    UserPreferencesService.Update(p => p.IsImportExpanded = value);
                    OnPropertyChanged();
                }
            }
        }
    }
}
