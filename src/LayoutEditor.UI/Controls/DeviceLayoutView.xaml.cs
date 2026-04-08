using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LayoutEditor.UI.Services;
using LedId = RGB.NET.Core.LedId;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Color = System.Windows.Media.Color;

namespace LayoutEditor.UI.Controls
{
    public partial class DeviceLayoutView : UserControl
    {
        private readonly OpenRgbService _openRgb = new();

        // All RGB.NET LedId names for mapping OpenRGB → RGB.NET
        private static readonly string[] AllLedIdNames =
            Enum.GetValues(typeof(LedId)).Cast<LedId>().Select(v => v.ToString()).ToArray();

        public DeviceLayoutView()
        {
            InitializeComponent();
            Unloaded += (_, _) => _openRgb.Dispose();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is DeviceLayoutViewModel vm)
            {
                // Set initial canvas dimensions from file (suppress scaling — these are the base values)
                Canvas.SuppressDimensionScaling = true;
                Canvas.LayoutWidth = vm.DeviceLayout.Width;
                Canvas.LayoutHeight = vm.DeviceLayout.Height;

                Canvas.SetViewModel(vm);
                vm.SetCanvas(Canvas);

                Canvas.SelectionChanged += () =>
                {
                    // Sync selection back to ViewModel for sidebar bindings
                    vm.SelectedLed = Canvas.SelectedLed;
                    UpdateStatusTexts();
                    SendSelectionToOpenRgb();
                };

                Canvas.ViewChanged += () =>
                {
                    UpdateStatusTexts();
                };

                Canvas.HoverChanged += (hoveredLed) =>
                {
                    SendHoverToOpenRgb(hoveredLed);
                };

                // Load initial device image directly (may auto-size canvas if Width/Height was 0)
                vm.RefreshCanvasDeviceImage();
                Canvas.SuppressDimensionScaling = false;

                Canvas.RedrawCanvas();
                UpdateStatusTexts();
            }
        }

        private void SendHoverToOpenRgb(LedViewModel hoveredLed)
        {
            if (!_openRgb.IsConnected || OrgbEnabledCheck.IsChecked != true)
                return;

            if (hoveredLed != null)
            {
                var ledId = hoveredLed.LedLayout.Id;
                if (ledId != null)
                    Task.Run(() => { try { _openRgb.HighlightLed(ledId); } catch { } });
            }
            else if (Canvas.SelectedLeds.Count == 0)
            {
                Task.Run(() => { try { _openRgb.ClearAll(); } catch { } });
            }
        }

        private void SendSelectionToOpenRgb()
        {
            if (!_openRgb.IsConnected || OrgbEnabledCheck.IsChecked != true)
                return;

            var selectedLeds = Canvas.SelectedLeds;
            if (selectedLeds.Count == 0)
            {
                Task.Run(() => { try { _openRgb.ClearAll(); } catch { } });
            }
            else
            {
                var ledIds = selectedLeds.Select(l => l.LedLayout.Id).Where(id => id != null).ToArray();
                Task.Run(() => { try { _openRgb.HighlightLeds(ledIds); } catch { } });
            }
        }

        private void OrgbConnect_Click(object sender, RoutedEventArgs e)
        {
            var host = OrgbHost.Text.Trim();
            if (!int.TryParse(OrgbPort.Text.Trim(), out var port))
                port = 6742;

            OrgbStatus.Text = "Connecting...";
            OrgbStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 100));
            OrgbConnectBtn.IsEnabled = false;

            Task.Run(() =>
            {
                try
                {
                    _openRgb.Connect(host, port);
                    Dispatcher.Invoke(() =>
                    {
                        OrgbStatus.Text = $"Connected ({_openRgb.Devices.Length} devices)";
                        OrgbStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 200, 100));
                        OrgbConnectBtn.IsEnabled = false;
                        OrgbDisconnectBtn.IsEnabled = true;
                        OrgbDeviceCombo.IsEnabled = true;
                        OrgbEnabledCheck.IsEnabled = true;
                        AutoFillBtn.IsEnabled = true;

                        OrgbDeviceCombo.Items.Clear();
                        foreach (var device in _openRgb.Devices)
                            OrgbDeviceCombo.Items.Add($"{device.Name} ({device.Leds.Length} LEDs)");

                        if (OrgbDeviceCombo.Items.Count > 0)
                            OrgbDeviceCombo.SelectedIndex = 0;
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        OrgbStatus.Text = $"Failed: {ex.Message}";
                        OrgbStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 100, 100));
                        OrgbConnectBtn.IsEnabled = true;
                    });
                }
            });
        }

        private void OrgbDisconnect_Click(object sender, RoutedEventArgs e)
        {
            _openRgb.Disconnect();
            OrgbStatus.Text = "Disconnected";
            OrgbStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(153, 153, 153));
            OrgbConnectBtn.IsEnabled = true;
            OrgbDisconnectBtn.IsEnabled = false;
            OrgbDeviceCombo.IsEnabled = false;
            OrgbDeviceCombo.Items.Clear();
            OrgbEnabledCheck.IsEnabled = false;
            AutoFillBtn.IsEnabled = false;
        }

        private void OrgbDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OrgbDeviceCombo.SelectedIndex >= 0)
            {
                _openRgb.SelectDevice(OrgbDeviceCombo.SelectedIndex);
                RebuildCustomIdMapping();
            }
        }

        /// <summary>
        /// Rebuild Keyboard_Custom{N} → OpenRGB name mapping from current layout + connected device.
        /// Matches Custom IDs to device LEDs that don't have a standard RGB.NET mapping.
        /// </summary>
        private void RebuildCustomIdMapping()
        {
            if (DataContext is not DeviceLayoutViewModel vm) return;
            if (!_openRgb.IsConnected || _openRgb.SelectedDeviceIndex < 0) return;

            var deviceLeds = _openRgb.GetSelectedDeviceLeds();
            if (deviceLeds.Count == 0) return;

            // Find which OpenRGB LEDs have no standard RGB.NET match
            var unmatchedOpenRgb = new List<string>();
            foreach (var led in deviceLeds)
            {
                var mapped = OpenRgbService.MapToRgbNetId(led.Name, AllLedIdNames, null);
                if (mapped.StartsWith("Keyboard_Custom", StringComparison.OrdinalIgnoreCase))
                    unmatchedOpenRgb.Add(led.Name);
            }

            // Find Custom IDs in the layout, in order
            var customIds = vm.Items
                .Select(i => i.LedLayout.Id)
                .Where(id => id != null && id.StartsWith("Keyboard_Custom", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Pair them up in order
            for (int i = 0; i < Math.Min(customIds.Count, unmatchedOpenRgb.Count); i++)
            {
                OpenRgbService.RegisterCustomMapping(customIds[i], unmatchedOpenRgb[i]);
            }
        }

        private void AutoFill_Click(object sender, RoutedEventArgs e)
        {
            if (!_openRgb.IsConnected || _openRgb.SelectedDeviceIndex < 0)
            {
                MessageBox.Show("Connect to OpenRGB and select a device first.", "Auto-fill");
                return;
            }

            if (DataContext is not DeviceLayoutViewModel vm) return;

            var deviceLeds = _openRgb.GetSelectedDeviceLeds();
            if (deviceLeds.Count == 0)
            {
                MessageBox.Show("No LEDs found on the selected device.", "Auto-fill");
                return;
            }

            var result = MessageBox.Show(
                $"Add {deviceLeds.Count} LEDs from OpenRGB device?\n\nLEDs already in the layout will be skipped.\nMatrix positions will be used if available.",
                "Auto-fill from OpenRGB",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            // Determine matrix dimensions to scale LEDs to fit the device image
            int maxMatrixRow = 0, maxMatrixCol = 0;
            bool hasMatrix = false;
            foreach (var led in deviceLeds)
            {
                if (led.Row.HasValue && led.Col.HasValue)
                {
                    hasMatrix = true;
                    if (led.Row.Value > maxMatrixRow) maxMatrixRow = led.Row.Value;
                    if (led.Col.Value > maxMatrixCol) maxMatrixCol = led.Col.Value;
                }
            }

            // Set LedUnitWidth/Height to 1 so descriptive values = mm directly.
            // This is saved to the file so reload also treats values as mm.
            vm.DeviceLayout.LedUnitWidth = 1;
            vm.DeviceLayout.LedUnitHeight = 1;

            // If device dimensions aren't set, use canvas dimensions (set from image)
            var devW = (double)vm.DeviceLayout.Width;
            var devH = (double)vm.DeviceLayout.Height;
            if (devW <= 0) devW = Canvas.LayoutWidth;
            if (devH <= 0) devH = Canvas.LayoutHeight;

            // Calculate grid spacing in mm
            double spacingW, spacingH;
            if (hasMatrix && devW > 0 && devH > 0)
            {
                spacingW = Math.Round(devW / (maxMatrixCol + 1), 2);
                spacingH = Math.Round(devH / (maxMatrixRow + 1), 2);
            }
            else
            {
                spacingW = 7;
                spacingH = 7;
            }

            // LED visual size - small enough to pick and place
            double ledW = Math.Min(5, spacingW * 0.8);
            double ledH = Math.Min(5, spacingH * 0.8);
            if (ledW < 2) ledW = 2;
            if (ledH < 2) ledH = 2;

            var leds = new List<(string id, string x, string y, string w, string h)>();
            var occupied = new HashSet<(int row, int col)>();
            // Track used RGB.NET IDs to assign unique Keyboard_Custom{N} for unmatched LEDs
            var usedIds = new HashSet<string>(vm.DeviceLayout.Leds.Select(l => l.Id).Where(id => !string.IsNullOrEmpty(id)), StringComparer.OrdinalIgnoreCase);

            // Place non-matrix LEDs well below the matrix
            int fallbackCol = 0;
            int fallbackRow = hasMatrix ? maxMatrixRow + 4 : 0;
            int fallbackMaxCols = hasMatrix ? maxMatrixCol + 1 : (devW > 0 ? Math.Max(1, (int)(devW / spacingW)) : 10);

            // First pass: place matrix LEDs
            var matrixLeds = deviceLeds.Where(l => l.Row.HasValue && l.Col.HasValue).ToList();
            var nonMatrixLeds = deviceLeds.Where(l => !l.Row.HasValue || !l.Col.HasValue).ToList();

            foreach (var led in matrixLeds)
            {
                var rgbNetId = OpenRgbService.MapToRgbNetId(led.Name, AllLedIdNames, usedIds);

                int row = led.Row.Value;
                int col = led.Col.Value;

                while (occupied.Contains((row, col)))
                    col++;

                occupied.Add((row, col));
                double x = col * spacingW;
                double y = row * spacingH;

                leds.Add((
                    rgbNetId,
                    x.ToString("F1", CultureInfo.InvariantCulture),
                    y.ToString("F1", CultureInfo.InvariantCulture),
                    ledW.ToString("F1", CultureInfo.InvariantCulture),
                    ledH.ToString("F1", CultureInfo.InvariantCulture)
                ));
            }

            // Second pass: place non-matrix LEDs grouped by zone, well below the matrix
            // Linear zones get placed in a horizontal strip; Single zones as individual LEDs
            var zoneGroups = nonMatrixLeds.GroupBy(l => l.ZoneName ?? "Other");
            foreach (var group in zoneGroups)
            {
                fallbackCol = 0;
                var zoneType = group.First().ZoneType;
                var ledsInGroup = group.ToList();

                if (zoneType == "Linear" && ledsInGroup.Count > fallbackMaxCols)
                {
                    // Linear strip: use smaller spacing to fit in one row
                    double stripSpacing = devW > 0
                        ? Math.Round(devW / ledsInGroup.Count, 2)
                        : spacingW;
                    double stripLedW = Math.Min(ledW, stripSpacing * 0.8);

                    foreach (var led in ledsInGroup)
                    {
                        var rgbNetId = OpenRgbService.MapToRgbNetId(led.Name, AllLedIdNames, usedIds);
                        double x = fallbackCol * stripSpacing;
                        double y = fallbackRow * spacingH;

                        leds.Add((
                            rgbNetId,
                            x.ToString("F1", CultureInfo.InvariantCulture),
                            y.ToString("F1", CultureInfo.InvariantCulture),
                            stripLedW.ToString("F1", CultureInfo.InvariantCulture),
                            ledH.ToString("F1", CultureInfo.InvariantCulture)
                        ));
                        fallbackCol++;
                    }
                }
                else
                {
                    foreach (var led in ledsInGroup)
                    {
                        var rgbNetId = OpenRgbService.MapToRgbNetId(led.Name, AllLedIdNames, usedIds);

                        while (occupied.Contains((fallbackRow, fallbackCol)))
                            fallbackCol++;

                        occupied.Add((fallbackRow, fallbackCol));
                        double x = fallbackCol * spacingW;
                        double y = fallbackRow * spacingH;

                        leds.Add((
                            rgbNetId,
                            x.ToString("F1", CultureInfo.InvariantCulture),
                            y.ToString("F1", CultureInfo.InvariantCulture),
                            ledW.ToString("F1", CultureInfo.InvariantCulture),
                            ledH.ToString("F1", CultureInfo.InvariantCulture)
                        ));

                        fallbackCol++;
                        if (fallbackCol >= fallbackMaxCols)
                        {
                            fallbackCol = 0;
                            fallbackRow++;
                        }
                    }
                }

                // Next zone group starts on a new row with a gap
                fallbackRow += 2;
                fallbackCol = 0;
            }

            vm.AddLedsFromDevice(leds);
            Canvas.RedrawCanvas();
        }

        private void UpdateStatusTexts()
        {
            var zoom = Canvas.Zoom;
            ZoomText.Text = $"{zoom:P0}";

            var sel = Canvas.SelectedLed;
            if (sel != null)
            {
                SelectedIdText.Text = sel.LedLayout.Id ?? "?";
                SelectedPosText.Text = $"{sel.LedLayout.X.ToString("F1", CultureInfo.InvariantCulture)}, {sel.LedLayout.Y.ToString("F1", CultureInfo.InvariantCulture)}";
                SelectedSizeText.Text = $"{sel.LedLayout.Width.ToString("F1", CultureInfo.InvariantCulture)} x {sel.LedLayout.Height.ToString("F1", CultureInfo.InvariantCulture)}";
            }
            else
            {
                SelectedIdText.Text = "None";
                SelectedPosText.Text = "-";
                SelectedSizeText.Text = "-";
            }
        }

        private void AutoSpaceH_Click(object sender, RoutedEventArgs e) => Canvas.AutoSpaceHorizontal();
        private void AutoSpaceV_Click(object sender, RoutedEventArgs e) => Canvas.AutoSpaceVertical();
        private void Undo_Click(object sender, RoutedEventArgs e) => Canvas.UndoPublic();
        private void Redo_Click(object sender, RoutedEventArgs e) => Canvas.RedoPublic();

        private void GridColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item && item.Tag is string colorStr)
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(colorStr);
                    Canvas.GridColor = color;
                }
                catch { }
            }
        }
    }
}
