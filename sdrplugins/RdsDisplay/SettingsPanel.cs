using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using SDRSharp.Common;

namespace SDRSharp.RdsDisplay
{
    public class SettingsPanel : UserControl
    {
        private readonly ISharpControl _control;
        private readonly PiCodeDatabase _db;
        private readonly Action? _onSettingChanged;
        private readonly Action? _onAppearanceChanged;

        // Display area
        private Label _lblDisplay;
        private Label _lblPs;
        private Label _lblPi;
        private Label _lblCsign;
        private Label _lblPty;
        private Label _lblRt;

        // iHeart setting
        private CheckBox _chkIHeart;

        // PS underscore setting
        private CheckBox _chkPsUnderscores;

        // PTY region
        private ComboBox _cmbPtyRegion;

        // Bar appearance
        private TextBox _txtFontName;
        private TextBox _txtFontSize;
        private ComboBox _cmbFontStyle;
        private ComboBox _cmbGraphicsUnit;
        private TextBox _txtForeColor;
        private TextBox _txtBackColor;
        private TextBox _txtScaleStretchX;

        // Custom PI override section
        private Label _lblCustomTitle;
        private Label _lblCustomPi;
        private TextBox _txtCustomPi;
        private Label _lblCustomCall;
        private TextBox _txtCustomCall;
        private Button _btnAddCustom;
        private Button _btnRemoveCustom;
        private ListBox _lstCustom;
        private Label _lblCustomNote;

        public SettingsPanel(ISharpControl control, PiCodeDatabase db,
                             Action? onSettingChanged = null, Action? onAppearanceChanged = null)
        {
            _control             = control;
            _db                  = db;
            _onSettingChanged    = onSettingChanged;
            _onAppearanceChanged = onAppearanceChanged;
            InitializeComponent();
            LoadSettings();
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            AutoScroll = true;
            Width = 340;

            int y   = 8;
            int pad = 8;

            // === Live RDS Display ===
            var lblSection1 = MakeLabel("Live RDS Display", pad, y, bold: true);
            y += 22;

            _lblDisplay = MakeLabel("(no RDS)", pad, y, width: 310, autosize: false);
            _lblDisplay.Font = new Font("Consolas", 9f, FontStyle.Bold);
            _lblDisplay.ForeColor = Color.LimeGreen;
            _lblDisplay.BackColor = Color.FromArgb(30, 30, 30);
            _lblDisplay.AutoEllipsis = true;
            y += 22;

            _lblPs    = MakeLabel("PS: —",   pad, y); y += 18;
            _lblPi    = MakeLabel("PI: —",   pad, y); y += 18;
            _lblCsign = MakeLabel("Call: —", pad, y); y += 18;
            _lblPty   = MakeLabel("PTY: —",  pad, y); y += 18;
            _lblRt    = MakeLabel("RT: —",   pad, y, width: 310, autosize: false);
            _lblRt.AutoEllipsis = true;
            y += 22;

            Controls.Add(MakeSep(pad, y)); y += 8;

            // === iHeart / PS Underscores ===
            Controls.Add(MakeLabel("RDS Options", pad, y, bold: true)); y += 22;

            _chkIHeart = new CheckBox
            {
                Text    = "Show iHeart Market callsign (e.g. WPAP instead of KERJ)",
                Left    = pad, Top = y, Width = 310,
                Checked = PluginSettings.UseIHeartMarket,
                AutoSize = false, Height = 36
            };
            _chkIHeart.CheckedChanged += (s, e) =>
            {
                PluginSettings.UseIHeartMarket = _chkIHeart.Checked;
                _onSettingChanged?.Invoke();
            };
            y += 44;

            _chkPsUnderscores = new CheckBox
            {
                Text    = "Show underscores in PS (e.g. __Show__ instead of Show)",
                Left    = pad, Top = y, Width = 310,
                Checked = PluginSettings.ShowPsUnderscores,
                AutoSize = false, Height = 36
            };
            _chkPsUnderscores.CheckedChanged += (s, e) =>
            {
                PluginSettings.ShowPsUnderscores = _chkPsUnderscores.Checked;
                _onSettingChanged?.Invoke();
            };
            y += 44;

            Controls.Add(MakeLabel("PTY Region:", pad, y)); y += 18;
            _cmbPtyRegion = new ComboBox
            {
                Left = pad, Top = y, Width = 160,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbPtyRegion.Items.Add("Global (Europe)");
            _cmbPtyRegion.Items.Add("North America (RBDS)");
            _cmbPtyRegion.SelectedIndexChanged += (s, e) =>
            {
                PluginSettings.UseNorthAmerica = _cmbPtyRegion.SelectedIndex == 1;
                _onSettingChanged?.Invoke();
            };
            y += 24;

            Controls.Add(MakeSep(pad, y)); y += 8;

            // === Bar Appearance ===
            Controls.Add(MakeLabel("Bar Appearance", pad, y, bold: true)); y += 22;
            Controls.Add(MakeLabel("Changes apply immediately. Restart SDRSharp to re-inject the bar.", pad, y)); y += 18;

            // Font Name
            Controls.Add(MakeLabel("Font Name:", pad, y)); y += 18;
            _txtFontName = new TextBox { Left = pad, Top = y, Width = 200 };
            _txtFontName.Leave += (s, e) => ApplyAppearanceSetting(() => PluginSettings.FontName = _txtFontName.Text.Trim());
            y += 28;

            // Font Size
            Controls.Add(MakeLabel("Font Size:", pad, y)); y += 18;
            _txtFontSize = new TextBox { Left = pad, Top = y, Width = 80 };
            _txtFontSize.Leave += (s, e) =>
            {
                if (float.TryParse(_txtFontSize.Text, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out float sz) && sz > 0)
                    ApplyAppearanceSetting(() => PluginSettings.FontSize = sz);
                else
                    _txtFontSize.Text = PluginSettings.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
            };
            y += 28;

            // Font Style
            Controls.Add(MakeLabel("Font Style:", pad, y)); y += 18;
            _cmbFontStyle = new ComboBox
            {
                Left = pad, Top = y, Width = 130,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var name in Enum.GetNames(typeof(FontStyle)))
                _cmbFontStyle.Items.Add(name);
            _cmbFontStyle.SelectedIndexChanged += (s, e) =>
            {
                if (_cmbFontStyle.SelectedItem is string v)
                    ApplyAppearanceSetting(() => PluginSettings.FontStyleName = v);
            };
            y += 28;

            // Graphics Unit
            Controls.Add(MakeLabel("Graphics Unit:", pad, y)); y += 18;
            _cmbGraphicsUnit = new ComboBox
            {
                Left = pad, Top = y, Width = 130,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var name in Enum.GetNames(typeof(GraphicsUnit)))
                _cmbGraphicsUnit.Items.Add(name);
            _cmbGraphicsUnit.SelectedIndexChanged += (s, e) =>
            {
                if (_cmbGraphicsUnit.SelectedItem is string v)
                    ApplyAppearanceSetting(() => PluginSettings.GraphicsUnitName = v);
            };
            y += 28;

            // ForeColor
            Controls.Add(MakeLabel("Text Color (hex, e.g. #EFEEEC):", pad, y)); y += 18;
            _txtForeColor = new TextBox { Left = pad, Top = y, Width = 120 };
            _txtForeColor.Leave += (s, e) => ApplyAppearanceSetting(() => PluginSettings.ForeColorHex = _txtForeColor.Text.Trim());
            y += 28;

            // BackColor
            Controls.Add(MakeLabel("Background Color (hex, e.g. #000000):", pad, y)); y += 18;
            _txtBackColor = new TextBox { Left = pad, Top = y, Width = 120 };
            _txtBackColor.Leave += (s, e) => ApplyAppearanceSetting(() => PluginSettings.BackColorHex = _txtBackColor.Text.Trim());
            y += 28;

            // ScaleStretchX
            Controls.Add(MakeLabel("Horizontal Stretch (px per char, e.g. 0.5):", pad, y)); y += 18;
            _txtScaleStretchX = new TextBox { Left = pad, Top = y, Width = 80 };
            _txtScaleStretchX.Leave += (s, e) =>
            {
                if (float.TryParse(_txtScaleStretchX.Text, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out float sx) && sx >= 0)
                    ApplyAppearanceSetting(() => PluginSettings.ScaleStretchX = sx);
                else
                    _txtScaleStretchX.Text = PluginSettings.ScaleStretchX.ToString(System.Globalization.CultureInfo.InvariantCulture);
            };
            y += 28;

            Controls.Add(MakeSep(pad, y)); y += 8;

            // === Custom PI Overrides ===
            _lblCustomTitle = MakeLabel("Custom PI Code Overrides", pad, y, bold: true); y += 22;
            _lblCustomNote  = MakeLabel("Override any PI code with your own callsign:", pad, y); y += 18;

            _lblCustomPi = MakeLabel("PI Code (hex or decimal):", pad, y); y += 18;
            _txtCustomPi = new TextBox { Left = pad, Top = y, Width = 100, PlaceholderText = "e.g. 1C4B" };
            y += 28;

            _lblCustomCall = MakeLabel("Callsign:", pad, y); y += 18;
            _txtCustomCall = new TextBox { Left = pad, Top = y, Width = 100, PlaceholderText = "e.g. WXYZ" };

            _btnAddCustom = new Button
            {
                Text = "Add / Update", Left = 114, Top = y, Width = 95, Height = 23
            };
            _btnAddCustom.Click += OnAddCustom;
            y += 32;

            _lstCustom = new ListBox
            {
                Left = pad, Top = y, Width = 310, Height = 80,
                Font = new Font("Consolas", 8.5f)
            };
            y += 88;

            _btnRemoveCustom = new Button
            {
                Text = "Remove Selected", Left = pad, Top = y, Width = 120, Height = 23
            };
            _btnRemoveCustom.Click += OnRemoveCustom;
            y += 32;

            Height = y + 8;

            Controls.AddRange(new Control[] {
                lblSection1, _lblDisplay, _lblPs, _lblPi, _lblCsign, _lblPty, _lblRt,
                _chkIHeart, _chkPsUnderscores, _cmbPtyRegion,
                _txtFontName, _txtFontSize, _cmbFontStyle, _cmbGraphicsUnit,
                _txtForeColor, _txtBackColor, _txtScaleStretchX,
                _lblCustomTitle, _lblCustomNote,
                _lblCustomPi, _txtCustomPi,
                _lblCustomCall, _txtCustomCall, _btnAddCustom,
                _lstCustom, _btnRemoveCustom
            });

            ResumeLayout();
        }

        private void ApplyAppearanceSetting(Action saveAction)
        {
            saveAction();
            _onAppearanceChanged?.Invoke();
        }

        private static Label MakeLabel(string text, int x, int y, int width = 310, bool bold = false, bool autosize = true)
        {
            var lbl = new Label { Text = text, Left = x, Top = y, AutoSize = autosize };
            if (!autosize) lbl.Width = width;
            if (bold) lbl.Font = new Font(lbl.Font, FontStyle.Bold);
            return lbl;
        }

        private static Label MakeSep(int x, int y) =>
            new Label { Text = "", Height = 1, Width = 310, Left = x, Top = y, BackColor = Color.Gray };

        private void LoadSettings()
        {
            _chkIHeart.Checked        = PluginSettings.UseIHeartMarket;
            _chkPsUnderscores.Checked = PluginSettings.ShowPsUnderscores;
            _cmbPtyRegion.SelectedIndex = PluginSettings.UseNorthAmerica ? 1 : 0;

            _txtFontName.Text      = PluginSettings.FontName;
            _txtFontSize.Text      = PluginSettings.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _txtForeColor.Text     = PluginSettings.ForeColorHex;
            _txtBackColor.Text     = PluginSettings.BackColorHex;
            _txtScaleStretchX.Text = PluginSettings.ScaleStretchX.ToString(System.Globalization.CultureInfo.InvariantCulture);

            SelectCombo(_cmbFontStyle,    PluginSettings.FontStyleName);
            SelectCombo(_cmbGraphicsUnit, PluginSettings.GraphicsUnitName);

            RefreshCustomList();
        }

        private static void SelectCombo(ComboBox cmb, string value)
        {
            int idx = cmb.Items.IndexOf(value);
            cmb.SelectedIndex = idx >= 0 ? idx : 0;
        }

        private void RefreshCustomList()
        {
            _lstCustom.Items.Clear();
            foreach (var kv in _db.CustomEntries)
                _lstCustom.Items.Add($"{kv.Key:X4} ({kv.Key}) = {kv.Value}");
        }

        private void OnAddCustom(object? sender, EventArgs e)
        {
            string piText = _txtCustomPi.Text.Trim();
            string call   = _txtCustomCall.Text.Trim().ToUpper();

            if (string.IsNullOrEmpty(piText) || string.IsNullOrEmpty(call))
            {
                MessageBox.Show("Enter both a PI code and a callsign.", "Missing input",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!TryParsePi(piText, out int pi))
            {
                MessageBox.Show($"'{piText}' is not a valid PI code.\nEnter hex (e.g. 1C4B) or decimal.", "Invalid PI",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _db.SetCustom(pi, call);
            _db.Save();
            RefreshCustomList();
            _txtCustomPi.Clear();
            _txtCustomCall.Clear();
        }

        private void OnRemoveCustom(object? sender, EventArgs e)
        {
            if (_lstCustom.SelectedItem == null) return;
            string item    = _lstCustom.SelectedItem.ToString() ?? "";
            int    paren   = item.IndexOf(' ');
            if (paren < 0) return;
            string hexPart = item.Substring(0, paren);
            if (int.TryParse(hexPart, System.Globalization.NumberStyles.HexNumber, null, out int pi))
            {
                _db.SetCustom(pi, "");
                _db.Save();
                RefreshCustomList();
            }
        }

        private static bool TryParsePi(string text, out int pi)
        {
            string t = text.TrimStart('0', 'x', 'X').ToUpperInvariant();
            if (t.Length > 0 && t.Length <= 4 &&
                int.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out pi))
                return true;
            return int.TryParse(text, out pi);
        }

        public void UpdateDisplay(int piCode, string piHex, string csign, string ps, string pty, string rt, string fullDisplay)
        {
            _lblDisplay.Text  = fullDisplay;
            _lblPs.Text       = $"PS: {(string.IsNullOrEmpty(ps)   ? "—" : ps)}";
            _lblPi.Text       = $"PI: {piHex}";
            _lblCsign.Text    = $"Call: {csign}";
            _lblPty.Text      = $"PTY: {(string.IsNullOrEmpty(pty) ? "—" : pty)}";
            _lblRt.Text       = $"RT: {(string.IsNullOrEmpty(rt)   ? "—" : rt)}";
        }
    }
}
