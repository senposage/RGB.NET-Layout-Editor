using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OpenRGB.NET;

namespace LayoutEditor.UI.Services
{
    public class OpenRgbService : IDisposable
    {
        private OpenRgbClient _client;
        private Device[] _devices;
        private int _selectedDeviceIndex = -1;
        private readonly HashSet<int> _lastHighlightedIndices = new();

        // OpenRGB "Key: X" → RGB.NET "Keyboard_Y" for keys where normalization fails
        // (symbol characters, compound names, naming mismatches)
        private static readonly Dictionary<string, string> OpenRgbToRgbNet = new(StringComparer.OrdinalIgnoreCase)
        {
            // Symbol keys — OpenRGB uses the character, RGB.NET uses descriptive names
            { "Key: -", "Keyboard_MinusAndUnderscore" },
            { "Key: =", "Keyboard_EqualsAndPlus" },
            { "Key: [", "Keyboard_BracketLeft" },
            { "Key: ]", "Keyboard_BracketRight" },
            { "Key: \\", "Keyboard_Backslash" },
            { "Key: \\ (ANSI)", "Keyboard_Backslash" },
            { "Key: \\ (ISO)", "Keyboard_NonUsBackslash" },
            { "Key: ;", "Keyboard_SemicolonAndColon" },
            { "Key: '", "Keyboard_ApostropheAndDoubleQuote" },
            { "Key: ,", "Keyboard_CommaAndLessThan" },
            { "Key: .", "Keyboard_PeriodAndBiggerThan" },
            { "Key: /", "Keyboard_SlashAndQuestionMark" },
            { "Key: `", "Keyboard_GraveAccentAndTilde" },
            // Numpad
            { "Key: Number Pad 0", "Keyboard_Num0" },
            { "Key: Number Pad 1", "Keyboard_Num1" },
            { "Key: Number Pad 2", "Keyboard_Num2" },
            { "Key: Number Pad 3", "Keyboard_Num3" },
            { "Key: Number Pad 4", "Keyboard_Num4" },
            { "Key: Number Pad 5", "Keyboard_Num5" },
            { "Key: Number Pad 6", "Keyboard_Num6" },
            { "Key: Number Pad 7", "Keyboard_Num7" },
            { "Key: Number Pad 8", "Keyboard_Num8" },
            { "Key: Number Pad 9", "Keyboard_Num9" },
            { "Key: Number Pad -", "Keyboard_NumMinus" },
            { "Key: Number Pad +", "Keyboard_NumPlus" },
            { "Key: Number Pad *", "Keyboard_NumAsterisk" },
            { "Key: Number Pad /", "Keyboard_NumSlash" },
            { "Key: Number Pad .", "Keyboard_NumPeriodAndDelete" },
            { "Key: Number Pad =", "Keyboard_NumEquals" },
            { "Key: Number Pad Enter", "Keyboard_NumEnter" },
            // Navigation / modifier naming differences
            { "Key: Left Windows", "Keyboard_LeftGui" },
            { "Key: Right Windows", "Keyboard_RightGui" },
            { "Key: Left Control", "Keyboard_LeftCtrl" },
            { "Key: Right Control", "Keyboard_RightCtrl" },
            { "Key: Right Fn", "Keyboard_Function" },
            { "Key: Left Fn", "Keyboard_Function" },
            { "Key: Menu", "Keyboard_Application" },
            { "Key: Num Lock", "Keyboard_NumLock" },
            { "Key: Up Arrow", "Keyboard_ArrowUp" },
            { "Key: Down Arrow", "Keyboard_ArrowDown" },
            { "Key: Left Arrow", "Keyboard_ArrowLeft" },
            { "Key: Right Arrow", "Keyboard_ArrowRight" },
            { "Key: Pause/Break", "Keyboard_PauseBreak" },
            { "Key: Print Screen", "Keyboard_PrintScreen" },
            { "Key: Scroll Lock", "Keyboard_ScrollLock" },
            { "Key: Caps Lock", "Keyboard_CapsLock" },
            { "Key: Media Play/Pause", "Keyboard_MediaPlay" },
            { "Key: Media Stop", "Keyboard_MediaStop" },
            { "Key: Media Next", "Keyboard_MediaNextTrack" },
            { "Key: Media Previous", "Keyboard_MediaPreviousTrack" },
            { "Key: Media Mute", "Keyboard_MediaMute" },
            { "Key: Media Volume +", "Keyboard_MediaVolumeUp" },
            { "Key: Media Volume -", "Keyboard_MediaVolumeDown" },
        };

        // Reverse lookup: RGB.NET name → OpenRGB name (for FindMatchingLed)
        private static readonly Dictionary<string, string> RgbNetToOpenRgb;

        // Runtime mapping: Keyboard_Custom{N} → original OpenRGB name (populated during auto-fill or device connect)
        private static readonly Dictionary<string, string> CustomIdToOpenRgbName = new(StringComparer.OrdinalIgnoreCase);

        public static void RegisterCustomMapping(string customId, string openRgbName)
        {
            CustomIdToOpenRgbName[customId] = openRgbName;
        }

        static OpenRgbService()
        {
            RgbNetToOpenRgb = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in OpenRgbToRgbNet)
                RgbNetToOpenRgb.TryAdd(kv.Value, kv.Key);
        }

        public bool IsConnected => _client?.Connected == true;
        public Device[] Devices => _devices ?? Array.Empty<Device>();
        public int SelectedDeviceIndex => _selectedDeviceIndex;

        public void Connect(string host = "127.0.0.1", int port = 6742)
        {
            Disconnect();
            _client = new OpenRgbClient(ip: host, port: port, name: "RGB.NET Layout Editor", autoConnect: false);
            _client.Connect();
            _devices = _client.GetAllControllerData();
        }

        public void Disconnect()
        {
            if (_client != null)
            {
                try { ClearAll(); } catch { }
                _client.Dispose();
                _client = null;
            }
            _devices = null;
            _selectedDeviceIndex = -1;
            _lastHighlightedIndices.Clear();
        }

        public void SelectDevice(int index)
        {
            if (_devices == null || index < 0 || index >= _devices.Length)
            {
                _selectedDeviceIndex = -1;
                return;
            }
            _selectedDeviceIndex = index;
            // Set device to custom/direct mode so we can control individual LEDs
            try { _client?.SetCustomMode(index); } catch { }
        }

        /// <summary>
        /// Highlight one or more LEDs by their RGB.NET LedId strings. All other LEDs go dark.
        /// </summary>
        public void HighlightLeds(IEnumerable<string> ledIds)
        {
            if (_client == null || _devices == null || _selectedDeviceIndex < 0 || _selectedDeviceIndex >= _devices.Length)
                return;

            var device = _devices[_selectedDeviceIndex];
            var colors = new Color[device.Leds.Length];
            var black = new Color(0, 0, 0);
            var highlight = new Color(255, 255, 255);

            for (int i = 0; i < colors.Length; i++)
                colors[i] = black;

            _lastHighlightedIndices.Clear();

            foreach (var ledId in ledIds)
            {
                var idx = FindMatchingLed(device, ledId);
                if (idx >= 0)
                {
                    colors[idx] = highlight;
                    _lastHighlightedIndices.Add(idx);
                }
            }

            try { _client.UpdateLeds(_selectedDeviceIndex, colors); } catch { }
        }

        /// <summary>
        /// Highlight a single LED by RGB.NET LedId. All other LEDs go dark.
        /// </summary>
        public void HighlightLed(string ledId)
        {
            HighlightLeds(new[] { ledId });
        }

        public void ClearAll()
        {
            if (_client == null || _devices == null || _selectedDeviceIndex < 0 || _selectedDeviceIndex >= _devices.Length)
                return;

            var device = _devices[_selectedDeviceIndex];
            var colors = new Color[device.Leds.Length];
            var black = new Color(0, 0, 0);
            for (int i = 0; i < colors.Length; i++)
                colors[i] = black;

            _lastHighlightedIndices.Clear();
            try { _client.UpdateLeds(_selectedDeviceIndex, colors); } catch { }
        }

        /// <summary>
        /// Returns the OpenRGB LED names for the selected device (for debugging/display).
        /// </summary>
        public string[] GetLedNames()
        {
            if (_devices == null || _selectedDeviceIndex < 0 || _selectedDeviceIndex >= _devices.Length)
                return Array.Empty<string>();
            return _devices[_selectedDeviceIndex].Leds.Select(l => l.Name).ToArray();
        }

        /// <summary>
        /// Returns LED info for the selected device, including matrix positions if available.
        /// </summary>
        public List<DeviceLedInfo> GetSelectedDeviceLeds()
        {
            if (_devices == null || _selectedDeviceIndex < 0 || _selectedDeviceIndex >= _devices.Length)
                return new List<DeviceLedInfo>();

            var device = _devices[_selectedDeviceIndex];
            var result = new List<DeviceLedInfo>();

            // Build matrix position lookup and zone membership from all zones
            var matrixPositions = new Dictionary<int, (int row, int col)>();
            var ledZoneInfo = new Dictionary<int, (string name, string type)>();
            int ledOffset = 0;
            foreach (var zone in device.Zones)
            {
                var zoneTypeName = zone.Type.ToString();
                for (int j = 0; j < (int)zone.LedCount; j++)
                    ledZoneInfo[ledOffset + j] = (zone.Name, zoneTypeName);

                if (zone.MatrixMap != null)
                {
                    for (int row = 0; row < (int)zone.MatrixMap.Height; row++)
                    {
                        for (int col = 0; col < (int)zone.MatrixMap.Width; col++)
                        {
                            var ledIdx = zone.MatrixMap.Matrix[row, col];
                            if (ledIdx != uint.MaxValue)
                                matrixPositions[(int)ledIdx] = (row, col);
                        }
                    }
                }
                ledOffset += (int)zone.LedCount;
            }

            for (int i = 0; i < device.Leds.Length; i++)
            {
                matrixPositions.TryGetValue(i, out var pos);
                ledZoneInfo.TryGetValue(i, out var zoneInfo);
                result.Add(new DeviceLedInfo(
                    device.Leds[i].Name,
                    i,
                    matrixPositions.ContainsKey(i) ? pos.row : null,
                    matrixPositions.ContainsKey(i) ? pos.col : null,
                    zoneInfo.name,
                    zoneInfo.type
                ));
            }

            return result;
        }

        /// <summary>
        /// Try to map an OpenRGB LED name to an RGB.NET LedId enum name.
        /// </summary>
        /// <summary>
        /// Map an OpenRGB LED name to an RGB.NET LedId enum name.
        /// Pass usedIds to track assignments; unmatched LEDs get Keyboard_Custom{N}.
        /// </summary>
        public static string MapToRgbNetId(string openRgbName, IEnumerable<string> allLedIds, HashSet<string> usedIds = null)
        {
            if (string.IsNullOrEmpty(openRgbName))
                return AssignCustomId(usedIds, openRgbName);

            // Direct dictionary lookup for known problem keys (symbols, compound names)
            if (OpenRgbToRgbNet.TryGetValue(openRgbName, out var mapped))
            {
                usedIds?.Add(mapped);
                return mapped;
            }

            // Fallback: normalize and compare
            var normalized = NormalizeName(openRgbName);
            foreach (var ledId in allLedIds)
            {
                if (NormalizeName(ledId) == normalized)
                {
                    usedIds?.Add(ledId);
                    return ledId;
                }
            }

            // No match — assign Keyboard_Custom{N} and remember the original OpenRGB name
            return AssignCustomId(usedIds, openRgbName);
        }

        private static string AssignCustomId(HashSet<string> usedIds, string originalOpenRgbName)
        {
            for (int i = 1; i <= 99; i++)
            {
                var id = $"Keyboard_Custom{i}";
                if (usedIds == null || !usedIds.Contains(id))
                {
                    usedIds?.Add(id);
                    if (!string.IsNullOrEmpty(originalOpenRgbName))
                        CustomIdToOpenRgbName[id] = originalOpenRgbName;
                    return id;
                }
            }
            return $"Keyboard_Custom{usedIds?.Count ?? 0}";
        }

        private int FindMatchingLed(Device device, string ledId)
        {
            if (string.IsNullOrEmpty(ledId)) return -1;

            // Check Custom ID mapping first (Keyboard_Custom{N} → original OpenRGB name)
            if (CustomIdToOpenRgbName.TryGetValue(ledId, out var customOriginal))
            {
                for (int i = 0; i < device.Leds.Length; i++)
                {
                    if (string.Equals(device.Leds[i].Name, customOriginal, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }

            // If ledId is an RGB.NET name, try ALL OpenRGB names that map to it
            // (e.g. Keyboard_Function → "Key: Right Fn" and "Key: Left Fn")
            foreach (var kv in OpenRgbToRgbNet)
            {
                if (string.Equals(kv.Value, ledId, StringComparison.OrdinalIgnoreCase))
                {
                    for (int i = 0; i < device.Leds.Length; i++)
                    {
                        if (string.Equals(device.Leds[i].Name, kv.Key, StringComparison.OrdinalIgnoreCase))
                            return i;
                    }
                }
            }

            var normalizedId = NormalizeName(ledId);

            // Normalized match
            for (int i = 0; i < device.Leds.Length; i++)
            {
                if (NormalizeName(device.Leds[i].Name) == normalizedId)
                    return i;
            }

            // Partial match: one contains the other (require min 3 chars to avoid false positives like "c" matching "function")
            for (int i = 0; i < device.Leds.Length; i++)
            {
                var normalizedLed = NormalizeName(device.Leds[i].Name);
                if (normalizedLed.Length >= 3 && normalizedId.Length >= 3 &&
                    (normalizedId.Contains(normalizedLed) || normalizedLed.Contains(normalizedId)))
                    return i;
            }

            return -1;
        }

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";

            // Strip common prefixes used by RGB.NET and OpenRGB
            // RGB.NET uses "Keyboard_", OpenRGB uses "Key: "
            name = Regex.Replace(name, @"^Keyboard[_:\s]+", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"^Key[_:\s]+", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"^Led[_:\s]*", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"^Mouse[_:\s]+", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"^Headset[_:\s]+", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"^Mousepad[_:\s]+", "", RegexOptions.IgnoreCase);

            // Normalize separators and whitespace
            // Note: symbol keys (-, =, etc.) are handled by the direct dictionary lookups
            // before NormalizeName is called, so stripping these is safe for word-based names
            name = name.Replace("_", "").Replace(" ", "").Replace("-", "").Replace("/", "");

            return name.Trim().ToLowerInvariant();
        }

        public void Dispose()
        {
            Disconnect();
        }
    }

    public record struct DeviceLedInfo(string Name, int Index, int? Row, int? Col, string ZoneName = null, string ZoneType = null);
}
