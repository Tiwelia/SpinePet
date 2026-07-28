using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private void RefreshKnownResources()
    {
        _knownResources = _resourceDiscovery
            .DiscoverAll(AppPaths.ResourceDirectory)
            .GroupBy(
                resource => resource.Identity.ResourceName,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(
                        resource => resource.ResourceType,
                        StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        resourceGroup => resourceGroup.Key,
                        resourceGroup => resourceGroup.First(),
                        StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
    }

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
        CharacterIdentity identity =
            _characterManager.GetCharacterIdentity(character);
        IReadOnlyCollection<string> availableResourceTypes =
            _knownResources.TryGetValue(
                identity.ResourceName,
                out Dictionary<string, CharacterResourceFiles>? variants)
                ? variants.Keys
                : [character.ResourceType];
        viewModel.Name = character.Name;
        viewModel.SkinLabel = identity.SkinLabel;
        viewModel.UpdateResourceTypes(
            character.ResourceType,
            availableResourceTypes);
        viewModel.ThumbnailPath =
            _characterManager.GetCharacterThumbnailPath(character);
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

    private async void OnAddCharacter(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Select a Spine skeleton or UnityFS character bundle",
            Filter =
                "Spine Skeleton|*.skel|" +
                "UnityFS Character Bundle|*.bundle;*.*|" +
                "All Files|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (string.Equals(
            Path.GetExtension(dialog.FileName),
            ".skel",
            StringComparison.OrdinalIgnoreCase))
        {
            ImportSkeleton(dialog.FileName);
            return;
        }

        Button? addButton = sender as Button;
        if (addButton != null)
        {
            addButton.IsEnabled = false;
        }

        Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        try
        {
            CharacterBundleImportResult result =
                await _bundleImporter.ImportAsync(
                    dialog.FileName,
                    AppPaths.ResourceDirectory);
            RefreshKnownResources();

            bool cardAdded = false;
            bool hasCharacterCard = _characterManager.Characters.Any(
                character => string.Equals(
                    _characterManager
                        .GetCharacterIdentity(character)
                        .ResourceName,
                    result.Resources.Identity.ResourceName,
                    StringComparison.OrdinalIgnoreCase));
            if (string.Equals(
                result.Resources.ResourceType,
                CharacterResourceTypes.Standing,
                StringComparison.OrdinalIgnoreCase))
            {
                cardAdded =
                    _characterManager.AddCharacter(result.Resources);
            }

            RefreshCharacterList();
            string message = cardAdded
                ? "The character was extracted and added to the library."
                : string.Equals(
                    result.Resources.ResourceType,
                    CharacterResourceTypes.Standing,
                    StringComparison.OrdinalIgnoreCase)
                    ? "The standing resources were extracted and refreshed."
                    : hasCharacterCard
                        ? "The character state was extracted and is now " +
                          "available from the card's context menu."
                        : "The character state was extracted. Add its standing " +
                          "bundle to create the character card.";
            Mouse.OverrideCursor = null;
            MessageBox.Show(
                $"{message}{Environment.NewLine}{result.DestinationDirectory}",
                "Character Imported",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(MainWindow),
                $"bundle-import-failed message={exception.Message}");
            Mouse.OverrideCursor = null;
            MessageBox.Show(
                exception.Message,
                "Character Import Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            if (addButton != null)
            {
                addButton.IsEnabled = true;
            }
        }
    }

    private void OnScanResources(object sender, RoutedEventArgs e)
    {
        RefreshKnownResources();
        foreach (CharacterResourceFiles resources in _knownResources
                     .Values
                     .SelectMany(variants => variants.Values)
                     .Where(resources => string.Equals(
                         resources.ResourceType,
                         CharacterResourceTypes.Standing,
                         StringComparison.OrdinalIgnoreCase))
                     .OrderBy(
                         resources => resources.Identity.DisplayName,
                         StringComparer.OrdinalIgnoreCase)
                     .ThenBy(
                         resources => resources.Identity.SkinCode,
                         StringComparer.OrdinalIgnoreCase))
        {
            _characterManager.AddCharacter(resources);
        }

        RefreshCharacterList();
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

        if (!string.Equals(
            resources.ResourceType,
            CharacterResourceTypes.Standing,
            StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "A character card must be created from standing resources. " +
                "Import aim and cover states from their UnityFS bundles, or " +
                "place them in the matching res state directory and scan.",
                "Standing Resources Required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _characterManager.AddCharacter(resources);
    }

    private async void OnCharacterResourceTypeExecuted(
        object sender,
        ExecutedRoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement menuElement ||
            menuElement.DataContext is not CharacterViewModel viewModel ||
            e.Parameter is not string requestedType ||
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

        CharacterIdentity identity =
            _characterManager.GetCharacterIdentity(character);
        if (!_knownResources.TryGetValue(
                identity.ResourceName,
                out Dictionary<string, CharacterResourceFiles>? variants) ||
            !variants.TryGetValue(
                requestedType,
                out CharacterResourceFiles? resources))
        {
            MessageBox.Show(
                $"No {requestedType} resources are available for this skin.",
                "Character State Unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            await _characterManager.SwitchCharacterResourcesAsync(
                character,
                resources);
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(MainWindow),
                $"resource-switch-failed id={character.Id} " +
                $"type={requestedType} message={exception.Message}");
            MessageBox.Show(
                "The character state could not be loaded. " +
                "Check the local log for details.",
                "Character State Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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
