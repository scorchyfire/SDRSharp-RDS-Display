using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using SDRSharp.Common;
using SDRSharp.Radio;

namespace SDRSharp.RdsDisplay
{
    public class RdsDisplayPlugin : ISharpPlugin, ICanLazyLoadGui, ISupportStatus, IExtendedNameProvider
    {
        private ISharpControl _control = null!;
        private SettingsPanel _gui = null!;
        private readonly PiCodeDatabase _db = new();

        private Timer _timer = null!;

        private int _lastPi = -1;
        private string _lastPs = "";
        private string _lastRt = "";
        private int _lastPty = -1;
        private bool _lastStereo = false;

        private int _hookedPty = -1;
        private bool _decoderHooked;

        private StretchedLabel? _rdsBar;
        private Control? _spectrumAnalyzer;

        private const int BarLeftOffset  = 40;
        private const int BarTopOffset   = 10;
        private const int BarRightMargin = 80;
        private const int BarHeight      = 14;

        public string DisplayName => "RDS Display";
        public string Category => "Radio";
        public string MenuItemName => DisplayName;
        public bool IsActive => _gui != null && _gui.Visible;

        public UserControl Gui
        {
            get { LoadGui(); return _gui; }
        }

        public void LoadGui()
        {
            if (_gui == null)
                _gui = new SettingsPanel(_control, _db, ResetCache, () => ApplyBarAppearance());
        }

        private void ResetCache()
        {
            _lastPi = -1;
            _lastPs = "";
            _lastRt = "";
            _lastPty = -1;
            _hookedPty = -1;
            _lastStereo = false;
        }

        public void Initialize(ISharpControl control)
        {
            _control = control;

            string pluginDir = System.IO.Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;
            string dbPath = System.IO.Path.Combine(pluginDir, "pi_codes.json");
            _db.Load(dbPath);
            PluginSettings.Init();

            _control.PropertyChanged += OnPropertyChanged;

            HookRdsDecoder();

            _timer = new Timer { Interval = 500 };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        public void Close()
        {
            _timer?.Stop();
            _timer?.Dispose();
            try { _control.PropertyChanged -= OnPropertyChanged; } catch { }
            RemoveRdsBar();
        }

        private void OnPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_spectrumAnalyzer != null && _spectrumAnalyzer.IsHandleCreated)
                _spectrumAnalyzer.BeginInvoke((Action)OnTickCore);
        }

        private void OnTick(object? sender, EventArgs e) => OnTickCore();

        private void OnTickCore()
        {
            try
            {
                int pi = (ushort)_control.RdsPICode;
                string psRaw = _control.RdsProgramService ?? "";
                string ps    = psRaw.Trim();
                string rt    = _control.RdsRadioText?.Trim() ?? "";
                int pty      = _hookedPty;
                bool stereo  = _control.FmPilotIsDetected;

                bool changed = pi != _lastPi || ps != _lastPs || rt != _lastRt || pty != _lastPty || stereo != _lastStereo;
                if (!changed) return;

                _lastPi     = pi;
                _lastPs     = ps;
                _lastRt     = rt;
                _lastPty    = pty;
                _lastStereo = stereo;

                if (pi == 0 && string.IsNullOrEmpty(ps) && string.IsNullOrEmpty(rt))
                {
                    UpdatePanel(0, "", "", "", "", "", "(no RDS)");
                    SetRdsBarText("");
                    return;
                }

                bool useIHeart       = PluginSettings.UseIHeartMarket;
                bool showUnderscores = PluginSettings.ShowPsUnderscores;

                string csign = _db.Resolve(pi, useIHeart, _control.Frequency, out bool isTranslator);
                if (isTranslator)
                {
                    int channel = PiCodeDatabase.FrequencyToChannel(_control.Frequency);
                    csign = PiCodeDatabase.ResolveTranslator(csign, channel);
                }

                string piHex = pi > 0 ? pi.ToString("X4") : "????";
                string ptyText = pty > 0 ? PtyCodes.GetProgrammeType(pty, PluginSettings.UseNorthAmerica) : "";

                string psFormatted = BuildPsField(psRaw, ps, stereo, showUnderscores);

                string display = BuildDisplay(psFormatted, csign, piHex, ptyText, rt);

                UpdatePanel(pi, piHex, csign, ps, ptyText, rt, display);
                SetRdsBarText(display);
            }
            catch { }
        }

        private static string BuildPsField(string psRaw, string psTrimmed, bool stereo, bool showUnderscores)
        {
            bool hasPs = !string.IsNullOrEmpty(psTrimmed);

            if (!stereo && !hasPs)
                return "";   // mono + no PS → omit

            string psText;
            if (showUnderscores)
            {
                // Pad/fill the raw 8-char field, replacing spaces with '_'
                // psRaw may be shorter than 8 if SDRSharp trims; pad to 8 first
                string raw8 = psRaw.PadRight(8).Substring(0, 8);
                psText = raw8.Replace(' ', '_');
            }
            else
            {
                psText = hasPs ? psTrimmed : "        "; // 8 spaces when no PS
            }

            return stereo ? $"((( {psText} )))" : psText;
        }

        private void HookRdsDecoder()
        {
            if (_decoderHooked) return;
            _decoderHooked = true;

            var decoder = FindRdsDecoder() ?? SearchRdsDecoderViaControl();
            if (decoder == null) return;

            try
            {
                var frameAvailableEvent = decoder.GetType().GetEvent("RdsFrameAvailable");
                if (frameAvailableEvent == null) return;

                var handlerType = frameAvailableEvent.EventHandlerType;
                if (handlerType == null) return;

                var method = typeof(RdsDisplayPlugin).GetMethod("OnRdsFrame",
                    BindingFlags.Public | BindingFlags.Instance);
                if (method == null) return;

                var handler = Delegate.CreateDelegate(handlerType, this, method);
                frameAvailableEvent.AddEventHandler(decoder, handler);
            }
            catch { }
        }

        private object? SearchRdsDecoderViaControl()
        {
            try
            {
                return SearchForRdsDecoder(_control);
            }
            catch
            {
                return null;
            }
        }

        public void OnRdsFrame(ref RdsFrame frame)
        {
            try
            {
                var fields = typeof(RdsFrame).GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                foreach (var f in fields)
                {
                    if (!IsIntegerType(f.FieldType)) continue;
                    string name = f.Name.ToLowerInvariant();
                    if (name.Contains("block2") || name.Contains("blockb"))
                    {
                        _hookedPty = ExtractPty(f.GetValue(frame));
                        return;
                    }
                }

                int idx = 0;
                foreach (var f in fields)
                {
                    if (!IsIntegerType(f.FieldType)) continue;
                    if (idx == 1)
                    {
                        _hookedPty = ExtractPty(f.GetValue(frame));
                        return;
                    }
                    idx++;
                }
            }
            catch { }
        }

        private static bool IsIntegerType(Type t) =>
            t == typeof(uint) || t == typeof(ushort) || t == typeof(int) || t == typeof(short) ||
            t == typeof(byte) || t == typeof(ulong);

        private static int ExtractPty(object? blockValue)
        {
            if (blockValue == null) return -1;
            uint val = Convert.ToUInt32(blockValue);

            if (val > 0xFFFF)
                return (int)((val >> 15) & 0x1F);

            return (int)((val >> 5) & 0x1F);
        }

        private static object? FindRdsDecoder()
        {
            foreach (Form form in Application.OpenForms)
            {
                var found = SearchForRdsDecoder(form);
                if (found != null) return found;
            }
            return null;
        }

        private static object? SearchForRdsDecoder(object root)
        {
            if (root == null) return null;
            var visited = new HashSet<object> { root };
            var queue = new Queue<object>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                var obj = queue.Dequeue();
                if (obj.GetType().Name == "RdsDecoder")
                    return obj;

                var type = obj.GetType();

                foreach (var fi in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    object? val = null;
                    try { val = fi.GetValue(obj); } catch { continue; }
                    if (val != null && visited.Add(val))
                        queue.Enqueue(val);
                }

                if (!(obj is Control))
                {
                    foreach (var pi in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        if (!pi.CanRead || pi.GetIndexParameters().Length > 0) continue;
                        object? pv = null;
                        try { pv = pi.GetValue(obj); } catch { continue; }
                        if (pv != null && visited.Add(pv))
                            queue.Enqueue(pv);
                    }
                }

                if (obj is Control c)
                {
                    foreach (Control child in c.Controls)
                    {
                        if (visited.Add(child))
                            queue.Enqueue(child);
                    }
                }
            }
            return null;
        }

        private static string BuildDisplay(string psFormatted, string csign, string piHex, string pty, string rt)
        {
            bool hasPs    = !string.IsNullOrEmpty(psFormatted);
            bool hasCsign = !string.IsNullOrEmpty(csign);
            bool hasPty   = !string.IsNullOrEmpty(pty);
            bool hasRt    = !string.IsNullOrEmpty(rt);

            if (hasPs && hasCsign && hasPty && hasRt) return $"{psFormatted} | {csign} ({piHex}) | {pty} | {rt}";
            if (hasPs && hasCsign && hasPty)          return $"{psFormatted} | {csign} ({piHex}) | {pty}";
            if (hasPs && hasCsign && hasRt)           return $"{psFormatted} | {csign} ({piHex}) | {rt}";
            if (hasPs && hasCsign)                    return $"{psFormatted} | {csign} ({piHex})";
            if (hasPs && hasPty && hasRt)             return $"{psFormatted} | {pty} | {rt}";
            if (hasPs && hasPty)                      return $"{psFormatted} | {pty}";
            if (hasPs && hasRt)                       return $"{psFormatted} | {rt}";
            if (hasPs)                                return psFormatted;
            if (hasCsign && hasPty && hasRt)          return $"{csign} ({piHex}) | {pty} | {rt}";
            if (hasCsign && hasPty)                   return $"{csign} ({piHex}) | {pty}";
            if (hasCsign && hasRt)                    return $"{csign} ({piHex}) | {rt}";
            if (hasCsign)                             return $"{csign} ({piHex})";
            if (hasPty && hasRt)                      return $"{pty} | {rt}";
            if (hasPty)                               return pty;
            if (hasRt)                                return rt;
            return "";
        }

        private void UpdatePanel(int pi, string piHex, string csign, string ps, string pty, string rt, string display)
        {
            if (_gui == null || _gui.IsDisposed) return;
            if (_gui.InvokeRequired)
                _gui.BeginInvoke((Action)(() => _gui.UpdateDisplay(pi, piHex, csign, ps, pty, rt, display)));
            else
                _gui.UpdateDisplay(pi, piHex, csign, ps, pty, rt, display);
        }

        // ── Injected RDS overlay bar ──────────────────────────────────────────────

        private void SetRdsBarText(string text)
        {
            if (_rdsBar == null || _rdsBar.IsDisposed)
                TryInjectRdsBar();

            if (_rdsBar == null || _rdsBar.IsDisposed) return;

            if (_rdsBar.InvokeRequired)
                _rdsBar.BeginInvoke((Action)(() => _rdsBar.Text = text));
            else
                _rdsBar.Text = text;
        }

        private void TryInjectRdsBar()
        {
            _spectrumAnalyzer = FindMainSpectrumAnalyzer();
            if (_spectrumAnalyzer == null) return;

            var parent = _spectrumAnalyzer.Parent;
            if (parent == null) return;

            foreach (Control c in parent.Controls)
                if (c is StretchedLabel lbl && lbl.Name == "RdsDisplayBar") { _rdsBar = lbl; return; }

            var bar = new StretchedLabel
            {
                Name        = "RdsDisplayBar",
                Text        = "",
                Dock        = DockStyle.None,
                AutoSize    = false,
                UseMnemonic = false,
                Left        = _spectrumAnalyzer.Left + BarLeftOffset,
                Top         = _spectrumAnalyzer.Top  + BarTopOffset,
                Height      = BarHeight,
                Width       = GetBarWidth(),
            };

            ApplyBarAppearance(bar);

            _spectrumAnalyzer.Resize += (s, e) =>
            {
                if (bar.IsDisposed || _spectrumAnalyzer.IsDisposed) return;
                bar.Left  = _spectrumAnalyzer.Left  + BarLeftOffset;
                bar.Top   = _spectrumAnalyzer.Top   + BarTopOffset;
                bar.Width = GetBarWidth();
            };

            parent.Controls.Add(bar);
            bar.BringToFront();
            _rdsBar = bar;
        }

        private int GetBarWidth()
        {
            if (_spectrumAnalyzer == null || _spectrumAnalyzer.IsDisposed) return 0;
            return _spectrumAnalyzer.Width - BarLeftOffset - BarRightMargin;
        }

        internal void ApplyBarAppearance(StretchedLabel? bar = null)
        {
            bar ??= _rdsBar;
            if (bar == null || bar.IsDisposed) return;

            Action apply = () =>
            {
                bar.Font          = new Font(PluginSettings.FontName, PluginSettings.FontSize,
                                             PluginSettings.ParsedFontStyle, PluginSettings.ParsedGraphicsUnit);
                bar.ForeColor     = PluginSettings.ParsedForeColor;
                bar.BackColor     = PluginSettings.ParsedBackColor;
                bar.ScaleStretchX = PluginSettings.ScaleStretchX;
                bar.Invalidate();
            };

            if (bar.InvokeRequired) bar.BeginInvoke(apply);
            else apply();
        }

        private void RemoveRdsBar()
        {
            if (_rdsBar == null || _rdsBar.IsDisposed) return;
            var parent = _rdsBar.Parent;
            parent?.Controls.Remove(_rdsBar);
            _rdsBar.Dispose();
            _rdsBar = null;
        }

        private static Control? FindMainSpectrumAnalyzer()
        {
            foreach (Form form in Application.OpenForms)
                foreach (Control c in GetAllControls(form))
                    if (c.GetType().Name == "SpectrumAnalyzer" && !IsInZoomOrMpxPanel(c))
                        return c;
            return null;
        }

        private static bool IsInZoomOrMpxPanel(Control c)
        {
            var cur = c.Parent;
            while (cur != null)
            {
                string n = cur.Name ?? "";
                if (n == "Zoom MPX" || n == "Zoom IF" || n == "Zoom AF" ||
                    n.Contains("MPX") || n.Contains("FM MPX"))
                    return true;
                cur = cur.Parent;
            }
            return false;
        }

        private static IEnumerable<Control> GetAllControls(Control root)
        {
            var stack = new Stack<Control>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                yield return c;
                foreach (Control child in c.Controls)
                    stack.Push(child);
            }
        }
    }

    // Label subclass that horizontally stretches text by ScaleStretchX extra pixels per character.
    internal sealed class StretchedLabel : Label
    {
        public float ScaleStretchX { get; set; } = 0.5f;

        public StretchedLabel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint  |
                     ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (string.IsNullOrEmpty(Text)) return;

            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using var sf = new StringFormat(StringFormat.GenericTypographic)
            {
                Trimming    = StringTrimming.None,
                FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces,
            };

            using var brush = new SolidBrush(ForeColor);

            // Measure full string height for vertical centering
            SizeF full = e.Graphics.MeasureString(Text, Font, int.MaxValue, sf);
            float drawY = (Height - full.Height) / 2f;
            float drawX = 2f;

            if (ScaleStretchX <= 0f)
            {
                // No stretch — draw whole string at once
                e.Graphics.DrawString(Text, Font, brush, new PointF(drawX, drawY), sf);
                return;
            }

            // Draw char-by-char, advancing by natural glyph width + ScaleStretchX extra pixels
            foreach (char ch in Text)
            {
                string s   = ch.ToString();
                SizeF  csz = e.Graphics.MeasureString(s, Font, int.MaxValue, sf);
                e.Graphics.DrawString(s, Font, brush, new PointF(drawX, drawY), sf);
                drawX += csz.Width + ScaleStretchX;
                if (drawX >= Width) break;
            }
        }
    }

}
