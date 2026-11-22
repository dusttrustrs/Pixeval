// Copyright (c) Pixeval.
// Licensed under the GPL-3.0 License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Pixeval.Controls;
using Pixeval.Models.Options;
using Pixeval.Utilities;
using Pixeval.ViewModels.Viewers;

namespace Pixeval.Views.Viewers;

public partial class IllustrationViewerPage : ContentPage
{
    private const double TopEdgeTapHeight = 72;
    private const double BottomEdgeTapHeight = 84;
    private const double TapMaxDistance = 12;
    private const double TapMaxDurationMs = 400;
    private const double SwipeActivationDistance = 14;
    private const double SwipeCommitDistance = 56;
    private const double SwipeFlickDistance = 24;
    private const double SwipeFlickVelocity = 320;

    private readonly HashSet<int> _activeTouchPointers = [];
    private readonly TranslateTransform _viewerSwipeTransform = new();
    private int? _trackedTouchId;
    private Point _touchStartPoint;
    private long _touchStartTimestamp;
    private bool _multiTouchActive;
    private bool _touchStartedInOverlay;
    private bool _isHorizontalSwiping;

    private IllustrationViewerPageViewModel ViewModel => (IllustrationViewerPageViewModel) DataContext!;

    public IllustrationViewerPage() : this(null)
    {
    }

    public IllustrationViewerPage(IllustrationViewerPageViewModel? viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        I.RenderTransform = _viewerSwipeTransform;

        EntryViewerFloatingPaneView.AddHandler(PointerPressedEvent, OnViewerPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        EntryViewerFloatingPaneView.AddHandler(PointerMovedEvent, OnViewerPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        EntryViewerFloatingPaneView.AddHandler(PointerReleasedEvent, OnViewerPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        EntryViewerFloatingPaneView.AddHandler(PointerCaptureLostEvent, OnViewerPointerCaptureLost, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        _ = KeyboardShortcut.TryExecute(e, Key.Left, ViewModel.PrevCommand)
            || KeyboardShortcut.TryExecute(e, Key.Right, ViewModel.NextCommand)
            || KeyboardShortcut.TryExecute(e, Key.Up, ViewModel.PrevWorkCommand)
            || KeyboardShortcut.TryExecute(e, Key.Down, ViewModel.NextWorkCommand);
    }

    private void OnViewerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Pointer.Type is not PointerType.Touch)
            return;

        _ = _activeTouchPointers.Add(e.Pointer.Id);
        if (_activeTouchPointers.Count > 1)
        {
            _multiTouchActive = true;
            ResetSwipeVisual();
            return;
        }

        _trackedTouchId = e.Pointer.Id;
        _multiTouchActive = false;
        _isHorizontalSwiping = false;
        _touchStartPoint = e.GetPosition(ViewerGesturePanel);
        _touchStartTimestamp = Stopwatch.GetTimestamp();

        var pointInPaneView = e.GetPosition(EntryViewerFloatingPaneView);
        _touchStartedInOverlay = IsPointInsideVisibleBottomOverlay(_touchStartPoint)
            || EntryViewerFloatingPaneView.IsPointInsideVisibleFloatingPane(EntryViewerFloatingPaneView, pointInPaneView);
    }

    private void OnViewerPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Type is not PointerType.Touch
            || _trackedTouchId != e.Pointer.Id
            || _multiTouchActive
            || _touchStartedInOverlay
            || !I.IsSwipeMode)
            return;

        var current = e.GetPosition(ViewerGesturePanel);
        var dx = current.X - _touchStartPoint.X;
        var dy = current.Y - _touchStartPoint.Y;

        if (!_isHorizontalSwiping)
        {
            if (Math.Abs(dx) < SwipeActivationDistance || Math.Abs(dx) <= Math.Abs(dy) * 1.3)
                return;

            if (!CanSwipeHorizontally(dx))
                return;

            _isHorizontalSwiping = true;
        }

        var canNavigate = dx < 0
            ? ViewModel.NextCommand.CanExecute(null)
            : ViewModel.PrevCommand.CanExecute(null);
        _viewerSwipeTransform.X = canNavigate ? dx : dx * 0.25;
    }

    private void OnViewerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer.Type is not PointerType.Touch)
            return;

        _ = _activeTouchPointers.Remove(e.Pointer.Id);
        if (_trackedTouchId != e.Pointer.Id)
            return;

        var endPoint = e.GetPosition(ViewerGesturePanel);
        var dx = endPoint.X - _touchStartPoint.X;
        var dy = endPoint.Y - _touchStartPoint.Y;
        var elapsedMs = Stopwatch.GetElapsedTime(_touchStartTimestamp).TotalMilliseconds;

        var wasMultiTouch = _multiTouchActive;
        var startedInOverlay = _touchStartedInOverlay;
        var wasSwiping = _isHorizontalSwiping;

        _trackedTouchId = null;
        _isHorizontalSwiping = false;
        if (_activeTouchPointers.Count is 0)
            _multiTouchActive = false;

        ResetSwipeVisual();

        if (wasMultiTouch || startedInOverlay)
            return;

        if (wasSwiping)
        {
            var velocity = elapsedMs > 1 ? dx / (elapsedMs / 1000.0) : 0;
            if ((dx <= -SwipeCommitDistance || (dx <= -SwipeFlickDistance && velocity <= -SwipeFlickVelocity))
                && ViewModel.NextCommand.CanExecute(null))
                ViewModel.NextCommand.Execute(null);
            else if ((dx >= SwipeCommitDistance || (dx >= SwipeFlickDistance && velocity >= SwipeFlickVelocity))
                && ViewModel.PrevCommand.CanExecute(null))
                ViewModel.PrevCommand.Execute(null);

            return;
        }

        if (double.Hypot(dx, dy) <= TapMaxDistance && elapsedMs <= TapMaxDurationMs)
            HandleViewerEdgeTap(endPoint);
    }

    private void OnViewerPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (e.Pointer.Type is not PointerType.Touch)
            return;

        _ = _activeTouchPointers.Remove(e.Pointer.Id);
        if (_trackedTouchId == e.Pointer.Id)
        {
            _trackedTouchId = null;
            _isHorizontalSwiping = false;
            ResetSwipeVisual();
        }

        if (_activeTouchPointers.Count is 0)
            _multiTouchActive = false;
    }

    private void HandleViewerEdgeTap(Point point)
    {
        var height = ViewerGesturePanel.Bounds.Height;
        if (height <= 0)
            return;

        if (point.Y <= TopEdgeTapHeight)
        {
            if (!EntryViewerFloatingPaneView.IsDocked)
            {
                var nextVisible = !EntryViewerFloatingPaneView.IsFloatingPaneTouchVisible;
                EntryViewerFloatingPaneView.SetFloatingPaneTouchVisible(nextVisible);
                if (nextVisible)
                    FloatingPane.SetTouchVisible(BottomOverlayPane, false);
            }

            return;
        }

        if (point.Y >= height - BottomEdgeTapHeight)
        {
            var nextVisible = !FloatingPane.IsTouchVisible(BottomOverlayPane);
            FloatingPane.SetTouchVisible(BottomOverlayPane, nextVisible);
            if (nextVisible && !EntryViewerFloatingPaneView.IsDocked && !EntryViewerFloatingPaneView.IsLocked)
                EntryViewerFloatingPaneView.SetFloatingPaneTouchVisible(false);
            return;
        }

        // Tapping the center image area dismisses any open floating overlay without triggering either pane.
        if (FloatingPane.IsTouchVisible(BottomOverlayPane))
            FloatingPane.SetTouchVisible(BottomOverlayPane, false);

        if (!EntryViewerFloatingPaneView.IsDocked && !EntryViewerFloatingPaneView.IsLocked
            && EntryViewerFloatingPaneView.IsFloatingPaneTouchVisible)
            EntryViewerFloatingPaneView.SetFloatingPaneTouchVisible(false);
    }

    private bool CanSwipeHorizontally(double deltaX)
    {
        if (I.SwipeImageViewer?.CurrentPage is not { ViewerScrollView: { } scrollView } page)
            return true;

        if (page.ImageViewer?.Source is not { Size: { Width: > 0, Height: > 0 } imageSize }
            || scrollView.Viewport is not { Width: > 0, Height: > 0 } viewport)
            return true;

        var fitScale = SingleImageViewer.GetPixelScale(viewport, imageSize);
        if (scrollView.ZoomFactor <= fitScale * 1.05)
            return true;

        const double edgeTolerance = 2;
        var renderedWidth = imageSize.Width * scrollView.ZoomFactor;
        var maxOffsetX = double.Max(0, renderedWidth - viewport.Width);
        return deltaX > 0
            ? scrollView.Offset.X <= edgeTolerance
            : scrollView.Offset.X >= maxOffsetX - edgeTolerance;
    }

    private bool IsPointInsideVisibleBottomOverlay(Point pointInGesturePanel)
    {
        if (!BottomOverlayPane.IsEffectivelyVisible || !BottomOverlayPane.IsHitTestVisible || BottomOverlayPane.Opacity <= 0.01)
            return false;

        return BottomOverlayPane.TranslatePoint(default, ViewerGesturePanel) is { } origin
            && new Rect(origin, BottomOverlayPane.Bounds.Size).Contains(pointInGesturePanel);
    }

    private void ResetSwipeVisual()
    {
        _viewerSwipeTransform.X = 0;
    }

    private void PrevButton_OnRightClick(object? sender, ContextRequestedEventArgs e) => ViewModel.PrevWorkCommand.Execute(null);

    private void NextButton_OnRightClick(object? sender, ContextRequestedEventArgs e) => ViewModel.NextWorkCommand.Execute(null);

    private void AutoPlayMenuFlyout_OnOpened(object? sender, RoutedEventArgs e)
    {
        SetMenuItem(AutoPlayInterval1SecondMenuItem, ViewModel.AutoPlayInterval);
        SetMenuItem(AutoPlayInterval3SecondsMenuItem, ViewModel.AutoPlayInterval);
        SetMenuItem(AutoPlayInterval5SecondsMenuItem, ViewModel.AutoPlayInterval);
        SetMenuItem(AutoPlayInterval10SecondsMenuItem, ViewModel.AutoPlayInterval);
        SetMenuItem(AutoPlayInterval15SecondsMenuItem, ViewModel.AutoPlayInterval);
        SetMenuItem(AutoPlayInterval30SecondsMenuItem, ViewModel.AutoPlayInterval);

        SetMenuItem(AutoPlaySequentialModeMenuItem, ViewModel.AutoPlayMode);
        SetMenuItem(AutoPlayLoopPlaybackModeMenuItem, ViewModel.AutoPlayMode);
        SetMenuItem(AutoPlayCurrentWorkScopeMenuItem, ViewModel.AutoPlayScope);
        SetMenuItem(AutoPlayAllWorksScopeMenuItem, ViewModel.AutoPlayScope);
        return;

        static void SetMenuItem(MenuItem menuItem, object value) => menuItem.IsChecked = Equals(value, menuItem.Tag);
    }

    private void AutoPlayIntervalMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: int interval })
            ViewModel.AutoPlayInterval = interval;
    }

    private void AutoPlayModeMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: IllustrationViewerAutoPlayMode mode })
            ViewModel.AutoPlayMode = mode;
    }

    private void AutoPlayScopeMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: IllustrationViewerAutoPlayScope scope })
            ViewModel.AutoPlayScope = scope;
    }

    #region Disposal

    /// <inheritdoc />
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        RaiseEvent(new ViewModelDisposalEventArgs(ViewModelDisposal.ViewModelDisposalEvent, ViewModel));
    }

    #endregion
}
