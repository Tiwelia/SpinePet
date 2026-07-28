using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SpinePet.Models;
using ComboBox = System.Windows.Controls.ComboBox;

namespace SpinePet.Views;

public partial class MainWindow
{
    private void OnCharacterCardsPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        ScrollViewer? characterScrollViewer =
            FindVisualChild<ScrollViewer>(CharacterCards);
        if (characterScrollViewer == null)
        {
            return;
        }

        if (CanScroll(characterScrollViewer, e.Delta))
        {
            ScrollByDelta(characterScrollViewer, e.Delta);
            e.Handled = true;
            return;
        }

        ScrollByDelta(MainScrollViewer, e.Delta);
        e.Handled = true;
    }

    private void OnCharacterScaleChanged(
        string characterId,
        double maximumScale,
        double currentScale)
    {
        var viewModel = Characters.FirstOrDefault(
            character => character.Id == characterId);
        if (viewModel != null)
        {
            viewModel.MaxScale = Math.Clamp(
                maximumScale,
                MinimumMaximumScale,
                DefaultMaxScale);
            viewModel.Scale = Math.Clamp(
                currentScale,
                MinimumScale,
                viewModel.MaxScale);
        }

        if (SelectedCharacter?.Id != characterId)
        {
            return;
        }

        _isRefreshingSelection = true;
        SelectedScaleMax = Math.Clamp(
            maximumScale,
            MinimumMaximumScale,
            DefaultMaxScale);
        SelectedScale = Math.Clamp(
            currentScale,
            MinimumScale,
            SelectedScaleMax);
        SelectedScalePercent = ConvertScaleToPercent(
            SelectedScale,
            SelectedScaleMax);
        SelectedCharacter.MaxScale = SelectedScaleMax;
        SelectedCharacter.Scale = SelectedScale;
        _isRefreshingSelection = false;
    }

    private void OnScaleChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isRefreshingSelection || SelectedCharacter == null)
        {
            return;
        }

        CharacterConfig? character = FindSelectedCharacterConfig();
        if (character == null)
        {
            return;
        }

        double effectiveMaximumScale =
            _characterManager.RenderHost.IsCharacterVisible(character.Id)
                ? _characterManager.RenderHost.GetMaxScale(character.Id)
                : SelectedScaleMax;

        if (!NearlyEquals(SelectedScaleMax, effectiveMaximumScale))
        {
            _isRefreshingSelection = true;
            SelectedScaleMax = effectiveMaximumScale;
            double rendererScale =
                _characterManager.RenderHost.GetCurrentScale(character.Id);
            double currentScale = Math.Clamp(
                rendererScale > 0 ? rendererScale : SelectedScale,
                MinimumScale,
                effectiveMaximumScale);
            SelectedScale = currentScale;
            SelectedScalePercent = ConvertScaleToPercent(
                currentScale,
                effectiveMaximumScale);
            SelectedCharacter.Scale = currentScale;
            character.Scale = currentScale;
            _isRefreshingSelection = false;
            return;
        }

        double scale = Math.Clamp(
            ConvertPercentToScale(
                SelectedScalePercent,
                SelectedScaleMax),
            MinimumScale,
            effectiveMaximumScale);
        character.Scale = scale;
        SelectedCharacter.Scale = scale;
        SelectedScale = scale;
        _characterManager.RenderHost.SetCharacterScale(character.Id, scale);
    }

    private void OnSpeedChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isRefreshingSelection || SelectedCharacter == null)
        {
            return;
        }

        CharacterConfig? character = FindSelectedCharacterConfig();
        if (character == null)
        {
            return;
        }

        double speed = SelectedSpeed / 100.0;
        character.AnimationSpeed = speed;
        SelectedCharacter.AnimationSpeed = speed;
        _characterManager.RenderHost.SetCharacterSpeed(character.Id, speed);
    }

    private void OnResetScale(object sender, RoutedEventArgs e)
    {
        if (SelectedCharacter != null)
        {
            SelectedScalePercent = ConvertScaleToPercent(
                DefaultScale,
                SelectedScaleMax);
        }
    }

    private void OnResetSpeed(object sender, RoutedEventArgs e)
    {
        if (SelectedCharacter != null)
        {
            SelectedSpeed = 100;
        }
    }

    private void OnResetSelectedPosition(object sender, RoutedEventArgs e)
    {
        CharacterConfig? character = FindSelectedCharacterConfig();
        if (character == null || SelectedCharacter == null)
        {
            return;
        }

        _characterManager.RenderHost.ResetCharacterPosition(character.Id);
        SelectedCharacter.PositionX = (int)character.PositionX;
        SelectedCharacter.PositionY = (int)character.PositionY;
    }

    private void OnAnimationChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isRefreshingSelection ||
            sender is not ComboBox ||
            string.IsNullOrEmpty(SelectedAnimation) ||
            SelectedCharacter == null)
        {
            return;
        }

        CharacterConfig? character = FindSelectedCharacterConfig();
        if (character == null)
        {
            return;
        }

        character.ConfiguredAnimation = SelectedAnimation;
        SelectedCharacter.ConfiguredAnimation = SelectedAnimation;
        _characterManager.RenderHost.PlayCharacterAnimation(
            character.Id,
            SelectedAnimation,
            repeat: true);
    }

    private void OnRemoveSelectedCharacter(object sender, RoutedEventArgs e)
    {
        CharacterConfig? character = FindSelectedCharacterConfig();
        if (character == null)
        {
            return;
        }

        _characterManager.RemoveCharacter(character);
        ClearSelection();
    }

    private void OnOverlayCharacterMoved(
        string characterId,
        double left,
        double top)
    {
        CharacterConfig? character = _characterManager.Characters.FirstOrDefault(
            item => item.Id == characterId);
        if (character == null)
        {
            return;
        }

        character.PositionX = left;
        character.PositionY = top;

        var viewModel = Characters.FirstOrDefault(
            item => item.Id == characterId);
        if (viewModel != null)
        {
            viewModel.PositionX = (int)left;
            viewModel.PositionY = (int)top;
        }
    }

    private void OnExitConfiguration(object sender, RoutedEventArgs e)
    {
        _characterManager.SaveAllState();
        _isConfigMode = false;
        ApplyConfigMode();
    }

    private CharacterConfig? FindSelectedCharacterConfig()
    {
        string? characterId = SelectedCharacter?.Id;
        return characterId == null
            ? null
            : _characterManager.Characters.FirstOrDefault(
                character => character.Id == characterId);
    }

    private static double ConvertScaleToPercent(
        double scale,
        double maximumScale)
    {
        if (maximumScale <= MinimumScale)
        {
            return 100;
        }

        double normalized =
            (scale - MinimumScale) / (maximumScale - MinimumScale);
        return Math.Clamp(normalized * 100, 0, 100);
    }

    private static double ConvertPercentToScale(
        double percent,
        double maximumScale)
    {
        double normalized = Math.Clamp(percent, 0, 100) / 100.0;
        return MinimumScale +
            ((maximumScale - MinimumScale) * normalized);
    }

    private static bool CanScroll(
        ScrollViewer scrollViewer,
        int wheelDelta)
    {
        if (scrollViewer.ScrollableHeight <= 0)
        {
            return false;
        }

        return wheelDelta < 0
            ? scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight
            : scrollViewer.VerticalOffset > 0;
    }

    private static void ScrollByDelta(
        ScrollViewer scrollViewer,
        int wheelDelta)
    {
        double step = Math.Max(
            48,
            SystemParameters.WheelScrollLines * 16);
        double nextOffset =
            scrollViewer.VerticalOffset - (wheelDelta / 120.0 * step);
        scrollViewer.ScrollToVerticalOffset(
            Math.Clamp(nextOffset, 0, scrollViewer.ScrollableHeight));
    }

    private static T? FindVisualChild<T>(DependencyObject? parent)
        where T : DependencyObject
    {
        if (parent == null)
        {
            return null;
        }

        for (int index = 0;
             index < VisualTreeHelper.GetChildrenCount(parent);
             index++)
        {
            DependencyObject child =
                VisualTreeHelper.GetChild(parent, index);
            if (child is T typedChild)
            {
                return typedChild;
            }

            T? descendant = FindVisualChild<T>(child);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
    }
}
