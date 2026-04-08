using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
                new FrameworkPropertyMetadata(480.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
                    OnLayoutDimensionChanged));

        public static readonly DependencyProperty LayoutHeightProperty =
            DependencyProperty.Register(nameof(LayoutHeight), typeof(double), typeof(LayoutCanvas),
                new FrameworkPropertyMetadata(276.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
                    OnLayoutDimensionChanged));

        public bool SuppressDimensionScaling { get; set; }

        private static void OnLayoutDimensionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not LayoutCanvas canvas || canvas._viewModel == null || canvas.SuppressDimensionScaling) return;

            var oldVal = (double)e.OldValue;
            var newVal = (double)e.NewValue;
            bool isWidth = e.Property == LayoutWidthProperty;

            if (newVal <= 0 || oldVal <= 0 || Math.Abs(oldVal - newVal) < 0.001) return;

            double scale = newVal / oldVal;

            foreach (var led in canvas._viewModel.Items)
            {
                if (isWidth)
                {
                    led.LedLayout.DescriptiveX = (led.LedLayout.X * scale).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                    led.LedLayout.DescriptiveWidth = (led.LedLayout.Width * scale).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    led.LedLayout.DescriptiveY = (led.LedLayout.Y * scale).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                    led.LedLayout.DescriptiveHeight = (led.LedLayout.Height * scale).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            canvas._viewModel.RecalcLeds();
            canvas.RedrawCanvas();
        }

        public static readonly DependencyProperty ShowGridProperty =
            DependencyProperty.Register(nameof(ShowGrid), typeof(bool), typeof(LayoutCanvas),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty GridSizeProperty =
            DependencyProperty.Register(nameof(GridSize), typeof(double), typeof(LayoutCanvas),
                new FrameworkPropertyMetadata(0.5, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SnapToGridProperty =
            DependencyProperty.Register(nameof(SnapToGrid), typeof(bool), typeof(LayoutCanvas),
                new PropertyMetadata(true));

        public static readonly DependencyProperty SnapToNeighborsProperty =
            DependencyProperty.Register(nameof(SnapToNeighbors), typeof(bool), typeof(LayoutCanvas),
                new PropertyMetadata(true));

        public static readonly DependencyProperty SnapThresholdProperty =
            DependencyProperty.Register(nameof(SnapThreshold), typeof(double), typeof(LayoutCanvas),
                new PropertyMetadata(0.5, null, CoerceSnapThreshold));

        private static object CoerceSnapThreshold(DependencyObject d, object value)
        {
            var v = (double)value;
            return v < 0.01 ? 0.01 : v;
        }

        public static readonly DependencyProperty GridColorProperty =
            DependencyProperty.Register(nameof(GridColor), typeof(Color), typeof(LayoutCanvas),
                new FrameworkPropertyMetadata(Color.FromArgb(96, 0, 0, 0), FrameworkPropertyMetadataOptions.AffectsRender));

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

        public Color GridColor
        {
            get => (Color)GetValue(GridColorProperty);
            set => SetValue(GridColorProperty, value);
        }

        #endregion

        #region State

        private double _zoom = 1.0;
        private double _panX, _panY;
        private Point? _lastPanPos;

        private bool _isDraggingLed;
        private Point _dragOffset;
        private Dictionary<LedViewModel, Point> _dragStartPositions;

        private double? _guideX, _guideY;

        private bool _isSelecting;
        private Point _selectionStart;
        private Rect? _selectionRect;

        // Expose for binding in sidebar
        public LedViewModel SelectedLed { get; private set; }
        public List<LedViewModel> SelectedLeds { get; } = new();
        public double Zoom => _zoom;

        public event Action SelectionChanged;
        public event Action ViewChanged;
        public event Action<LedViewModel> HoverChanged;

        #endregion

        #region Brushes (static, frozen)

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
            GuidePen = new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 255, 0)), 0.5) { DashStyle = DashStyles.Dash };
            GuidePen.Freeze();

            var selColor = Color.FromRgb(237, 65, 131);
            SelectedBorderPen = new Pen(new SolidColorBrush(selColor), 0.7);
            SelectedBorderPen.Freeze();
            SelectedFill = new SolidColorBrush(Color.FromArgb(80, selColor.R, selColor.G, selColor.B));
            SelectedFill.Freeze();

            var hovColor = Color.FromRgb(116, 97, 167);
            HoverBorderPen = new Pen(new SolidColorBrush(hovColor), 1.0);
            HoverBorderPen.Freeze();
            HoverFill = new SolidColorBrush(Color.FromArgb(50, hovColor.R, hovColor.G, hovColor.B));
            HoverFill.Freeze();

            var normColor = Color.FromRgb(62, 180, 203);
            NormalBorderPen = new Pen(new SolidColorBrush(normColor), 0.4);
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
                var screenGridSize = GridSize * _zoom;
                // Major lines every 10 grid steps
                var majorStep = GridSize * 10;

                // Minor grid: only draw if screen pixels per cell >= 4 (readable)
                if (screenGridSize >= 4)
                {
                    var minorColor = Color.FromArgb((byte)(GridColor.A / 2), GridColor.R, GridColor.G, GridColor.B);
                    var minorPen = new Pen(new SolidColorBrush(minorColor), 0.3);
                    minorPen.Freeze();
                    for (double x = 0; x <= LayoutWidth; x += GridSize)
                    {
                        if (Math.Abs(x % majorStep) > GridSize * 0.1)
                            dc.DrawLine(minorPen, new Point(x, 0), new Point(x, LayoutHeight));
                    }
                    for (double y = 0; y <= LayoutHeight; y += GridSize)
                    {
                        if (Math.Abs(y % majorStep) > GridSize * 0.1)
                            dc.DrawLine(minorPen, new Point(0, y), new Point(LayoutWidth, y));
                    }
                }

                // Major grid: always draw (every 10 steps)
                var majorPen = new Pen(new SolidColorBrush(GridColor), 0.7);
                majorPen.Freeze();
                for (double x = 0; x <= LayoutWidth; x += majorStep)
                    dc.DrawLine(majorPen, new Point(x, 0), new Point(x, LayoutHeight));
                for (double y = 0; y <= LayoutHeight; y += majorStep)
                    dc.DrawLine(majorPen, new Point(0, y), new Point(LayoutWidth, y));
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

                    if (led.LedLayout.Shape == RGB.NET.Core.Shape.Circle)
                        dc.DrawEllipse(fill, border, new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), rect.Width / 2, rect.Height / 2);
                    else
                        dc.DrawRectangle(fill, border, rect);
                }
            }

            // Selection rectangle
            if (_selectionRect.HasValue)
            {
                var selFill = new SolidColorBrush(Color.FromArgb(30, 100, 150, 255));
                selFill.Freeze();
                var selPen = new Pen(new SolidColorBrush(Color.FromArgb(180, 100, 150, 255)), 0.5);
                selPen.Freeze();
                dc.DrawRectangle(selFill, selPen, _selectionRect.Value);
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

                // Make the clicked LED the primary selection (drag reference)
                SelectedLed = hitLed;

                // Save undo state before drag
                SaveUndoForSelected();

                // Start drag — capture all selected LED positions
                _isDraggingLed = true;
                _dragOffset = new Point(layoutPos.X - hitLed.LedLayout.X, layoutPos.Y - hitLed.LedLayout.Y);
                _dragStartPositions = new Dictionary<LedViewModel, Point>();
                foreach (var sl in SelectedLeds)
                    _dragStartPositions[sl] = new Point(sl.LedLayout.X, sl.LedLayout.Y);
                Mouse.OverrideCursor = Cursors.SizeAll;
                CaptureMouse();
            }
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                // Shift+drag background: selection rectangle
                _isSelecting = true;
                _selectionStart = layoutPos;
                _selectionRect = new Rect(layoutPos, new Size(0, 0));
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

                var precision = GetRoundingPrecision();
                targetX = Math.Round(targetX, precision);
                targetY = Math.Round(targetY, precision);

                // Move all selected LEDs by the same delta from their start positions
                var primaryStart = _dragStartPositions.GetValueOrDefault(SelectedLed, new Point(SelectedLed.LedLayout.X, SelectedLed.LedLayout.Y));
                var dx = targetX - primaryStart.X;
                var dy = targetY - primaryStart.Y;

                foreach (var led in SelectedLeds)
                {
                    if (!_dragStartPositions.TryGetValue(led, out var startPos))
                        continue;
                    var nx = Math.Round(startPos.X + dx, precision);
                    var ny = Math.Round(startPos.Y + dy, precision);
                    led.InputX = nx.ToString(CultureInfo.InvariantCulture);
                    led.InputY = ny.ToString(CultureInfo.InvariantCulture);
                    led.ApplyPositionDirect();
                }

                InvalidateVisual();
            }
            else if (_isSelecting && e.LeftButton == MouseButtonState.Pressed)
            {
                // Selection rectangle
                var x = Math.Min(_selectionStart.X, layoutPos.X);
                var y = Math.Min(_selectionStart.Y, layoutPos.Y);
                var w = Math.Abs(layoutPos.X - _selectionStart.X);
                var h = Math.Abs(layoutPos.Y - _selectionStart.Y);
                _selectionRect = new Rect(x, y, w, h);
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
                {
                    InvalidateVisual();
                    HoverChanged?.Invoke(_hoveredLed);
                }
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);

            if (_isDraggingLed)
            {
                _isDraggingLed = false;
                _dragStartPositions = null;
                _guideX = null;
                _guideY = null;
                Mouse.OverrideCursor = null;
                InvalidateVisual();
            }
            else if (_isSelecting && _selectionRect.HasValue && _viewModel != null)
            {
                _isSelecting = false;
                var rect = _selectionRect.Value;
                _selectionRect = null;

                // Select all LEDs whose center is inside the selection rectangle
                ClearSelection();
                foreach (var led in _viewModel.Items)
                {
                    var cx = led.LedLayout.X + led.LedLayout.Width / 2;
                    var cy = led.LedLayout.Y + led.LedLayout.Height / 2;
                    if (rect.Contains(new Point(cx, cy)))
                        AddToSelection(led);
                }
                InvalidateVisual();
            }

            _isSelecting = false;
            _selectionRect = null;
            _lastPanPos = null;
            ReleaseMouseCapture();
            e.Handled = true;
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);
            if (_viewModel == null) return;

            var layoutPos = ScreenToLayout(e.GetPosition(this));
            var hitLed = HitTestLed(layoutPos);

            var menu = new ContextMenu();

            if (hitLed != null)
            {
                // Select this LED if not already selected
                if (!hitLed.Selected)
                {
                    ClearSelection();
                    AddToSelection(hitLed);
                }

                // Rename LED
                var renameItem = new MenuItem { Header = $"Rename '{hitLed.LedLayout.Id}'..." };
                renameItem.Click += (_, _) =>
                {
                    var dlg = new Dialogs.RenameDialog(hitLed.LedLayout.Id);
                    if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.NewName))
                    {
                        hitLed.InputId = dlg.NewName;
                        hitLed.ApplyInput();
                        InvalidateVisual();
                        SelectionChanged?.Invoke();
                    }
                };
                menu.Items.Add(renameItem);

                // Delete LED
                var deleteItem = new MenuItem { Header = "Delete LED", InputGestureText = "Del" };
                deleteItem.Click += (_, _) =>
                {
                    _viewModel.RemoveLed();
                    InvalidateVisual();
                };
                menu.Items.Add(deleteItem);

                menu.Items.Add(new Separator());

                // Snap to grid (works for single or multi)
                if (GridSize > 0)
                {
                    var snapItem = new MenuItem { Header = "Snap to grid" };
                    snapItem.Click += (_, _) => SnapSelectedToGrid();
                    menu.Items.Add(snapItem);
                }

                // Multi-select operations
                if (SelectedLeds.Count == 2)
                {
                    var swapItem = new MenuItem { Header = $"Swap IDs ({SelectedLeds[0].LedLayout.Id} \u2194 {SelectedLeds[1].LedLayout.Id})" };
                    swapItem.Click += (_, _) =>
                    {
                        var a = SelectedLeds[0];
                        var b = SelectedLeds[1];
                        var tempId = a.LedLayout.Id;
                        a.InputId = b.LedLayout.Id;
                        b.InputId = tempId;
                        a.ApplyInputWithoutUpdate();
                        b.ApplyInputWithoutUpdate();
                        InvalidateVisual();
                        SelectionChanged?.Invoke();
                    };
                    menu.Items.Add(swapItem);
                }

                if (SelectedLeds.Count > 1)
                {
                    var alignMenu = new MenuItem { Header = "Align" };
                    var alignT = new MenuItem { Header = "Align Top" };
                    alignT.Click += (_, _) => AlignTop();
                    alignMenu.Items.Add(alignT);
                    var alignB = new MenuItem { Header = "Align Bottom" };
                    alignB.Click += (_, _) => AlignBottom();
                    alignMenu.Items.Add(alignB);
                    var alignL = new MenuItem { Header = "Align Left" };
                    alignL.Click += (_, _) => AlignLeft();
                    alignMenu.Items.Add(alignL);
                    var alignR = new MenuItem { Header = "Align Right" };
                    alignR.Click += (_, _) => AlignRight();
                    alignMenu.Items.Add(alignR);
                    menu.Items.Add(alignMenu);

                    var distMenu = new MenuItem { Header = "Distribute" };
                    var spaceH = new MenuItem { Header = "Space evenly (horizontal)", InputGestureText = "Ctrl+H" };
                    spaceH.Click += (_, _) => AutoSpaceHorizontal();
                    distMenu.Items.Add(spaceH);
                    var spaceV = new MenuItem { Header = "Space evenly (vertical)", InputGestureText = "Ctrl+J" };
                    spaceV.Click += (_, _) => AutoSpaceVertical();
                    distMenu.Items.Add(spaceV);
                    menu.Items.Add(distMenu);

                    var sizeMenu = new MenuItem { Header = "Match size" };
                    var matchW = new MenuItem { Header = "Match Width" };
                    matchW.Click += (_, _) => MatchWidth();
                    sizeMenu.Items.Add(matchW);
                    var matchH = new MenuItem { Header = "Match Height" };
                    matchH.Click += (_, _) => MatchHeight();
                    sizeMenu.Items.Add(matchH);
                    menu.Items.Add(sizeMenu);

                    menu.Items.Add(new Separator());
                }
            }

            // Add LED options
            var addBefore = new MenuItem { Header = "Add LED before" };
            addBefore.Click += (_, _) => _viewModel.AddLed("True");
            menu.Items.Add(addBefore);

            var addAfter = new MenuItem { Header = "Add LED after" };
            addAfter.Click += (_, _) => _viewModel.AddLed("False");
            menu.Items.Add(addAfter);

            if (_viewModel.Items.Count > 0)
            {
                var removeAll = new MenuItem { Header = "Remove all LEDs" };
                removeAll.Click += (_, _) =>
                {
                    _viewModel.RemoveAllLeds();
                    InvalidateVisual();
                };
                menu.Items.Add(removeAll);
            }

            if (_undoStack.Count > 0 || _redoStack.Count > 0)
            {
                menu.Items.Add(new Separator());
                if (_undoStack.Count > 0)
                {
                    var undoItem = new MenuItem { Header = "Undo", InputGestureText = "Ctrl+Z" };
                    undoItem.Click += (_, _) => Undo();
                    menu.Items.Add(undoItem);
                }
                if (_redoStack.Count > 0)
                {
                    var redoItem = new MenuItem { Header = "Redo", InputGestureText = "Ctrl+Y" };
                    redoItem.Click += (_, _) => Redo();
                    menu.Items.Add(redoItem);
                }
            }

            menu.IsOpen = true;
            ContextMenu = menu;
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
            else if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                Undo();
                e.Handled = true;
            }
            else if (e.Key == Key.Y && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                Redo();
                e.Handled = true;
            }
            else if (e.Key == Key.H && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                AutoSpaceHorizontal();
                e.Handled = true;
            }
            else if (e.Key == Key.J && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                AutoSpaceVertical();
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
                    var precision = GetRoundingPrecision();
                    led.InputX = Math.Round(x, precision).ToString(CultureInfo.InvariantCulture);
                    led.InputY = Math.Round(y, precision).ToString(CultureInfo.InvariantCulture);
                    led.ApplyPositionDirect();
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
            // Only populate the expensive LED ID list for single selection
            if (SelectedLeds.Count == 1)
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
            }
            SelectionChanged?.Invoke();
            InvalidateVisual();
        }

        #endregion

        public void ClearSelectionPublic()
        {
            ClearSelection();
        }

        /// <summary>
        /// Clear selection state without firing events or invalidating visual.
        /// Use before removing LEDs to avoid accessing stale references in callbacks.
        /// </summary>
        public void ClearSelectionSilent()
        {
            foreach (var led in SelectedLeds)
                led.Selected = false;
            SelectedLeds.Clear();
            SelectedLed = null;
        }

        public void RedrawCanvas()
        {
            InvalidateVisual();
        }

        private int GetRoundingPrecision()
        {
            if (GridSize <= 0) return 2;
            var s = GridSize.ToString(CultureInfo.InvariantCulture);
            var dotIndex = s.IndexOf('.');
            return dotIndex < 0 ? 0 : s.Length - dotIndex - 1;
        }

        #region Undo / Redo

        private readonly Stack<List<UndoEntry>> _undoStack = new();
        private readonly Stack<List<UndoEntry>> _redoStack = new();

        private struct UndoEntry
        {
            public LedViewModel Led;
            public float OldX, OldY, OldW, OldH;
            public float NewX, NewY, NewW, NewH;
        }

        public void PushUndo(List<(LedViewModel led, float oldX, float oldY, float oldW, float oldH)> entries)
        {
            _redoStack.Clear();
            var batch = new List<UndoEntry>(entries.Count);
            foreach (var (led, oldX, oldY, oldW, oldH) in entries)
            {
                batch.Add(new UndoEntry
                {
                    Led = led,
                    OldX = oldX, OldY = oldY, OldW = oldW, OldH = oldH,
                    NewX = led.LedLayout.X, NewY = led.LedLayout.Y,
                    NewW = led.LedLayout.Width, NewH = led.LedLayout.Height
                });
            }
            _undoStack.Push(batch);
        }

        public void UndoPublic() => Undo();
        public void RedoPublic() => Redo();

        private void Undo()
        {
            if (_undoStack.Count == 0) return;
            var batch = _undoStack.Pop();
            _redoStack.Push(batch);
            foreach (var entry in batch)
            {
                entry.Led.InputX = entry.OldX.ToString(CultureInfo.InvariantCulture);
                entry.Led.InputY = entry.OldY.ToString(CultureInfo.InvariantCulture);
                entry.Led.InputWidth = entry.OldW.ToString(CultureInfo.InvariantCulture);
                entry.Led.InputHeight = entry.OldH.ToString(CultureInfo.InvariantCulture);
                entry.Led.ApplyPositionDirect();
            }
            InvalidateVisual();
            SelectionChanged?.Invoke();
        }

        private void Redo()
        {
            if (_redoStack.Count == 0) return;
            var batch = _redoStack.Pop();
            _undoStack.Push(batch);
            foreach (var entry in batch)
            {
                entry.Led.InputX = entry.NewX.ToString(CultureInfo.InvariantCulture);
                entry.Led.InputY = entry.NewY.ToString(CultureInfo.InvariantCulture);
                entry.Led.InputWidth = entry.NewW.ToString(CultureInfo.InvariantCulture);
                entry.Led.InputHeight = entry.NewH.ToString(CultureInfo.InvariantCulture);
                entry.Led.ApplyPositionDirect();
            }
            InvalidateVisual();
            SelectionChanged?.Invoke();
        }

        #endregion

        #region Auto Spacing

        /// <summary>
        /// Distribute selected LEDs evenly in a horizontal row (even X spacing, same Y).
        /// </summary>
        public void AutoSpaceHorizontal()
        {
            if (SelectedLeds.Count < 2) return;
            var precision = GetRoundingPrecision();
            var sorted = SelectedLeds.OrderBy(l => l.LedLayout.X).ToList();

            // Save undo
            SaveUndoForSelected();

            // Distribute: keep first and last positions, space others evenly
            var first = sorted[0];
            var last = sorted[^1];
            var totalSpan = (last.LedLayout.X + last.LedLayout.Width) - first.LedLayout.X;
            var totalLedWidth = sorted.Sum(l => l.LedLayout.Width);
            var gap = (totalSpan - totalLedWidth) / (sorted.Count - 1);

            var currentX = (double)first.LedLayout.X;
            foreach (var led in sorted)
            {
                led.InputX = Math.Round(currentX, precision).ToString(CultureInfo.InvariantCulture);
                led.ApplyPositionDirect();
                currentX += led.LedLayout.Width + gap;
            }

            InvalidateVisual();
            SelectionChanged?.Invoke();
        }

        /// <summary>
        /// Distribute selected LEDs evenly in a vertical column (even Y spacing, same X).
        /// </summary>
        public void AutoSpaceVertical()
        {
            if (SelectedLeds.Count < 2) return;
            var precision = GetRoundingPrecision();
            var sorted = SelectedLeds.OrderBy(l => l.LedLayout.Y).ToList();

            SaveUndoForSelected();

            var first = sorted[0];
            var last = sorted[^1];
            var totalSpan = (last.LedLayout.Y + last.LedLayout.Height) - first.LedLayout.Y;
            var totalLedHeight = sorted.Sum(l => l.LedLayout.Height);
            var gap = (totalSpan - totalLedHeight) / (sorted.Count - 1);

            var currentY = (double)first.LedLayout.Y;
            foreach (var led in sorted)
            {
                led.InputY = Math.Round(currentY, precision).ToString(CultureInfo.InvariantCulture);
                led.ApplyPositionDirect();
                currentY += led.LedLayout.Height + gap;
            }

            InvalidateVisual();
            SelectionChanged?.Invoke();
        }

        /// <summary>Align all selected LEDs to the top edge of the topmost LED.</summary>
        public void AlignTop()
        {
            if (SelectedLeds.Count < 2) return;
            SaveUndoForSelected();
            var minY = SelectedLeds.Min(l => l.LedLayout.Y);
            foreach (var led in SelectedLeds)
            {
                led.InputY = Math.Round(minY, GetRoundingPrecision()).ToString(CultureInfo.InvariantCulture);
                led.ApplyPositionDirect();
            }
            InvalidateVisual();
            SelectionChanged?.Invoke();
        }

        /// <summary>Align all selected LEDs to the bottom edge of the bottommost LED.</summary>
        public void AlignBottom()
        {
            if (SelectedLeds.Count < 2) return;
            SaveUndoForSelected();
            var maxBottom = SelectedLeds.Max(l => l.LedLayout.Y + l.LedLayout.Height);
            var p = GetRoundingPrecision();
            foreach (var led in SelectedLeds)
            {
                led.InputY = Math.Round(maxBottom - led.LedLayout.Height, p).ToString(CultureInfo.InvariantCulture);
                led.ApplyPositionDirect();
            }
            InvalidateVisual();
            SelectionChanged?.Invoke();
        }

        /// <summary>Align all selected LEDs to the left edge of the leftmost LED.</summary>
        public void AlignLeft()
        {
            if (SelectedLeds.Count < 2) return;
            SaveUndoForSelected();
            var minX = SelectedLeds.Min(l => l.LedLayout.X);
            foreach (var led in SelectedLeds)
            {
                led.InputX = Math.Round(minX, GetRoundingPrecision()).ToString(CultureInfo.InvariantCulture);
                led.ApplyPositionDirect();
            }
            InvalidateVisual();
            SelectionChanged?.Invoke();
        }

        /// <summary>Align all selected LEDs to the right edge of the rightmost LED.</summary>
        public void AlignRight()
        {
            if (SelectedLeds.Count < 2) return;
            SaveUndoForSelected();
            var maxRight = SelectedLeds.Max(l => l.LedLayout.X + l.LedLayout.Width);
            var p = GetRoundingPrecision();
            foreach (var led in SelectedLeds)
            {
                led.InputX = Math.Round(maxRight - led.LedLayout.Width, p).ToString(CultureInfo.InvariantCulture);
                led.ApplyPositionDirect();
            }
            InvalidateVisual();
            SelectionChanged?.Invoke();
        }

        /// <summary>Set all selected LEDs to the same width as the first selected (primary).</summary>
        public void MatchWidth()
        {
            if (SelectedLeds.Count < 2 || SelectedLed == null) return;
            SaveUndoForSelected();
            var targetW = SelectedLed.LedLayout.Width;
            foreach (var led in SelectedLeds)
            {
                led.InputWidth = Math.Round(targetW, GetRoundingPrecision()).ToString(CultureInfo.InvariantCulture);
                led.ApplyInputWithoutUpdate();
                led.ApplyPositionDirect();
            }
            InvalidateVisual();
            SelectionChanged?.Invoke();
        }

        /// <summary>Set all selected LEDs to the same height as the first selected (primary).</summary>
        public void MatchHeight()
        {
            if (SelectedLeds.Count < 2 || SelectedLed == null) return;
            SaveUndoForSelected();
            var targetH = SelectedLed.LedLayout.Height;
            foreach (var led in SelectedLeds)
            {
                led.InputHeight = Math.Round(targetH, GetRoundingPrecision()).ToString(CultureInfo.InvariantCulture);
                led.ApplyInputWithoutUpdate();
                led.ApplyPositionDirect();
            }
            InvalidateVisual();
            SelectionChanged?.Invoke();
        }

        /// <summary>
        /// Snap all selected LEDs to the nearest grid intersection.
        /// </summary>
        public void SnapSelectedToGrid()
        {
            if (SelectedLeds.Count == 0 || GridSize <= 0) return;
            var precision = GetRoundingPrecision();

            SaveUndoForSelected();

            foreach (var led in SelectedLeds)
            {
                var snappedX = Math.Round(Math.Round(led.LedLayout.X / GridSize) * GridSize, precision);
                var snappedY = Math.Round(Math.Round(led.LedLayout.Y / GridSize) * GridSize, precision);
                led.InputX = snappedX.ToString(CultureInfo.InvariantCulture);
                led.InputY = snappedY.ToString(CultureInfo.InvariantCulture);
                led.ApplyPositionDirect();
            }

            InvalidateVisual();
            SelectionChanged?.Invoke();
        }

        public void SaveUndoForSelectedPublic() => SaveUndoForSelected();

        private void SaveUndoForSelected()
        {
            var entries = SelectedLeds.Select(l => (l, l.LedLayout.X, l.LedLayout.Y, l.LedLayout.Width, l.LedLayout.Height)).ToList();
            PushUndo(entries);
        }

        #endregion
    }
}
