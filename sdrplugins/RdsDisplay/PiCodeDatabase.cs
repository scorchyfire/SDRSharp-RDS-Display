using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SDRSharp.RdsDisplay
{
    // Holds one iHeart dual-mapping entry
    public class IHeartEntry
    {
        public string Primary { get; set; } = "";
        public string IHeart { get; set; } = "";
        public long Frequency { get; set; }
    }

    public class PiCodeDatabase
    {
        // PI (decimal int as string key) -> callsign
        private Dictionary<int, string> _normal = new();
        // PI -> { primary, iheart }
        private Dictionary<int, IHeartEntry> _iheart = new();
        // User overrides: PI -> callsign (overrides both normal and iheart lookup)
        private Dictionary<int, string> _custom = new();

        private static readonly string DefaultDbPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "pi_codes.json");

        public string DatabasePath { get; private set; } = DefaultDbPath;

        public void Load(string path)
        {
            DatabasePath = path;
            if (!File.Exists(path)) return;

            try
            {
                // Read without BOM so JsonNode.Parse doesn't choke on UTF-8 BOM bytes
                var raw = File.ReadAllBytes(path);
                int start = (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF) ? 3 : 0;
                var json = System.Text.Encoding.UTF8.GetString(raw, start, raw.Length - start);
                var root = JsonNode.Parse(json);
                if (root == null) return;

                _normal.Clear();
                _iheart.Clear();
                _custom.Clear();

                var normalNode = root["normal"]?.AsObject();
                if (normalNode != null)
                    foreach (var kv in normalNode)
                        if (int.TryParse(kv.Key, out int pi))
                            _normal[pi] = kv.Value?.GetValue<string>() ?? "";

                var iheartNode = root["iheart"]?.AsObject();
                if (iheartNode != null)
                    foreach (var kv in iheartNode)
                        if (int.TryParse(kv.Key, out int pi))
                        {
                            var obj = kv.Value?.AsObject();
                            if (obj != null)
                                _iheart[pi] = new IHeartEntry
                                {
                                    Primary   = obj["primary"]?.GetValue<string>() ?? "",
                                    IHeart    = obj["iheart"]?.GetValue<string>() ?? "",
                                    Frequency = ParseFrequency(obj["iheartFrequency"]?.GetValue<string>())
                                };
                        }

                var customNode = root["custom"]?.AsObject();
                if (customNode != null)
                    foreach (var kv in customNode)
                        if (int.TryParse(kv.Key, out int pi))
                            _custom[pi] = kv.Value?.GetValue<string>() ?? "";
            }
            catch { /* silently ignore corrupt JSON */ }
        }

        public void Save()
        {
            try
            {
                var json = File.ReadAllText(DatabasePath);
                var root = JsonNode.Parse(json) as JsonObject;
                if (root == null) return;

                root.Remove("settings");

                var customObj = new JsonObject();
                foreach (var kv in _custom)
                    customObj[kv.Key.ToString()] = JsonValue.Create(kv.Value);
                root["custom"] = customObj;

                var opts = new JsonSerializerOptions { WriteIndented = true };
                var utf8NoBom = new System.Text.UTF8Encoding(false);
                File.WriteAllText(DatabasePath, root.ToJsonString(opts), utf8NoBom);
            }
            catch { }
        }

        // Resolve a PI code to a callsign string.
        // currentFrequencyHz: current tuned frequency in Hz, for iHeart frequency matching.
        // useIHeart: if true and frequency matches (or unset), return the iHeart market station.
        public string Resolve(int piCode, bool useIHeart, long currentFrequencyHz, out bool isTranslator)
        {
            isTranslator = false;

            // User custom overrides take priority
            if (_custom.TryGetValue(piCode, out string? custom) && !string.IsNullOrEmpty(custom))
                return custom;

            // iHeart dual-mapping
            if (_iheart.TryGetValue(piCode, out IHeartEntry? ih))
            {
                bool freqMatch = ih.Frequency == 0 || ih.Frequency == currentFrequencyHz;
                return (useIHeart && freqMatch) ? ih.IHeart : ih.Primary;
            }

            // Normal lookup
            if (_normal.TryGetValue(piCode, out string? normal) && !string.IsNullOrEmpty(normal))
            {
                // Check if this is a translator entry (contains comma-separated callsigns like "K266CN, W279DI")
                if (IsTranslatorEntry(normal))
                {
                    isTranslator = true;
                    return normal; // caller will resolve by frequency channel
                }
                return normal;
            }

            // Algorithmic decode for 4-letter callsigns (standard NRSC PI algorithm)
            return AlgorithmicDecode(piCode);
        }

        private static long ParseFrequency(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim().ToUpperInvariant()
                .Replace("MHZ", "").Replace("KHZ", "")
                .Replace("HZ", "").Trim();
            if (double.TryParse(text,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double mhz))
                return (long)(mhz * 1_000_000);
            return 0;
        }

        // Check if a csign string is a translator list
        private static bool IsTranslatorEntry(string csign)
        {
            // Translator entries look like "K266CN, W279DI, K271BS" — multiple FM translator calls
            // FM translators are 6-char: K/W + 3-digit channel + 2-letter suffix
            var parts = csign.Split(',');
            if (parts.Length < 2) return false;
            foreach (var p in parts)
            {
                var t = p.Trim();
                if (t.Length == 6 &&
                    (t[0] == 'K' || t[0] == 'W') &&
                    char.IsDigit(t[1]) && char.IsDigit(t[2]) && char.IsDigit(t[3]) &&
                    char.IsLetter(t[4]) && char.IsLetter(t[5]))
                    return true;
            }
            return false;
        }

        // Given a translator entry string and a channel number (200-300), return the matching callsign
        public static string ResolveTranslator(string translatorEntry, int channel)
        {
            if (channel < 200 || channel > 300) return translatorEntry;
            string channelStr = channel.ToString();
            var parts = translatorEntry.Split(',');
            foreach (var p in parts)
            {
                var t = p.Trim();
                if (t.Length == 6 &&
                    (t[0] == 'K' || t[0] == 'W') &&
                    t.Substring(1, 3) == channelStr)
                    return t;
            }
            // No exact match — return the first one as fallback
            return parts[0].Trim();
        }

        // Standard NRSC FM PI algorithmic decode
        private static string AlgorithmicDecode(int pi)
        {
            if (pi <= 4095 || pi >= 39247) return pi.ToString("X4");

            string call1;
            int code;
            if (pi > 21671)
            {
                call1 = "W";
                code = pi - 21672;
            }
            else
            {
                call1 = "K";
                code = pi - 4096;
            }

            int c2 = code / 676;
            code -= 676 * c2;
            int c3 = code / 26;
            int c4 = code - 26 * c3;

            return call1 +
                   ((char)(c2 + 65)).ToString() +
                   ((char)(c3 + 65)).ToString() +
                   ((char)(c4 + 65)).ToString();
        }

        // Frequency (Hz) to FM channel number (200-300)
        public static int FrequencyToChannel(long frequencyHz)
        {
            // Channel N = 87.9 + (N-200)*0.2 MHz
            // N = (freq_MHz - 87.9) / 0.2 + 200
            double freqMhz = frequencyHz / 1_000_000.0;
            int channel = (int)Math.Round((freqMhz - 87.9) / 0.2) + 200;
            return channel;
        }

        public void SetCustom(int pi, string callsign)
        {
            if (string.IsNullOrWhiteSpace(callsign))
                _custom.Remove(pi);
            else
                _custom[pi] = callsign.Trim();
        }

        public bool TryGetCustom(int pi, out string callsign)
        {
            return _custom.TryGetValue(pi, out callsign!);
        }

        public IReadOnlyDictionary<int, string> CustomEntries => _custom;
    }
}
