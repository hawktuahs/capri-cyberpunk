using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed class Mainframe : Form
    {
        private static readonly Color DeckBg = Color.FromArgb(10, 9, 18);
        private static readonly Color CardBg = Color.FromArgb(16, 15, 28);
        private static readonly Color Field = Color.FromArgb(20, 19, 34);
        private static readonly Color Hover = Color.FromArgb(34, 28, 54);
        private static readonly Color Pressed = Color.FromArgb(48, 36, 74);
        private static readonly Color Edge = Color.FromArgb(68, 56, 102);
        private static readonly Color Track = Color.FromArgb(28, 26, 44);
        private static readonly Color NeonPink = Color.FromArgb(255, 0, 102);
        private static readonly Color NeonGreen = Color.FromArgb(57, 255, 110);
        private static readonly Color NeonPurple = Color.FromArgb(148, 68, 255);

        private const int Wide = 420;
        private const int Tall = 712;
        private const int SetupHeight = 160;
        private const int VoiceHeight = 174;

        private sealed class LayoutEntry
        {
            internal Control Ctrl;
            internal Rectangle BaseBounds;
            internal float BaseFontSize;
            internal FontStyle Style;
            internal bool ShiftWithSetup;
        }

        private readonly List<LayoutEntry> layoutItems = new List<LayoutEntry>();
        private float uiScale = 1.35f;

        private readonly Companion capri;
        private readonly Label brainName;
        private readonly Label brainNote;
        private readonly Key models;
        private readonly ContextMenuStrip modelMenu;
        private readonly Key refresh;
        private readonly Entry cloudKeyEntry;
        private readonly Key cloudConnectBtn;
        private readonly Key freeKeyBtn;
        private readonly Key freeGroqBtn;
        private readonly Label[] steps = new Label[3];
        private readonly string[] stepText = new string[3];
        private readonly Key setup;
        private readonly Label hint;
        private readonly Key showHide;
        private readonly Key perch;
        private readonly Key boxModeBtn;
        private readonly Key sayHiBtn;
        private readonly Key teleportBtn;
        private readonly Key localVoiceBtn;
        private readonly Key localMicBtn;
        private readonly Key pcControlBtn;
        private readonly Key[] sizes;
        private readonly Toggle motion;
        private readonly Toggle onTop;
        private readonly Toggle wander;
        private readonly Toggle possess;
        private readonly Entry keyEntry;
        private readonly Key connect;
        private readonly Entry voiceEntry;
        private readonly Key voiceToggleBtn;
        private readonly ContextMenuStrip voiceMenu;
        private readonly Key speechModels;
        private readonly ContextMenuStrip speechModelMenu;
        private readonly Label voiceNote;
        private readonly Key sttLangBtn;
        private readonly Key micGateBtn;
        private readonly Key searchBtn;
        private readonly Key takeoverBtn;
        private readonly Key scaleDownBtn;
        private readonly Key scaleUpBtn;
        private bool keySaved;
        private string[] lastInstalled;
        private float filled;
        private bool expanded = true;
        private bool loaded;
        private bool syncing;
        internal bool Quitting;

        internal Mainframe(Companion owner, Icon icon, string version)
        {
            capri = owner;
            Text = "Capri Mainframe";
            Icon = icon;
            ShowInTaskbar = true;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;

            try
            {
                int sh = Screen.PrimaryScreen != null ? Screen.PrimaryScreen.Bounds.Height : 1080;
                if (sh >= 2000) uiScale = 1.85f;
                else if (sh >= 1400) uiScale = 1.50f;
                else uiScale = 1.25f;
            }
            catch { uiScale = 1.40f; }

            Font = Look.Mono(12f * uiScale, FontStyle.Regular, GraphicsUnit.Pixel);
            BackColor = DeckBg;
            ForeColor = Look.Ink;
            DoubleBuffered = true;

            // Top Cyberdeck Header
            Caption("CAPRICORN // CYBERDECK", 24, 16, 15.5f, FontStyle.Bold, NeonGreen, false);
            Caption("ARASAKA SOULKILLER ENGRAM OS · v" + version, 24, 36, 9.5f, FontStyle.Regular, Look.Dim, false);

            scaleDownBtn = Make("A-", 292, 18, 30, 24, delegate { AdjustScale(-0.15f); }, false);
            scaleDownBtn.Accent = Look.Cyan;
            scaleUpBtn = Make("A+", 326, 18, 30, 24, delegate { AdjustScale(0.15f); }, false);
            scaleUpBtn.Accent = Look.Cyan;

            Key minimize = Make("", 360, 18, 24, 24, delegate { WindowState = FormWindowState.Minimized; }, false);
            minimize.Glyph = 1;
            minimize.AccessibleName = "Minimize";
            Key close = Make("", 386, 18, 24, 24, delegate { Close(); }, false);
            close.Glyph = 2;
            close.AccessibleName = "Close";

            // Section 1: Neural Engram Core (Ollama / LLaMA 3.1 8B / NVIDIA Cloud NIM)
            Caption("01 // NEURAL ENGRAM CORE [OLLAMA / NVIDIA GPU]", 24, 62, 11.5f, FontStyle.Bold, Look.Yellow, false);
            brainName = Caption("", 24, 82, 13.5f, FontStyle.Bold, NeonGreen, false);
            brainNote = Caption("", 24, 103, 11f, FontStyle.Regular, Look.Dim, false);
            modelMenu = Dropdown();
            models = Make("", 24, 134, 280, 28, delegate { modelMenu.Show(models, new Point(0, models.Height)); }, false);
            models.Chevron = true;
            models.Accent = Look.Cyan;
            refresh = Make("Rescan", 312, 134, 84, 28, delegate { capri.FindBrain(); }, false);
            cloudKeyEntry = Input(24, 168, 164, false);
            cloudKeyEntry.Cue = "gsk_... or nvapi-...";
            cloudKeyEntry.Box.UseSystemPasswordChar = true;
            cloudKeyEntry.Box.AccessibleName = "Cloud API Key (Groq / NVIDIA NIM)";
            cloudConnectBtn = Make("Connect", 194, 168, 64, 26, delegate { ConnectCloud(); }, false);
            cloudConnectBtn.Accent = Look.Yellow;
            freeGroqBtn = Make("Groq Key", 264, 168, 64, 26, delegate { DesktopActions.OpenUrl("https://console.groq.com/keys"); }, false);
            freeGroqBtn.Accent = Look.Cyan;
            freeKeyBtn = Make("NVIDIA Key", 332, 168, 64, 26, delegate { DesktopActions.OpenUrl("https://build.nvidia.com"); }, false);
            freeKeyBtn.Accent = NeonGreen;
            for (int i = 0; i < 3; i++) steps[i] = Caption("", 24, 210 + i * 22, 11f, FontStyle.Regular, Look.Dim, false);
            setup = Make("Install Local LLM", 24, 286, 280, 28, delegate { if (capri.SettingUp) capri.CancelSetup(); else capri.SetUpBrain(); }, false);
            setup.Accent = Look.Cyan;
            setup.Loud = true;
            hint = Caption("Connect a cloud key above or install a local model.", 24, 325, 10.5f, FontStyle.Italic, Look.Dim, false);

            // Section 2: Biochip Voice & Speech Recognition
            Caption("02 // BIOCHIP AUDIO SYNTH & EARS [LOCAL]", 24, 216, 11.5f, FontStyle.Bold, NeonPink, true);
            keyEntry = Input(24, 238, 280, true);
            keyEntry.Box.UseSystemPasswordChar = true;
            keyEntry.Box.AccessibleName = "ElevenLabs API key";
            connect = Make("Connect", 312, 238, 84, 28, delegate { Connect(); }, true);
            keyEntry.Box.TextChanged += delegate { Relabel(); };
            keyEntry.Box.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Connect(); } };

            localVoiceBtn = Make("Voice Synth: ON", 24, 238, 182, 28, delegate { capri.ToggleOfflineVoice(); Sync(); }, true);
            localVoiceBtn.Accent = NeonGreen;
            localMicBtn = Make("Mic Ears: ON", 214, 238, 182, 28, delegate { capri.ToggleChat(); Sync(); }, true);
            localMicBtn.Accent = Look.Cyan;

            voiceMenu = Dropdown();
            voiceEntry = Input(24, 274, 232, true);
            voiceEntry.Visible = false;
            voiceToggleBtn = Make("Voice: " + capri.VoiceProfileLabel, 24, 274, 232, 28, delegate { capri.ToggleVoiceProfile(); Sync(); }, true);
            voiceToggleBtn.Accent = (capri.VoiceProfileLabel == "Mommy ASMR") ? NeonPink : Look.Cyan;

            speechModelMenu = Dropdown();
            speechModels = Make("Engine: Kokoro", 264, 274, 132, 28, delegate { speechModelMenu.Show(speechModels, new Point(0, speechModels.Height)); }, true);
            speechModels.Accent = NeonGreen;

            sttLangBtn = Make("STT: EN", 24, 308, 114, 26, delegate { capri.CycleSttLanguage(); Sync(); }, true);
            sttLangBtn.Accent = Look.Cyan;
            micGateBtn = Make("Gate: Normal", 146, 308, 120, 26, delegate { capri.CycleMicGate(); Sync(); }, true);
            micGateBtn.Accent = NeonGreen;
            Key testVoice = Make("Speak Test", 274, 308, 122, 26, delegate { capri.TestVoice(); }, true);
            testVoice.Accent = Look.Yellow;
            voiceNote = Caption("", 24, 342, 10f, FontStyle.Regular, Look.Dim, true);

            // Section 3: Construct Manifest & Show Animations
            int v = VoiceHeight + 34;
            Caption("03 // CONSTRUCT MANIFEST & SHOW HOLO-BOX", 24, 180 + v, 11.5f, FontStyle.Bold, Look.Cyan, true);
            showHide = Make("Hide Capri", 24, 202 + v, 118, 28, delegate { capri.ToggleHidden(); }, true);
            Make("Summon Here", 150, 202 + v, 118, 28, delegate { capri.BringHere(); }, true);
            perch = Make("Ride Cursor", 276, 202 + v, 120, 28, delegate { capri.TogglePerch(); }, true);

            boxModeBtn = Make("Mode: Human Sprite", 24, 238 + v, 182, 28, delegate { capri.CycleBoxMode(); Sync(); }, true);
            boxModeBtn.Accent = NeonPink;
            sayHiBtn = Make("Show [hi] Clip", 214, 238 + v, 102, 28, delegate { capri.PlayHiBoxAnimation(); }, true);
            sayHiBtn.Accent = NeonGreen;
            teleportBtn = Make("Glitch Hop", 322, 238 + v, 74, 28, delegate { capri.Teleport(); }, true);
            teleportBtn.Accent = Look.Cyan;

            Caption("Scale", 24, 278 + v, 11f, FontStyle.Regular, Look.Dim, true);
            sizes = new Key[5];
            int[] values = { 180, 260, 350, 460, 580 };
            string[] names = { "S", "M", "L", "XL", "XXL" };
            for (int i = 0; i < 5; i++)
            {
                int size = values[i];
                sizes[i] = Make(names[i], 72 + i * 66, 274 + v, 60, 26, delegate { capri.SetCharacterSize(size); }, true);
                sizes[i].Tag = size;
            }

            motion = Check("Show animations", 24, 310 + v, 180, delegate { if (!syncing) capri.ToggleMotion(); }, true);
            onTop = Check("Always on top", 214, 310 + v, 180, delegate { if (!syncing) capri.ToggleOnTop(); }, true);
            wander = Check("Glitch-teleport idle", 24, 336 + v, 180, delegate { if (!syncing) capri.ToggleWander(); }, true);
            possess = Check("Perch on windows", 214, 336 + v, 180, delegate { if (!syncing) capri.ToggleSitting(); }, true);

            // Netrunner PyAutoGUI Deck Actions
            searchBtn = Make(capri.InSearchMode ? "Search: ON" : "Search Mode", 24, 368 + v, 118, 26, delegate {
                if (capri.InSearchMode) capri.Hear("exit search");
                else capri.Hear("search mode");
                Sync();
            }, true);
            searchBtn.Accent = capri.InSearchMode ? Look.Yellow : Look.Cyan;
            Key screenBtn = Make("Scan Screen", 150, 368 + v, 118, 26, delegate { capri.Hear("what is on my screen"); }, true);
            screenBtn.Accent = NeonGreen;
            takeoverBtn = Make(capri.TakeoverEnabled ? "Takeover: 10m" : "Takeover: OFF", 276, 368 + v, 120, 26, delegate {
                capri.ToggleTakeover();
            }, true);
            takeoverBtn.Accent = capri.TakeoverEnabled ? NeonPink : Look.Dim;

            pcControlBtn = Make(capri.PcControlEnabled ? "PC Control: ON" : "PC Control: OFF", 24, 402 + v, 118, 28, delegate { capri.TogglePcControl(); }, true);
            pcControlBtn.Accent = capri.PcControlEnabled ? NeonGreen : Look.Dim;
            Key praiseBtn = Make("Praise (\"Good Boy\")", 150, 402 + v, 118, 28, delegate { capri.PraiseGoodBoy(); }, true);
            praiseBtn.Accent = NeonPink;
            Key quit = Make("Flatline (Quit)", 276, 402 + v, 120, 28, delegate { capri.Quit(); }, true);
            quit.Accent = Look.Red;

            Searching();
            Sync();
            ApplyScale();
        }

        private void AdjustScale(float delta)
        {
            float next = Math.Max(1.0f, Math.Min(2.4f, uiScale + delta));
            if (Math.Abs(next - uiScale) < 0.01f) return;
            uiScale = next;
            ApplyScale();
        }

        private void ApplyScale()
        {
            Font = Look.Mono(11.5f * uiScale, FontStyle.Regular, GraphicsUnit.Pixel);
            int shift = expanded ? SetupHeight : 0;
            int availableHeight = Screen.FromControl(this).WorkingArea.Height - 24;
            if (Scaled(Tall + shift) > availableHeight) uiScale = availableHeight / (float)(Tall + shift);
            foreach (LayoutEntry item in layoutItems)
            {
                int top = item.BaseBounds.Y + (item.ShiftWithSetup ? shift : 0);
                if (item.Ctrl == refresh) top = expanded ? 286 : 134;
                item.Ctrl.Font = Look.Mono(item.BaseFontSize * uiScale, item.Style, GraphicsUnit.Pixel);
                if (item.Ctrl is Label && ((Label)item.Ctrl).AutoSize)
                {
                    item.Ctrl.Location = new Point(Scaled(item.BaseBounds.X), Scaled(top));
                }
                else
                {
                    item.Ctrl.Bounds = new Rectangle(
                        Scaled(item.BaseBounds.X),
                        Scaled(top),
                        Scaled(item.BaseBounds.Width),
                        Scaled(item.BaseBounds.Height)
                    );
                }
            }
            brainNote.MaximumSize = new Size(Scaled(372), 0);
            voiceNote.MaximumSize = new Size(Scaled(372), 0);
            ClientSize = new Size(Scaled(Wide), Scaled(Tall + shift));
            Shape();
            Invalidate(true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.Style = (value.Style | 0x00020000 | 0x00080000) & ~0x00010000;
                return value;
            }
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x0084 && message.Result == new IntPtr(1))
            {
                Point pt = PointToClient(new Point(message.LParam.ToInt32() & 0xFFFF, message.LParam.ToInt32() >> 16));
                if (pt.X >= Width - 22 && pt.Y >= Height - 22)
                {
                    message.Result = new IntPtr(17); // HTBOTTOMRIGHT resize grip
                    return;
                }
                message.Result = new IntPtr(2); // HTCAPTION drag
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            loaded = true;
            Arrange(expanded);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (loaded && Width > 280)
            {
                float inferred = Width / (float)Wide;
                if (Math.Abs(inferred - uiScale) > 0.06f)
                {
                    uiScale = Math.Max(1.0f, Math.Min(2.5f, inferred));
                    ApplyScale();
                    return;
                }
            }
            Shape();
            Invalidate(true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float s = DeviceScale();
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Backdrop(graphics, this, s);

            // Outer chamfered Cyberpunk border with dual neon glow
            using (GraphicsPath outer = Look.Plate(new RectangleF(1.5f * s, 1.5f * s, Width - 3 * s - 1, Height - 3 * s - 1), 16 * s, -1, Height / 2f, 0, true))
            using (Pen pinkPen = new Pen(NeonPink, 2.2f * s))
                graphics.DrawPath(pinkPen, outer);

            // Corner HUD brackets
            using (Pen cyanPen = new Pen(Look.Cyan, 2.5f * s))
            {
                float cl = 26 * s;
                graphics.DrawLine(cyanPen, 2 * s, 22 * s, 2 * s, 52 * s);
                graphics.DrawLine(cyanPen, Width - 3 * s, 3 * s, Width - 3 * s - cl, 3 * s);
                graphics.DrawLine(cyanPen, Width - 3 * s, 3 * s, Width - 3 * s, 3 * s + cl);
                graphics.DrawLine(cyanPen, 3 * s, Height - 3 * s, 3 * s + cl, Height - 3 * s);
            }

            // Top header separator line
            using (Pen sep = new Pen(Color.FromArgb(110, 255, 0, 102), 1.2f * s))
                graphics.DrawLine(sep, 20 * s, 52 * s, Width - 20 * s, 52 * s);

            int shift = expanded ? SetupHeight : 0;
            int v = VoiceHeight + 34;
            using (Brush greenBar = new SolidBrush(NeonGreen))
            using (Brush pinkBar = new SolidBrush(NeonPink))
            using (Brush cyanBar = new SolidBrush(Look.Cyan))
            {
                graphics.FillRectangle(greenBar, 11 * s, 62 * s, 3.5f * s, ((expanded ? 350 : 196) - 62) * s);
                graphics.FillRectangle(pinkBar, 11 * s, (216 + shift) * s, 3.5f * s, 116 * s);
                graphics.FillRectangle(cyanBar, 11 * s, (180 + v + shift) * s, 3.5f * s, 252 * s);
            }

            if (expanded)
            {
                float pitch = 6 * s;
                int count = (int)(368 / 6f);
                int lit = (int)Math.Round(count * filled);
                using (Brush on = new SolidBrush(NeonGreen))
                using (Brush off = new SolidBrush(Track))
                    for (int i = 0; i < count; i++) graphics.FillRectangle(i < lit ? on : off, 24 * s + i * pitch, 276 * s, pitch - 2 * s, 6 * s);
            }

            // Bottom-right resize grip indicator
            using (Pen gripPen = new Pen(Look.Cyan, 1.5f * s))
            {
                graphics.DrawLine(gripPen, Width - 16 * s, Height - 6 * s, Width - 6 * s, Height - 16 * s);
                graphics.DrawLine(gripPen, Width - 11 * s, Height - 6 * s, Width - 6 * s, Height - 11 * s);
            }
        }

        private static void Backdrop(Graphics graphics, Control control, float s)
        {
            graphics.Clear(DeckBg);
            float pitch = 4 * s;
            float first = control is Form ? 0 : (pitch - control.Top % pitch) % pitch;
            using (Pen scan = new Pen(Color.FromArgb(12, 148, 68, 255), Math.Max(1f, s)))
                for (float row = first; row < control.Height; row += pitch) graphics.DrawLine(scan, 0, row, control.Width, row);
        }

        private void Shape()
        {
            float s = DeviceScale();
            Region old = Region;
            using (GraphicsPath plate = Look.Plate(new RectangleF(0, 0, Width, Height), 16 * s, -1, Height / 2f, 0, true)) Region = new Region(plate);
            if (old != null) old.Dispose();
        }

        private float DeviceScale() { return uiScale; }

        private int Scaled(int value) { return (int)Math.Round(value * uiScale); }

        private void Arrange(bool setupShown)
        {
            expanded = setupShown;
            foreach (Label step in steps) step.Visible = setupShown;
            setup.Visible = setupShown;
            if (cloudKeyEntry != null) cloudKeyEntry.Visible = true;
            if (cloudConnectBtn != null) cloudConnectBtn.Visible = true;
            if (freeGroqBtn != null) freeGroqBtn.Visible = true;
            if (freeKeyBtn != null) freeKeyBtn.Visible = true;
            ApplyScale();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                System.IO.File.AppendAllText(System.IO.Path.Combine(baseDir, "launch_debug.log"), "[" + DateTime.Now + "] Mainframe OnFormClosing: CloseReason=" + e.CloseReason + ", Quitting=" + Quitting + "\n" + new System.Diagnostics.StackTrace(true).ToString() + "\n");
            }
            catch {}
            if (!Quitting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; WindowState = FormWindowState.Minimized; }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { modelMenu.Dispose(); voiceMenu.Dispose(); speechModelMenu.Dispose(); }
            base.Dispose(disposing);
        }

        internal void Restore()
        {
            Screen cur = Screen.FromPoint(Cursor.Position) ?? Screen.PrimaryScreen;
            if (cur != null)
            {
                Rectangle work = cur.WorkingArea;
                Location = new Point(work.Left + Math.Max(20, (work.Width - Width) / 2), work.Top + Math.Max(20, (work.Height - Height) / 2));
            }
            ShowInTaskbar = true;
            TopMost = true;
            if (!Visible) Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
            Focus();
        }

        internal void Sync()
        {
            syncing = true;
            showHide.Text = capri.IsHidden ? "Show Capri" : "Hide Capri";
            perch.Text = capri.Riding ? "Release Cursor" : "Ride Cursor";
            if (boxModeBtn != null) boxModeBtn.Text = "Mode: " + capri.BoxModeLabel;
            if (localVoiceBtn != null)
            {
                localVoiceBtn.Text = capri.OfflineVoiceEnabled ? "Voice Synth: ON" : "Voice Synth: OFF";
                localVoiceBtn.Picked = capri.OfflineVoiceEnabled;
                localVoiceBtn.Invalidate();
            }
            if (voiceToggleBtn != null)
            {
                voiceToggleBtn.Text = "Voice: " + capri.VoiceProfileLabel;
                voiceToggleBtn.Accent = (capri.VoiceProfileLabel == "Mommy ASMR") ? NeonPink : Look.Cyan;
                voiceToggleBtn.Picked = (capri.VoiceProfileLabel == "Mommy ASMR");
                voiceToggleBtn.Invalidate();
            }
            if (localMicBtn != null)
            {
                localMicBtn.Text = capri.IsChatting ? "Mic Ears: ON" : "Mic Ears: OFF";
                localMicBtn.Picked = capri.IsChatting;
                localMicBtn.Invalidate();
            }
            if (sttLangBtn != null)
            {
                string lang = capri.SttLanguage;
                sttLangBtn.Text = "STT: " + (lang == "ja" ? "JA" : (lang == "auto" ? "Auto" : "EN"));
                sttLangBtn.Invalidate();
            }
            if (micGateBtn != null)
            {
                micGateBtn.Text = capri.MicGateLabel;
                micGateBtn.Invalidate();
            }
            if (pcControlBtn != null)
            {
                pcControlBtn.Text = capri.PcControlEnabled ? "PC Control: ON" : "PC Control: OFF";
                pcControlBtn.Accent = capri.PcControlEnabled ? NeonGreen : Look.Dim;
                pcControlBtn.Picked = capri.PcControlEnabled;
                pcControlBtn.Invalidate();
            }
            if (searchBtn != null)
            {
                searchBtn.Text = capri.InSearchMode ? "Search: ON" : "Search Mode";
                searchBtn.Accent = capri.InSearchMode ? Look.Yellow : Look.Cyan;
                searchBtn.Picked = capri.InSearchMode;
                searchBtn.Invalidate();
            }
            if (takeoverBtn != null)
            {
                takeoverBtn.Text = capri.TakeoverEnabled ? "Takeover: 10m" : "Takeover: OFF";
                takeoverBtn.Accent = capri.TakeoverEnabled ? NeonPink : Look.Dim;
                takeoverBtn.Picked = capri.TakeoverEnabled;
                takeoverBtn.Invalidate();
            }
            foreach (Key option in sizes)
            {
                option.Picked = (int)option.Tag == capri.CharacterSize;
                option.Invalidate();
            }
            motion.Checked = capri.Motion;
            onTop.Checked = capri.OnTop;
            if (wander != null) wander.Checked = capri.Wandering;
            if (possess != null) possess.Checked = capri.Sitting;
            syncing = false;
        }

        internal string Status { get { return brainName.Text + " | " + brainNote.Text; } }

        internal void Searching()
        {
            brainName.Text = "Project Capricorn · Linking Ollama";
            brainNote.Text = "Connecting to local llama3.1:8b & PyAutoGUI bridge...";
            models.Visible = false;
            hint.Visible = false;
            Arrange(false);
        }

        private void ConnectCloud()
        {
            string typed = cloudKeyEntry.Box.Text.Trim();
            if (typed.Length > 0)
            {
                cloudKeyEntry.Box.Text = "";
                capri.SetCloudKey(typed);
            }
            else if (!string.IsNullOrEmpty(capri.CloudKey))
            {
                capri.SetCloudKey(null);
            }
            else
            {
                cloudKeyEntry.Box.Focus();
            }
        }

        internal void ShowCloud(string model, string key)
        {
            if (key != null && key.StartsWith("gsk_", StringComparison.OrdinalIgnoreCase))
            {
                brainName.Text = "Groq Cloud GPU · " + model;
                brainNote.Text = "Groq key saved (" + Voice.Tail(key) + ") · Send a message to test";
            }
            else
            {
                brainName.Text = "NVIDIA NIM Cloud · " + model;
                brainNote.Text = "NVIDIA key saved (" + Voice.Tail(key) + ") · Send a message to test";
            }
            models.Text = model;
            models.Visible = true;
            hint.Visible = false;
            if (cloudKeyEntry != null) cloudKeyEntry.Cue = "Connected: " + Voice.Tail(key);
            if (cloudConnectBtn != null)
            {
                cloudConnectBtn.Text = "Forget";
                cloudConnectBtn.Accent = Look.Red;
                cloudConnectBtn.Invalidate();
            }
            Arrange(false);
        }

        internal void ShowOllama(string[] installed, string chosen)
        {
            brainName.Text = "Ollama · " + chosen;
            brainNote.Text = "100% Local Neural Link + PyAutoGUI Screen Vision";
            models.Text = chosen;
            models.Visible = true;
            hint.Visible = false;
            if (cloudKeyEntry != null) cloudKeyEntry.Cue = "nvapi-... (NVIDIA Cloud Key)";
            if (cloudConnectBtn != null)
            {
                cloudConnectBtn.Text = "Cloud GPU";
                cloudConnectBtn.Accent = Look.Yellow;
                cloudConnectBtn.Invalidate();
            }
            lastInstalled = installed;
            modelMenu.Items.Clear();
            foreach (string name in installed)
            {
                string pick = name;
                ToolStripMenuItem item = new ToolStripMenuItem(name, null, delegate { capri.ChooseModel(pick); ShowOllama(lastInstalled, pick); });
                item.Checked = name == chosen;
                modelMenu.Items.Add(item);
            }
            Arrange(false);
        }

        internal void ShowLocal(Pick pick)
        {
            brainName.Text = "Built-in · " + pick.Name;
            brainNote.Text = "Running on this PC";
            models.Visible = false;
            hint.Visible = false;
            if (cloudKeyEntry != null) cloudKeyEntry.Cue = "nvapi-... (NVIDIA Cloud Key)";
            if (cloudConnectBtn != null)
            {
                cloudConnectBtn.Text = "Cloud GPU";
                cloudConnectBtn.Accent = Look.Yellow;
                cloudConnectBtn.Invalidate();
            }
            Arrange(false);
        }

        internal void ShowSetup(Machine machine, Pick pick, bool viaOllama, string problem)
        {
            if (capri.IsTesting)
            {
                brainName.Text = problem == null ? "No brain yet" : "Setup stopped";
                brainNote.Text = problem ?? "Best fit for this PC: " + pick.Name;
                models.Visible = false;
                Step(0, "✓", machine.Describe(), Look.Ink);
                Step(1, "2", "Download " + pick.Name + " (" + pick.Download + ")" + (viaOllama ? " with Ollama" : ""), Look.Dim);
                Step(2, "3", "Wake her up", Look.Dim);
                filled = 0;
                setup.Text = problem == null ? "Set up her brain" : "Try again";
                setup.Enabled = true;
                hint.Visible = !viaOllama;
                Arrange(true);
                return;
            }
            brainName.Text = problem == null ? "Capricorn Construct · Online" : "Capricorn Construct · Active";
            brainNote.Text = problem ?? ("100% Local Construct Active · Optional LLM: " + pick.Name);
            models.Visible = false;
            Step(0, "✓", machine.Describe(), Look.Ink);
            Step(1, "✓", "PyAutoGUI Screen Vision & Voice Active", NeonGreen);
            Step(2, "+", "Optional: Install " + pick.Name + " (" + pick.Download + ") local LLM", Look.Dim);
            filled = 1f;
            setup.Text = problem == null ? ("Install " + pick.Name + " LLM (Optional)") : "Retry LLM Download";
            setup.Enabled = true;
            hint.Visible = true;
            Arrange(true);
        }

        internal void ShowProgress(int step, string text, long done, long total)
        {
            Step(1, step > 1 ? "✓" : "2", stepText[1], step == 1 ? Look.Cyan : Look.Ink);
            Step(2, "3", stepText[2], step == 2 ? Look.Cyan : Look.Dim);
            brainName.Text = step == 1 ? "Setting up her brain" : "Waking her up";
            brainNote.Text = total > 0 ? text + " · " + Engine.Amount(done) + " of " + Engine.Amount(total) : text;
            filled = total > 0 ? Math.Min(1f, done / (float)total) : step == 2 ? 1f : 0f;
            setup.Text = step == 1 ? "Cancel" : "Starting...";
            setup.Enabled = step == 1;
            hint.Visible = false;
            if (!expanded) Arrange(true);
            else Invalidate(new Rectangle(0, Scaled(204), Width, Scaled(18)));
        }

        internal void ShowProblem(string problem) { brainNote.Text = problem; }

        internal void ShowVoice(string tail, string voiceId, string model, VoiceListing listing)
        {
            keySaved = tail != null;
            keyEntry.Cue = keySaved ? "Key saved, ends " + tail : "Paste ElevenLabs API key";
            Relabel();
            bool localOnly = !capri.IsTesting && !keySaved;
            keyEntry.Visible = !localOnly;
            connect.Visible = !localOnly;
            if (localVoiceBtn != null) localVoiceBtn.Visible = localOnly;
            if (localMicBtn != null) localMicBtn.Visible = localOnly;
            if (localOnly)
            {
                if (voiceId == null || voiceId == "chatterbox" || voiceId == "default" || voiceId == Voice.Default)
                {
                    voiceEntry.Box.Text = "Chatterbox Turbo (Edgerunners Clone)";
                    speechModels.Text = "Chatterbox (CUDA)";
                }
                else if (voiceId == "af_bella" || voiceId == "sassy")
                {
                    voiceEntry.Box.Text = "Kokoro Bella (High-Energy)";
                    speechModels.Text = "Kokoro (CUDA)";
                }
                else if (voiceId == "af_nicole" || voiceId == "warm")
                {
                    voiceEntry.Box.Text = "Kokoro Nicole (Warm Choom)";
                    speechModels.Text = "Kokoro (CUDA)";
                }
                else
                {
                    voiceEntry.Box.Text = voiceId;
                    speechModels.Text = "Neural Engine";
                }
            }
            else
            {
                if (!voiceEntry.Box.Focused || voiceEntry.Box.Text.Trim().Length == 0) voiceEntry.Box.Text = voiceId;
                speechModels.Text = string.IsNullOrEmpty(model) ? "Default model" : model;
            }
            voiceMenu.Items.Clear();
            voiceMenu.Items.Add(Choice("1: Chatterbox Turbo (Edgerunners Capri Neural Clone - CUDA)", voiceId == "chatterbox" || voiceId == "default" || voiceId == null || voiceId == Voice.Default, delegate { voiceEntry.Box.Text = "Chatterbox Turbo (Edgerunners Clone)"; capri.ChooseVoice("chatterbox"); }));
            voiceMenu.Items.Add(Choice("2: Kokoro Bella (Realistic Anime High-Energy - CUDA)", voiceId == "af_bella" || voiceId == "sassy", delegate { voiceEntry.Box.Text = "Kokoro Bella (High-Energy)"; capri.ChooseVoice("af_bella"); }));
            voiceMenu.Items.Add(Choice("3: Kokoro Nicole (Warm Emotive Choom - CUDA)", voiceId == "af_nicole" || voiceId == "af_heart" || voiceId == "warm", delegate { voiceEntry.Box.Text = "Kokoro Nicole (Warm Choom)"; capri.ChooseVoice("af_nicole"); }));
            speechModelMenu.Items.Clear();
            speechModelMenu.Items.Add(Choice("Anime Pitch (+5Hz / 1.08x)", string.IsNullOrEmpty(model), delegate { capri.ChooseVoiceModel(null); }));
            if (listing != null)
            {
                if (listing.Voices.Count > 0) voiceMenu.Items.Add(new ToolStripSeparator());
                foreach (VoiceEntry entry in listing.Voices)
                {
                    string id = entry.Id;
                    voiceMenu.Items.Add(Choice(entry.Name ?? id, id == voiceId, delegate { voiceEntry.Box.Text = id; capri.ChooseVoice(id); }));
                }
                foreach (VoiceModel option in listing.Models)
                {
                    string id = option.Id;
                    string name = option.Name ?? id;
                    if (id == model) speechModels.Text = name.StartsWith("Eleven ", StringComparison.OrdinalIgnoreCase) ? name.Substring(7) : name;
                    speechModelMenu.Items.Add(Choice(name, id == model, delegate { capri.ChooseVoiceModel(id); }));
                }
            }
            speechModels.Invalidate();
        }

        internal void VoiceNote(string text, bool problem)
        {
            voiceNote.Text = text;
            voiceNote.ForeColor = problem ? Look.Red : NeonGreen;
        }

        internal string VoiceStatus { get { return voiceNote.Text + " | " + voiceEntry.Box.Text + " | " + speechModels.Text + " | " + connect.Text; } }

        private void Connect()
        {
            string typed = keyEntry.Box.Text.Trim();
            if (typed.Length > 0) { keyEntry.Box.Text = ""; capri.ConnectVoice(typed); }
            else if (keySaved) capri.ForgetVoice();
            else keyEntry.Box.Focus();
        }

        private void Relabel()
        {
            bool forget = keySaved && keyEntry.Box.Text.Trim().Length == 0;
            connect.Text = forget ? "Forget" : "Connect";
            connect.Accent = forget ? Look.Red : Look.Ink;
            connect.Invalidate();
        }

        private static ToolStripMenuItem Choice(string text, bool chosen, EventHandler click)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text, null, click);
            item.Checked = chosen;
            return item;
        }

        private ContextMenuStrip Dropdown()
        {
            ContextMenuStrip strip = new ContextMenuStrip();
            strip.Renderer = new ToolStripProfessionalRenderer(new CapriMenuColors());
            strip.BackColor = DeckBg;
            strip.ForeColor = Look.Ink;
            strip.Font = Font;
            strip.ShowImageMargin = false;
            strip.ShowCheckMargin = true;
            return strip;
        }

        private Entry Input(int x, int y, int width, bool shift)
        {
            Entry entry = new Entry();
            entry.Bounds = new Rectangle(x, y, width, 28);
            Controls.Add(entry);
            layoutItems.Add(new LayoutEntry { Ctrl = entry, BaseBounds = new Rectangle(x, y, width, 28), BaseFontSize = 11.5f, Style = FontStyle.Regular, ShiftWithSetup = shift });
            return entry;
        }

        private void Step(int index, string mark, string text, Color colour)
        {
            stepText[index] = text;
            steps[index].Text = mark + "  " + text;
            steps[index].ForeColor = colour;
        }

        private Label Caption(string text, int x, int y, float fontSize, FontStyle style, Color colour, bool shift)
        {
            Label label = new Note();
            label.Text = text;
            label.Font = Look.Mono(fontSize * uiScale, style, GraphicsUnit.Pixel);
            label.ForeColor = colour;
            label.BackColor = DeckBg;
            label.AutoSize = true;
            label.Location = new Point(x, y);
            Controls.Add(label);
            layoutItems.Add(new LayoutEntry { Ctrl = label, BaseBounds = new Rectangle(x, y, 100, 20), BaseFontSize = fontSize, Style = style, ShiftWithSetup = shift });
            return label;
        }

        private Key Make(string text, int x, int y, int width, int height, EventHandler click, bool shift)
        {
            Key key = new Key();
            key.Text = text;
            key.Bounds = new Rectangle(x, y, width, height);
            key.Click += click;
            Controls.Add(key);
            layoutItems.Add(new LayoutEntry { Ctrl = key, BaseBounds = new Rectangle(x, y, width, height), BaseFontSize = 11.5f, Style = FontStyle.Bold, ShiftWithSetup = shift });
            return key;
        }

        private Toggle Check(string text, int x, int y, int width, EventHandler changed, bool shift)
        {
            Toggle box = new Toggle();
            box.Text = text;
            box.Bounds = new Rectangle(x, y, width, 22);
            box.CheckedChanged += changed;
            Controls.Add(box);
            layoutItems.Add(new LayoutEntry { Ctrl = box, BaseBounds = new Rectangle(x, y, width, 22), BaseFontSize = 11.5f, Style = FontStyle.Regular, ShiftWithSetup = shift });
            return box;
        }

        private sealed class Note : Label
        {
            internal Note() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true); }

            protected override void OnPaintBackground(PaintEventArgs e) { Backdrop(e.Graphics, this, ((Mainframe)Parent).DeviceScale()); }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
                base.WndProc(ref message);
            }
        }

        private sealed class Key : Button
        {
            internal Color Accent = Look.Ink;
            internal bool Loud;
            internal bool Picked;
            internal bool Chevron;
            internal int Glyph;
            private bool hot;
            private bool down;

            internal Key()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                BackColor = DeckBg;
                Cursor = Cursors.Hand;
            }

            protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }

            protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }

            protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                float s = ((Mainframe)Parent).DeviceScale();
                Graphics graphics = e.Graphics;
                Backdrop(graphics, this, s);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                bool lit = hot || Picked || (Focused && ShowFocusCues);
                Color hotColour = Accent == Look.Ink ? Look.Cyan : Accent;
                if (Glyph == 0 || hot)
                {
                    using (GraphicsPath plate = Look.Plate(new RectangleF(0.7f * s, 0.7f * s, Width - 1.4f * s - 1, Height - 1.4f * s - 1), 6 * s, -1, Height / 2f, 0, true))
                    {
                        using (Brush fill = new SolidBrush(down ? Pressed : hot ? Hover : Field)) graphics.FillPath(fill, plate);
                        if (Glyph == 0)
                        {
                            Color edge = !Enabled ? Field : lit ? hotColour : Loud ? Color.FromArgb(185, Accent) : Edge;
                            using (Pen pen = new Pen(edge, 1.5f * s)) graphics.DrawPath(pen, plate);
                        }
                    }
                }
                Color ink = !Enabled ? Look.Dim : Picked ? NeonGreen : Accent;
                float x = Width / 2f;
                float y = Height / 2f;
                if (Glyph != 0)
                {
                    using (Pen pen = new Pen(hot ? (Glyph == 2 ? Look.Red : Look.Cyan) : Look.Dim, 1.6f * s))
                    {
                        if (Glyph == 1) graphics.DrawLine(pen, x - 5 * s, y, x + 5 * s, y);
                        else
                        {
                            graphics.DrawLine(pen, x - 4.5f * s, y - 4.5f * s, x + 4.5f * s, y + 4.5f * s);
                            graphics.DrawLine(pen, x - 4.5f * s, y + 4.5f * s, x + 4.5f * s, y - 4.5f * s);
                        }
                    }
                    return;
                }
                if (Chevron)
                {
                    float left = Width - 18 * s;
                    using (Brush mark = new SolidBrush(Look.Dim)) graphics.FillPolygon(mark, new[] { new PointF(left, y - 2 * s), new PointF(left + 8 * s, y - 2 * s), new PointF(left + 4 * s, y + 3 * s) });
                }
                Rectangle area = Chevron ? new Rectangle((int)(10 * s), 0, Width - (int)(30 * s), Height) : ClientRectangle;
                TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | (Chevron ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(graphics, Text, Font, area, ink, flags);
            }
        }

        private sealed class Entry : Control
        {
            internal readonly TextBox Box = new TextBox();
            internal event EventHandler Drop;
            private bool chevron;
            private string cue = "";
            private bool hot;

            internal Entry()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = DeckBg;
                Cursor = Cursors.IBeam;
                Box.BorderStyle = BorderStyle.None;
                Box.BackColor = Field;
                Box.ForeColor = Look.Ink;
                Box.GotFocus += delegate { Invalidate(); };
                Box.LostFocus += delegate { Invalidate(); };
                Box.HandleCreated += delegate { ShowCue(); };
                Controls.Add(Box);
            }

            internal string Cue { set { cue = value ?? ""; ShowCue(); } }

            internal bool Chevron { get { return chevron; } set { chevron = value; PerformLayout(); Invalidate(); } }

            private float Unit()
            {
                Mainframe owner = Parent as Mainframe;
                return owner == null ? 1f : owner.DeviceScale();
            }

            private bool OnChevron(int x) { return Chevron && x >= Width - 28 * Unit(); }

            private void ShowCue() { if (Box.IsHandleCreated) Native.SendMessage(Box.Handle, 0x1501, new IntPtr(1), cue); }

            protected override void OnLayout(LayoutEventArgs e)
            {
                base.OnLayout(e);
                float s = Unit();
                Box.Font = Look.Mono(11.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);
                int left = (int)(10 * s);
                int right = (int)((Chevron ? 28 : 10) * s);
                int height = Box.PreferredHeight;
                Box.SetBounds(left, (Height - height) / 2, Math.Max(1, Width - left - right), height);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                bool over = OnChevron(e.X);
                Cursor = over ? Cursors.Hand : Cursors.IBeam;
                if (over != hot) { hot = over; Invalidate(); }
                base.OnMouseMove(e);
            }

            protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    if (!OnChevron(e.X)) Box.Focus();
                    else if (Drop != null) Drop(this, EventArgs.Empty);
                }
                base.OnMouseDown(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                float s = Unit();
                Graphics graphics = e.Graphics;
                Backdrop(graphics, this, s);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath plate = Look.Plate(new RectangleF(0.7f * s, 0.7f * s, Width - 1.4f * s - 1, Height - 1.4f * s - 1), 6 * s, -1, Height / 2f, 0, true))
                {
                    using (Brush fill = new SolidBrush(Field)) graphics.FillPath(fill, plate);
                    using (Pen pen = new Pen(Box.Focused ? Look.Cyan : Edge, 1.4f * s)) graphics.DrawPath(pen, plate);
                }
                if (!Chevron) return;
                float left = Width - 18 * s;
                float y = Height / 2f;
                using (Brush mark = new SolidBrush(hot ? Look.Cyan : Look.Dim)) graphics.FillPolygon(mark, new[] { new PointF(left, y - 2 * s), new PointF(left + 8 * s, y - 2 * s), new PointF(left + 4 * s, y + 3 * s) });
            }
        }

        private sealed class Toggle : CheckBox
        {
            private bool hot;

            internal Toggle()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                BackColor = DeckBg;
                Cursor = Cursors.Hand;
            }

            protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }

            protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                float s = ((Mainframe)Parent).DeviceScale();
                Graphics graphics = e.Graphics;
                Backdrop(graphics, this, s);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float side = 13 * s;
                RectangleF box = new RectangleF(s, (Height - side) / 2, side, side);
                using (Brush fill = new SolidBrush(Field)) graphics.FillRectangle(fill, box);
                using (Pen edge = new Pen(hot || (Focused && ShowFocusCues) ? Look.Cyan : Edge, 1.4f * s)) graphics.DrawRectangle(edge, box.X, box.Y, box.Width, box.Height);
                if (Checked) using (Brush mark = new SolidBrush(NeonGreen)) graphics.FillRectangle(mark, box.X + 3.5f * s, box.Y + 3.5f * s, box.Width - 7 * s, box.Height - 7 * s);
                int left = (int)(box.Right + 9 * s);
                TextRenderer.DrawText(graphics, Text, Font, new Rectangle(left, 0, Width - left, Height), Look.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }
    }
}
