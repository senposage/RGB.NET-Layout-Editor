using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
                vm.SetCanvas(Canvas);

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
