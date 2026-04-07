using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace LayoutEditor.UI.Controls
{
    public partial class DeviceLayoutView : UserControl
    {
        public DeviceLayoutView()
        {
            InitializeComponent();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is DeviceLayoutViewModel vm)
            {
                Canvas.SetViewModel(vm);

                Canvas.SelectionChanged += () =>
                {
                    // Sync selection back to ViewModel for sidebar bindings
                    vm.SelectedLed = Canvas.SelectedLed;
                    UpdateStatusTexts();
                };

                Canvas.ViewChanged += () =>
                {
                    UpdateStatusTexts();
                };

                Canvas.RedrawCanvas();
                UpdateStatusTexts();
            }
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
    }
}
