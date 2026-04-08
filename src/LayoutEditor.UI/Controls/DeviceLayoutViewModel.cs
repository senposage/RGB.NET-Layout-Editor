using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using LayoutEditor.UI.Dialogs;
using LayoutEditor.UI.Layout;
using LayoutEditor.UI.Models;
using LayoutEditor.UI.Pages;
using RGB.NET.Layout;
using Stylet;

namespace LayoutEditor.UI.Controls
{
    public class DeviceLayoutViewModel : Conductor<LedViewModel>.Collection.AllActive
    {
        private readonly IWindowManager _windowManager;

        public DeviceLayoutViewModel(LayoutEditModel model, DeviceLayoutEditorViewModel editorViewModel, IWindowManager windowManager)
        {
            _windowManager = windowManager;
            Model = model;
            DeviceLayout = model.DeviceLayout;
            EditorViewModel = editorViewModel;
        }

        public LayoutEditModel Model { get; }
        public DeviceLayout DeviceLayout { get; }
        public DeviceLayoutEditorViewModel EditorViewModel { get; }
        public LedViewModel SelectedLed { get; set; }

        public void UpdateLeds()
        {
            RecalcLayoutValues();
            foreach (var ledViewModel in Items)
                ledViewModel.Update();
        }

        /// <summary>
        /// Lightweight recalc: only recomputes layout positions/sizes from descriptive values
        /// and refreshes Input* fields. Skips expensive ApplyLogicalLayout and CreateLedGeometry.
        /// Use this for drag/resize/nudge operations where the canvas renders directly.
        /// </summary>
        public void RecalcLeds()
        {
            RecalcLayoutValues();
            foreach (var ledViewModel in Items)
                ledViewModel.PopulateInputOnly();
        }

        private void RecalcLayoutValues()
        {
            if (DeviceLayout.Leds != null)
            {
                LedLayout lastLed = null;
                foreach (var ledLayout in DeviceLayout.Leds)
                {
                    var led = (LedLayout)ledLayout;
                    led.CalculateValues(DeviceLayout, lastLed);
                    lastLed = led;
                }
            }
        }

        public void ApplyLed()
        {
            if (SelectedLed == null) return;

            var canvas = GetCanvas();
            if (canvas != null && canvas.SelectedLeds.Count > 1)
            {
                // Capture undo state before batch resize
                canvas.SaveUndoForSelectedPublic();

                // Batch: set width/height/shape on all selected, apply each independently
                foreach (var led in canvas.SelectedLeds)
                {
                    led.InputWidth = SelectedLed.InputWidth;
                    led.InputHeight = SelectedLed.InputHeight;
                    led.InputShape = SelectedLed.InputShape;
                    led.InputShapeData = SelectedLed.InputShapeData;
                    led.ApplyInputWithoutUpdate();
                    led.ApplyPositionDirect();
                }
                canvas.RedrawCanvas();
            }
            else
            {
                // Capture undo for single LED
                if (canvas != null)
                    canvas.SaveUndoForSelectedPublic();

                SelectedLed.ApplyInputWithoutUpdate();
                SelectedLed.ApplyPositionDirect();
                canvas?.RedrawCanvas();
            }
        }

        private LayoutCanvas GetCanvas()
        {
            // Walk visual tree to find the LayoutCanvas - used for multi-select operations
            return _canvas;
        }

        private LayoutCanvas _canvas;
        public void SetCanvas(LayoutCanvas canvas) => _canvas = canvas;

        /// <summary>
        /// Push DeviceLayout.Width/Height to the canvas, triggering proportional LED scaling.
        /// </summary>
        public void PushDimensionsToCanvas()
        {
            if (_canvas == null) return;
            var w = (double)DeviceLayout.Width;
            var h = (double)DeviceLayout.Height;
            if (w > 0) _canvas.LayoutWidth = w;
            if (h > 0) _canvas.LayoutHeight = h;
        }

        /// <summary>
        /// Directly loads and applies the device image to the canvas, bypassing binding/event chains.
        /// </summary>
        public void RefreshCanvasDeviceImage()
        {
            if (_canvas == null) return;
            try
            {
                // 1. Use the absolute path stored at selection time (most reliable)
                string absolutePath = EditorViewModel.AbsoluteDeviceImagePath;

                // 2. Fallback: resolve from LayoutCustomDeviceData.DeviceImage
                if (string.IsNullOrEmpty(absolutePath) || !System.IO.File.Exists(absolutePath))
                {
                    absolutePath = null;
                    var customData = EditorViewModel.LayoutCustomDeviceData;
                    if (customData == null || string.IsNullOrEmpty(customData.DeviceImage))
                    {
                        _canvas.DeviceImageSource = null;
                        _canvas.RedrawCanvas();
                        return;
                    }

                    // 2a. Resolve relative to the layout file
                    try
                    {
                        var fileUri = new System.Uri(new System.Uri(EditorViewModel.Model.FilePath), customData.DeviceImage);
                        if (System.IO.File.Exists(fileUri.LocalPath))
                            absolutePath = fileUri.LocalPath;
                    }
                    catch { }

                    // 2b. Check same directory as the layout file (handles cross-drive filename-only saves)
                    if (absolutePath == null)
                    {
                        var layoutDir = System.IO.Path.GetDirectoryName(EditorViewModel.Model.FilePath);
                        if (layoutDir != null)
                        {
                            var sameDir = System.IO.Path.Combine(layoutDir, System.IO.Path.GetFileName(customData.DeviceImage));
                            if (System.IO.File.Exists(sameDir))
                                absolutePath = sameDir;
                        }
                    }

                    // 2c. Treat DeviceImage as absolute path
                    if (absolutePath == null && System.IO.File.Exists(customData.DeviceImage))
                        absolutePath = System.IO.Path.GetFullPath(customData.DeviceImage);
                }

                if (absolutePath == null)
                {
                    _canvas.DeviceImageSource = null;
                    _canvas.RedrawCanvas();
                    return;
                }

                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
                image.UriSource = new System.Uri(absolutePath);
                image.EndInit();
                image.Freeze();

                _canvas.DeviceImageSource = image;

                // If layout has no dimensions, set size from image aspect ratio
                // Write back to DeviceLayout so the value stays in sync with the canvas
                if (_canvas.LayoutWidth <= 0 || _canvas.LayoutHeight <= 0)
                {
                    if (image.PixelWidth > 0 && image.PixelHeight > 0)
                    {
                        double aspect = (double)image.PixelWidth / image.PixelHeight;
                        double w, h;
                        if (aspect >= 1)
                        {
                            w = 200;
                            h = System.Math.Round(200 / aspect);
                        }
                        else
                        {
                            h = 200;
                            w = System.Math.Round(200 * aspect);
                        }
                        DeviceLayout.Width = (float)w;
                        DeviceLayout.Height = (float)h;
                        _canvas.SuppressDimensionScaling = true;
                        _canvas.LayoutWidth = w;
                        _canvas.LayoutHeight = h;
                        _canvas.SuppressDimensionScaling = false;
                    }
                }

                _canvas.RedrawCanvas();
            }
            catch
            {
                _canvas.DeviceImageSource = null;
                _canvas.RedrawCanvas();
            }
        }

        public void AddLed(string addBefore)
        {
            _windowManager.ShowDialog(new AddLedViewModel(bool.Parse(addBefore), this));
        }

        public void FinishAddLed(bool addBefore, string ledId)
        {
            int index;
            if (SelectedLed == null)
                index = addBefore ? 0 : Items.Count;
            else
                index = addBefore ? Items.IndexOf(SelectedLed) : Items.IndexOf(SelectedLed) + 1;

            var ledLayout = new LedLayout { Id = ledId, CustomData = new LayoutCustomLedData() };
            var ledViewModel = new LedViewModel(Model, this, _windowManager, ledLayout);

            DeviceLayout.InternalLeds.Insert(index, ledLayout);
            Items.Insert(index, ledViewModel);

            UpdateLeds();
        }

        public void RemoveLed()
        {
            if (SelectedLed == null) return;

            var led = SelectedLed;

            // Clear selection silently first, then remove, then redraw
            _canvas?.ClearSelectionSilent();
            SelectedLed = null;

            Items.Remove(led);
            DeviceLayout.InternalLeds.Remove(led.LedLayout);

            _canvas?.RedrawCanvas();
        }

        public void RemoveAllLeds()
        {
            var result = _windowManager.ShowMessageBox(
                $"Remove all {Items.Count} LEDs? This cannot be undone.",
                "Confirm",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            _canvas?.ClearSelectionSilent();
            SelectedLed = null;

            Items.Clear();
            DeviceLayout.InternalLeds.Clear();

            _canvas?.RedrawCanvas();
        }

        /// <summary>
        /// Add LEDs from an external source (OpenRGB) with optional positions.
        /// </summary>
        public void AddLedsFromDevice(List<(string id, string x, string y, string w, string h)> leds)
        {
            var existingIds = new HashSet<string>(DeviceLayout.Leds.Select(l => l.Id));

            // Use a temp device with unit size 1 so descriptive values are treated as mm directly
            // (avoids the real DeviceLayout.LedUnitWidth multiplier inflating sizes)
            var tempDevice = new DeviceLayout { LedUnitWidth = 1, LedUnitHeight = 1 };

            var added = 0;
            foreach (var (id, x, y, w, h) in leds)
            {
                if (existingIds.Contains(id)) continue;

                var ledLayout = new LedLayout
                {
                    Id = id,
                    DescriptiveX = x,
                    DescriptiveY = y,
                    DescriptiveWidth = w,
                    DescriptiveHeight = h,
                    CustomData = new LayoutCustomLedData()
                };
                // Calculate with unit=1 so values pass through as mm
                ledLayout.CalculateValues(tempDevice, null);

                DeviceLayout.InternalLeds.Add(ledLayout);
                var vm = new LedViewModel(Model, this, _windowManager, ledLayout);
                vm.PopulateInputOnly();
                Items.Add(vm);
                added++;
            }
        }

        protected override void OnInitialActivate()
        {
            Items.AddRange(DeviceLayout.Leds.Select(l => new LedViewModel(Model, this, _windowManager, (LedLayout)l)));
            // First calculate with original units so existing layouts load correctly
            UpdateLeds();

            // Now normalize: write calculated absolute mm back as descriptive values
            // and set unit=1 so all editor operations (drag, resize, save/reload) are consistent
            foreach (var ledVm in Items)
            {
                ledVm.LedLayout.DescriptiveX = ledVm.LedLayout.X.ToString(System.Globalization.CultureInfo.InvariantCulture);
                ledVm.LedLayout.DescriptiveY = ledVm.LedLayout.Y.ToString(System.Globalization.CultureInfo.InvariantCulture);
                ledVm.LedLayout.DescriptiveWidth = ledVm.LedLayout.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
                ledVm.LedLayout.DescriptiveHeight = ledVm.LedLayout.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            DeviceLayout.LedUnitWidth = 1;
            DeviceLayout.LedUnitHeight = 1;

            EditorViewModel.PropertyChanged += EditorViewModelOnPropertyChanged;

            var ledWithLayout = Items
                .FirstOrDefault(i => i.LayoutCustomLedData?.LogicalLayouts.Any() == true)
                ?.LayoutCustomLedData.LogicalLayouts.FirstOrDefault();
            if (ledWithLayout != null)
                EditorViewModel.LedSubfolder = Path.GetDirectoryName(ledWithLayout.Image);

            base.OnInitialActivate();
        }

        protected override void OnClose()
        {
            EditorViewModel.PropertyChanged -= EditorViewModelOnPropertyChanged;
            base.OnClose();
        }

        private void EditorViewModelOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EditorViewModel.SelectedLogicalLayout))
                UpdateLeds();
        }
    }
}
