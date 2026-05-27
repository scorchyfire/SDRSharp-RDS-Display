using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace SDRSharp.RdsDisplay
{
    public static class PluginSettings
    {
        private const string KeyPrefix = "rdsDisplay.";

        private static string? _configPath;
        private static bool    _loaded;

        private static bool   _useIHeartMarket;
        private static bool   _showPsUnderscores;
        private static bool   _useNorthAmerica;
        private static string _fontName      = "Lucida Console";
        private static float  _fontSize      = 9f;
        private static string _fontStyle     = "Regular";
        private static string _graphicsUnit  = "Point";
        private static string _foreColor     = "#FFFFFF";
        private static string _backColor     = "#000000";
        private static float  _scaleStretchX = 0f;

        public static void Init()
        {
            _configPath = FindConfigFile();
            Load();
        }

        public static bool UseIHeartMarket
        {
            get { if (!_loaded) Load(); return _useIHeartMarket; }
            set { _useIHeartMarket = value; Save(); }
        }

        public static bool ShowPsUnderscores
        {
            get { if (!_loaded) Load(); return _showPsUnderscores; }
            set { _showPsUnderscores = value; Save(); }
        }

        public static bool UseNorthAmerica
        {
            get { if (!_loaded) Load(); return _useNorthAmerica; }
            set { _useNorthAmerica = value; Save(); }
        }

        public static string FontName
        {
            get { if (!_loaded) Load(); return _fontName; }
            set { _fontName = value; Save(); }
        }

        public static float FontSize
        {
            get { if (!_loaded) Load(); return _fontSize; }
            set { _fontSize = value; Save(); }
        }

        public static string FontStyleName
        {
            get { if (!_loaded) Load(); return _fontStyle; }
            set { _fontStyle = value; Save(); }
        }

        public static string GraphicsUnitName
        {
            get { if (!_loaded) Load(); return _graphicsUnit; }
            set { _graphicsUnit = value; Save(); }
        }

        public static string ForeColorHex
        {
            get { if (!_loaded) Load(); return _foreColor; }
            set { _foreColor = value; Save(); }
        }

        public static string BackColorHex
        {
            get { if (!_loaded) Load(); return _backColor; }
            set { _backColor = value; Save(); }
        }

        public static float ScaleStretchX
        {
            get { if (!_loaded) Load(); return _scaleStretchX; }
            set { _scaleStretchX = value; Save(); }
        }

        public static FontStyle ParsedFontStyle =>
            Enum.TryParse<FontStyle>(_fontStyle, true, out var fs) ? fs : FontStyle.Regular;

        public static GraphicsUnit ParsedGraphicsUnit =>
            Enum.TryParse<GraphicsUnit>(_graphicsUnit, true, out var gu) ? gu : GraphicsUnit.Point;

        public static Color ParsedForeColor => ParseColor(_foreColor, Color.White);
        public static Color ParsedBackColor  => ParseColor(_backColor, Color.Black);

        private static Color ParseColor(string hex, Color fallback)
        {
            try
            {
                hex = hex.TrimStart('#');
                if (hex.Length == 6)
                    return Color.FromArgb(
                        Convert.ToInt32(hex[0..2], 16),
                        Convert.ToInt32(hex[2..4], 16),
                        Convert.ToInt32(hex[4..6], 16));
                if (hex.Length == 8)
                    return Color.FromArgb(
                        Convert.ToInt32(hex[0..2], 16),
                        Convert.ToInt32(hex[2..4], 16),
                        Convert.ToInt32(hex[4..6], 16),
                        Convert.ToInt32(hex[6..8], 16));
            }
            catch { }
            return fallback;
        }

        private static string FindConfigFile()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, "SDRSharp.RdsDisplay.xml");
        }

        private static void Load()
        {
            _loaded = true;
            if (_configPath == null || !File.Exists(_configPath)) return;
            try
            {
                var doc = XDocument.Load(_configPath);
                var root = doc.Root;
                if (root == null) return;

                _useIHeartMarket   = ReadBool(root,   "useIHeartMarket",   false);
                _showPsUnderscores = ReadBool(root,   "showPsUnderscores", false);
                _useNorthAmerica   = ReadBool(root,   "useNorthAmerica",   false);
                _fontName          = ReadString(root, "fontName",          _fontName);
                _fontSize          = ReadFloat(root,  "fontSize",          _fontSize);
                _fontStyle         = ReadString(root, "fontStyle",         _fontStyle);
                _graphicsUnit      = ReadString(root, "graphicsUnit",      _graphicsUnit);
                _foreColor         = ReadString(root, "foreColor",         _foreColor);
                _backColor         = ReadString(root, "backColor",         _backColor);
                _scaleStretchX     = ReadFloat(root,  "scaleStretchX",     _scaleStretchX);
            }
            catch { }
        }

        private static string ReadString(XElement root, string key, string defaultValue)
        {
            var el = root.Elements("add")
                .FirstOrDefault(e => e.Attribute("key")?.Value == KeyPrefix + key);
            return el?.Attribute("value")?.Value ?? defaultValue;
        }

        private static bool ReadBool(XElement root, string key, bool defaultValue)
        {
            var val = ReadString(root, key, "");
            return bool.TryParse(val, out bool result) ? result : defaultValue;
        }

        private static float ReadFloat(XElement root, string key, float defaultValue)
        {
            var val = ReadString(root, key, "");
            return float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
                ? result : defaultValue;
        }

        private static void Save()
        {
            if (_configPath == null) return;
            try
            {
                XDocument doc;
                if (File.Exists(_configPath))
                    doc = XDocument.Load(_configPath, LoadOptions.PreserveWhitespace);
                else
                    doc = new XDocument(
                        new XDeclaration("1.0", "utf-8", null),
                        new XElement("configuration", Environment.NewLine));

                var root = doc.Root;
                if (root == null) return;

                var toRemove = new List<XNode>();
                foreach (var node in root.Nodes())
                {
                    if (node is XElement el && el.Name == "add"
                        && el.Attribute("key")?.Value?.StartsWith(KeyPrefix) == true)
                    {
                        var prev = node.PreviousNode;
                        if (prev is XText txt && string.IsNullOrWhiteSpace(txt.Value))
                            toRemove.Add(prev);
                        toRemove.Add(node);
                    }
                }
                foreach (var node in toRemove)
                    node.Remove();

                string indent = "    ";
                var firstAdd = root.Elements("add").FirstOrDefault();
                if (firstAdd != null)
                {
                    var prevNode = firstAdd.PreviousNode;
                    if (prevNode is XElement prevEl && prevEl.Name == "add")
                        prevNode = prevEl.PreviousNode;
                    else if (prevNode is XComment)
                    {
                        var beforeComment = prevNode.PreviousNode;
                        if (beforeComment is XText beforeTxt)
                            prevNode = beforeTxt;
                    }
                }
                if (firstAdd != null)
                {
                    var prev = firstAdd.PreviousNode;
                    while (prev != null && !(prev is XText))
                        prev = prev.PreviousNode;
                    if (prev is XText prevTxt)
                    {
                        var lines = prevTxt.Value.Split('\n');
                        if (lines.Length > 0)
                        {
                            var last = lines[lines.Length - 1];
                            if (!string.IsNullOrEmpty(last))
                                indent = last;
                        }
                    }
                }

                var entries = new (string key, string value)[]
                {
                    ("useIHeartMarket",   _useIHeartMarket.ToString()),
                    ("showPsUnderscores", _showPsUnderscores.ToString()),
                    ("useNorthAmerica",   _useNorthAmerica.ToString()),
                    ("fontName",          _fontName),
                    ("fontSize",          _fontSize.ToString(CultureInfo.InvariantCulture)),
                    ("fontStyle",         _fontStyle),
                    ("graphicsUnit",      _graphicsUnit),
                    ("foreColor",         _foreColor),
                    ("backColor",         _backColor),
                    ("scaleStretchX",     _scaleStretchX.ToString(CultureInfo.InvariantCulture)),
                };

                foreach (var (key, value) in entries)
                {
                    root.Add(new XText(indent));
                    root.Add(new XElement("add",
                        new XAttribute("key", KeyPrefix + key),
                        new XAttribute("value", value)));
                    root.Add(new XText(Environment.NewLine));
                }

                doc.Save(_configPath);
            }
            catch { }
        }
    }
}
