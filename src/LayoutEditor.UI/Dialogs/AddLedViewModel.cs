using System.Linq;
using System.Text.RegularExpressions;
using LayoutEditor.UI.Controls;
using Stylet;

namespace LayoutEditor.UI.Dialogs
{
    public class AddLedViewModel : Screen
    {
        private readonly bool _addBefore;
        private readonly DeviceLayoutViewModel _deviceLayoutViewModel;

        public AddLedViewModel(bool addBefore, DeviceLayoutViewModel deviceLayoutViewModel)
        {
            _addBefore = addBefore;
            _deviceLayoutViewModel = deviceLayoutViewModel;

            AvailableLedIds = new BindableCollection<string>();
            AvailableLedIds.AddRange(deviceLayoutViewModel.Model.GetAvailableLedIds());

            // Suggest next sequential LED based on the last LED in the layout
            SuggestNextLed();
        }

        public BindableCollection<string> AvailableLedIds { get; set; }
        public string SelectedId { get; set; }
        public bool CanAddLed => SelectedId != null;

        public void AddLed()
        {
            _deviceLayoutViewModel.FinishAddLed(_addBefore, SelectedId);
            RequestClose(true);
        }

        public void Cancel()
        {
            RequestClose(false);
        }

        private void SuggestNextLed()
        {
            // Find the last LED in the layout to suggest the next one
            var items = _deviceLayoutViewModel.Items;
            if (items.Count == 0)
            {
                // Default to first available
                SelectedId = AvailableLedIds.FirstOrDefault();
                return;
            }

            var lastLed = _deviceLayoutViewModel.SelectedLed ?? items.Last();
            var lastId = lastLed.LedLayout.Id;
            if (string.IsNullOrEmpty(lastId))
            {
                SelectedId = AvailableLedIds.FirstOrDefault();
                return;
            }

            // Try to find the next sequential LED ID
            // Match pattern: prefix + number (e.g., "Key_F1" → try "Key_F2", or "Led1" → try "Led2")
            var match = Regex.Match(lastId, @"^(.+?)(\d+)$");
            if (match.Success)
            {
                var prefix = match.Groups[1].Value;
                var number = int.Parse(match.Groups[2].Value);
                var nextId = prefix + (number + 1);
                if (AvailableLedIds.Contains(nextId))
                {
                    SelectedId = nextId;
                    return;
                }
            }

            // Try to find the next LED alphabetically in the same prefix group
            // e.g., "Key_A" → "Key_B", "Key_Z" → "Key_BracketLeft" etc.
            var prefixMatch = Regex.Match(lastId, @"^(.+_)(.+)$");
            if (prefixMatch.Success)
            {
                var prefix = prefixMatch.Groups[1].Value;
                // Find all available IDs with the same prefix, pick the first one alphabetically after the current
                var nextInGroup = AvailableLedIds
                    .Where(id => id.StartsWith(prefix))
                    .OrderBy(id => id)
                    .FirstOrDefault(id => string.CompareOrdinal(id, lastId) > 0);
                if (nextInGroup != null)
                {
                    SelectedId = nextInGroup;
                    return;
                }
            }

            // Fallback: first available
            SelectedId = AvailableLedIds.FirstOrDefault();
        }
    }
}
