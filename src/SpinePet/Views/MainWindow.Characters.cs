using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SpinePet.Infrastructure;
using SpinePet.Models;
using SpinePet.Services;
using SpinePet.ViewModels;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace SpinePet.Views;

public partial class MainWindow
{
    private void RefreshCharacterList()
    {
        string? selectedId = SelectedCharacter?.Id;
        Dictionary<string, CharacterViewModel> existing = Characters.ToDictionary(
            character => character.Id,
            StringComparer.Ordinal);
        HashSet<string> currentIds = new(StringComparer.Ordinal);

        foreach (CharacterConfig character in _characterManager.Characters)
        {
            currentIds.Add(character.Id);
            List<string> animationNames = _characterManager.RenderHost
                .GetAnimationNames(character.Id)
                .ToList();
            double maximumScale = Math.Clamp(
                _characterManager.RenderHost.GetMaxScale(character.Id),
                MinimumMaximumScale,
                DefaultMaxScale);
            double scale = Math.Clamp(
                character.Scale > 0 ? character.Scale : DefaultScale,
                MinimumScale,
                maximumScale);

            if (animationNames.Count == 0 &&
                !string.IsNullOrEmpty(character.ConfiguredAnimation))
            {
                animationNames.Add(character.ConfiguredAnimation);
            }

            if (existing.TryGetValue(
                character.Id,
                out CharacterViewModel? viewModel))
            {
                UpdateCharacterViewModel(
                    viewModel,
                    character,
                    animationNames,
                    maximumScale,
                    scale);
            }
            else
            {
                CharacterViewModel newViewModel = new()
                {
                    Id = character.Id
                };
                UpdateCharacterViewModel(
                    newViewModel,
                    character,
                    animationNames,
                    maximumScale,
                    scale);
                Characters.Add(newViewModel);
            }
        }

        for (int index = Characters.Count - 1; index >= 0; index--)
        {
            if (!currentIds.Contains(Characters[index].Id))
            {
                Characters.RemoveAt(index);
            }
        }

        SelectedCharacter =
            Characters.FirstOrDefault(character => character.Id == selectedId) ??
            Characters.FirstOrDefault();
        CharacterCards.SelectedItem = SelectedCharacter;
        SyncSelectedCharacterState();
    }

    private void UpdateCharacterViewModel(
        CharacterViewModel viewModel,
        CharacterConfig character,
        IReadOnlyList<string> animationNames,
        double maximumScale,
        double scale)
    {
        viewModel.Name = character.Name;
        viewModel.ThumbnailPath = character.TexturePath;
        viewModel.Scale = scale;
        viewModel.MaxScale = maximumScale;
        viewModel.PositionX = (int)character.PositionX;
        viewModel.PositionY = (int)character.PositionY;
        viewModel.IsVisible = character.Visible;
        viewModel.IsLoading = _characterManager.IsCharacterLoading(character.Id);
        viewModel.UpdateAnimationNames(animationNames);
        viewModel.ConfiguredAnimation = character.ConfiguredAnimation;
        viewModel.AnimationSpeed = character.AnimationSpeed;
    }

    private void OnAddCharacter(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Select Spine .skel file",
            Filter = "Spine Skeleton|*.skel|All Files|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            ImportSkeleton(dialog.FileName);
        }
    }

    private void OnScanResources(object sender, RoutedEventArgs e)
    {
        foreach (CharacterResourceFiles resources in
                 _resourceDiscovery.Discover(AppPaths.ResourceDirectory))
        {
            _characterManager.AddCharacter(resources);
        }
    }

    private void ImportSkeleton(string skeletonPath)
    {
        CharacterResourceFiles? resources =
            _resourceDiscovery.DiscoverForSkeleton(skeletonPath);
        if (resources == null)
        {
            MessageBox.Show(
                "The selected folder must contain matching .skel, .atlas, and .png files.",
                "Missing Files",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _characterManager.AddCharacter(resources);
    }

    private void OnCharacterSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        SelectedCharacter =
            CharacterCards.SelectedItem as CharacterViewModel;
        SyncSelectedCharacterState();
    }

    private void OnMoveSelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateOverlayState();
    }

    private async void OnToggleCharacter(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not CharacterViewModel viewModel ||
            viewModel.IsLoading)
        {
            return;
        }

        CharacterConfig? character = _characterManager.Characters.FirstOrDefault(
            item => item.Id == viewModel.Id);
        if (character == null)
        {
            return;
        }

        if (viewModel.IsVisible)
        {
            _characterManager.HideCharacter(character);
        }
        else
        {
            try
            {
                await _characterManager.ShowCharacterAsync(character);
            }
            catch (Exception exception)
            {
                AppLogger.Write(
                    nameof(MainWindow),
                    $"show-character-failed id={character.Id} message={exception.Message}");
                MessageBox.Show(
                    "The character could not be displayed. Check the local log for details.",
                    "Render Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        UpdateOverlayState();
        if (character.Visible && _isConfigMode)
        {
            ShowOverlay();
            Activate();
        }
    }
}
