using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RGB.NET.Layout;

namespace LayoutEditor.UI.Controls
{
    /// <summary>
    /// Custom canvas that renders device image, LEDs, grid, and alignment guides.
    /// Handles all mouse interaction directly - no nested controls or event routing issues.
    /// </summary>
    public class LayoutCanvas : FrameworkElement
    {
        #region Dependency Properties

        public static readonly DependencyProperty DeviceImageSourceProperty =
            DependencyProperty.Register(nameof(DeviceImageSource), typeof(ImageSource), typeof(LayoutCanvas),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LayoutWidthProperty =
            DependencyProperty.Register(nameof(LayoutWidth), typeof(double), typeof(LayoutCanvas),
                new FrameworkPropertyMetadata(480.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LayoutHeightProperty =
            DependencyProperty.Register(nameof(LayoutHeight), typeof(double), typeof(LayoutCanvas),
                new FrameworkPropertyMetadata(276.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShowGridProperty =
            DependencyProperty.Register(nameof(ShowGrid), typeof(bool), typeof(LayoutCanvas),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty GridSizeProperty =
            DependencyProperty.Register(nameof(GridSize), typeof(double), typeof(LayoutCanvas),
                new FrameworkPropertyMetadata(5.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SnapToGridProperty =
            DependencyProperty.Register(nameof(SnapToGrid), typeof(bool), typeof(LayoutCanvas),
                new PropertyMetadata(true));

        public static readonly DependencyProperty SnapToNeighborsProperty =
            DependencyProperty.Register(nameof(SnapToNeighbors), typeof(bool), typeof(LayoutCanvas),
                new PropertyMetadata(true));

        public static readonly DependencyProperty SnapThresholdProperty =
            DependencyProperty.Register(nameof(SnapThreshold), typeof(double), typeof(LayoutCanvas),
                new PropertyMetadata(2.0));

        public ImageSource DeviceImageSource
        {
            get => (ImageSource)GetValue(DeviceImageSourceProperty);
            set => SetValue(DeviceImageSourceProperty, value);
        }

        public double LayoutWidth
        {
            get => (double)GetValue(LayoutWidthProperty);
            set => SetValue(LayoutWidthProperty, value);
        }

        public double LayoutHeight
        {
            get => (double)GetValue(LayoutHeightProperty);
            set => SetValue(LayoutHeightProperty, value);
        }

        public bool ShowGrid
        {
            get => (bool)GetValue(ShowGridProperty);
            set => SetValue(ShowGridProperty, value);
        }

        public double GridSize
        {
            get => (double)GetValue(GridSizeProperty);
            set => SetValue(GridSizeProperty, value);
        }

        public bool SnapToGrid
        {
            get => (bool)GetValue(SnapToGridProperty);
            set => SetValue(SnapToGridProperty, value);
        }

        public bool SnapToNeighbors
        {
            get => (bool)GetValue(SnapToNeighborsProperty);
            set => SetValue(SnapToNeighborsProperty, value);
        }

        public double SnapThreshold
        {
            get => (double)GetValue(SnapThresholdProperty);
            set => SetValue(SnapThresholdProperty, value);
        }

        #endregion

        #region State

        private double _zoom = 1.0;
        private double _panX, _panY;
        private Point? _lastPanPos;

        private bool _isDraggingLed;
        private Point _dragOffset;

        private double? _guideX, _guideY;

        // Expose for binding in sidebar
        public LedViewModel SelectedLed { get; private set; }
        public List<LedViewModel> SelectedLeds { get; } = new();
        public double Zoom => _zoom;

        public event Action SelectionChanged;
        public event Action ViewChanged;

        #endregion

        #region Brushes (static, frozen)

        private static readonly Pen GridPen;
        private static readonly Pen GuidePen;
        private static readonly Pen SelectedBorderPen;
        private static readonly Pen HoverBorderPen;
        private static readonly Pen NormalBorderPen;
        private static readonly Brush SelectedFill;
        private static readonly Brush HoverFill;
        private static readonly Brush NormalFill;
        private static readonly Typeface TooltipTypeface = new("Segoe UI");

        static LayoutCanvas()
        {
            GridPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 0.5);
            GridPen.Freeze();

            GuidePen = new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 255, 0)), 0.5) { DashStyle = DashStyles.Dash };
            GuidePen.Freeze();

            var selColor = Color.FromRgb(237, 65, 131);
            SelectedBorderPen = new Pen(new SolidColorBrush(selColor), 1.5);
            SelectedBorderPen.Freeze();
            SelectedFill = new SolidColorBrush(Color.FromArgb(80, selColor.R, selColor.G, selColor.B));
            SelectedFill.Freeze();

            var hovColor = Color.FromRgb(116, 97, 167);
            HoverBorderPen = new Pen(new SolidColorBrush(hovColor), 1.0);
            HoverBorderPen.Freeze();
            HoverFill = new SolidColorBrush(Color.FromArgb(50, hovColor.R, hovColor.G, hovColor.B));
            HoverFill.Freeze();

            var normColor = Color.FromRgb(62, 180, 203);
            NormalBorderPen = new Pen(new SolidColorBrush(normColor), 1.0);
            NormalBorderPen.Freeze();
            NormalFill = new SolidColorBrush(Color.FromArgb(40, normColor.R, normColor.G, normColor.B));
            NormalFill.Freeze();
        }

        #endregion

        private DeviceLayoutViewModel _viewModel;
        private LedViewModel _hoveredLed;

        public LayoutCanvas()
        {
            ClipToBounds = true;
            Focusable = true;
        }

        public void SetViewModel(DeviceLayoutViewModel vm)
        {
            _viewModel = vm;
            InvalidateVisual();
        }

        // FrameworkElement has no Background, so override hit test to always hit
        protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
        {
            return new PointHitTestResult(this, hitTestParameters.HitPoint);
        }

        #region Layout

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(
                double.IsInfinity(availableSize.Width) ? LayoutWidth * _zoom : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? LayoutHeight * _zoom : availableSize.Height
            );
        }

        #endregion

        #region Rendering

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var totalTransform = new TransformGroup();
            totalTransform.Children.Add(new ScaleTransform(_zoom, _zoom));
            totalTransform.Children.Add(new TranslateTransform(_panX, _panY));
            dc.PushTransform(totalTransform);

            // Background
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 30, 30)), null,
                new Rect(0, 0, LayoutWidth, LayoutHeight));

            // Device image
            if (DeviceImageSource != null)
                dc.DrawImage(DeviceImageSource, new Rect(0, 0, LayoutWidth, LayoutHeight));

            // Grid
            if (ShowGrid && GridSize > 0)
            {
                for (double x = 0; x <= LayoutWidth; x += GridSize)
                    dc.DrawLine(GridPen, new Point(x, 0), new Point(x, LayoutHeight));
                for (double y = 0; y <= LayoutHeight; y += GridSize)
                    dc.DrawLine(GridPen, new Point(0, y), new Point(LayoutWidth, y));
            }

            // LEDs
            if (_viewModel != null)
            {
                foreach (var led in _viewModel.Items)
                {
                    var rect = new Rect(led.LedLayout.X, led.LedLayout.Y, led.LedLayout.Width, led.LedLayout.Height);

                    Brush fill;
                    Pen border;
                    if (led.Selected)
                    {
                        fill = SelectedFill;
                        border = SelectedBorderPen;
                    }
                    else if (led == _hoveredLed)
                    {
                        fill = HoverFill;
                        border = HoverBorderPen;
                    }
                    else
                    {
                        fill = NormalFill;
                        border = NormalBorderPen;
                    }

                    dc.DrawRectangle(fill, border, rect);
                }
            }

            // Alignment guides
            if (_guideX.HasValue)
                dc.DrawLine(GuidePen, new Point(_guideX.Value, 0), new Point(_guideX.Value, LayoutHeight));
            if (_guideY.HasValue)
                dc.DrawLine(GuidePen, new Point(0, _guideY.Value), new Point(LayoutWidth, _guideY.Value));

            dc.Pop(); // transform

            // Tooltip for hovered LED (drawn in screen space, outside the transform)
            if (_hoveredLed != null)
            {
                var text = new FormattedText(
                    _hoveredLed.LedLayout.Id,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    TooltipTypeface,
                    12,
                    Brushes.White,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);

                // Position tooltip near mouse
                var mousePos = Mouse.GetPosition(this);
                var tipX = mousePos.X + 14;
                var tipY = mousePos.Y + 14;

                // Background
                var tipRect = new Rect(tipX - 2, tipY - 1, text.Width + 8, text.Height + 4);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(220, 40, 40, 40)), null, tipRect);
                dc.DrawText(text, new Point(tipX + 2, tipY + 1));
            }
        }

        #endregion

        #region Hit Testing

        private LedViewModel HitTestLed(Point layoutPos)
        {
            if (_viewModel == null) return null;

            // Iterate in reverse so topmost LEDs are hit first
            for (int i = _viewModel.Items.Count - 1; i >= 0; i--)
            {
                var led = _viewModel.Items[i];
                var rect = new Rect(led.LedLayout.X, led.LedLayout.Y, led.LedLayout.Width, led.LedLayout.Height);
                if (rect.Contains(layoutPos))
                    return led;
            }
            return null;
        }

        private Point ScreenToLayout(Point screenPos)
        {
            return new Point(
                (screenPos.X - _panX) / _zoom,
                (screenPos.Y - _panY) / _zoom
            );
        }

        #endregion

        #region Mouse Handling

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Focus();

            var layoutPos = ScreenToLayout(e.GetPosition(this));
            var hitLed = HitTestLed(layoutPos);

            if (hitLed != null)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    // Ctrl+click: toggle multi-select
                    ToggleSelection(hitLed);
                }
                else if (!hitLed.Selected)
                {
                    // Click unselected LED: single select
                    ClearSelection();
                    AddToSelection(hitLed);
                }

                // Start drag
                _isDraggingLed = true;
                _dragOffset = new Point(layoutPos.X - hitLed.LedLayout.X, layoutPos.Y - hitLed.LedLayout.Y);
                Mouse.OverrideCursor = Cursors.SizeAll;
                CaptureMouse();
            }
            else
            {
                // Click background: deselect all, start pan
                ClearSelection();
                _lastPanPos = e.GetPosition(this);
                CaptureMouse();
            }

            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            var screenPos = e.GetPosition(this);
            var layoutPos = ScreenToLayout(screenPos);

            if (_isDraggingLed && SelectedLed != null && e.LeftButton == MouseButtonState.Pressed)
            {
                // Drag LED(s)
                var targetX = layoutPos.X - _dragOffset.X;
                var targetY = layoutPos.Y - _dragOffset.Y;

                // Snap to grid
                if (SnapToGrid && GridSize > 0)
                {
                    targetX = Math.Round(targetX / GridSize) * GridSize;
                    targetY = Math.Round(targetY / GridSize) * GridSize;
                }

                // Snap to neighbors
                _guideX = null;
                _guideY = null;
                if (SnapToNeighbors && _viewModel != null)
                {
                    var cx = targetX + SelectedLed.LedLayout.Width / 2;
                    var cy = targetY + SelectedLed.LedLayout.Height / 2;
                    var threshold = SnapThreshold;

                    foreach (var other in _viewModel.Items)
                    {
                        if (other.Selected) continue;
                        var ocx = other.LedLayout.X + other.LedLayout.Width / 2;
                        var ocy = other.LedLayout.Y + other.LedLayout.Height / 2;

                        // Center alignment
                        if (Math.Abs(cx - ocx) < threshold) { targetX = ocx - SelectedLed.LedLayout.Width / 2; _guideX = ocx; }
                        if (Math.Abs(cy - ocy) < threshold) { targetY = ocy - SelectedLed.LedLayout.Height / 2; _guideY = ocy; }

                        // Edge alignment
                        if (Math.Abs(targetX - other.LedLayout.X) < threshold) { targetX = other.LedLayout.X; _guideX = targetX; }
                        if (Math.Abs(targetY - other.LedLayout.Y) < threshold) { targetY = other.LedLayout.Y; _guideY = targetY; }
                        if (Math.Abs((targetX + SelectedLed.LedLayout.Width) - (other.LedLayout.X + other.LedLayout.Width)) < threshold)
                        { targetX = other.LedLayout.X + other.LedLayout.Width - SelectedLed.LedLayout.Width; _guideX = targetX + SelectedLed.LedLayout.Width; }
                        if (Math.Abs((targetY + SelectedLed.LedLayout.Height) - (other.LedLayout.Y + other.LedLayout.Height)) < threshold)
                        { targetY = other.LedLayout.Y + other.LedLayout.Height - SelectedLed.LedLayout.Height; _guideY = targetY + SelectedLed.LedLayout.Height; }
                    }
                }

                targetX = Math.Round(targetX, 1);
                targetY = Math.Round(targetY, 1);

                // Move all selected LEDs by the same delta
                var dx = targetX - SelectedLed.LedLayout.X;
                var dy = targetY - SelectedLed.LedLayout.Y;

                foreach (var led in SelectedLeds)
                {
                    var nx = Math.Round(led.LedLayout.X + dx, 1);
                    var ny = Math.Round(led.LedLayout.Y + dy, 1);
                    led.InputX = nx.ToString(CultureInfo.InvariantCulture);
                    led.InputY = ny.ToString(CultureInfo.InvariantCulture);
                    led.ApplyInput();
                }

                InvalidateVisual();
            }
            else if (_lastPanPos.HasValue && e.LeftButton == MouseButtonState.Pressed)
            {
                // Pan
                var delta = screenPos - _lastPanPos.Value;
                _panX += delta.X;
                _panY += delta.Y;
                _lastPanPos = screenPos;
                ViewChanged?.Invoke();
                InvalidateVisual();
            }
            else
            {
                // Hover detection
                var oldHover = _hoveredLed;
                _hoveredLed = HitTestLed(layoutPos);
                Cursor = _hoveredLed != null ? Cursors.Hand : Cursors.Arrow;
                if (oldHover != _hoveredLed)
                    InvalidateVisual();
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);

            if (_isDraggingLed)
            {
                _isDraggingLed = false;
                _guideX = null;
                _guideY = null;
                Mouse.OverrideCursor = null;
                InvalidateVisual();
            }

            _lastPanPos = null;
            ReleaseMouseCapture();
            e.Handled = true;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);

            var pos = e.GetPosition(this);
            var layoutPosBefore = ScreenToLayout(pos);

            if (e.Delta > 0)
                _zoom = Math.Min(_zoom * 1.15, 20.0);
            else
                _zoom = Math.Max(_zoom / 1.15, 0.1);

            // Zoom toward mouse position
            _panX = pos.X - layoutPosBefore.X * _zoom;
            _panY = pos.Y - layoutPosBefore.Y * _zoom;

            ViewChanged?.Invoke();
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.Escape)
            {
                ClearSelection();
                e.Handled = true;
            }
            else if (e.Key == Key.G && !e.IsRepeat)
            {
                ShowGrid = !ShowGrid;
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && SelectedLed != null)
            {
                _viewModel?.RemoveLed();
                InvalidateVisual();
                e.Handled = true;
            }
            else if (SelectedLed != null && (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down))
            {
                var nudge = SnapToGrid && GridSize > 0 ? GridSize : 0.5;
                foreach (var led in SelectedLeds)
                {
                    double x = led.LedLayout.X;
                    double y = led.LedLayout.Y;
                    switch (e.Key)
                    {
                        case Key.Left: x -= nudge; break;
                        case Key.Right: x += nudge; break;
                        case Key.Up: y -= nudge; break;
                        case Key.Down: y += nudge; break;
                    }
                    led.InputX = Math.Round(x, 1).ToString(CultureInfo.InvariantCulture);
                    led.InputY = Math.Round(y, 1).ToString(CultureInfo.InvariantCulture);
                    led.ApplyInput();
                }
                InvalidateVisual();
                SelectionChanged?.Invoke();
                e.Handled = true;
            }
        }

        #endregion

        #region Selection

        private void ClearSelection()
        {
            foreach (var led in SelectedLeds)
                led.Selected = false;
            SelectedLeds.Clear();
            SelectedLed = null;
            SelectionChanged?.Invoke();
            InvalidateVisual();
        }

        private void AddToSelection(LedViewModel led)
        {
            led.Selected = true;
            SelectedLeds.Add(led);
            SelectedLed = led;
            led.RefreshAvailableLedIds();
            SelectionChanged?.Invoke();
            InvalidateVisual();
        }

        private void ToggleSelection(LedViewModel led)
        {
            if (led.Selected)
            {
                led.Selected = false;
                SelectedLeds.Remove(led);
                SelectedLed = SelectedLeds.LastOrDefault();
            }
            else
            {
                led.Selected = true;
                SelectedLeds.Add(led);
                SelectedLed = led;
                led.RefreshAvailableLedIds();
            }
            SelectionChanged?.Invoke();
            InvalidateVisual();
        }

        #endregion

        public void RedrawCanvas()
        {
            InvalidateVisual();
        }
    }
}
