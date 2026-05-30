using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

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
                var raw = File.ReadAllBytes(path);
                // Strip UTF-8 BOM if present
                int start = (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF) ? 3 : 0;
                var json = Encoding.UTF8.GetString(raw, start, raw.Length - start);

                _normal.Clear();
                _iheart.Clear();
                _custom.Clear();

                // Extract the three top-level sections using a minimal JSON reader
                var normalSection  = ExtractSection(json, "normal");
                var iheartSection  = ExtractSection(json, "iheart");
                var customSection  = ExtractSection(json, "custom");

                // "normal": { "12345": "WXYZ", ... }
                foreach (var kv in ParseFlatStringObject(normalSection))
                    if (int.TryParse(kv.Key, out int pi))
                        _normal[pi] = kv.Value;

                // "iheart": { "12345": { "primary": "WXYZ", "iheart": "WABC", "iheartFrequency": "98.7 MHz" }, ... }
                foreach (var kv in ParseIHeartSection(iheartSection))
                    _iheart[kv.Key] = kv.Value;

                // "custom": { "12345": "WXYZ", ... }
                foreach (var kv in ParseFlatStringObject(customSection))
                    if (int.TryParse(kv.Key, out int pi))
                        _custom[pi] = kv.Value;
            }
            catch { /* silently ignore corrupt JSON */ }
        }

        public void Save()
        {
            try
            {
                if (!File.Exists(DatabasePath)) return;
                var raw = File.ReadAllBytes(DatabasePath);
                int bom = (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF) ? 3 : 0;
                var json = Encoding.UTF8.GetString(raw, bom, raw.Length - bom);

                // Replace the "custom" section with the current custom entries
                var customJson = BuildFlatStringObject(_custom);
                json = ReplaceSection(json, "custom", customJson);

                File.WriteAllText(DatabasePath, json, new UTF8Encoding(false));
            }
            catch { }
        }

        // Resolve a PI code to a callsign string.
        public string Resolve(int piCode, bool useIHeart, long currentFrequencyHz, out bool isTranslator)
        {
            isTranslator = false;

            // Custom entries are keyed by raw PI (user entered them that way)
            if (_custom.TryGetValue(piCode, out string? custom) && !string.IsNullOrEmpty(custom))
                return custom;

            // Database entries are keyed by remapped PI (matching rdscalculator.js switch keys)
            int lookupPi = RemapPi(piCode);

            if (_iheart.TryGetValue(lookupPi, out IHeartEntry? ih))
            {
                bool freqMatch = ih.Frequency == 0 || ih.Frequency == currentFrequencyHz;
                return (useIHeart && freqMatch) ? ih.IHeart : ih.Primary;
            }

            if (_normal.TryGetValue(lookupPi, out string? normal) && !string.IsNullOrEmpty(normal))
            {
                if (IsTranslatorEntry(normal))
                {
                    isTranslator = true;
                    return normal;
                }
                return normal;
            }

            return AlgorithmicDecode(piCode);
        }

        // Apply the A-prefix remapping from rdscalculator.js before any lookup or decode.
        // AF__ → __00, A___ (non-AF) → _0__
        private static int RemapPi(int pi)
        {
            string piHex = pi.ToString("X4");
            if (piHex.Length == 4 && piHex[0] == 'A')
            {
                string remapped = piHex[1] == 'F'
                    ? piHex.Substring(2) + "00"
                    : piHex[1] + "0" + piHex.Substring(2);
                if (int.TryParse(remapped, System.Globalization.NumberStyles.HexNumber, null, out int remappedPi))
                    return remappedPi;
            }
            return pi;
        }

        // ── Minimal JSON helpers ────────────────────────────────────────────────────

        // Extract the raw JSON text of a top-level object section by key name.
        // Returns the content between the outermost { } of that section (not including braces).
        private static string ExtractSection(string json, string key)
        {
            // Find "key": {
            int keyPos = FindKey(json, key, 0);
            if (keyPos < 0) return "";

            int braceStart = json.IndexOf('{', keyPos);
            if (braceStart < 0) return "";

            int depth = 0;
            for (int i = braceStart; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"') { i = SkipString(json, i + 1); continue; }
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return json.Substring(braceStart + 1, i - braceStart - 1); }
            }
            return "";
        }

        // Find the position of "key": in the json string (avoids matching inside string values)
        private static int FindKey(string json, string key, int startAt)
        {
            string needle = "\"" + key + "\"";
            int pos = startAt;
            while ((pos = json.IndexOf(needle, pos, StringComparison.Ordinal)) >= 0)
            {
                // Check that the next non-whitespace after the key is ':'
                int after = pos + needle.Length;
                while (after < json.Length && json[after] == ' ') after++;
                if (after < json.Length && json[after] == ':')
                    return pos;
                pos++;
            }
            return -1;
        }

        // Skip past a JSON string starting right after the opening quote.
        // Returns the index of the closing quote.
        private static int SkipString(string json, int i)
        {
            while (i < json.Length)
            {
                if (json[i] == '\\') { i += 2; continue; }
                if (json[i] == '"') return i;
                i++;
            }
            return i;
        }

        // Parse { "key": "value", ... } into a flat string dictionary.
        private static IEnumerable<KeyValuePair<string, string>> ParseFlatStringObject(string section)
        {
            int i = 0;
            while (i < section.Length)
            {
                // Find next "key"
                int qs = section.IndexOf('"', i);
                if (qs < 0) yield break;
                int qe = SkipString(section, qs + 1);
                string key = section.Substring(qs + 1, qe - qs - 1);
                i = qe + 1;

                // Find ':'
                int colon = section.IndexOf(':', i);
                if (colon < 0) yield break;
                i = colon + 1;

                // Skip whitespace
                while (i < section.Length && (section[i] == ' ' || section[i] == '\r' || section[i] == '\n' || section[i] == '\t')) i++;
                if (i >= section.Length) yield break;

                if (section[i] == '"')
                {
                    // String value
                    int vs = i;
                    int ve = SkipString(section, vs + 1);
                    string val = UnescapeJsonString(section.Substring(vs + 1, ve - vs - 1));
                    yield return new KeyValuePair<string, string>(key, val);
                    i = ve + 1;
                }
                else if (section[i] == '{')
                {
                    // Nested object — skip it (not expected for flat sections)
                    int depth2 = 0;
                    while (i < section.Length)
                    {
                        if (section[i] == '"') { i = SkipString(section, i + 1) + 1; continue; }
                        if (section[i] == '{') depth2++;
                        else if (section[i] == '}') { depth2--; if (depth2 == 0) { i++; break; } }
                        i++;
                    }
                }
                else
                {
                    // null, number, bool — skip to next comma or end
                    while (i < section.Length && section[i] != ',' && section[i] != '}') i++;
                    i++;
                }
            }
        }

        // Parse the iheart section: { "12345": { "primary": "X", "iheart": "Y", "iheartFrequency": "Z" }, ... }
        private static IEnumerable<KeyValuePair<int, IHeartEntry>> ParseIHeartSection(string section)
        {
            int i = 0;
            while (i < section.Length)
            {
                int qs = section.IndexOf('"', i);
                if (qs < 0) yield break;
                int qe = SkipString(section, qs + 1);
                string key = section.Substring(qs + 1, qe - qs - 1);
                i = qe + 1;

                if (!int.TryParse(key, out int pi)) { i++; continue; }

                int colon = section.IndexOf(':', i);
                if (colon < 0) yield break;
                i = colon + 1;

                while (i < section.Length && (section[i] == ' ' || section[i] == '\r' || section[i] == '\n' || section[i] == '\t')) i++;
                if (i >= section.Length || section[i] != '{') { i++; continue; }

                // Find matching }
                int objStart = i;
                int depth2 = 0;
                int objEnd = i;
                for (int j = i; j < section.Length; j++)
                {
                    if (section[j] == '"') { j = SkipString(section, j + 1); continue; }
                    if (section[j] == '{') depth2++;
                    else if (section[j] == '}') { depth2--; if (depth2 == 0) { objEnd = j; break; } }
                }

                string inner = section.Substring(objStart + 1, objEnd - objStart - 1);
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in ParseFlatStringObject(inner))
                    fields[kv.Key] = kv.Value;

                fields.TryGetValue("primary", out string? primary);
                fields.TryGetValue("iheart", out string? iheart);
                fields.TryGetValue("iheartFrequency", out string? freq);

                yield return new KeyValuePair<int, IHeartEntry>(pi, new IHeartEntry
                {
                    Primary   = primary ?? "",
                    IHeart    = iheart ?? "",
                    Frequency = ParseFrequency(freq)
                });

                i = objEnd + 1;
            }
        }

        // Replace the content of "key": { ... } with newContent (a pre-built JSON object string)
        private static string ReplaceSection(string json, string key, string newContent)
        {
            int keyPos = FindKey(json, key, 0);
            if (keyPos < 0) return json;

            int braceStart = json.IndexOf('{', keyPos);
            if (braceStart < 0) return json;

            int depth = 0;
            for (int i = braceStart; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"') { i = SkipString(json, i + 1); continue; }
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                        return json.Substring(0, braceStart) + newContent + json.Substring(i + 1);
                }
            }
            return json;
        }

        // Build a JSON object string from a dictionary
        private static string BuildFlatStringObject(Dictionary<int, string> dict)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            bool first = true;
            foreach (var kv in dict)
            {
                if (!first) sb.Append(",\n");
                sb.Append("    \"").Append(kv.Key).Append("\": \"").Append(EscapeJsonString(kv.Value)).Append('"');
                first = false;
            }
            sb.Append("\n  }");
            return sb.ToString();
        }

        private static string UnescapeJsonString(string s)
        {
            if (!s.Contains('\\')) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    switch (s[i])
                    {
                        case '"':  sb.Append('"');  break;
                        case '\\': sb.Append('\\'); break;
                        case '/':  sb.Append('/');  break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        case 'u' when i + 4 < s.Length:
                            if (int.TryParse(s.Substring(i + 1, 4),
                                System.Globalization.NumberStyles.HexNumber, null, out int code))
                                sb.Append((char)code);
                            i += 4;
                            break;
                        default: sb.Append(s[i]); break;
                    }
                }
                else
                {
                    sb.Append(s[i]);
                }
            }
            return sb.ToString();
        }

        private static string EscapeJsonString(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        // ── Existing helpers (unchanged) ────────────────────────────────────────────

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

        private static bool IsTranslatorEntry(string csign)
        {
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
            return parts[0].Trim();
        }

        private static string AlgorithmicDecode(int pi)
        {
            pi = RemapPi(pi);
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

        public static int FrequencyToChannel(long frequencyHz)
        {
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
