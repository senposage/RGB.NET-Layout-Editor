using System.ComponentModel;
using System.IO;
using System.Linq;
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
                // Batch: set width/height on all selected, then one UpdateLeds()
                foreach (var led in canvas.SelectedLeds)
                {
                    led.InputWidth = SelectedLed.InputWidth;
                    led.InputHeight = SelectedLed.InputHeight;
                    led.ApplyInputWithoutUpdate();
                }
                RecalcLeds();
            }
            else
            {
                SelectedLed.ApplyInputWithoutUpdate();
                RecalcLeds();
            }
        }

        private LayoutCanvas GetCanvas()
        {
            // Walk visual tree to find the LayoutCanvas - used for multi-select operations
            return _canvas;
        }

        private LayoutCanvas _canvas;
        public void SetCanvas(LayoutCanvas canvas) => _canvas = canvas;

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

            Items.Remove(SelectedLed);
            DeviceLayout.InternalLeds.Remove(SelectedLed.LedLayout);
            SelectedLed = null;
            UpdateLeds();
        }

        protected override void OnInitialActivate()
        {
            Items.AddRange(DeviceLayout.Leds.Select(l => new LedViewModel(Model, this, _windowManager, (LedLayout)l)));
            UpdateLeds();

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
