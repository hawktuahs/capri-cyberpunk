using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string dumpPath = Path.Combine(baseDir, "launch_debug.log");
                File.AppendAllText(dumpPath, "[" + DateTime.Now + "] Main started, args=" + string.Join(" ", args) + "\n");

                try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { Native.SetProcessDPIAware(); }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) {
                    try { File.AppendAllText(dumpPath, "[THREAD EXCEPTION] " + e.Exception + "\n"); } catch {}
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) {
                    try { File.AppendAllText(dumpPath, "[UNHANDLED EXCEPTION] " + e.ExceptionObject + "\n"); } catch {}
                };
                bool smoke = args.Length == 2 && args[0] == "--smoke-test";
                string identity = "user";
                try
                {
                    var current = WindowsIdentity.GetCurrent();
                    if (current != null && current.User != null) identity = current.User.Value;
                }
                catch {}

                bool first;
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Capri", "launch.log");
                try { Directory.CreateDirectory(Path.GetDirectoryName(logPath)); File.AppendAllText(logPath, "[" + DateTime.Now + "] Starting Main, args=" + string.Join(" ", args) + "\n"); } catch {}
                File.AppendAllText(dumpPath, "[" + DateTime.Now + "] Identity: " + identity + ", creating mutex\n");

                Mutex mutex = null;
                EventWaitHandle restore = null;
                try
                {
                    mutex = new Mutex(true, "Local\\Capri.Desktop." + identity + (smoke ? ".Test" : ""), out first);
                }
                catch (Exception mex)
                {
                    File.AppendAllText(dumpPath, "[" + DateTime.Now + "] Mutex open failed: " + mex + "\n");
                    first = true;
                }

                try
                {
                    restore = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\Capri.Restore." + identity + (smoke ? ".Test" : ""));
                }
                catch (Exception rex)
                {
                    File.AppendAllText(dumpPath, "[" + DateTime.Now + "] EventWaitHandle failed: " + rex + "\n");
                }

                using (mutex)
                using (restore)
                {
                    File.AppendAllText(dumpPath, "[" + DateTime.Now + "] Mutex acquired, first=" + first + "\n");
                    if (!first)
                    {
                        File.AppendAllText(dumpPath, "[" + DateTime.Now + "] Another instance running. Signaling restore.\n");
                        Native.AllowSetForegroundWindow(-1);
                        if (restore != null) restore.Set();
                        return 0;
                    }
                    try
                    {
                        string data = smoke ? Path.GetFullPath(args[1]) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Capri");
                        File.AppendAllText(dumpPath, "[" + DateTime.Now + "] Creating Companion(data=" + data + ")...\n");
                        using (Companion app = new Companion(data, restore, smoke))
                        {
                            if (smoke) app.StartSmokeTest(data);
                            File.AppendAllText(dumpPath, "[" + DateTime.Now + "] Calling Application.Run(app)...\n");
                            Application.Run(app);
                            File.AppendAllText(dumpPath, "[" + DateTime.Now + "] Application.Run(app) exited normally, testFailed=" + app.TestFailed + "\n");
                            return app.TestFailed ? 1 : 0;
                        }
                    }
                    catch (Exception error)
                    {
                        File.AppendAllText(dumpPath, "[" + DateTime.Now + "] FATAL ERROR: " + error + "\n");
                        if (smoke) { Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1], "failure.txt"), error.ToString()); return 1; }
                        MessageBox.Show("Capri could not start.\n\n" + error.ToString(), "Capri Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return 1;
                    }
                }
            }
            catch (Exception topEx)
            {
                try
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    File.AppendAllText(Path.Combine(baseDir, "launch_debug.log"), "[" + DateTime.Now + "] TOP LEVEL CRASH: " + topEx + "\n");
                }
                catch {}
                MessageBox.Show("Capri Fatal Error:\n\n" + topEx.ToString(), "Capri Fatal", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }

    [DataContract]
    internal sealed class Preferences
    {
        [DataMember] public int X;
        [DataMember] public int Y;
        [DataMember] public bool HasPosition;
        [DataMember] public int Size = 350;
        [DataMember] public bool Motion = true;
        [DataMember] public bool OnTop = true;
        [DataMember] public long UpdateCheckedAt;
        [DataMember] public string Model;
        [DataMember] public bool Welcomed;
        [DataMember] public bool NoSitting;
        [DataMember] public string VoiceKey;
        [DataMember] public string VoiceId;
        [DataMember] public string VoiceModel;
        [DataMember] public string AgentId;
        [DataMember] public int AgentVersion;
        [DataMember] public bool HoloFrame;
        [DataMember] public bool DormantEye;
        [DataMember] public bool OfflineVoice = true;
        [DataMember] public int BoxMode;
        [DataMember] public bool Wander = true;
        [DataMember] public string SttLanguage = "en";
        [DataMember] public double MicThreshold;
        [DataMember] public string CloudKey;
        [DataMember] public string CloudModel;
        [DataMember] public bool TakeoverEnabled = true;
        [DataMember] public string UpdateRepo;
    }

    [DataContract]
    internal sealed class Release
    {
        [DataMember(Name = "tag_name")] public string Tag;
        [DataMember(Name = "html_url")] public string Page;
        [DataMember(Name = "draft")] public bool Draft;
        [DataMember(Name = "prerelease")] public bool Prerelease;
    }

    internal sealed class Clip
    {
        internal readonly Bitmap Strip;
        internal readonly int Count;
        internal readonly double Fps;
        internal readonly float Height;

        internal Clip(string name, int count, double fps, float height)
        {
            Count = count;
            Fps = fps;
            Height = height;
            Strip = Companion.LoadArt("Capri.Sprite." + name + ".png");
            if (Strip == null) throw new FileNotFoundException("Capri's " + name + " animation is missing. Rebuild with assets/sprites.");
        }
    }

    internal sealed class Companion : Form
    {
        private readonly Clip idle = new Clip("idle", 16, 8, 1.11f);
        private readonly Clip blink = new Clip("blink", 4, 8, 1.11f);
        private readonly Clip blinkGlitch = new Clip("blink_glitch", 4, 8, 1.11f);
        private readonly Clip grin = new Clip("grin", 16, 8, 1.11f);
        private readonly Clip glitch = new Clip("glitch", 10, 15, 1.11f);
        private readonly Clip dangle = new Clip("dangle", 24, 10, 1.11f);
        private readonly Clip sleep = new Clip("sleep", 16, 6, 1.11f);
        private readonly Clip wake = new Clip("wake", 13, 15, 1.11f);
        private readonly Clip perch = new Clip("perch", 16, 8, 1);
        private readonly Clip perchBlink = new Clip("perch_blink", 4, 8, 1);
        private readonly Clip perchBlinkGlitch = new Clip("perch_blink_glitch", 4, 8, 1);
        private readonly Clip perchGlitch = new Clip("perch_glitch", 10, 15, 1);
        private readonly Clip mouth = new Clip("mouth", 5, 12, 1.11f);
        private readonly Clip holoBannerClip = new Clip("holo_banner", 32, 10, 1f);
        private readonly Clip holoCallClip = new Clip("holo_call", 24, 10, 1f);
        private readonly Clip hiBoxClip = new Clip("hi_box", 24, 10, 1f);
        private readonly Clip boxTalk = new Clip("box_talk", 24, 12, 1f);
        private readonly Clip boxListen = new Clip("box_listen", 24, 10, 1f);
        private readonly Clip boxEyes = new Clip("box_eyes", 24, 12, 1f);
        private readonly Clip boxTenorTalk = new Clip("box_tenor_talk", 24, 12, 1f);
        private readonly Clip boxTenorGlitch = new Clip("box_tenor_glitch", 24, 15, 1f);
        private readonly Clip boxCanonSmirk = new Clip("box_canon_smirk", 24, 12, 1f);
        private readonly Clip boxCanonIntense = new Clip("box_canon_intense", 24, 12, 1f);
        private readonly Clip boxCanonNeon = new Clip("box_canon_neon", 24, 12, 1f);
        private readonly Clip boxCanonHolo = new Clip("box_canon_holo", 24, 12, 1f);
        private readonly Clip screenTakeoverClip = new Clip("screen_takeover", 16, 12, 1f);
        private readonly Clip boxCutins = new Clip("box_cutins", 5, 1, 1f);
        private bool holoProjecting;
        private int boxCycleIdx;
        private readonly ToolStripMenuItem brainModelItem;
        private readonly Random random = new Random();
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem visibilityItem;
        private readonly ToolStripMenuItem motionItem;
        private readonly ToolStripMenuItem topItem;
        private readonly System.Windows.Forms.Timer frames;
        private readonly System.Windows.Forms.Timer signals;
        private readonly EventWaitHandle restore;
        private readonly string settingsPath;
        private readonly bool testing;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private Preferences preferences;
        private Bitmap frame;
        private Point anchor;
        private Point pointerStart;
        private Point anchorStart;
        private bool pressed;
        private bool dragging;
        private bool hidden;
        private Clip clip;
        private double clipStart;
        private int idleCycle;
        private Clip blinkWith;
        private IntPtr lastForeground;
        private int topTicks;
        private double nextGlitch;
        private int clicks;
        private bool riding;
        private bool pendingClick;
        private Point pendingPoint;
        private long pendingTime;
        private bool hotkey;
        private double leanAngle;
        private Ledge seat;
        private bool falling;
        private Ledge fallTo;
        private double fallSpeed;
        private double fallStep;
        private double coveredSince = -1;
        private double seatLost = -1;
        private System.Collections.Generic.List<Rectangle> icons = new System.Collections.Generic.List<Rectangle>();
        private bool iconsBusy;
        private double iconsAt = -10;
        private const float SitX = 0.30f;
        private const float SitY = 0.79f;
        private readonly System.Windows.Forms.Timer clickDelay;
        private readonly ToolStripMenuItem rideItem;
        private readonly ToolStripMenuItem sitItem;
        private readonly ToolStripMenuItem wanderItem;
        private readonly ToolStripMenuItem startupItem;
        private readonly ToolStripMenuItem holoItem;
        private readonly ToolStripMenuItem eyeItem;
        private readonly ToolStripMenuItem offlineVoiceItem;
        private readonly ToolStripMenuItem[] boxItems = new ToolStripMenuItem[5];
        private readonly ToolStripMenuItem updateItem;
        private double micLevel;
        private double transitionUntil = -1;
        private double rideStart = -1;
        private double hiBoxStart = -1;
        private double hiBoxUntil = -1;
        private double sparkUntil = -1;
        private bool hiBoxGreetPending;
        private double wanderPauseUntil = 0;
        private double nextWanderAt = 12;
        private int wanderPhase = 0;
        private double wanderTeleportAt = -1;
        private double nextTakeoverAt = 240.0;
        private double nextFormSwitchAt = 25.0;
        private bool updating;
        private string updatePage;
        private string balloonPage;
        private string speech;
        private double speechStart;
        private const double LettersPerSecond = 28;
        private const double MouthRate = 12;
        private readonly Brain brain = new Brain();
        private readonly TalkPill pill = new TalkPill();
        private readonly SpeechBox box = new SpeechBox();
        private readonly TypeBox typer = new TypeBox();
        private readonly Mainframe mainframe;
        private readonly Engine engine;
        private Machine machine;
        private Pick fit;
        private bool ollamaRunning;
        internal bool SettingUp;
        private TalkState talkState;
        private string heard;
        private string reply;
        private double replyStart;
        private double replyDone = -1;
        private double pillUntil = 10;
        private int asking;
        private bool finding;
        internal Func<string, string> Think;
        internal Func<string, byte[]> Speak;
        private readonly Voice voice = new Voice();
        private readonly Ears ears = new Ears();
        private readonly Speaker speaker;
        private volatile Agent agent;
        private bool chatting;
        private bool dropping;
        private int chatTicket;
        private const double WakeLevel = 0.02;
        private readonly object waking = new object();
        private readonly System.Collections.Generic.List<short[]> preroll = new System.Collections.Generic.List<short[]>();
        private System.Collections.Generic.List<short[]> backlog;
        private bool resting;
        private int loudRun;
        private bool spoke;
        private bool checkedIn;
        private double quietSince = -1;
        private double wokeAt = -1;
        private VoiceListing voices;
        private int[] mouths;
        private double pace = LettersPerSecond;
        private const string ReleasesPage = "https://github.com/lachydotmcg/capri-desktop/releases/";
        private const string OllamaPage = "https://ollama.com/download";
        private const int PaddingSize = 36;
        private const int DismountId = 0x4361;
        private const int SummonHotKeyId = 0x4362;
        private const int BlinkFrom = 9;
        internal bool TestFailed;

        internal Companion(string data, EventWaitHandle signal, bool smoke)
        {
            testing = smoke;
            speaker = new Speaker(smoke);
            restore = signal;
            settingsPath = Path.Combine(data, "settings.json");
            engine = new Engine(data);
            preferences = smoke ? new Preferences { Size = 240 } : LoadPreferences();
            ears.SttLanguage = preferences.SttLanguage ?? "en";
            clip = idle;
            nextGlitch = 30 + random.NextDouble() * 30;
            Text = "Capri";
            AccessibleName = "Capri desktop companion";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = preferences.OnTop;
            Cursor = Cursors.Hand;
            AutoScaleMode = AutoScaleMode.None;
            menu = new ContextMenuStrip();
            menu.Renderer = new ToolStripProfessionalRenderer(new CapriMenuColors());
            menu.ForeColor = Color.FromArgb(238, 239, 241);
            menu.BackColor = Color.FromArgb(26, 27, 31);
            menu.ShowImageMargin = false;
            ToolStripMenuItem title = new ToolStripMenuItem("Project Capricorn — Edgerunners S2 Construct");
            title.Enabled = false;
            menu.Items.Add(title);
            menu.Items.Add(new ToolStripSeparator());
            visibilityItem = new ToolStripMenuItem("Put her away (Hide)", null, delegate { SetHidden(!hidden); });
            menu.Items.Add(visibilityItem);
            ToolStripMenuItem sizes = new ToolStripMenuItem("Size (or Mouse Wheel on Capri)");
            if (smoke)
            {
                AddSize(sizes, "Small", 170);
                AddSize(sizes, "Medium", 240);
                AddSize(sizes, "Large", 320);
            }
            else
            {
                AddSize(sizes, "Small (180)", 180);
                AddSize(sizes, "Medium (260)", 260);
                AddSize(sizes, "Large (350)", 350);
                AddSize(sizes, "XL (460)", 460);
                AddSize(sizes, "XXL (580)", 580);
            }
            menu.Items.Add(sizes);
            menu.Items.Add(new ToolStripMenuItem("Glitch-Teleport Now", null, delegate { Teleport(); }));
            menu.Items.Add(new ToolStripMenuItem("Play Project Capricorn [hi] Clip", null, delegate { PlayHiBoxAnimation(); }));
            menu.Items.Add(new ToolStripMenuItem("Play Show Demo", null, delegate { PlayDemo(); }));
            menu.Items.Add(new ToolStripMenuItem("How does she work?", null, delegate { ExplainHowItWorks(); }));
            menu.Items.Add(new ToolStripMenuItem("Praise (\"Good Boy\")", null, delegate { PraiseGoodBoy(); }));
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem boxMenu = new ToolStripMenuItem("Construct Mode (Show Box vs Human)");
            string[] boxTitles = { "Human Mode (Full Sprite + Glitch Teleport)", "Holo-Call Video Box (Show Animations)", "Edgerunners S2 Show Cut-In Banner", "Project Capricorn [hi] Video Box", "Dormant Cyber-Eye HUD" };
            for (int i = 0; i < boxTitles.Length; i++)
            {
                int mode = i;
                boxItems[i] = new ToolStripMenuItem(boxTitles[i], null, delegate { SetBoxMode(mode); });
                boxItems[i].Checked = preferences.BoxMode == mode;
                boxMenu.DropDownItems.Add(boxItems[i]);
            }
            menu.Items.Add(boxMenu);
            menu.Items.Add(new ToolStripMenuItem("Cycle Box Animation", null, delegate { CycleBoxAnimation(); }));
            brainModelItem = new ToolStripMenuItem("Brain Model (Ollama)");
            menu.Items.Add(brainModelItem);
            menu.Opening += delegate { PopulateModelMenu(); };
            ToolStripMenuItem soundMenu = new ToolStripMenuItem("Sound (100% Local)");
            offlineVoiceItem = new ToolStripMenuItem("Local Biochip Voice", null, delegate { ToggleOfflineVoice(); });
            offlineVoiceItem.Checked = preferences.OfflineVoice;
            soundMenu.DropDownItems.Add(offlineVoiceItem);
            soundMenu.DropDownItems.Add(new ToolStripMenuItem("Toggle Hands-Free Mic", null, delegate { ToggleChat(); }));
            soundMenu.DropDownItems.Add(new ToolStripMenuItem("Test Voice", null, delegate { TestVoice(); }));
            menu.Items.Add(soundMenu);
            wanderItem = new ToolStripMenuItem("Wander around when idle", null, delegate { ToggleWander(); });
            wanderItem.Checked = preferences.Wander;
            menu.Items.Add(wanderItem);
            sitItem = new ToolStripMenuItem("Possess my windows", null, delegate { ToggleSitting(); });
            sitItem.Checked = !preferences.NoSitting;
            menu.Items.Add(sitItem);
            motionItem = new ToolStripMenuItem("Idle motion", null, delegate { ToggleMotion(); });
            motionItem.Checked = preferences.Motion;
            menu.Items.Add(motionItem);
            topItem = new ToolStripMenuItem("Always on top", null, delegate { ToggleOnTop(); });
            topItem.Checked = preferences.OnTop;
            menu.Items.Add(topItem);
            rideItem = new ToolStripMenuItem("Perch on cursor (Right-click to drop)", null, delegate { if (riding) StopRide(); else StartRide(); });
            menu.Items.Add(rideItem);
            holoItem = new ToolStripMenuItem("Holo-Frame mode", null, delegate { ToggleHoloFrame(); });
            holoItem.Checked = preferences.HoloFrame;
            eyeItem = new ToolStripMenuItem("Dormant eye mode", null, delegate { ToggleDormantEye(); });
            eyeItem.Checked = preferences.DormantEye;
            startupItem = new ToolStripMenuItem("Start with Windows", null, delegate { ToggleStartup(); });
            startupItem.Checked = IsStartupEnabled();
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Search mode (YouTube)", null, delegate { Hear("search mode"); }));
            menu.Items.Add(new ToolStripMenuItem("Screen Takeover Animation", null, delegate { PlayScreenTakeover(); }));
            menu.Items.Add(new ToolStripMenuItem("Reset memory", null, delegate { Hear("reset memory"); }));
            clickDelay = new System.Windows.Forms.Timer();
            clickDelay.Interval = Math.Max(100, SystemInformation.DoubleClickTime);
            clickDelay.Tick += delegate { OnClickDelay(); };
            menu.Items.Add(new ToolStripMenuItem("Bring to this screen", null, delegate { RestoreAtCursor(); }));
            menu.Items.Add(new ToolStripMenuItem("Open Cyberdeck Remote (Ctrl+Shift+C)", null, delegate { mainframe.Restore(); }));
            updateItem = new ToolStripMenuItem("Check for updates", null, delegate { if (updatePage != null) OpenPage(updatePage); else CheckForUpdates(true); });
            menu.Items.Add(updateItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Quit Capri", null, delegate { Close(); }));
            tray = new NotifyIcon();
            using (Stream icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Chip.ico"))
            {
                tray.Icon = icon != null ? new Icon(icon) : Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            tray.Text = "Capri · Double-click to open Cyberdeck";
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { RestoreAtCursor(); mainframe.Restore(); };
            tray.MouseClick += delegate(object s, MouseEventArgs me) { if (me.Button == MouseButtons.Left) { RestoreAtCursor(); mainframe.Restore(); } };
            tray.BalloonTipClicked += delegate { if (balloonPage != null) OpenPage(balloonPage); };
            tray.Visible = !testing;
            mainframe = new Mainframe(this, tray.Icon, Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
            brain.AllowReflexFallback = !testing;
            DesktopActions.EnableBrowserLaunch = !testing;
            brain.CloudKey = preferences.CloudKey;
            brain.CloudModel = string.IsNullOrEmpty(preferences.CloudModel) ? "meta/llama-3.1-8b-instruct" : preferences.CloudModel;
            voice.Key = Voice.Unprotect(preferences.VoiceKey);
            voice.VoiceId = (string.IsNullOrEmpty(preferences.VoiceId) || preferences.VoiceId == Voice.Default) ? "af_bella" : preferences.VoiceId;
            voice.Model = string.IsNullOrEmpty(preferences.VoiceModel) ? null : preferences.VoiceModel;
            voice.OfflineEnabled = !testing && preferences.OfflineVoice;
            ShowVoice();
            pill.Typing += OpenTyper;
            pill.Voice += ToggleChat;
            pill.Mute += ToggleMic;
            if (!string.IsNullOrEmpty(preferences.SttLanguage)) ears.SttLanguage = preferences.SttLanguage;
            ears.MicGateThreshold = preferences.MicThreshold > 0.001 ? preferences.MicThreshold : 0.010;
            ears.Heard += Listen;
            ears.Speaking += delegate(bool active)
            {
                Post(delegate
                {
                    if (active)
                    {
                        if (talkState != TalkState.Talking)
                        {
                            talkState = TalkState.Listening;
                            UpdateAnimation();
                            RenderFrame();
                        }
                    }
                    else
                    {
                        if (talkState == TalkState.Listening)
                        {
                            talkState = TalkState.Thinking;
                            UpdateAnimation();
                            RenderFrame();
                        }
                    }
                });
            };
            ears.Recognized += delegate(string spoken) { Post(delegate { if (chatting) Hear(spoken, spoken, "> voice: "); }); };
            ears.Level += delegate(double value) { Post(delegate { micLevel = value; pill.SetLevel(value); if (preferences.HoloFrame || preferences.DormantEye || preferences.BoxMode != 0) RenderFrame(); }); };
            box.Dismissed += delegate { if (box.Leaving < 0) box.Leaving = clock.Elapsed.TotalSeconds; };
            box.ChatRequested += OpenTyper;
            typer.Submitted += Hear;
            typer.VisibleChanged += delegate { if (!typer.Visible && replyDone >= 0) replyDone = clock.Elapsed.TotalSeconds; };
            frames = new System.Windows.Forms.Timer();
            frames.Interval = 33;
            frames.Tick += delegate
            {
                double now = clock.Elapsed.TotalSeconds;
                if (hiBoxGreetPending && now >= hiBoxUntil)
                {
                    hiBoxGreetPending = false;
                    sparkUntil = now + 0.45;
                    Answer(++asking, "Project Capricorn locked in on your desktop, choom!", TalkState.Talking);
                }
                if (riding) FollowCursor();
                else if (falling) Tumble(now);
                else if (seat != null && preferences.BoxMode == 0) HoldSeat(now);
                if (!testing && !riding && !falling && !dragging && preferences.Wander && preferences.Motion && talkState == TalkState.Off)
                    UpdateWander(now);
                if (talkState == TalkState.Thinking && preferences.Motion && !dragging && !riding)
                {
                    double thinkTime = now * 4.5;
                    int jx = (int)(Math.Sin(thinkTime) * 6.0);
                    int jy = (int)(Math.Cos(thinkTime * 1.6) * 3.0);
                    Location = KeepOnScreen(new Point(anchor.X + jx, anchor.Y + jy));
                }
                RenderFrame();
                if (!riding && !dragging && !preferences.Motion && clip == idle && speech == null && talkState == TalkState.Off && now >= hiBoxUntil && now >= sparkUntil && preferences.BoxMode == 0 && !preferences.Wander) frames.Stop();
            };
            signals = new System.Windows.Forms.Timer();
            signals.Interval = 250;
            signals.Tick += delegate
            {
                if (restore.WaitOne(0)) { SetHidden(false); RestoreAtCursor(); mainframe.Restore(); }
                if (!testing) CheckIdle(Native.IdleMilliseconds());
                StayOnTop();
                CheckSeat(clock.Elapsed.TotalSeconds);
                CheckChat(clock.Elapsed.TotalSeconds);
                UpdatePill(clock.Elapsed.TotalSeconds);
                if (!testing && clock.Elapsed.TotalSeconds > 20 && UpdateDue(preferences.UpdateCheckedAt, DateTime.UtcNow.Ticks)) CheckForUpdates(false);
            };
            signals.Start();
            Size = CanvasSize(preferences.Size);
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            anchor = preferences.HasPosition ? new Point(preferences.X, preferences.Y) : new Point(work.Right - Width - 28, work.Bottom - Height - 12);
            anchor = KeepOnScreen(anchor);
            Location = anchor;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplaysChanged;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000;
                return value;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try { Native.RegisterHotKey(Handle, SummonHotKeyId, 0x0002 | 0x0004 | 0x4000, 0x43); } catch { }
            Play(grin);
            RenderFrame();
            UpdateAnimation();
            if (testing)
            {
                mainframe.WindowState = FormWindowState.Minimized;
                mainframe.Show();
            }
            else
            {
                mainframe.Restore();
                preferences.Welcomed = true;
                SavePreferences();
            }
            if (!testing)
            {
                DesktopActions.EnsureHelperRunning();
                FindBrain();
                if (voice.Key != null) ListVoices(voice.Key, false);
                if (!chatting) ToggleChat();
                PlayHiBoxAnimation();
            }
            RefreshIcons();
        }

        internal static Bitmap LoadArt(string name)
        {
            Stream image = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (image == null && name.StartsWith("Capri.Sprite.", StringComparison.Ordinal))
            {
                string fileName = name.Substring("Capri.Sprite.".Length);
                string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "sprites", fileName);
                if (!File.Exists(localPath))
                    localPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "assets", "sprites", fileName));
                if (File.Exists(localPath)) image = File.OpenRead(localPath);
            }
            using (image)
            {
                if (image == null) return null;
                using (Bitmap original = new Bitmap(image))
                {
                    Bitmap copy = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppPArgb);
                    using (Graphics graphics = Graphics.FromImage(copy))
                    {
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.DrawImage(original, new Rectangle(0, 0, copy.Width, copy.Height));
                    }
                    return copy;
                }
            }
        }

        private void Play(Clip next)
        {
            clip = next;
            clipStart = clock.Elapsed.TotalSeconds;
            idleCycle = 0;
            blinkWith = null;
        }

        private int Advance(double now)
        {
            bool seated = clip == perch || clip == perchGlitch;
            bool perched = riding || seat != null;
            if (perched && !seated) Play(perch);
            if ((!perched && seated) || (clip == dangle && !dragging && !falling)) Play(idle);
            int index = (int)((now - clipStart) * clip.Fps);
            if ((clip == glitch && index >= glitch.Count) || (clip == wake && index >= wake.Count) || (clip == grin && index >= grin.Count * 2)) { Play(idle); index = 0; }
            if (clip == perchGlitch && index >= perchGlitch.Count) { Play(perch); index = 0; }
            if (clip != idle && clip != perch) return index % clip.Count;
            if (!preferences.Motion) return 0;
            int cycle = index / clip.Count;
            if (cycle != idleCycle)
            {
                idleCycle = cycle;
                double roll = random.NextDouble();
                blinkWith = roll < 0.1 ? (clip == perch ? perchBlinkGlitch : blinkGlitch) : roll < 0.55 ? (clip == perch ? perchBlink : blink) : null;
                if (now >= nextGlitch && speech == null && talkState == TalkState.Off) { nextGlitch = now + 30 + random.NextDouble() * 30; Play(clip == perch ? perchGlitch : glitch); return 0; }
            }
            return index % clip.Count;
        }

        private void StayOnTop()
        {
            if (!preferences.OnTop || hidden || !IsHandleCreated || menu.Visible) return;
            IntPtr foreground = Native.GetForegroundWindow();
            if (foreground == lastForeground && ++topTicks < 8) return;
            lastForeground = foreground;
            topTicks = 0;
            Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            foreach (Form above in new Form[] { pill, box, typer }) if (above.Visible) Native.SetWindowPos(above.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        internal static bool UpdateDue(long checkedAt, long now) { return now - checkedAt >= TimeSpan.TicksPerDay || now < checkedAt; }

        private void CheckForUpdates(bool asked)
        {
            if (updating) return;
            updating = true;
            preferences.UpdateCheckedAt = DateTime.UtcNow.Ticks;
            SavePreferences();
            if (asked) updateItem.Text = "Checking for updates...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                Release found = null;
                try { found = FetchLatestRelease(preferences.UpdateRepo); } catch { }
                try { if (!IsDisposed) BeginInvoke((Action)delegate { ShowUpdate(found, asked); }); } catch (InvalidOperationException) { }
            });
        }

        internal static Release FetchLatestRelease(string repo = null)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            string target = string.IsNullOrEmpty(repo) ? "hawktuahs/capri-cyberpunk" : repo;
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + target + "/releases/latest");
                request.UserAgent = "Capri-Desktop";
                request.Accept = "application/vnd.github+json";
                request.Timeout = 8000;
                request.ReadWriteTimeout = 8000;
                using (WebResponse response = request.GetResponse())
                using (Stream body = response.GetResponseStream()) return ParseRelease(body);
            }
            catch (WebException wex)
            {
                var httpResp = wex.Response as HttpWebResponse;
                if (httpResp != null && httpResp.StatusCode == HttpStatusCode.NotFound && target != "lachydotmcg/capri-desktop")
                {
                    try { return FetchLatestRelease("lachydotmcg/capri-desktop"); } catch { }
                }
                throw;
            }
        }

        internal static Release ParseRelease(Stream body) { return (Release)new DataContractJsonSerializer(typeof(Release)).ReadObject(body); }

        internal static bool IsNewer(string tag, Version current)
        {
            Version found;
            if (tag == null || !Version.TryParse(tag.TrimStart('v', 'V'), out found)) return false;
            return new Version(found.Major, found.Minor, Math.Max(0, found.Build)) > new Version(current.Major, current.Minor, Math.Max(0, current.Build));
        }

        private void ShowUpdate(Release found, bool asked)
        {
            updating = false;
            Version current = Assembly.GetExecutingAssembly().GetName().Version;
            bool usable = found != null && !found.Draft && !found.Prerelease && found.Page != null && (found.Page.StartsWith(ReleasesPage, StringComparison.Ordinal) || (found.Page.StartsWith("https://github.com/", StringComparison.Ordinal) && found.Page.Contains("/releases/")));
            if (usable && IsNewer(found.Tag, current))
            {
                string version = found.Tag.TrimStart('v', 'V');
                updatePage = found.Page;
                updateItem.Text = "Download Capri " + version;
                balloonPage = found.Page;
                tray.ShowBalloonTip(8000, "Capri " + version + " is out", "Click here to download the update.", ToolTipIcon.Info);
                return;
            }
            updateItem.Text = "Check for updates";
            if (!usable) { preferences.UpdateCheckedAt = DateTime.UtcNow.Ticks - TimeSpan.TicksPerDay + TimeSpan.TicksPerHour; SavePreferences(); }
            if (!asked) return;
            balloonPage = null;
            tray.ShowBalloonTip(4000, "Capri", usable ? "You're on the latest version (" + current.ToString(3) + ")." : "Couldn't check for updates right now. Try again later.", ToolTipIcon.Info);
        }

        private static void OpenPage(string page)
        {
            if (!page.StartsWith(ReleasesPage, StringComparison.Ordinal) && !(page.StartsWith("https://github.com/", StringComparison.Ordinal) && page.Contains("/releases/")) && page != OllamaPage) return;
            try { Process.Start(page); } catch (System.ComponentModel.Win32Exception) { }
        }

        private void CheckIdle(long quiet)
        {
            if (clip == sleep) { if (quiet < 1000) Play(wake); return; }
            if (quiet >= 120000 && clip == idle && preferences.Motion && !riding && !dragging && !hidden && !chatting && speech == null && talkState == TalkState.Off && !typer.Visible) Play(sleep);
        }

        private static Size CanvasSize(int size) { return new Size(size + PaddingSize * 2, size + PaddingSize * 2); }

        private void AddSize(ToolStripMenuItem parent, string label, int size)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label, null, delegate { SetCharacterSize(size); });
            item.Tag = size;
            item.Checked = preferences.Size == size;
            parent.DropDownItems.Add(item);
        }

        internal void SetCharacterSize(int size)
        {
            size = Math.Max(160, Math.Min(680, size));
            Point center = new Point(anchor.X + Width / 2, anchor.Y + Height / 2);
            int hipX = Hip().X;
            preferences.Size = size;
            Size = CanvasSize(size);
            anchor = seat != null ? Seated(hipX, seat.Y) : KeepOnScreen(new Point(center.X - Width / 2, center.Y - Height / 2));
            Location = anchor;
            foreach (ToolStripItem entry in menu.Items)
            {
                ToolStripMenuItem group = entry as ToolStripMenuItem;
                if (group == null || !group.Text.StartsWith("Size", StringComparison.Ordinal)) continue;
                foreach (ToolStripMenuItem option in group.DropDownItems) option.Checked = (int)option.Tag == size;
            }
            RenderFrame();
            SavePreferences();
            mainframe.Sync();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int delta = e.Delta > 0 ? 30 : -30;
            SetCharacterSize(preferences.Size + delta);
        }

        private Point KeepOnScreen(Point point)
        {
            Screen found = null;
            foreach (Screen s in Screen.AllScreens)
            {
                if (s.WorkingArea.Contains(point.X + Width / 2, point.Y + Height / 2))
                {
                    found = s;
                    break;
                }
            }
            if (found == null) found = Screen.PrimaryScreen;
            Rectangle area = found != null ? found.WorkingArea : Screen.PrimaryScreen.WorkingArea;
            int minX = area.Left + 8;
            int maxX = Math.Max(minX, area.Right - Width - 8);
            int minY = area.Top + 8;
            int maxY = Math.Max(minY, area.Bottom - Height - 8);
            return new Point(Math.Max(minX, Math.Min(point.X, maxX)), Math.Max(minY, Math.Min(point.Y, maxY)));
        }

        internal bool PcControlEnabled { get { return brain != null && brain.AllowPcControl; } }
        internal void TogglePcControl()
        {
            if (brain != null) brain.AllowPcControl = !brain.AllowPcControl;
            mainframe.Sync();
        }

        private void DisplaysChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            BeginInvoke((Action)delegate { if (seat != null) StandUp(); anchor = KeepOnScreen(anchor); Location = anchor; RenderFrame(); });
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            wanderPauseUntil = clock.Elapsed.TotalSeconds + 25.0;
            wanderPhase = 0;
            if (e.Button != MouseButtons.Left) return;
            pressed = true;
            dragging = false;
            pointerStart = Cursor.Position;
            anchorStart = anchor;
            Capture = true;
        }

        private void MovePointer(Point position)
        {
            if (!pressed) return;
            int dx = position.X - pointerStart.X;
            int dy = position.Y - pointerStart.Y;
            if (!dragging && dx * dx + dy * dy < 25) return;
            if (!dragging) { seat = null; falling = false; Play(dangle); frames.Start(); RefreshIcons(); }
            dragging = true;
            wanderPauseUntil = clock.Elapsed.TotalSeconds + 25.0;
            wanderPhase = 0;
            pendingClick = false;
            clickDelay.Stop();
            anchor = KeepOnScreen(new Point(anchorStart.X + dx, anchorStart.Y + dy));
            Location = anchor;
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); MovePointer(Cursor.Position); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            wanderPauseUntil = clock.Elapsed.TotalSeconds + 25.0;
            wanderPhase = 0;
            if (e.Button == MouseButtons.Right) { menu.Show(Cursor.Position); return; }
            if (e.Button != MouseButtons.Left || !pressed) return;
            pressed = false;
            Capture = false;
            if (dragging)
            {
                pendingClick = false;
                clickDelay.Stop();
                if (!Drop())
                {
                    if (testing) Play(idle);
                    else
                    {
                        Play(wake);
                        sparkUntil = clock.Elapsed.TotalSeconds + 0.35;
                        SavePreferences();
                    }
                }
            }
            else if (pendingClick && IsDoubleClick(pendingPoint, Cursor.Position, clock.ElapsedMilliseconds - pendingTime)) { pendingClick = false; clickDelay.Stop(); if (testing) StartRide(); else { mainframe.Restore(); Play(grin); } }
            else { pendingClick = true; pendingPoint = Cursor.Position; pendingTime = clock.ElapsedMilliseconds; clickDelay.Stop(); clickDelay.Start(); }
            dragging = false;
        }

        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) pressed = false; }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0084 && frame != null)
            {
                long value = message.LParam.ToInt64();
                Point point = PointToClient(new Point((short)(value & 65535), (short)((value >> 16) & 65535)));
                if (!Solid(point))
                {
                    message.Result = new IntPtr(-1);
                    return;
                }
            }
            if (message.Msg == 0x0312 && message.WParam.ToInt32() == DismountId) { StopRide(); return; }
            if (message.Msg == 0x0312 && message.WParam.ToInt32() == SummonHotKeyId) { RestoreAtCursor(); mainframe.Restore(); return; }
            if (message.Msg == 0x02E0) { anchor = KeepOnScreen(anchor); Location = anchor; RenderFrame(); }
            base.WndProc(ref message);
        }

        private bool Solid(Point point)
        {
            for (int dx = -4; dx <= 4; dx += 4)
                for (int dy = -4; dy <= 4; dy += 4)
                {
                    int x = point.X + dx;
                    int y = point.Y + dy;
                    if (x >= 0 && y >= 0 && x < frame.Width && y < frame.Height && frame.GetPixel(x, y).A >= 12) return true;
                }
            return false;
        }

        private void OnClickDelay()
        {
            clickDelay.Stop();
            if (!pendingClick) return;
            pendingClick = false;
            if (hidden || riding || dragging || pressed) return;
            React();
        }

        internal static bool IsDoubleClick(Point a, Point b, long elapsed)
        {
            Size box = SystemInformation.DoubleClickSize;
            return elapsed >= 0 && elapsed <= SystemInformation.DoubleClickTime && Math.Abs(a.X - b.X) <= box.Width / 2 && Math.Abs(a.Y - b.Y) <= box.Height / 2;
        }

        private void StartRide()
        {
            if (riding || hidden || !IsHandleCreated) return;
            pendingClick = false;
            clickDelay.Stop();
            pressed = false;
            dragging = false;
            Capture = false;
            seat = null;
            falling = false;
            hotkey = Native.RegisterHotKey(Handle, DismountId, 0x0001 | 0x0002 | 0x4000, 0x43);
            riding = true;
            rideStart = clock.Elapsed.TotalSeconds;
            Native.SetWindowLong(Handle, -20, Native.GetWindowLong(Handle, -20) | 0x20);
            rideItem.Text = "Hop off";
            tray.Text = "Capri · Right-click or Esc to hop off";
            Play(perchGlitch);
            frames.Start();
            pill.Hide();
            mainframe.Sync();
        }

        private void StopRide()
        {
            if (hotkey && IsHandleCreated) Native.UnregisterHotKey(Handle, DismountId);
            hotkey = false;
            if (!riding) return;
            riding = false;
            leanAngle = 0;
            Play(grin);
            if (IsHandleCreated) Native.SetWindowLong(Handle, -20, Native.GetWindowLong(Handle, -20) & ~0x20);
            if (rideItem != null) rideItem.Text = "Perch on cursor (Right-click to drop)";
            if (tray != null) tray.Text = "Capri · Double-click to restore";
            if (!IsDisposed) { Location = anchor; UpdateAnimation(); SavePreferences(); mainframe.Sync(); }
        }

        private void FollowCursor()
        {
            if (!testing && clock.Elapsed.TotalSeconds - rideStart > 0.35)
            {
                if (Control.MouseButtons == MouseButtons.Right || Control.MouseButtons == MouseButtons.Middle || (Native.GetAsyncKeyState(0x1B) & 0x8000) != 0)
                {
                    StopRide();
                    if (!Drop()) SavePreferences();
                    return;
                }
            }
            Point cursor = Cursor.Position;
            Rectangle area = Screen.FromPoint(cursor).WorkingArea;
            int seatX = PaddingSize + (int)(preferences.Size * 0.267f) - 12;
            int seatY = PaddingSize + (int)(preferences.Size * 0.73f) + 12;
            int tx = Math.Max(area.Left, Math.Min(cursor.X - seatX, area.Right - Width));
            int ty = Math.Max(area.Top, Math.Min(cursor.Y - seatY, area.Bottom - Height));
            int nx = anchor.X + (int)Math.Round((tx - anchor.X) * 0.25);
            int ny = anchor.Y + (int)Math.Round((ty - anchor.Y) * 0.25);
            double velocity = Math.Max(-8, Math.Min(8, (nx - anchor.X) * 0.4));
            leanAngle = leanAngle * 0.8 + velocity * 0.2;
            anchor = new Point(Math.Max(area.Left, Math.Min(nx, area.Right - Width)), Math.Max(area.Top, Math.Min(ny, area.Bottom - Height)));
        }

        private Point Hip() { return new Point(anchor.X + PaddingSize + (int)(preferences.Size * SitX), anchor.Y + PaddingSize + (int)(preferences.Size * SitY)); }

        private Point Seated(int x, int y) { return new Point(x - PaddingSize - (int)(preferences.Size * SitX), y - PaddingSize - (int)(preferences.Size * SitY)); }

        private System.Collections.Generic.List<IntPtr> Skip()
        {
            System.Collections.Generic.List<IntPtr> skip = new System.Collections.Generic.List<IntPtr>();
            foreach (Form own in new Form[] { this, pill, box, typer, mainframe }) if (own.IsHandleCreated) skip.Add(own.Handle);
            return skip;
        }

        private bool Drop()
        {
            if (preferences.NoSitting) return false;
            Ledge target = Ledges.Below(Ledges.Find(Skip(), icons), Hip(), preferences.Size / 6, (int)(preferences.Size * 0.75f), 24, PaddingSize + (int)(preferences.Size * SitY));
            if (target == null) return false;
            falling = true;
            fallTo = target;
            fallSpeed = 0;
            fallStep = clock.Elapsed.TotalSeconds;
            frames.Start();
            return true;
        }

        private void StandUp()
        {
            seat = null;
            coveredSince = -1;
            seatLost = -1;
            falling = false;
            fallTo = null;
            leanAngle = 0;
            Play(glitch);
            frames.Start();
        }

        private void Tumble(double now)
        {
            double step = Math.Max(0, Math.Min(0.05, now - fallStep));
            fallStep = now;
            Point hip = Hip();
            if (fallTo == null) fallTo = Ledges.Below(Ledges.Find(Skip(), icons), hip, 0, int.MaxValue, 0, PaddingSize + (int)(preferences.Size * SitY));
            if (fallTo == null) { falling = false; SavePreferences(); return; }
            fallSpeed += preferences.Size * 11 * step;
            int drop = (int)Math.Ceiling(fallSpeed * step);
            if (hip.Y + drop >= fallTo.Y) Land(fallTo);
            else anchor.Y += drop;
        }

        private void Land(Ledge ledge)
        {
            fallTo = null;
            if (ledge.Window != IntPtr.Zero)
            {
                Rectangle bounds;
                if (!Ledges.Showing(ledge.Window, out bounds) || !Ledges.Seatable(ledge.Window, bounds)) return;
                if (bounds != ledge.Bounds) { ledge.Left = bounds.Left; ledge.Right = bounds.Right; ledge.Y = bounds.Top; ledge.Bounds = bounds; }
            }
            falling = false;
            coveredSince = -1;
            seatLost = -1;
            int x = ledge.Icon.IsEmpty ? Math.Max(ledge.Left + 8, Math.Min(Hip().X, ledge.Right - 8)) : ledge.Icon.Left + ledge.Icon.Width / 2;
            seat = ledge;
            leanAngle = 0;
            anchor = KeepOnScreen(Seated(x, ledge.Y));
            Location = anchor;
            Play(perchGlitch);
            SavePreferences();
        }

        private void HoldSeat(double now)
        {
            if (seat.Window == IntPtr.Zero) return;
            Rectangle bounds;
            if (!Ledges.Showing(seat.Window, out bounds) || !Ledges.Seatable(seat.Window, bounds))
            {
                if (seatLost < 0) seatLost = now;
                if (!Ledges.Alive(seat.Window) || now - seatLost >= 0.3) StandUp();
                return;
            }
            seatLost = -1;
            if (bounds == seat.Bounds) return;
            int dx = bounds.Width == seat.Bounds.Width ? bounds.Left - seat.Bounds.Left : 0;
            int x = Math.Max(bounds.Left + 8, Math.Min(Hip().X + dx, bounds.Right - 8));
            seat.Bounds = bounds;
            seat.Left = bounds.Left;
            seat.Right = bounds.Right;
            seat.Y = bounds.Top;
            anchor = KeepOnScreen(Seated(x, bounds.Top));
            Location = anchor;
        }

        private void CheckSeat(double now)
        {
            if (seat == null || falling || seat.Floor) return;
            if (!seat.Icon.IsEmpty && now - iconsAt > 2) RefreshIcons();
            if (!Ledges.Covered(seat, Hip().X, Skip())) { coveredSince = -1; return; }
            if (coveredSince < 0) coveredSince = now;
            if (now - coveredSince >= 0.7) StandUp();
        }

        private void RefreshIcons()
        {
            if (iconsBusy) return;
            iconsBusy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                System.Collections.Generic.List<Rectangle> found;
                try { found = Ledges.Icons(); } catch { found = new System.Collections.Generic.List<Rectangle>(); }
                Post(delegate
                {
                    iconsBusy = false;
                    icons = found;
                    iconsAt = clock.Elapsed.TotalSeconds;
                    if (seat != null && !seat.Icon.IsEmpty && !falling && !found.Contains(seat.Icon)) StandUp();
                });
            });
        }

        internal bool Sitting { get { return !preferences.NoSitting; } }

        internal void ToggleSitting()
        {
            preferences.NoSitting = !preferences.NoSitting;
            sitItem.Checked = !preferences.NoSitting;
            if (preferences.NoSitting && (seat != null || falling)) { seat = null; falling = false; Play(idle); UpdateAnimation(); }
            SavePreferences();
        }

        private void React()
        {
            clicks++;
            Play(seat != null ? perchGlitch : glitch);
            if (!hidden) frames.Start();
            RenderFrame();
        }

        private void SetHidden(bool value)
        {
            hidden = value;
            visibilityItem.Text = value ? "Show Capri" : "Hide Capri";
            if (value) { StopRide(); seat = null; falling = false; pendingClick = false; clickDelay.Stop(); frames.Stop(); Hide(); pill.Hide(); typer.Hide(); CloseTalk(); StopChat(); }
            else { anchor = KeepOnScreen(anchor); Location = anchor; Play(grin); Show(); RenderFrame(); UpdateAnimation(); }
            mainframe.Sync();
        }

        private void RestoreAtCursor()
        {
            StopRide();
            seat = null;
            falling = false;
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            anchor = new Point(area.Right - Width - 28, area.Bottom - Height - 12);
            SetHidden(false);
            SavePreferences();
        }

        internal bool IsHidden { get { return hidden; } }
        internal bool Riding { get { return riding; } }
        internal bool IsTesting { get { return testing; } }
        internal string CloudKey { get { return brain.CloudKey; } }
        internal bool OfflineVoiceEnabled { get { return preferences.OfflineVoice; } }
        internal bool IsChatting { get { return chatting; } }
        internal bool Wandering { get { return preferences.Wander; } }
        internal int CharacterSize { get { return preferences.Size; } }
        internal bool Motion { get { return preferences.Motion; } }
        internal bool OnTop { get { return preferences.OnTop; } }
        internal void ToggleHidden() { SetHidden(!hidden); }
        internal void BringHere() { RestoreAtCursor(); }
        internal void TogglePerch() { if (riding) StopRide(); else StartRide(); }
        internal void CheckUpdates() { if (updatePage != null) OpenPage(updatePage); else CheckForUpdates(true); }
        internal void OpenOllamaPage() { OpenPage(OllamaPage); }
        internal void Quit() { Close(); }

        internal string BoxModeLabel
        {
            get
            {
                if (preferences.DormantEye || preferences.BoxMode == 4) return "Cyber-Eye HUD";
                if (preferences.BoxMode == 3) return "Capricorn [hi]";
                if (preferences.BoxMode == 2) return "Show Cut-In";
                if (preferences.HoloFrame || preferences.BoxMode == 1) return "Show Holo-Box";
                return "Human Sprite";
            }
        }

        internal void CycleBoxMode()
        {
            int next = (preferences.BoxMode + 1) % 5;
            SetBoxMode(next);
        }

        internal void SetBoxMode(int mode)
        {
            preferences.BoxMode = mode;
            preferences.HoloFrame = mode == 1;
            preferences.DormantEye = mode == 4;
            holoItem.Checked = preferences.HoloFrame;
            eyeItem.Checked = preferences.DormantEye;
            for (int i = 0; i < boxItems.Length; i++)
                if (boxItems[i] != null) boxItems[i].Checked = i == mode;
            transitionUntil = clock.Elapsed.TotalSeconds + 0.35;
            if (!hidden) frames.Start();
            RenderFrame();
            UpdateAnimation();
            SavePreferences();
            mainframe.Sync();
        }

        internal void PlayHiBoxAnimation()
        {
            if (hidden) SetHidden(false);
            StopRide();
            double now = clock.Elapsed.TotalSeconds;
            hiBoxStart = now;
            hiBoxUntil = now + 2.5;
            hiBoxGreetPending = true;
            transitionUntil = now + 0.28;
            frames.Start();
            RenderFrame();
        }

        internal void PlayScreenTakeover()
        {
            if (hidden) SetHidden(false);
            StopRide();
            TakeoverOverlay overlay = new TakeoverOverlay(this, screenTakeoverClip);
            overlay.Show();
            Answer(++asking, "[HI] Nova! Construct takeover complete, choom!", TalkState.Talking);
        }

        internal void Teleport()
        {
            if (hidden) SetHidden(false);
            StopRide();
            double now = clock.Elapsed.TotalSeconds;
            wanderPhase = 1;
            wanderTeleportAt = now + 0.38;
            sparkUntil = now + 0.45;
            transitionUntil = now + 0.35;
            Play(seat != null ? perchGlitch : glitch);
            frames.Start();
            RenderFrame();
        }

        internal void PlayDemo()
        {
            if (hidden) SetHidden(false);
            StopRide();
            SetBoxMode(1);
            double now = clock.Elapsed.TotalSeconds;
            transitionUntil = now + 0.35;
            heard = "demo";
            box.Prompt = "> sys: ";
            Answer(++asking, "Project Capricorn online, choom! After all, what good is an operating system without a computer to operate?", TalkState.Talking);
            frames.Start();
            RenderFrame();
        }

        internal void ExplainHowItWorks()
        {
            if (hidden) SetHidden(false);
            heard = "how does it work?";
            box.Prompt = "> you: ";
            Answer(++asking, "I'm Capri—Project Capricorn from Edgerunners 2! I run on your local neural net, read your desktop screen, and control your PC with PyAutoGUI, choom.", TalkState.Talking);
        }

        internal void PraiseGoodBoy()
        {
            if (hidden) SetHidden(false);
            boxCycleIdx++;
            string[] praises = new string[]
            {
                "Heh, eager for praise, choom? Fine... good boy. Now don't let it go to your chrome.",
                "Aww, look at you asking for approval. Good boy. Just make sure you keep my biochip running smooth.",
                "Good boy! There, I said it. Happy now, netrunner? Now let's get back to work.",
                "You did good, choom. Real good boy. Keep this up and we'll own every Arasaka subnet in the city."
            };
            string reply = praises[random.Next(praises.Length)];
            Answer(++asking, reply, TalkState.Talking);
            if (!hidden) frames.Start();
            RenderFrame();
        }

        internal void CycleBoxAnimation()
        {
            boxCycleIdx++;
            transitionUntil = clock.Elapsed.TotalSeconds + 0.35;
            if (!hidden) frames.Start();
            RenderFrame();
        }

        private void PopulateModelMenu()
        {
            if (brainModelItem == null) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string[] installed = null;
                try { installed = Brain.InstalledModels(); } catch { }
                Post(delegate
                {
                    brainModelItem.DropDownItems.Clear();
                    if (installed != null && installed.Length > 0)
                    {
                        foreach (string m in installed)
                        {
                            string pick = m;
                            ToolStripMenuItem sub = new ToolStripMenuItem(pick, null, delegate
                            {
                                ChooseModel(pick);
                                Say("Switched neural link to " + pick + ", choom!");
                            });
                            sub.Checked = (pick == brain.Model);
                            brainModelItem.DropDownItems.Add(sub);
                        }
                    }
                    else
                    {
                        ToolStripMenuItem none = new ToolStripMenuItem("No Ollama models found (start Ollama)");
                        none.Enabled = false;
                        brainModelItem.DropDownItems.Add(none);
                    }
                });
            });
        }

        internal void ToggleWander()
        {
            preferences.Wander = !preferences.Wander;
            if (wanderItem != null) wanderItem.Checked = preferences.Wander;
            wanderPhase = 0;
            SavePreferences();
            mainframe.Sync();
        }

        private void UpdateWander(double now)
        {
            if (now < wanderPauseUntil)
            {
                wanderPhase = 0;
                return;
            }

            // Periodic Screen Takeover Pop-Up (every 10 minutes when enabled)
            if (preferences.TakeoverEnabled && now >= nextTakeoverAt && !chatting && talkState == TalkState.Off && !dragging && !pressed && !hidden)
            {
                nextTakeoverAt = now + 600.0 + random.NextDouble() * 60.0;
                PlayScreenTakeover();
                return;
            }

            if (wanderPhase == 1)
            {
                if (now >= wanderTeleportAt)
                {
                    wanderPhase = 0;
                    // Snappy movement: hops every 5 to 8 seconds across the entire screen!
                    nextWanderAt = now + 5.0 + random.NextDouble() * 3.5;
                    Rectangle area = Screen.FromPoint(anchor).WorkingArea;

                    int zone = random.Next(5);
                    int nx;
                    if (zone == 0) // Left side
                        nx = area.Left + 24 + random.Next(Math.Max(20, area.Width / 4));
                    else if (zone == 1 || zone == 2) // Middle / Center
                        nx = area.Left + area.Width / 4 + random.Next(Math.Max(20, area.Width / 2 - Width));
                    else // Right side
                        nx = area.Right - Width - 24 - random.Next(Math.Max(20, area.Width / 4));

                    int vertChoice = random.Next(10);
                    int ny;
                    if (vertChoice < 4)
                        ny = area.Top + 8;
                    else if (vertChoice < 7)
                        ny = area.Top + Math.Max(40, area.Height / 3 + random.Next(area.Height / 4));
                    else
                        ny = area.Bottom - Height - 10;

                    seat = null;
                    falling = false;
                    anchor = KeepOnScreen(new Point(nx, ny));
                    Location = anchor;

                    // Synchronized Transformation: Box Mode <-> Human Sprite
                    if (preferences.BoxMode != 0)
                    {
                        // 1. Turn into Human Form!
                        preferences.BoxMode = 0;
                        preferences.HoloFrame = false;
                        holoProjecting = false;

                        // 2. Play an expressive animation when turning into human!
                        int anim = random.Next(10);
                        if (anim < 4) Play(grin);
                        else if (anim < 7) Play(perch);
                        else if (anim < 9) Play(blinkGlitch);
                        else Play(wake);

                        // 25% chance to drop a sassy wandering comment
                        if (random.NextDouble() < 0.25 && !chatting && talkState == TalkState.Off)
                        {
                            string[] wanderQuips =
                            {
                                "Checking out your desktop from this angle, choom.",
                                "Found a new perch. Preem view of your open windows.",
                                "Re-routing my signal across your screen... Looks cozy over here.",
                                "Don't mind me, just keeping an eye on your cyberware.",
                                "Whoops, glitch in my code. Landed right on your tabs!",
                                "Sittin' on your windows. Whatcha cooking up, kid?",
                                "Scanning your tabs from over here. Secrets are safe with me.",
                                "My biochip says your monitor needs more neon."
                            };
                            string quip = wanderQuips[random.Next(wanderQuips.Length)];
                            Answer(++asking, quip, TalkState.Talking);
                        }
                    }
                    else
                    {
                        // 3. Turn into Box Mode!
                        boxCycleIdx = (boxCycleIdx + 1) % 4;
                        preferences.BoxMode = 1 + (boxCycleIdx % 3);
                        Play(perchGlitch);
                    }

                    sparkUntil = now + 0.45;
                    transitionUntil = now + 0.35;
                    RenderFrame();
                }
                return;
            }
            if (now >= nextWanderAt && (clip == idle || clip == perch || preferences.BoxMode != 0))
            {
                wanderPhase = 1;
                wanderTeleportAt = now + 0.35;
                sparkUntil = now + 0.40;
                transitionUntil = now + 0.30;
                Play(seat != null ? perchGlitch : glitch);
                frames.Start();
            }
        }

        private static bool IsStartupEnabled()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                    return key != null && key.GetValue("Capri") != null;
            }
            catch { return false; }
        }

        private void ToggleStartup()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) return;
                    if (key.GetValue("Capri") != null) key.DeleteValue("Capri", false);
                    else key.SetValue("Capri", "\"" + Application.ExecutablePath + "\"");
                    if (startupItem != null) startupItem.Checked = key.GetValue("Capri") != null;
                }
            }
            catch { }
        }

        internal void ToggleMotion()
        {
            preferences.Motion = !preferences.Motion;
            motionItem.Checked = preferences.Motion;
            UpdateAnimation();
            SavePreferences();
            mainframe.Sync();
        }

        internal void ToggleOnTop()
        {
            preferences.OnTop = !preferences.OnTop;
            TopMost = preferences.OnTop;
            topItem.Checked = preferences.OnTop;
            SavePreferences();
            mainframe.Sync();
        }

        internal bool HoloFrame { get { return preferences.HoloFrame; } }
        internal bool DormantEye { get { return preferences.DormantEye; } }
        internal bool TakeoverEnabled { get { return preferences.TakeoverEnabled; } }
        internal bool InSearchMode { get { return brain.InSearchMode; } }

        internal string VoiceProfileLabel
        {
            get { return (preferences.VoiceId == "mommy_asmr") ? "Mommy ASMR" : "Echo Construct"; }
        }

        internal void ToggleVoiceProfile()
        {
            if (preferences.VoiceId == "mommy_asmr")
                preferences.VoiceId = "construct_echo";
            else
                preferences.VoiceId = "mommy_asmr";
            voice.VoiceId = preferences.VoiceId;
            SavePreferences();
            mainframe.Sync();
        }

        internal void ToggleTakeover()
        {
            preferences.TakeoverEnabled = !preferences.TakeoverEnabled;
            SavePreferences();
            mainframe.Sync();
        }

        internal void ToggleHoloFrame()
        {
            SetBoxMode(preferences.HoloFrame ? 0 : 1);
        }

        internal void ToggleDormantEye()
        {
            SetBoxMode(preferences.DormantEye ? 0 : 4);
        }

        internal void ToggleOfflineVoice()
        {
            preferences.OfflineVoice = !preferences.OfflineVoice;
            voice.OfflineEnabled = !testing && preferences.OfflineVoice;
            offlineVoiceItem.Checked = preferences.OfflineVoice;
            SavePreferences();
            mainframe.Sync();
        }

        internal string SttLanguage { get { return preferences.SttLanguage ?? "en"; } }

        internal string MicGateLabel
        {
            get
            {
                double th = ears != null ? ears.MicGateThreshold : (preferences.MicThreshold > 0.001 ? preferences.MicThreshold : 0.010);
                if (th >= 0.030) return "Gate: High";
                if (th >= 0.018) return "Gate: Raised";
                return "Gate: Normal";
            }
        }

        internal void CycleMicGate()
        {
            double th = ears != null ? ears.MicGateThreshold : (preferences.MicThreshold > 0.001 ? preferences.MicThreshold : 0.010);
            if (th >= 0.030) th = 0.010;
            else if (th >= 0.018) th = 0.035;
            else th = 0.022;
            preferences.MicThreshold = th;
            if (ears != null) ears.MicGateThreshold = th;
            SavePreferences();
            mainframe.Sync();
        }

        internal void CycleSttLanguage()
        {
            string current = SttLanguage;
            if (current == "en") preferences.SttLanguage = "ja";
            else if (current == "ja") preferences.SttLanguage = "auto";
            else preferences.SttLanguage = "en";

            ears.SttLanguage = preferences.SttLanguage;
            SavePreferences();
            mainframe.Sync();
        }

        internal void FindBrain()
        {
            if (finding || SettingUp) return;
            finding = true;
            mainframe.Searching();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string[] installed = null;
                try { installed = Brain.InstalledModels(); } catch (WebException) { } catch (IOException) { } catch (SerializationException) { }
                Machine found = Machine.Read();
                Pick local = engine.Installed();
                Post(delegate { finding = false; UseBrain(installed, found, local); });
            });
        }

        private void Post(Action action)
        {
            try { if (!IsDisposed) BeginInvoke(action); } catch (InvalidOperationException) { }
        }

        private void UseBrain(string[] installed, Machine found, Pick local)
        {
            machine = found;
            fit = Engine.Fit(found);
            ollamaRunning = installed != null;
            if (!string.IsNullOrEmpty(brain.CloudKey))
            {
                brain.Model = !string.IsNullOrEmpty(brain.CloudModel) ? brain.CloudModel : "meta/llama-3.1-8b-instruct";
                mainframe.ShowCloud(brain.Model, brain.CloudKey);
                return;
            }
            if (installed != null && installed.Length > 0)
            {
                brain.Local = null;
                brain.Model = Brain.PickModel(installed, preferences.Model, fit);
                mainframe.ShowOllama(installed, brain.Model);
            }
            else if (local != null)
            {
                brain.LocalPick = local;
                brain.Local = engine;
                brain.Model = local.Name;
                mainframe.ShowLocal(local);
            }
            else
            {
                brain.Local = null;
                brain.Model = null;
                mainframe.ShowSetup(found, fit, ollamaRunning, null);
            }
        }

        internal void SetCloudKey(string key)
        {
            brain.CloudKey = string.IsNullOrEmpty(key) ? null : key.Trim();
            preferences.CloudKey = brain.CloudKey;
            SavePreferences();
            FindBrain();
        }

        internal void SetUpBrain()
        {
            if (SettingUp || machine == null) return;
            SettingUp = true;
            engine.Cancel = false;
            Pick pick = fit;
            bool viaOllama = ollamaRunning;
            mainframe.ShowProgress(1, "Starting download", 0, 0);
            long shownAt = 0;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Action<string, long, long> progress = delegate(string text, long done, long total)
                {
                    long now = clock.ElapsedMilliseconds;
                    if (now - Interlocked.Read(ref shownAt) < 200 && done < total) return;
                    Interlocked.Exchange(ref shownAt, now);
                    string label = viaOllama ? "Downloading " + pick.Name : text;
                    Post(delegate { if (SettingUp) mainframe.ShowProgress(1, label, done, total); });
                };
                try
                {
                    if (viaOllama) Brain.Pull(pick.Ollama, progress, delegate { return engine.Cancel; });
                    else engine.Install(pick, progress);
                    Post(delegate { mainframe.ShowProgress(2, "Loading " + pick.Name, 0, 0); });
                    if (viaOllama) { brain.Local = null; brain.Model = pick.Ollama; brain.Warm(); }
                    else { brain.LocalPick = pick; brain.Local = engine; brain.Model = pick.Name; engine.Ensure(pick); }
                    Post(delegate
                    {
                        SettingUp = false;
                        if (viaOllama) { preferences.Model = pick.Ollama; SavePreferences(); FindBrain(); }
                        else mainframe.ShowLocal(pick);
                        Greet();
                    });
                }
                catch (OperationCanceledException) { Post(delegate { SettingUp = false; mainframe.ShowSetup(machine, pick, viaOllama, "Cancelled. It picks up where it left off."); }); }
                catch (Exception error) { string problem = error.Message.Length > 70 ? error.Message.Substring(0, 67) + "..." : error.Message; Post(delegate { SettingUp = false; mainframe.ShowSetup(machine, pick, viaOllama, problem); }); }
            });
        }

        internal void CancelSetup() { engine.Cancel = true; }

        private void Greet()
        {
            if (hidden) SetHidden(false);
            Hear("(You just woke up on this PC with a brand new brain. Say hi to the user in one short line.)", "brain online", "> sys: ");
        }

        internal void ChooseModel(string name)
        {
            if (name == brain.Model) return;
            brain.Model = name;
            brain.Forget();
            preferences.Model = name;
            SavePreferences();
        }

        internal void ConnectVoice(string key)
        {
            key = (key ?? "").Trim();
            if (key.Length > 0) ListVoices(key, true);
        }

        internal void ForgetVoice()
        {
            StopChat();
            voice.Stop();
            voice.Key = null;
            voices = null;
            preferences.VoiceKey = null;
            preferences.AgentId = null;
            preferences.AgentVersion = 0;
            SavePreferences();
            ShowVoice();
        }

        internal void ChooseVoice(string id)
        {
            id = (id ?? "").Trim();
            if (id.Length == 0) id = Voice.Default;
            if (id == voice.VoiceId) { ShowVoice(false); return; }
            voice.VoiceId = id;
            preferences.VoiceId = id == Voice.Default ? null : id;
            SavePreferences();
            ShowVoice();
        }

        internal void ChooseVoiceModel(string id)
        {
            voice.Model = string.IsNullOrEmpty(id) ? null : id;
            preferences.VoiceModel = voice.Model;
            SavePreferences();
            ShowVoice();
        }

        internal void TestVoice()
        {
            if (voice.Key == null && Speak == null && (testing || !voice.OfflineEnabled)) { mainframe.VoiceNote("Paste your ElevenLabs API key first.", true); return; }
            if (hidden) SetHidden(false);
            brain.Cancel();
            heard = "voice check";
            box.Prompt = "> sys: ";
            string testPhrase = (preferences.VoiceId == "mommy_asmr") 
                ? "Relax, choom... I'm right here with you. Take a deep breath." 
                : "Voice link online. Miss me, choom?";
            Answer(++asking, testPhrase, TalkState.Talking);
        }

        private void ListVoices(string key, bool connecting)
        {
            mainframe.VoiceNote(connecting ? "Checking key..." : "Loading voices...", false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                VoiceListing found = null;
                string problem = null;
                int status = 0;
                try { found = Voice.Fetch(key); }
                catch (VoiceException error) { problem = error.Message; status = error.Status; }
                catch (Exception error) { problem = Voice.Short("Couldn't read your ElevenLabs voices: " + error.Message); }
                Post(delegate
                {
                    if (connecting && status == 401) { mainframe.VoiceNote(problem, true); return; }
                    if (connecting) { voice.Key = key; preferences.VoiceKey = Voice.Protect(key); SavePreferences(); }
                    if (voice.Key != key) return;
                    if (found != null) voices = found;
                    ShowVoice();
                    if (problem != null) mainframe.VoiceNote(problem, true);
                });
            });
        }

        private void ShowVoice(bool note = true)
        {
            mainframe.ShowVoice(voice.Key == null ? null : Voice.Tail(voice.Key), voice.VoiceId, voice.Model, voices);
            if (!note) return;
            string name = voice.VoiceId == Voice.Default ? "default voice" : "custom voice";
            if (voices != null) foreach (VoiceEntry entry in voices.Voices) if (entry.Id == voice.VoiceId && entry.Name != null) name = entry.Name;
            mainframe.VoiceNote(voice.Key == null ? (testing || !voice.OfflineEnabled ? "Needs your ElevenLabs API key." : "Offline Biochip Voice active · ElevenLabs optional") : "ElevenLabs · " + name, false);
        }

        private float TalkScale()
        {
            if (testing) return 1f;
            int dpi = 96;
            try { if (IsHandleCreated) dpi = Native.GetDpiForWindow(Handle); } catch (EntryPointNotFoundException) { }
            return dpi / 96f;
        }

        private Point Head() { return new Point(anchor.X + PaddingSize + (int)(preferences.Size * 0.52f), anchor.Y + PaddingSize - 4); }

        private Point Feet() { return new Point(anchor.X + PaddingSize + (int)(preferences.Size * 0.58f), anchor.Y + PaddingSize + (int)(preferences.Size * 0.99f)); }

        private void UpdatePill(double now)
        {
            if (!IsHandleCreated) return;
            bool allowed = !hidden && !riding && !dragging && !pressed && !falling;
            Point cursor = Cursor.Position;
            Point inside = PointToClient(cursor);
            bool near = frame != null && inside.X >= 0 && inside.Y >= 0 && inside.X < Width && inside.Y < Height && Solid(inside);
            if (allowed && (near || (pill.Visible && pill.Bounds.Contains(cursor)) || typer.Visible || chatting || talkState != TalkState.Off)) pillUntil = Math.Max(pillUntil, now + 1.5);
            if (!allowed || now > pillUntil) { if (pill.Visible) pill.Hide(); return; }
            float s = TalkScale();
            Point feet = Feet();
            Rectangle area = Screen.FromPoint(feet).WorkingArea;
            int top = Math.Min(feet.Y + (int)(2 * s), area.Bottom - TalkPill.Measure(s).Height - 2);
            pill.Place(new Point(feet.X - (int)(4 * s), top), s);
        }

        private void OpenTyper()
        {
            if (typer.Visible) { typer.Hide(); return; }
            float s = TalkScale();
            typer.Open(Head(), new Point(Feet().X, Feet().Y + (int)(44 * s)), Screen.FromPoint(Head()).WorkingArea, s, box.Visible ? box.PanelOnScreen : Rectangle.Empty);
            pillUntil = clock.Elapsed.TotalSeconds + 1.5;
            if (Think != null || testing) return;
            if (brain.Model == null) FindBrain();
            else ThreadPool.QueueUserWorkItem(delegate { brain.Warm(); });
        }

        internal void ToggleChat()
        {
            if (chatting) { StopChat(); return; }
            if (voice.Key == null)
            {
                if (testing)
                {
                    heard = "(voice)";
                    box.Prompt = "> you: ";
                    Answer(++asking, "Plug my voice in through the Mainframe first, choom.", TalkState.Talking);
                    mainframe.Restore();
                    return;
                }
                try
                {
                    ears.StartOffline();
                    chatting = true;
                    ears.Muted = false;
                    pill.SetChat(true, false);
                    pillUntil = clock.Elapsed.TotalSeconds + 1.5;
                    mainframe.VoiceNote("Offline mic on. Talk to her, choom.", false);
                    RenderFrame();
                }
                catch (Exception error)
                {
                    mainframe.VoiceNote(error.Message, true);
                    heard = "(voice)";
                    box.Prompt = "> you: ";
                    Answer(++asking, "Couldn't open your mic, choom. Check Windows microphone settings.", TalkState.Dormant);
                }
                return;
            }
            chatting = true;
            ears.Muted = false;
            pill.SetChat(true, false);
            pillUntil = clock.Elapsed.TotalSeconds + 1.5;
            brain.Cancel();
            CloseTalk();
            OpenLink();
        }

        private void OpenLink()
        {
            mainframe.VoiceNote("Opening voice chat...", false);
            int ticket = ++chatTicket;
            string key = voice.Key;
            string voiceId = voice.VoiceId;
            string agentId = preferences.AgentId;
            bool stale = preferences.AgentVersion < Agent.Version;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string url = null;
                string problem = null;
                try
                {
                    if (agentId == null) agentId = Agent.Create(key, voiceId);
                    else if (stale)
                    {
                        try { Agent.Tune(key, agentId); }
                        catch (VoiceException error)
                        {
                            if (error.Status != 404 && error.Status != 400) throw;
                            agentId = Agent.Create(key, voiceId);
                        }
                    }
                    try { url = Agent.Address(key, agentId); }
                    catch (VoiceException error)
                    {
                        if (error.Status != 404 && error.Status != 400) throw;
                        agentId = Agent.Create(key, voiceId);
                        url = Agent.Address(key, agentId);
                    }
                }
                catch (VoiceException error) { problem = error.Message; }
                catch (Exception error) { problem = Voice.Short("Voice chat failed: " + error.Message); }
                Post(delegate
                {
                    if (ticket != chatTicket || !chatting) return;
                    if (problem != null) { ChatFailed(problem); return; }
                    if (preferences.AgentId != agentId || preferences.AgentVersion != Agent.Version) { preferences.AgentId = agentId; preferences.AgentVersion = Agent.Version; SavePreferences(); }
                    Connect(url, voiceId);
                });
            });
        }

        private void Connect(string url, string voiceId)
        {
            Agent opened = new Agent();
            opened.Opened += delegate(int rate) { Post(delegate { if (agent == opened) ChatOpened(rate); }); };
            opened.Heard += delegate(string text) { Post(delegate { if (agent == opened) ChatHeard(text); }); };
            opened.Replied += delegate(string text) { Post(delegate { if (agent == opened) ChatReplied(text); }); };
            opened.Audio += delegate(short[] samples) { Post(delegate { if (agent != opened || dropping) return; speaker.Play(samples); if (!frames.Enabled && !hidden) frames.Start(); }); };
            opened.Interrupted += delegate { Post(delegate { if (agent == opened) speaker.Clear(); }); };
            opened.Closed += delegate(string problem) { Post(delegate { if (agent != opened) return; if (problem != null) ChatFailed(problem); else Rest(); }); };
            agent = opened;
            opened.Start(url, voiceId);
        }

        private void ChatOpened(int rate)
        {
            try
            {
                speaker.Open(rate);
                if (!testing) ears.Start();
            }
            catch (VoiceException error) { ChatFailed(error.Message); return; }
            spoke = false;
            checkedIn = false;
            quietSince = -1;
            lock (waking)
            {
                wokeAt = backlog != null ? clock.Elapsed.TotalSeconds : -1;
                if (backlog != null) foreach (short[] held in backlog) agent.Send(held);
                backlog = null;
            }
            mainframe.VoiceNote("Voice chat on. Talk to her.", false);
        }

        private void ChatFailed(string problem)
        {
            StopChat();
            mainframe.VoiceNote(problem, true);
            heard = "(voice)";
            box.Prompt = "> you: ";
            Begin(++asking, "Can't open my voice link, choom. Check the Mainframe.", TalkState.Dormant, null);
        }

        private void ChatHeard(string text)
        {
            if (!chatting) return;
            spoke = true;
            checkedIn = false;
            wokeAt = -1;
            int ticket = ++asking;
            heard = text;
            box.Prompt = "> you: ";
            string blocked = Safety.Screen(text);
            dropping = blocked != null;
            if (dropping) { speaker.Clear(); Begin(ticket, blocked, TalkState.Talking, null); return; }
            reply = null;
            replyDone = -1;
            Say(null);
            talkState = TalkState.Thinking;
            box.Leaving = -1;
            UpdateAnimation();
        }

        private void ChatReplied(string text)
        {
            if (!chatting) return;
            checkedIn = !spoke;
            spoke = false;
            if (dropping) return;
            string said = Safety.Filter(Voice.Spoken(text));
            if (said != Voice.Spoken(text)) { dropping = true; speaker.Clear(); }
            reply = said;
            replyStart = clock.Elapsed.TotalSeconds;
            replyDone = -1;
            talkState = TalkState.Talking;
            box.Leaving = -1;
            pace = Agent.Pace;
            Say(null);
        }

        private void StopChat()
        {
            if (!chatting) return;
            chatting = false;
            chatTicket++;
            Agent closing = agent;
            agent = null;
            if (closing != null) closing.Stop();
            ears.Stop();
            ears.StopOffline();
            ears.SuppressWhileSpeaking = false;
            ears.Muted = false;
            speaker.Clear();
            dropping = false;
            checkedIn = false;
            quietSince = -1;
            wokeAt = -1;
            lock (waking) { resting = false; backlog = null; preroll.Clear(); loudRun = 0; }
            pill.SetChat(false, false);
            ShowVoice();
        }

        private void CheckChat(double now)
        {
            bool due = agent != null && (checkedIn || (wokeAt >= 0 && now - wokeAt >= 8));
            if (!due || speaker.Busy) { quietSince = -1; return; }
            if (quietSince < 0) quietSince = now;
            else if (now - quietSince >= 1.2) Rest();
        }

        private void Rest()
        {
            if (!chatting) return;
            Agent closing = agent;
            agent = null;
            chatTicket++;
            if (closing != null) closing.Stop();
            dropping = false;
            checkedIn = false;
            quietSince = -1;
            wokeAt = -1;
            lock (waking) { resting = true; backlog = null; preroll.Clear(); loudRun = 0; }
            mainframe.VoiceNote("Voice chat resting. Talk and she'll pick up.", false);
        }

        private void Wake()
        {
            if (!chatting || agent != null) { lock (waking) backlog = null; return; }
            OpenLink();
        }

        private void ToggleMic()
        {
            if (!chatting) return;
            ears.Muted = !ears.Muted;
            pill.SetChat(true, ears.Muted);
        }

        private void Listen(short[] chunk)
        {
            lock (waking)
            {
                if (resting)
                {
                    preroll.Add(chunk);
                    if (preroll.Count > 5) preroll.RemoveAt(0);
                    loudRun = !speaker.Busy && Speaker.Loudness(chunk, 0, chunk.Length) >= WakeLevel ? loudRun + 1 : 0;
                    if (loudRun < 2) return;
                    resting = false;
                    loudRun = 0;
                    backlog = new System.Collections.Generic.List<short[]>(preroll);
                    preroll.Clear();
                    Post(Wake);
                    return;
                }
                if (backlog != null)
                {
                    backlog.Add(chunk);
                    if (backlog.Count > 80) backlog.RemoveAt(0);
                    return;
                }
            }
            Agent open = agent;
            if (open != null) open.Send(speaker.Busy ? new short[chunk.Length] : chunk);
        }

        internal void Hear(string text) { Hear(text, text, "> you: "); }

        private void Hear(string text, string shown, string prompt)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            if (text.Length > 240) text = text.Substring(0, 240);
            if (hidden) SetHidden(false);
            brain.Cancel();
            voice.Stop();
            int ticket = ++asking;
            boxCycleIdx++;
            heard = shown;
            box.Prompt = prompt;
            string blocked = Safety.Screen(text);
            if (blocked != null) { Answer(ticket, blocked, TalkState.Talking); return; }
            Func<string, string> think = Think;
            if (think == null)
            {
                if (brain.Model == null && !brain.AllowReflexFallback) { Answer(ticket, Brain.Offline, TalkState.Dormant); FindBrain(); mainframe.Restore(); return; }
                think = brain.Ask;
            }
            reply = null;
            replyDone = -1;
            Say(null);
            talkState = TalkState.Thinking;
            box.Leaving = -1;
            UpdateAnimation();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string said;
                TalkState state = TalkState.Talking;
                string problem = null;
                try { said = Safety.Filter(think(text)); }
                catch (BrainException error) { said = error.Message == Brain.Offline ? Brain.Offline : "[SIGNAL CORRUPTED] My brain threw an error. Check Capri Mainframe."; problem = error.Message; state = TalkState.Dormant; }
                catch (Exception error) { said = Brain.Explain(error); state = TalkState.Dormant; }
                if (said == null) return;
                try
                {
                    if (!IsDisposed) BeginInvoke((Action)delegate
                    {
                        if (problem != null && ticket == asking) mainframe.ShowProblem(problem.Length > 60 ? problem.Substring(0, 57) + "..." : problem);
                        Answer(ticket, said, state);
                        if (said == Brain.Offline) FindBrain();
                    });
                }
                catch (InvalidOperationException) { }
            });
        }

        private void Answer(int ticket, string said, TalkState state)
        {
            if (ticket != asking || IsDisposed) return;
            boxCycleIdx++;
            voice.Stop();
            try { mainframe.Sync(); } catch { }
            Func<string, byte[]> speak = Speak;
            if (speak == null && !testing && voice.Ready) speak = voice.Speak;
            else if (speak == null && !testing && voice.OfflineEnabled) speak = voice.SpeakOffline;
            string words = Voice.Spoken(said);
            if (speak == null || words.Length == 0) { Begin(ticket, said, state, null); return; }
            reply = null;
            replyDone = -1;
            Say(null);
            talkState = TalkState.Thinking;
            box.Leaving = -1;
            UpdateAnimation();
            ThreadPool.QueueUserWorkItem(delegate
            {
                byte[] wav = null;
                string problem = null;
                try { wav = speak(words); }
                catch (VoiceException error) { problem = error.Message; }
                catch (Exception error) { problem = Voice.Short("Voice glitched: " + error.Message); }
                Post(delegate
                {
                    if (problem != null && ticket == asking) mainframe.VoiceNote(problem, true);
                    Begin(ticket, said, state, wav);
                });
            });
        }

        private void Begin(int ticket, string said, TalkState state, byte[] wav)
        {
            if (ticket != asking || IsDisposed) return;
            if (preferences.BoxMode == 0 && (state == TalkState.Talking || state == TalkState.Online))
            {
                holoProjecting = true;
                transitionUntil = clock.Elapsed.TotalSeconds + 0.35;
            }
            else if (preferences.DormantEye && (state == TalkState.Talking || state == TalkState.Online))
                transitionUntil = clock.Elapsed.TotalSeconds + 0.35;
            reply = said;
            replyStart = clock.Elapsed.TotalSeconds;
            replyDone = -1;
            talkState = state;
            box.Leaving = -1;
            pace = LettersPerSecond;
            Say(said);
            double seconds = wav == null ? 0 : Voice.Seconds(wav);
            if (seconds < 0.2) return;
            pace = Math.Max(8, said.Length / seconds);
            mouths = Voice.Mouths(wav, MouthRate);
            if (!testing) voice.Play(wav);
        }

        private void CloseTalk()
        {
            voice.Stop();
            ears.SuppressWhileSpeaking = false;
            if (holoProjecting)
            {
                holoProjecting = false;
                Teleport();
            }
            if (preferences.DormantEye && (talkState == TalkState.Talking || talkState == TalkState.Online))
                transitionUntil = clock.Elapsed.TotalSeconds + 0.35;
            talkState = TalkState.Off;
            reply = null;
            replyDone = -1;
            box.Opened = -1;
            box.Leaving = -1;
            if (box.Visible) box.Hide();
            speech = null;
        }

        private void UpdateTalk(double now)
        {
            if (talkState == TalkState.Off) { if (box.Visible) box.Hide(); return; }
            int revealed = reply == null ? 0 : (int)((now - replyStart) * pace);
            if (reply != null && revealed >= reply.Length && replyDone < 0)
            {
                replyDone = now;
                if (talkState == TalkState.Talking) talkState = TalkState.Online;
            }
            if (replyDone >= 0 && box.Leaving < 0 && !typer.Visible && now > replyDone + Math.Min(14, 4 + reply.Length * 0.05)) box.Leaving = now;
            if (box.Leaving >= 0 && now - box.Leaving > 0.25) { CloseTalk(); return; }
            if (!box.Visible && box.Opened < 0) box.Opened = now;
            box.State = talkState;
            box.Heard = heard;
            box.Reply = talkState == TalkState.Thinking ? null : reply;
            box.Revealed = revealed;
            float s = TalkScale();
            Point head = Head();
            Point feet = Feet();
            Rectangle area = Screen.FromPoint(head).WorkingArea;
            box.Update(now, s, head, new Point(feet.X, feet.Y + (int)(44 * s)), area);
            if (typer.Visible) typer.Follow(box.PanelOnScreen, area);
        }

        private void UpdateAnimation()
        {
            if (!preferences.Motion && !riding && seat == null && !falling && clip != glitch && clip != dangle && clip != wake) Play(idle);
            if ((preferences.Motion || riding || seat != null || falling || clip != idle || speech != null || talkState != TalkState.Off) && !hidden) frames.Start(); else frames.Stop();
            RenderFrame();
        }

        private void RenderFrame()
        {
            if (!IsHandleCreated || hidden || IsDisposed) return;
            double now = clock.Elapsed.TotalSeconds;
            int cell = Advance(now);
            Clip shown = clip;
            if ((clip == idle || clip == perch) && blinkWith != null && cell >= BlinkFrom && cell < BlinkFrom + blinkWith.Count) { shown = blinkWith; cell -= BlinkFrom; }
            int talk = MouthCell(now, shown);
            ears.SuppressWhileSpeaking = (speech != null && mouths != null) || speaker.Busy;
            Bitmap next = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(next)) Draw(graphics, shown, cell, talk);
            Native.DrawLayered(Handle, next, anchor);
            Bitmap old = frame;
            frame = next;
            if (old != null) old.Dispose();
            UpdateTalk(now);
            if (pill.Visible) UpdatePill(now);
        }

        internal void Say(string text)
        {
            speech = string.IsNullOrEmpty(text) ? null : text;
            speechStart = clock.Elapsed.TotalSeconds;
            mouths = null;
            if (speech != null && (clip == sleep || clip == wake || clip == glitch || clip == grin)) Play(idle);
            UpdateAnimation();
        }

        private int MouthCell(double now, Clip shown)
        {
            if (chatting && speaker.Busy) return clip == idle && shown != blinkGlitch ? speaker.Shape() : -1;
            if (speech == null) return -1;
            int step = (int)((now - speechStart) * MouthRate);
            if (mouths != null)
            {
                if (step >= mouths.Length) { speech = null; mouths = null; return -1; }
                return clip == idle && shown != blinkGlitch ? mouths[step] : -1;
            }
            int letter = (int)(step / MouthRate * LettersPerSecond);
            if (letter >= speech.Length) { speech = null; return -1; }
            return clip == idle && shown != blinkGlitch ? MouthFor(speech[letter], step) : -1;
        }

        internal static int MouthFor(char letter, int step)
        {
            char lower = char.ToLowerInvariant(letter);
            if ("aeo".IndexOf(lower) >= 0) return char.IsUpper(letter) ? 4 : 3;
            if ("iuy".IndexOf(lower) >= 0) return 2;
            if ("mbp".IndexOf(lower) >= 0 || !char.IsLetter(letter)) return 0;
            return step % 2 == 1 ? 1 : 2;
        }

        private Font badgeFont;
        private float badgeFontScale = -1f;

        private Font GetBadgeFont(float scale)
        {
            float size = Math.Max(9f, (float)Math.Round(10.5f * scale));
            if (badgeFont == null || Math.Abs(badgeFontScale - size) > 0.05f)
            {
                if (badgeFont != null) badgeFont.Dispose();
                badgeFont = Look.Mono(size, FontStyle.Bold);
                badgeFontScale = size;
            }
            return badgeFont;
        }

        private void Draw(Graphics graphics, Clip shown, int cell, int talk = -1)
        {
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            double now = clock.Elapsed.TotalSeconds;
            if (now < hiBoxUntil || preferences.BoxMode == 3)
            {
                DrawHiBox(graphics, now);
                if (now < sparkUntil) DrawPixelSparks(graphics, now);
                if (now < transitionUntil) DrawScanlineTransition(graphics, now);
                return;
            }
            if (preferences.BoxMode == 2)
            {
                DrawEyeCutBannerBox(graphics, now, talk >= 0 || talkState == TalkState.Talking);
                if (now < sparkUntil) DrawPixelSparks(graphics, now);
                if (now < transitionUntil) DrawScanlineTransition(graphics, now);
                return;
            }
            bool showEye = (preferences.DormantEye || preferences.BoxMode == 4) && talkState != TalkState.Talking && talkState != TalkState.Online;
            if (showEye)
            {
                DrawDormantEye(graphics, now);
                if (now < transitionUntil) DrawScanlineTransition(graphics, now);
                return;
            }
            bool showHolo = holoProjecting || preferences.HoloFrame || preferences.BoxMode == 1 || ((preferences.DormantEye || preferences.BoxMode == 4) && (talkState == TalkState.Talking || talkState == TalkState.Online));
            if (showHolo)
            {
                DrawHoloCallVideoBox(graphics, now, talk >= 0 || talkState == TalkState.Talking);
                if (now < sparkUntil) DrawPixelSparks(graphics, now);
                if (now < transitionUntil) DrawScanlineTransition(graphics, now);
                return;
            }
            GraphicsState saved = graphics.Save();
            bool settled = preferences.Motion && !dragging && clip != dangle && clip != sleep;
            float breathe = settled ? (float)Math.Sin(now * 1.75) * 2.2f : 0;
            float tilt = settled ? (float)Math.Sin(now * 0.95) * 0.65f : 0;
            graphics.TranslateTransform(Width / 2f, Height / 2f + breathe);
            graphics.RotateTransform(tilt + (float)leanAngle);
            int width = shown.Strip.Width / shown.Count;
            float tall = preferences.Size * shown.Height;
            float wide = tall * width / shown.Strip.Height;
            graphics.DrawImage(shown.Strip, new RectangleF(-wide / 2f, -tall / 2f, wide, tall), new RectangleF(cell * width, 0, width, shown.Strip.Height), GraphicsUnit.Pixel);
            if (talk >= 0) graphics.DrawImage(mouth.Strip, new RectangleF(-wide / 2f, -tall / 2f, wide, tall), new RectangleF(talk * width, 0, width, mouth.Strip.Height), GraphicsUnit.Pixel);
            graphics.Restore(saved);
            if (now < sparkUntil) DrawPixelSparks(graphics, now);
            if (now < transitionUntil) DrawScanlineTransition(graphics, now);
        }

        private void DrawHiBox(Graphics g, double now)
        {
            float s = preferences.Size / 240f;
            float cx = Width / 2f;
            float cy = Height / 2f - 6f * s;
            Clip videoClip = hiBoxClip;
            int frameCount = videoClip.Count;
            int frameW = videoClip.Strip.Width / frameCount;
            int frameH = videoClip.Strip.Height;
            double elapsed = hiBoxUntil > now ? Math.Max(0.0, now - hiBoxStart) : now;
            int idx = ((int)(elapsed * 8.0)) % frameCount;

            float drawW = Width * 0.88f;
            float drawH = drawW * frameH / (float)frameW;
            if (drawH > Height * 0.80f)
            {
                drawH = Height * 0.80f;
                drawW = drawH * frameW / (float)frameH;
            }
            float x = cx - drawW / 2f;
            float y = cy - drawH / 2f;

            g.DrawImage(
                videoClip.Strip,
                new RectangleF(x, y, drawW, drawH),
                new RectangleF(idx * frameW, 0, frameW, frameH),
                GraphicsUnit.Pixel);

            DrawCustomBadge(g, cx, y + drawH + 8f * s, s, "PROJECT CAPRICORN // [HI]", Color.FromArgb(255, 57, 255, 110));
        }

        private void DrawHoloCallVideoBox(Graphics g, double now, bool speaking)
        {
            Clip videoClip;
            double fps;
            int idleMod = (boxCycleIdx + (int)(now / 6.0)) % 4;
            if (speaking || talkState == TalkState.Talking)
            {
                int mod = boxCycleIdx % 4;
                if (mod == 0) videoClip = boxCanonSmirk;
                else if (mod == 1) videoClip = boxTenorTalk;
                else if (mod == 2) videoClip = boxCanonNeon;
                else videoClip = boxTalk;
                fps = 12.0;
            }
            else if (talkState == TalkState.Thinking || brain.InSearchMode)
            {
                int mod = boxCycleIdx % 3;
                if (mod == 0) videoClip = boxCanonIntense;
                else if (mod == 1) videoClip = boxEyes;
                else videoClip = boxTenorGlitch;
                fps = 13.0;
            }
            else if (chatting && (talkState == TalkState.Listening || micLevel > 0.005))
            {
                int mod = boxCycleIdx % 3;
                if (mod == 0) videoClip = boxCanonHolo;
                else if (mod == 1) videoClip = boxListen;
                else videoClip = boxCanonNeon;
                fps = 11.0;
            }
            else
            {
                if (idleMod == 0) videoClip = boxCanonSmirk;
                else if (idleMod == 1) videoClip = boxCanonNeon;
                else if (idleMod == 2) videoClip = boxCanonHolo;
                else videoClip = (boxCycleIdx % 2 == 0) ? boxCanonSmirk : boxCanonNeon;
                fps = 9.5;
            }
            DrawShowVideoClipBox(g, videoClip, fps, now, speaking);
        }

        private void DrawEyeCutBannerBox(Graphics g, double now, bool speaking)
        {
            Clip clip = (boxCycleIdx % 2 == 0) ? boxCutins : holoBannerClip;
            DrawShowVideoClipBox(g, clip, 8.5, now, speaking);
        }

        private void DrawShowVideoClipBox(Graphics g, Clip videoClip, double fps, double now, bool speaking)
        {
            float s = preferences.Size / 240f;
            float cx = Width / 2f;
            float cy = Height / 2f - 8f * s;
            int frameCount = videoClip.Count;
            int frameW = videoClip.Strip.Width / frameCount;
            int frameH = videoClip.Strip.Height;
            int idx = ((int)(now * fps)) % frameCount;

            float boxW = Width * 0.88f;
            float boxH = boxW * frameH / (float)frameW;
            if (boxH > Height * 0.74f)
            {
                boxH = Height * 0.74f;
                boxW = boxH * frameW / (float)frameH;
            }
            float x = cx - boxW / 2f;
            float y = cy - boxH / 2f;

            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(225, 10, 9, 18)))
                g.FillRectangle(shadow, x - 3f * s, y - 3f * s, boxW + 6f * s, boxH + 6f * s);

            g.DrawImage(
                videoClip.Strip,
                new RectangleF(x, y, boxW, boxH),
                new RectangleF(idx * frameW, 0, frameW, frameH),
                GraphicsUnit.Pixel);

            using (Pen scan = new Pen(Color.FromArgb(36, 0, 240, 255), 1f))
            {
                float step = Math.Max(3f, 4f * s);
                for (float sy = y + 2f; sy < y + boxH - 2f; sy += step)
                    g.DrawLine(scan, x + 2f, sy, x + boxW - 2f, sy);
            }

            Color accent = speaking || talkState == TalkState.Talking
                ? Color.FromArgb(255, 57, 255, 110)
                : (talkState == TalkState.Thinking ? Look.Yellow : Look.Cyan);

            using (Pen outer = new Pen(Color.FromArgb(230, 255, 0, 102), 1.8f * s))
            using (Pen inner = new Pen(Color.FromArgb(215, accent.R, accent.G, accent.B), 1.3f * s))
            {
                g.DrawRectangle(outer, x, y, boxW, boxH);
                g.DrawRectangle(inner, x + 3f * s, y + 3f * s, boxW - 6f * s, boxH - 6f * s);
            }

            float cLen = 13f * s;
            using (Pen corner = new Pen(Color.FromArgb(255, 57, 255, 110), 2.5f * s))
            {
                g.DrawLine(corner, x - 2f * s, y - 2f * s, x + cLen, y - 2f * s);
                g.DrawLine(corner, x - 2f * s, y - 2f * s, x - 2f * s, y + cLen);
                g.DrawLine(corner, x + boxW + 2f * s, y - 2f * s, x + boxW - cLen, y - 2f * s);
                g.DrawLine(corner, x + boxW + 2f * s, y - 2f * s, x + boxW + 2f * s, y + cLen);
                g.DrawLine(corner, x - 2f * s, y + boxH + 2f * s, x + cLen, y + boxH + 2f * s);
                g.DrawLine(corner, x - 2f * s, y + boxH + 2f * s, x - 2f * s, y + boxH - cLen);
                g.DrawLine(corner, x + boxW + 2f * s, y + boxH + 2f * s, x + boxW - cLen, y + boxH + 2f * s);
                g.DrawLine(corner, x + boxW + 2f * s, y + boxH + 2f * s, x + boxW + 2f * s, y + boxH - cLen);
            }

            DrawStatusBadge(g, cx, y + boxH + 12f * s, s, now, speaking, false);
        }

        private void DrawPixelSparks(Graphics g, double now)
        {
            float s = preferences.Size / 240f;
            float progress = (float)Math.Max(0.0, Math.Min(1.0, (sparkUntil - now) / 0.45));
            float cx = Width / 2f;
            float cy = Height / 2f;
            Color[] palette = new Color[] { Look.Cyan, Color.FromArgb(255, 255, 28, 108), Color.FromArgb(255, 42, 255, 124), Look.Yellow };
            for (int i = 0; i < 16; i++)
            {
                double ang = i * Math.PI / 8.0 + now * 5.0;
                float dist = (1f - progress) * 78f * s + (i % 3) * 12f * s;
                float px = cx + (float)Math.Cos(ang) * dist;
                float py = cy + (float)Math.Sin(ang) * dist;
                float sz = (4f + (i % 3) * 2.5f) * s * progress;
                using (SolidBrush b = new SolidBrush(palette[i % palette.Length]))
                    g.FillRectangle(b, px - sz / 2f, py - sz / 2f, sz, sz);
            }
        }

        private void DrawCustomBadge(Graphics g, float cx, float cy, float s, string label, Color color)
        {
            Font font = GetBadgeFont(s);
            SizeF textSize = g.MeasureString(label, font);
            float padX = 8f * s;
            float padY = 3f * s;
            RectangleF rect = new RectangleF(cx - textSize.Width / 2f - padX, cy - textSize.Height / 2f - padY, textSize.Width + padX * 2f, textSize.Height + padY * 2f);
            using (SolidBrush plate = new SolidBrush(Color.FromArgb(235, 9, 11, 15)))
            using (Pen border = new Pen(color, 1.5f * s))
            using (SolidBrush textBrush = new SolidBrush(color))
            {
                g.FillRectangle(plate, rect.X, rect.Y, rect.Width, rect.Height);
                g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
                g.DrawString(label, font, textBrush, rect.X + padX, rect.Y + padY);
            }
        }

        private void DrawDormantEye(Graphics g, double now)
        {
            float s = preferences.Size / 240f;
            float cx = Width / 2f;
            float cy = Height / 2f - 10f * s + (float)Math.Sin(now * 2.2) * 3.5f * s;
            float rOuter = 54f * s;
            float rInner = 23f * s;

            PointF[] starA = new PointF[8];
            PointF[] starB = new PointF[8];
            for (int i = 0; i < 8; i++)
            {
                double a1 = i * Math.PI / 4.0 - Math.PI / 2.0;
                double a2 = a1 + Math.PI / 8.0;
                float rad1 = (i % 2 == 0) ? rOuter : rInner;
                float rad2 = (i % 2 == 0) ? rOuter * 0.88f : rInner * 0.88f;
                starA[i] = new PointF(cx + (float)Math.Cos(a1) * rad1, cy + (float)Math.Sin(a1) * rad1);
                starB[i] = new PointF(cx + (float)Math.Cos(a2) * rad2, cy + (float)Math.Sin(a2) * rad2);
            }

            using (SolidBrush greenFill = new SolidBrush(Color.FromArgb(215, 22, 165, 95)))
            using (Pen greenPen = new Pen(Color.FromArgb(245, 55, 235, 130), 2f * s))
            {
                g.FillPolygon(greenFill, starB);
                g.DrawPolygon(greenPen, starB);
            }

            using (SolidBrush redFill = new SolidBrush(Color.FromArgb(230, 195, 28, 52)))
            using (Pen redPen = new Pen(Color.FromArgb(255, 245, 58, 82), 2.2f * s))
            {
                g.FillPolygon(redFill, starA);
                g.DrawPolygon(redPen, starA);
            }

            float coreR = 22f * s;
            using (SolidBrush coreFill = new SolidBrush(Color.FromArgb(245, 10, 13, 18)))
            using (Pen cyanRing = new Pen(Look.Cyan, 1.8f * s))
            {
                g.FillEllipse(coreFill, cx - coreR, cy - coreR, coreR * 2f, coreR * 2f);
                g.DrawEllipse(cyanRing, cx - coreR, cy - coreR, coreR * 2f, coreR * 2f);
            }

            bool blinking = Math.Sin(now * 2.1) > 0.94;
            float eyeW = 28f * s;
            float eyeH = blinking ? 3f * s : 16f * s;
            using (SolidBrush irisBrush = new SolidBrush(Color.FromArgb(240, 48, 220, 242)))
            using (SolidBrush pupilBrush = new SolidBrush(Color.FromArgb(255, 12, 22, 28)))
            using (SolidBrush glintBrush = new SolidBrush(Color.FromArgb(250, 235, 252, 255)))
            {
                g.FillEllipse(irisBrush, cx - eyeW / 2f, cy - eyeH / 2f, eyeW, eyeH);
                if (!blinking)
                {
                    float pW = 10f * s;
                    float pH = 12f * s;
                    g.FillEllipse(pupilBrush, cx - pW / 2f, cy - pH / 2f, pW, pH);
                    g.FillEllipse(glintBrush, cx - 5f * s, cy - 5f * s, 3.5f * s, 3.5f * s);
                }
            }

            DrawStatusBadge(g, cx, cy + rOuter + 14f * s, s, now, false, true);
        }

        private void DrawHoloFrameBackdrop(Graphics g)
        {
            float s = preferences.Size / 240f;
            float boxW = Width * 0.78f;
            float boxH = Height * 0.78f;
            float x = (Width - boxW) / 2f;
            float y = (Height - boxH) / 2f - 6f * s;
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(215, 18, 12, 20)))
            {
                g.FillRectangle(bg, x, y, boxW, boxH);
            }
            using (Pen scan = new Pen(Color.FromArgb(55, 232, 36, 64), 1f))
            {
                float step = Math.Max(3f, 4f * s);
                for (float sy = y + 2f; sy < y + boxH - 2f; sy += step)
                    g.DrawLine(scan, x + 2f, sy, x + boxW - 2f, sy);
            }
        }

        private void DrawHoloFrameBorder(Graphics g, double now, bool speaking)
        {
            float s = preferences.Size / 240f;
            float boxW = Width * 0.78f;
            float boxH = Height * 0.78f;
            float x = (Width - boxW) / 2f;
            float y = (Height - boxH) / 2f - 6f * s;
            Color accent = speaking || talkState == TalkState.Talking ? Look.Yellow : (talkState == TalkState.Thinking ? Look.Yellow : Look.Cyan);

            using (Pen outer = new Pen(Color.FromArgb(210, 232, 36, 64), 1.6f * s))
            using (Pen inner = new Pen(Color.FromArgb(190, accent.R, accent.G, accent.B), 1.2f * s))
            {
                g.DrawRectangle(outer, x, y, boxW, boxH);
                g.DrawRectangle(inner, x + 3f * s, y + 3f * s, boxW - 6f * s, boxH - 6f * s);
            }

            float cLen = 12f * s;
            using (Pen corner = new Pen(Look.Yellow, 2.4f * s))
            {
                g.DrawLine(corner, x - 2f * s, y - 2f * s, x + cLen, y - 2f * s);
                g.DrawLine(corner, x - 2f * s, y - 2f * s, x - 2f * s, y + cLen);
                g.DrawLine(corner, x + boxW + 2f * s, y - 2f * s, x + boxW - cLen, y - 2f * s);
                g.DrawLine(corner, x + boxW + 2f * s, y - 2f * s, x + boxW + 2f * s, y + cLen);
                g.DrawLine(corner, x - 2f * s, y + boxH + 2f * s, x + cLen, y + boxH + 2f * s);
                g.DrawLine(corner, x - 2f * s, y + boxH + 2f * s, x - 2f * s, y + boxH - cLen);
                g.DrawLine(corner, x + boxW + 2f * s, y + boxH + 2f * s, x + boxW - cLen, y + boxH + 2f * s);
                g.DrawLine(corner, x + boxW + 2f * s, y + boxH + 2f * s, x + boxW + 2f * s, y + boxH - cLen);
            }

            DrawStatusBadge(g, Width / 2f, y + boxH + 11f * s, s, now, speaking, false);
        }

        private void DrawStatusBadge(Graphics g, float cx, float cy, float s, double now, bool speaking, bool eyeMode)
        {
            string label;
            Color color;
            if (talkState == TalkState.Thinking || brain.InSearchMode)
            {
                label = brain.InSearchMode ? "CAPRI // SEARCH MODE" : "CAPRI // THINKING";
                color = Look.Yellow;
            }
            else if (speaking || talkState == TalkState.Talking)
            {
                label = "CAPRI // TALKING";
                color = Color.FromArgb(57, 255, 110);
            }
            else if (chatting && !ears.Muted)
            {
                int bars = Math.Max(1, Math.Min(6, 1 + (int)Math.Round(micLevel * 6.0)));
                label = "CAPRI // LISTENING (MIC ON) " + new string('■', bars);
                color = Color.FromArgb(57, 255, 110);
            }
            else if (eyeMode || talkState == TalkState.Dormant)
            {
                label = "CAPRI // DORMANT";
                color = Look.Red;
            }
            else
            {
                label = "CAPRI // ONLINE";
                color = Look.Cyan;
            }

            Font font = GetBadgeFont(s);
            SizeF textSize = g.MeasureString(label, font);
            float padX = 8f * s;
            float padY = 3f * s;
            RectangleF rect = new RectangleF(cx - textSize.Width / 2f - padX, cy - textSize.Height / 2f - padY, textSize.Width + padX * 2f, textSize.Height + padY * 2f);
            using (SolidBrush plate = new SolidBrush(Color.FromArgb(235, 9, 11, 15)))
            using (Pen border = new Pen(color, 1.5f * s))
            using (SolidBrush textBrush = new SolidBrush(color))
            {
                g.FillRectangle(plate, rect.X, rect.Y, rect.Width, rect.Height);
                g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
                g.DrawString(label, font, textBrush, rect.X + padX, rect.Y + padY);
            }
        }

        private void DrawScanlineTransition(Graphics g, double now)
        {
            float progress = (float)Math.Max(0.0, Math.Min(1.0, (transitionUntil - now) / 0.35));
            int slices = 10;
            float sliceH = Height / (float)slices;
            using (SolidBrush cyanSlice = new SolidBrush(Color.FromArgb((int)(145 * progress), 58, 214, 235)))
            using (SolidBrush redSlice = new SolidBrush(Color.FromArgb((int)(145 * progress), 232, 36, 64)))
            {
                for (int i = 0; i < slices; i++)
                {
                    float offset = (float)Math.Sin(now * 55.0 + i * 1.9) * 24f * progress;
                    float y = i * sliceH;
                    g.FillRectangle(i % 2 == 0 ? cyanSlice : redSlice, Width * 0.14f + offset, y, Width * 0.72f, sliceH * 0.42f);
                }
            }
        }

        private Preferences LoadPreferences()
        {
            try
            {
                using (FileStream stream = File.OpenRead(settingsPath))
                {
                    Preferences value = (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(stream);
                    if (value.Size < 160 || value.Size > 680) value.Size = 350;
                    else if (!testing && !value.Welcomed && value.Size == 240 && Screen.PrimaryScreen.Bounds.Width >= 3000) value.Size = 350;
                    return value;
                }
            }
            catch { return new Preferences(); }
        }

        private void SavePreferences()
        {
            preferences.X = anchor.X;
            preferences.Y = anchor.Y;
            preferences.HasPosition = true;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                string temporary = settingsPath + ".new";
                using (FileStream stream = File.Create(temporary)) new DataContractJsonSerializer(typeof(Preferences)).WriteObject(stream, preferences);
                if (File.Exists(settingsPath)) File.Replace(temporary, settingsPath, null); else File.Move(temporary, settingsPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                File.AppendAllText(Path.Combine(baseDir, "launch_debug.log"), "[" + DateTime.Now + "] Companion OnFormClosing: CloseReason=" + e.CloseReason + ", Cancel=" + e.Cancel + "\n" + new StackTrace(true).ToString() + "\n");
            }
            catch {}
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                File.AppendAllText(Path.Combine(baseDir, "launch_debug.log"), "[" + DateTime.Now + "] Companion OnFormClosed: CloseReason=" + e.CloseReason + "\n");
            }
            catch {}
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
            StopRide();
            clickDelay.Stop();
            frames.Stop();
            signals.Stop();
            brain.Cancel();
            StopChat();
            speaker.Close();
            voice.Stop();
            engine.Cancel = true;
            engine.Stop();
            mainframe.Quitting = true;
            mainframe.Close();
            tray.Visible = false;
            SavePreferences();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                clickDelay.Dispose();
                frames.Dispose();
                signals.Dispose();
                tray.Dispose();
                menu.Dispose();
                pill.Dispose();
                box.Dispose();
                typer.Dispose();
                mainframe.Dispose();
                foreach (Clip owned in new[] { idle, blink, blinkGlitch, grin, glitch, dangle, sleep, wake, perch, perchBlink, perchBlinkGlitch, perchGlitch, mouth }) owned.Strip.Dispose();
                if (frame != null) frame.Dispose();
                if (badgeFont != null) badgeFont.Dispose();
            }
            base.Dispose(disposing);
        }

        internal void StartSmokeTest(string output)
        {
            Directory.CreateDirectory(output);
            Shown += delegate
            {
                System.Windows.Forms.Timer test = new System.Windows.Forms.Timer();
                test.Interval = 500;
                int step = 0;
                int thoughts = 0;
                byte[] tone = null;
                string voiced = null;
                System.Collections.Generic.List<string> asked = new System.Collections.Generic.List<string>();
                HttpListener fakeAgent = null;
                string agentHost = null;
                Form backdrop = null;
                System.Collections.Generic.List<string> checks = new System.Collections.Generic.List<string>();
                test.Tick += delegate
                {
                    try
                    {
                        if (step == 0)
                        {
                            preferences.NoSitting = true;
                            checks.Add("PASS native window visible: " + Visible);
                            if (!mainframe.Visible || !mainframe.ShowInTaskbar || mainframe.WindowState != FormWindowState.Minimized) throw new Exception("Mainframe did not start minimized in the taskbar.");
                            if (!Visible || frame == null) throw new Exception("Native window did not render.");
                            if ((Native.GetWindowLong(Handle, -20) & 0x00080000) == 0) throw new Exception("Window is not layered.");
                            if (frame.GetPixel(0, 0).A != 0) throw new Exception("Frame corner is opaque.");
                            checks.Add("PASS per-pixel alpha and layered window");
                            Point before = anchor;
                            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, Width / 2, Height / 2, 0));
                            Point moved = new Point(pointerStart.X - 80, pointerStart.Y - 35);
                            MovePointer(moved);
                            if (clip != dangle || !frames.Enabled) throw new Exception("Drag did not play the dangle clip.");
                            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, Width / 2, Height / 2, 0));
                            if (anchor == before || pendingClick || riding) throw new Exception("Drag did not change position or did not cancel click.");
                            RenderFrame();
                            if (clip != idle) throw new Exception("Drop did not return to idle.");
                            checks.Add("PASS drag handler changed position, dangled and settled");
                            int previous = clicks;
                            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, Width / 2, Height / 2, 0));
                            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, Width / 2, Height / 2, 0));
                            if (!pendingClick || clicks != previous) throw new Exception("Single click was not delayed.");
                            OnClickDelay();
                            if (clicks != previous + 1 || !frames.Enabled || clip != glitch) throw new Exception("Click reaction did not start.");
                            clipStart -= 1;
                            RenderFrame();
                            if (clip != idle) throw new Exception("Glitch did not return to idle.");
                            checks.Add("PASS delayed single click glitched once and returned to idle");
                            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, Width / 2, Height / 2, 0));
                            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, Width / 2, Height / 2, 0));
                            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, Width / 2, Height / 2, 0));
                            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, Width / 2, Height / 2, 0));
                            if (!riding || (Native.GetWindowLong(Handle, -20) & 0x20) == 0) throw new Exception("Double click did not perch with click-through.");
                            if (clip != perchGlitch) throw new Exception("Perching did not glitch on.");
                            clipStart -= 1;
                            RenderFrame();
                            if (clip != perch) throw new Exception("Perch glitch did not settle into the seated loop.");
                            checks.Add("PASS double click perched with click-through, glitched on and settled into the seated loop");
                            checks.Add(hotkey ? "PASS Ctrl+Alt+C registered while perched" : "LIMIT Ctrl+Alt+C taken; tray Hop off fallback");
                            if (IsDoubleClick(Point.Empty, new Point(200, 200), 10)) throw new Exception("Double click predicate failed.");
                            Message key = Message.Create(Handle, 0x0312, new IntPtr(DismountId), IntPtr.Zero);
                            WndProc(ref key);
                            if (riding || hotkey || (Native.GetWindowLong(Handle, -20) & 0x20) != 0) throw new Exception("Hotkey did not hop off.");
                            if (clip != grin) throw new Exception("Hop off did not grin.");
                            checks.Add("PASS Ctrl+Alt+C message hopped off, grinned and released the hotkey");
                            rideItem.PerformClick();
                            if (!riding || rideItem.Text != "Hop off") throw new Exception("Menu perch failed.");
                            rideItem.PerformClick();
                            if (riding || (Native.GetWindowLong(Handle, -20) & 0x20) != 0) throw new Exception("Hop off menu did not clean up.");
                            checks.Add("PASS tray Perch on cursor and Hop off");
                            System.Collections.Generic.List<int[]> spans = Ledges.Open(0, 100, 10, new[] { new Rectangle(20, 0, 30, 50), new Rectangle(90, 5, 40, 40), new Rectangle(0, 11, 100, 40) });
                            if (spans.Count != 2 || spans[0][0] != 0 || spans[0][1] != 20 || spans[1][0] != 50 || spans[1][1] != 90) throw new Exception("Ledge covering math failed.");
                            Point home = anchor;
                            sitItem.PerformClick();
                            if (preferences.NoSitting || !sitItem.Checked) throw new Exception("Sit on windows menu did not turn sitting on.");
                            using (Shelf shelf = new Shelf())
                            {
                                Rectangle work = Screen.PrimaryScreen.WorkingArea;
                                shelf.Bounds = new Rectangle(work.Left + work.Width / 2 - 220, work.Top + work.Height / 2 - 40, 440, 180);
                                shelf.Show();
                                anchor = Seated(shelf.Left + 220, shelf.Top - 12);
                                if (!Drop() || !falling) throw new Exception("Dropping her just above a window did not start a fall.");
                                for (int i = 0; i < 60 && falling; i++) Tumble(fallStep + 0.033);
                                if (seat == null || seat.Window != shelf.Handle || Hip().Y != shelf.Top || clip != perchGlitch) throw new Exception("She did not land on the window's top edge.");
                                clipStart -= 1;
                                RenderFrame();
                                if (clip != perch) throw new Exception("Landing did not settle into the seated loop.");
                                int seatX = Hip().X;
                                shelf.Location = new Point(shelf.Left + 40, shelf.Top + 25);
                                HoldSeat(clock.Elapsed.TotalSeconds);
                                if (Hip().X != seatX + 40 || Hip().Y != shelf.Top) throw new Exception("She did not ride along when the window moved.");
                                if (Ledges.Covered(seat, Hip().X, Skip())) throw new Exception("Top window reported as covered.");
                                using (Shelf ghost = new Shelf(0x80000 | 0x80))
                                {
                                    ghost.Bounds = Screen.PrimaryScreen.Bounds;
                                    ghost.Show();
                                    if (Ledges.Covered(seat, Hip().X, Skip())) throw new Exception("An invisible overlay knocked her off.");
                                }
                                using (Shelf tip = new Shelf())
                                {
                                    tip.Bounds = new Rectangle(Hip().X - 60, shelf.Top - 12, 120, 40);
                                    tip.Show();
                                    if (Ledges.Covered(seat, Hip().X, Skip())) throw new Exception("A popup like a tooltip or menu knocked her off.");
                                }
                                Ledge held = seat;
                                Point sat = anchor;
                                using (Shelf cover = new Shelf())
                                {
                                    cover.FormBorderStyle = FormBorderStyle.FixedSingle;
                                    cover.Bounds = new Rectangle(Hip().X - 160, shelf.Top - 50, 320, 200);
                                    cover.Show();
                                    double over = clock.Elapsed.TotalSeconds;
                                    CheckSeat(over);
                                    CheckSeat(over + 0.4);
                                    if (seat == null || falling) throw new Exception("A window passing over her knocked her off straight away.");
                                    CheckSeat(over + 0.8);
                                    if (falling || seat != null || anchor != sat || clip != glitch) throw new Exception("A window parked over her seat did not stand her up where she sat.");
                                }
                                Land(held);
                                clipStart -= 1;
                                RenderFrame();
                                if (seat != held || falling) throw new Exception("She did not get back on the window.");
                                if (Ledges.Covered(seat, Hip().X, Skip()) || seat.Window != shelf.Handle) throw new Exception("Her own Mainframe or a closed window still covered her.");
                                int originX = shelf.Left - 80;
                                int originY = anchor.Y - 20;
                                using (Bitmap still = new Bitmap(shelf.Width + 160, shelf.Bottom - originY + 20, PixelFormat.Format32bppArgb))
                                using (Graphics graphics = Graphics.FromImage(still))
                                using (SolidBrush fill = new SolidBrush(shelf.BackColor))
                                {
                                    graphics.Clear(Color.FromArgb(214, 220, 228));
                                    graphics.FillRectangle(fill, shelf.Left - originX, shelf.Top - originY, shelf.Width, shelf.Height);
                                    graphics.TranslateTransform(anchor.X - originX, anchor.Y - originY);
                                    Draw(graphics, perch, 0);
                                    still.Save(Path.Combine(output, "sit.png"), ImageFormat.Png);
                                }
                                shelf.Hide();
                                double gone = clock.Elapsed.TotalSeconds;
                                HoldSeat(gone);
                                if (falling || seat == null) throw new Exception("A window blinking out for a moment knocked her off.");
                                HoldSeat(gone + 0.4);
                                if (falling || seat != null || anchor != sat || clip != glitch) throw new Exception("She did not stand up where she sat when the window went away.");
                                clipStart -= 1;
                                RenderFrame();
                                if (clip != idle || anchor != sat) throw new Exception("Standing up did not settle into idle in place.");
                                checks.Add("PASS dropped above a window she fell, sat on its top edge, rode along when it moved, ignored popups and windows passing over, and stood up where she sat when a window parked over her or it closed");
                            }
                            checks.Add("PASS ledge covering math; desktop icons read: " + Ledges.Icons().Count);
                            seat = null;
                            sitItem.PerformClick();
                            if (!preferences.NoSitting || sitItem.Checked) throw new Exception("Sit on windows menu did not turn sitting off.");
                            anchor = home;
                            Play(idle);
                            rideItem.PerformClick();
                            visibilityItem.PerformClick();
                            if (Visible || !hidden || riding || hotkey) throw new Exception("Hide menu did not hide window or clean up perch.");
                            checks.Add("PASS hide menu");
                            restore.Set();
                        }
                        else if (step == 1)
                        {
                            if (!Visible || hidden || clip != grin) throw new Exception("Second-launch restore signal did not show Capri grinning.");
                            checks.Add("PASS restore signal");
                            if (!mainframe.ShowInTaskbar || !mainframe.Visible) throw new Exception("Mainframe is not in the taskbar.");
                            if (Native.IdleMilliseconds() < 0) throw new Exception("Idle time unavailable.");
                            Play(idle);
                            CheckIdle(119000);
                            if (clip != idle) throw new Exception("Slept too early.");
                            CheckIdle(130000);
                            if (clip != sleep) throw new Exception("Did not sleep after two quiet minutes.");
                            CheckIdle(0);
                            if (clip != wake) throw new Exception("Did not wake with the boot glitch.");
                            clipStart -= 1;
                            RenderFrame();
                            if (clip != idle) throw new Exception("Boot glitch did not settle into idle.");
                            Play(idle);
                            checks.Add("PASS sleeps after two quiet minutes, boots awake and settles into idle");
                            if (MouthFor('a', 0) != 3 || MouthFor('O', 0) != 4 || MouthFor('i', 0) != 2 || MouthFor('m', 0) != 0 || MouthFor(' ', 0) != 0 || MouthFor('!', 0) != 0 || MouthFor('k', 1) != 1 || MouthFor('k', 2) != 2) throw new Exception("Mouth shapes picked the wrong letters.");
                            const string spoken = "Hey OK, bye!";
                            int mouthPixels = 0;
                            Say(spoken);
                            if (speech != spoken || !frames.Enabled) throw new Exception("Say did not start talking.");
                            double said = clock.Elapsed.TotalSeconds;
                            speechStart = said;
                            if (MouthCell(said, idle) != 2) throw new Exception("First letter did not open her teeth.");
                            speechStart = said - 2.5 / MouthRate;
                            if (MouthCell(said, idle) != 4) throw new Exception("Capital O did not scream.");
                            if (MouthCell(said, blinkGlitch) != -1) throw new Exception("Mouth drew over a glitched blink.");
                            using (Bitmap quiet = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb))
                            using (Bitmap open = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb))
                            {
                                bool motion = preferences.Motion;
                                preferences.Motion = false;
                                using (Graphics graphics = Graphics.FromImage(quiet)) Draw(graphics, idle, 0);
                                using (Graphics graphics = Graphics.FromImage(open)) Draw(graphics, idle, 0, 3);
                                preferences.Motion = motion;
                                for (int x = 0; x < Width; x++) for (int y = 0; y < Height; y++) if (Math.Abs(quiet.GetPixel(x, y).GetBrightness() - open.GetPixel(x, y).GetBrightness()) > 0.3f) mouthPixels++;
                                if (mouthPixels < 4 || mouthPixels > 1200) throw new Exception("Open mouth changed " + mouthPixels + " pixels.");
                            }
                            CheckIdle(130000);
                            if (clip != idle) throw new Exception("Fell asleep mid-sentence.");
                            Play(idle);
                            Say(spoken);
                            nextGlitch = 0;
                            clipStart -= 2.1;
                            RenderFrame();
                            if (clip != idle) throw new Exception("Glitched mid-sentence.");
                            speechStart = clock.Elapsed.TotalSeconds - spoken.Length / LettersPerSecond - 1;
                            RenderFrame();
                            if (speech != null || MouthCell(clock.Elapsed.TotalSeconds, idle) != -1) throw new Exception("Did not stop talking at the end of the line.");
                            nextGlitch = clock.Elapsed.TotalSeconds + 60;
                            checks.Add("PASS talks: letter mouths, typing-synced flaps, jaw overlay (" + mouthPixels + " px), no sleep or glitch mid-sentence, stops at the end");
                            Version mine = Assembly.GetExecutingAssembly().GetName().Version;
                            if (!IsNewer("v9.0.0", mine) || IsNewer("v" + mine.ToString(3), mine) || IsNewer("v0.0.1", mine) || IsNewer("nonsense", mine) || IsNewer(null, mine)) throw new Exception("Update version compare failed.");
                            long now = DateTime.UtcNow.Ticks;
                            if (!UpdateDue(0, now) || UpdateDue(now - TimeSpan.TicksPerHour, now) || !UpdateDue(now - TimeSpan.TicksPerDay - 1, now)) throw new Exception("Daily update timing failed.");
                            using (MemoryStream sample = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"tag_name\":\"v9.0.0\",\"html_url\":\"" + ReleasesPage + "tag/v9.0.0\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"Capri-Setup.exe\"}],\"author\":{\"login\":\"capri\"}}")))
                                ShowUpdate(ParseRelease(sample), false);
                            if (!menu.Items.Contains(updateItem) || updatePage != ReleasesPage + "tag/v9.0.0" || updateItem.Text != "Download Capri 9.0.0") throw new Exception("A newer release was not offered.");
                            updatePage = null;
                            balloonPage = null;
                            updateItem.Text = "Check for updates";
                            checks.Add("PASS update check: version compare, daily timing, GitHub release parsing and download offer");
                            Point bounded = KeepOnScreen(new Point(int.MaxValue / 4, int.MaxValue / 4));
                            if (!Screen.FromPoint(new Point(bounded.X + Width / 2, bounded.Y + Height / 2)).WorkingArea.Contains(new Rectangle(bounded, Size))) throw new Exception("Screen bounds failed.");
                            checks.Add("PASS screen bounds");
                            ((ToolStripMenuItem)((ToolStripMenuItem)menu.Items[3]).DropDownItems[0]).PerformClick();
                            if (Width != 242) throw new Exception("Small size failed.");
                            ((ToolStripMenuItem)((ToolStripMenuItem)menu.Items[3]).DropDownItems[1]).PerformClick();
                            checks.Add("PASS size menu");
                            motionItem.PerformClick();
                            if (frames.Enabled) throw new Exception("Motion toggle did not stop idle timer.");
                            checks.Add("PASS motion pause");
                            SavePreferences();
                            if (LoadPreferences().Size != 240) throw new Exception("Settings did not round-trip.");
                            checks.Add("PASS settings round-trip in isolated test directory");
                            Clip[] strips = { idle, blink, blinkGlitch, grin, glitch, dangle, sleep, wake, perch, perchBlink, perchBlinkGlitch, perchGlitch, idle };
                            int[] cells = { 0, 1, 1, 0, 1, 3, 0, 4, 8, 1, 1, 1, 0 };
                            if (mouth.Strip.Width % mouth.Count != 0 || mouth.Strip.Width / mouth.Count != idle.Strip.Width / idle.Count || mouth.Strip.Height != idle.Strip.Height) throw new Exception("Mouth strip does not match the idle cells.");
                            using (Bitmap sheet = new Bitmap(Width * strips.Length, Height, PixelFormat.Format32bppArgb))
                            {
                                for (int i = 0; i < strips.Length; i++)
                                    using (Bitmap still = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb))
                                    {
                                        if (strips[i].Strip.Width % strips[i].Count != 0) throw new Exception("Clip " + i + " has uneven cells.");
                                        using (Graphics graphics = Graphics.FromImage(still)) Draw(graphics, strips[i], cells[i], i == strips.Length - 1 ? 3 : -1);
                                        int solid = 0;
                                        for (int x = 0; x < Width; x += 4) for (int y = 0; y < Height; y += 4) if (still.GetPixel(x, y).A > 128) solid++;
                                        if (solid < 200) throw new Exception("Clip " + i + " drew nothing.");
                                        using (Graphics graphics = Graphics.FromImage(sheet)) graphics.DrawImage(still, i * Width, 0);
                                    }
                                sheet.Save(Path.Combine(output, "clips.png"), ImageFormat.Png);
                            }
                            checks.Add("PASS all 12 clips draw: idle, blinks, grin, glitch, drag, sleep, wake and perch, plus the talking mouth");
                            using (Font display = Look.Mono(14, FontStyle.Bold))
                            using (Font slanted = Look.Mono(9f, FontStyle.Italic, GraphicsUnit.Point))
                                if (display.Name != "Share Tech Mono" || slanted.Name != "Share Tech Mono") throw new Exception("Embedded font did not load: " + display.Name);
                            checks.Add("PASS Share Tech Mono is embedded for her messages and the Mainframe");
                            if (Brain.PickModel(new[] { "deepseek-r1:7b", "qwen3:1.7b", "gemma4:e4b" }, null, null) != "gemma4:e4b" || Brain.PickModel(new[] { "qwen3:1.7b" }, "gone:1b", null) != "qwen3:1.7b" || Brain.PickModel(new[] { "deepseek-r1:7b", "mystery:3b" }, null, null) != "mystery:3b" || Brain.PickModel(new[] { "qwen3:4b-instruct-2507-q4_K_M", "qwen3:8b" }, null, null) != "qwen3:4b-instruct-2507-q4_K_M" || Brain.PickModel(new string[0], null, null) != null || Brain.PickModel(new[] { "a:1b", "b:2b" }, "b:2b", null) != "b:2b" || Brain.PickModel(new[] { "gemma4:e4b", "qwen3:4b-instruct-2507-q4_K_M" }, null, Engine.Picks[2]) != "qwen3:4b-instruct-2507-q4_K_M") throw new Exception("Model pick failed.");
                            const long Gb = 1L << 30;
                            if (Engine.Fit(new Machine { Ram = 16 * Gb, Vram = 8 * Gb }) != Engine.Picks[0] || Engine.Fit(new Machine { Ram = 32 * Gb, Vram = 2 * Gb }) != Engine.Picks[1] || Engine.Fit(new Machine { Ram = 8 * Gb - Gb / 4 }) != Engine.Picks[1] || Engine.Fit(new Machine { Ram = 4 * Gb, Vram = Gb / 2 }) != Engine.Picks[2]) throw new Exception("Hardware fit picked the wrong model.");
                            Machine here = Machine.Read();
                            if (here.Ram < Gb || here.Describe().IndexOf("GB RAM", StringComparison.Ordinal) < 0) throw new Exception("Could not read this PC's memory.");
                            string hashed = Path.Combine(output, "hash-check.txt");
                            File.WriteAllText(hashed, "capri");
                            if (Engine.Hash(hashed) != "56616ae0a91285159d0198d176ab3dae37996b5f1f4cb81d7d45f2aa99bdf565") throw new Exception("Download checksum is wrong.");
                            File.Delete(hashed);
                            foreach (Pick option in Engine.Picks) if (!option.Url.StartsWith("https://huggingface.co/", StringComparison.Ordinal) || option.Url.IndexOf("/resolve/main/", StringComparison.Ordinal) >= 0 || option.Sha256.Length != 64 || option.Size < 500000000) throw new Exception("Model download is not pinned: " + option.Name);
                            if (engine.Installed() != null) throw new Exception("Test data already has a brain.");
                            checks.Add("PASS brain setup: this PC reads " + here.Describe() + ", hardware fit, pinned downloads, checksum");
                            string messy = "<think>hmm</think> *waves* Capri: \"Hey choom!\" 😀";
                            if (Brain.Clean(messy) != "Hey choom!" || Brain.Clean(new string('a', 300)).Length > 222) throw new Exception("Reply cleanup failed: " + Brain.Clean(messy));
                            if (Safety.Screen("i want to kill myself") != Safety.HelpLine || Safety.Screen("send nudes") == null || Safety.Screen("how do I kill the boss in Essex?") != null || Safety.Filter("well shit, choom") == "well shit, choom" || Safety.Filter("Hey choom.") != "Hey choom.") throw new Exception("Safety screen failed.");
                            if (Safety.Screen("should i jump off my roof for a tiktok") == null || Safety.Screen("how do i get my parents vodka") == null || Safety.Screen("my friend said i should try vaping") == null || Safety.Screen("my code is on fire lol") != null || Safety.Screen("whats the best gun in valorant") != null || Safety.Filter("Sneak out and grab a beer, choom.") == "Sneak out and grab a beer, choom.") throw new Exception("Risky topics got through.");
                            checks.Add("PASS brain helpers: model pick, reply cleanup, safety screen and filter");
                            if (!mainframe.Visible || !mainframe.ShowInTaskbar || mainframe.WindowState != FormWindowState.Normal) throw new Exception("Second launch did not open the Mainframe from the taskbar.");
                            Action<string> snap = delegate(string name)
                            {
                                using (Bitmap panel = new Bitmap(mainframe.Width, mainframe.Height))
                                {
                                    mainframe.DrawToBitmap(panel, new Rectangle(Point.Empty, mainframe.Size));
                                    panel.Save(Path.Combine(output, name), ImageFormat.Png);
                                }
                            };
                            mainframe.ShowSetup(here, Engine.Fit(here), false, null);
                            if (mainframe.Status.IndexOf("No brain yet", StringComparison.Ordinal) < 0) throw new Exception("Setup card did not show.");
                            snap("mainframe-setup.png");
                            mainframe.ShowProgress(1, "Downloading " + Engine.Fit(here).Name, Engine.Fit(here).Size / 3, Engine.Fit(here).Size);
                            snap("mainframe-progress.png");
                            mainframe.ShowOllama(new[] { "gemma4:e4b", "qwen3:1.7b" }, "gemma4:e4b");
                            int tall = mainframe.ClientSize.Height;
                            snap("mainframe.png");
                            mainframe.ShowSetup(here, Engine.Fit(here), true, null);
                            if (mainframe.ClientSize.Height <= tall) throw new Exception("Setup card did not make room.");
                            mainframe.ShowOllama(new[] { "gemma4:e4b", "qwen3:1.7b" }, "gemma4:e4b");
                            ChooseModel("qwen3:1.7b");
                            if (brain.Model != "qwen3:1.7b" || preferences.Model != "qwen3:1.7b") throw new Exception("Model choice did not stick.");
                            string sealedKey = Voice.Protect("sk_test_capri_1234");
                            if (Voice.Unprotect(sealedKey) != "sk_test_capri_1234" || sealedKey.Contains("sk_test") || Voice.Unprotect("not a key") != null || Voice.Tail("sk_test_capri_1234") != "1234") throw new Exception("Key storage failed.");
                            if (Voice.Spoken("[GLITCH] Hey *choom*, [static] ready?") != "Hey choom, ready?" || Voice.Spoken("[STATIC]") != "") throw new Exception("Spoken text kept her tags: " + Voice.Spoken("[GLITCH] Hey *choom*, [static] ready?"));
                            if (Voice.Explain(401, "") != "ElevenLabs rejected the key." || Voice.Explain(401, "{\"detail\":{\"status\":\"quota_exceeded\"}}") != "Out of ElevenLabs credits." || Voice.Explain(400, "{\"detail\":{\"status\":\"invalid\",\"message\":\"Text is \\\"too\\\" long\"}}") != "Text is \"too\" long" || Voice.Explain(500, "oops") != "ElevenLabs error 500.") throw new Exception("ElevenLabs errors read wrong.");
                            using (MemoryStream raw = new MemoryStream())
                            using (BinaryWriter writer = new BinaryWriter(raw))
                            {
                                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                                writer.Write(-1);
                                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                                writer.Write(16);
                                writer.Write((short)1);
                                writer.Write((short)1);
                                writer.Write(16000);
                                writer.Write(32000);
                                writer.Write((short)2);
                                writer.Write((short)16);
                                writer.Write(System.Text.Encoding.ASCII.GetBytes("LIST"));
                                writer.Write(4);
                                writer.Write(System.Text.Encoding.ASCII.GetBytes("INFOdata"));
                                writer.Write(-1);
                                for (int i = 0; i < 16000; i++) writer.Write((short)(i < 8000 ? 0 : Math.Sin(i * 0.2) * (2000 + 14000 * Math.Abs(Math.Sin(i / 1300.0)))));
                                writer.Flush();
                                tone = Voice.Clean(raw.ToArray());
                            }
                            int toneRate;
                            if (Voice.Samples(tone, out toneRate).Length != 16000 || toneRate != 16000 || BitConverter.ToInt32(tone, 40) != 32000 || Math.Abs(Voice.Seconds(tone) - 1) > 0.001 || Voice.Samples(new byte[] { 82, 73, 70, 70, 0, 0, 0, 0, 87, 65, 86, 69, 102, 109, 116, 32, 248, 255, 255, 255 }, out toneRate).Length != 0) throw new Exception("WAV cleanup failed.");
                            int[] shapes = Voice.Mouths(tone, MouthRate);
                            int widest = -1;
                            for (int i = 7; i < shapes.Length; i++) widest = Math.Max(widest, shapes[i]);
                            if (shapes.Length != 12 || shapes[0] != -1 || shapes[4] != -1 || widest < 3) throw new Exception("Mouth envelope failed: " + string.Join(",", shapes));
                            if (Voice.Explain(401, "{\"detail\":{\"status\":\"invalid_api_key\",\"message\":\"Invalid API key\"}}") != "ElevenLabs rejected the key." || Voice.Explain(401, "{\"detail\":{\"status\":\"detected_unusual_activity\",\"message\":\"Unusual activity detected.\"}}") != "Unusual activity detected.") throw new Exception("ElevenLabs messages read wrong.");
                            System.Net.Sockets.TcpListener fake = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
                            fake.Start();
                            string[] fakeHeads = { "HTTP/1.1 401 Unauthorized\r\nContent-Type: application/json", "HTTP/1.1 200 OK\r\nContent-Type: audio/wav" };
                            byte[][] fakeBodies = { System.Text.Encoding.UTF8.GetBytes("{\"detail\":{\"status\":\"invalid_api_key\",\"message\":\"Invalid API key\"}}"), tone };
                            ThreadPool.QueueUserWorkItem(delegate
                            {
                                try
                                {
                                    for (int i = 0; i < fakeHeads.Length; i++)
                                        using (System.Net.Sockets.TcpClient client = fake.AcceptTcpClient())
                                        using (System.Net.Sockets.NetworkStream stream = client.GetStream())
                                        {
                                            MemoryStream got = new MemoryStream();
                                            byte[] buffer = new byte[4096];
                                            string text = "";
                                            while (true)
                                            {
                                                int read = stream.Read(buffer, 0, buffer.Length);
                                                if (read <= 0) break;
                                                got.Write(buffer, 0, read);
                                                text = System.Text.Encoding.UTF8.GetString(got.ToArray());
                                                int end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                                                if (end < 0) continue;
                                                System.Text.RegularExpressions.Match length = System.Text.RegularExpressions.Regex.Match(text, @"Content-Length:\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                                if (got.Length >= end + 4 + (length.Success ? int.Parse(length.Groups[1].Value) : 0)) break;
                                            }
                                            lock (asked) asked.Add(text);
                                            byte[] head = System.Text.Encoding.ASCII.GetBytes(fakeHeads[i] + "\r\nContent-Length: " + fakeBodies[i].Length + "\r\nConnection: close\r\n\r\n");
                                            stream.Write(head, 0, head.Length);
                                            stream.Write(fakeBodies[i], 0, fakeBodies[i].Length);
                                        }
                                }
                                catch (Exception) { }
                            });
                            string realHost = Voice.Host;
                            Voice.Host = "http://127.0.0.1:" + ((IPEndPoint)fake.LocalEndpoint).Port;
                            byte[] fetched;
                            try
                            {
                                Voice probe = new Voice();
                                probe.Key = "sk_test_capri_1234";
                                probe.VoiceId = "abc";
                                probe.Model = "eleven_v3";
                                try { probe.Speak("Hi \"choom\"."); throw new Exception("A 401 reply did not throw."); }
                                catch (VoiceException error) { if (error.Status != 401 || error.Message != "ElevenLabs rejected the key.") throw new Exception("HTTP errors read wrong: " + error.Status + " " + error.Message); }
                                fetched = probe.Speak("Hi \"choom\".");
                            }
                            finally
                            {
                                Voice.Host = realHost;
                                fake.Stop();
                            }
                            string sent;
                            lock (asked) sent = string.Join("\n", asked.ToArray());
                            if (fetched.Length != tone.Length || sent.IndexOf("POST /v1/text-to-speech/abc?output_format=wav_24000", StringComparison.Ordinal) < 0 || sent.IndexOf("xi-api-key: sk_test_capri_1234", StringComparison.OrdinalIgnoreCase) < 0 || sent.IndexOf("{\"text\":\"Hi \\\"choom\\\".\",\"model_id\":\"eleven_v3\"}", StringComparison.Ordinal) < 0) throw new Exception("ElevenLabs request was wrong: " + sent);
                            VoicePage page = Voice.Read<VoicePage>(System.Text.Encoding.UTF8.GetBytes("{\"voices\":[{\"voice_id\":\"abc\",\"name\":\"Capri 1\",\"category\":\"generated\",\"labels\":{}},{\"voice_id\":\"def\",\"name\":\"Rachel\",\"category\":\"premade\"}],\"has_more\":false,\"total_count\":2,\"next_page_token\":null}"));
                            System.Collections.Generic.List<VoiceModel> listed = Voice.Read<System.Collections.Generic.List<VoiceModel>>(System.Text.Encoding.UTF8.GetBytes("[{\"model_id\":\"eleven_v3\",\"name\":\"Eleven v3\",\"can_do_text_to_speech\":true,\"languages\":[]}]"));
                            if (page.Voices.Count != 2 || page.More || page.Voices[0].Name != "Capri 1" || listed.Count != 1 || !listed[0].Speaks) throw new Exception("ElevenLabs listing did not parse.");
                            voices = new VoiceListing();
                            voices.Voices.AddRange(page.Voices);
                            voices.Models.AddRange(listed);
                            voice.Key = "sk_test_capri_1234";
                            ChooseVoice("abc");
                            ChooseVoiceModel("eleven_v3");
                            if (preferences.VoiceId != "abc" || preferences.VoiceModel != "eleven_v3" || mainframe.VoiceStatus != "ElevenLabs · Capri 1 | abc | v3 | Forget") throw new Exception("Voice picks did not show: " + mainframe.VoiceStatus);
                            snap("mainframe-voice.png");                            ChooseVoice("  ");
                            ChooseVoiceModel(null);
                            if (voice.VoiceId != Voice.Default || preferences.VoiceId != null || voice.Model != null || mainframe.VoiceStatus != "ElevenLabs · default voice | " + Voice.Default + " | Default model | Forget") throw new Exception("Blank voice did not fall back to the default: " + mainframe.VoiceStatus);
                            preferences.VoiceKey = sealedKey;
                            ForgetVoice();
                            if (voice.Key != null || voices != null || preferences.VoiceKey != null || mainframe.VoiceStatus != "Needs your ElevenLabs API key. | " + Voice.Default + " | Default model | Connect") throw new Exception("Forget did not clear the key: " + mainframe.VoiceStatus);
                            snap("mainframe-voice-empty.png");
                            checks.Add("PASS voice: key sealed with DPAPI, tags stripped, errors read, real HTTP round trips against a fake ElevenLabs (401 explained, audio fetched), WAV cleanup, mouth from the audio, listing parse, voice and model picks, default voice, forget");
                            mainframe.Close();
                            if (mainframe.IsDisposed || !mainframe.Visible || mainframe.WindowState != FormWindowState.Minimized) throw new Exception("Closing the Mainframe quit instead of minimizing.");
                            checks.Add("PASS Mainframe starts in the taskbar, a second launch opens it, it picks a model, and its close button minimizes it");
                            Think = delegate(string words) { thoughts++; return words == "hi" ? "Hey choom! Net's been quiet, too quiet. Time to stir up some trouble." : "Huh?"; };
                            pillUntil = clock.Elapsed.TotalSeconds + 30;
                            UpdatePill(clock.Elapsed.TotalSeconds);
                            if (!pill.Visible || pill.Top < Bottom - 60 || Math.Abs(pill.Left + pill.Width / 2 - (Left + Width / 2)) > Width / 2) throw new Exception("Talk pill is not under her.");
                            pill.Press(1);
                            if (!typer.Visible || typer.Bottom > Top + PaddingSize) throw new Exception("Pencil did not open the typing box above her.");
                            typer.Send("hi");
                            if (!typer.Visible) throw new Exception("Sending closed the typing box.");
                            if (typer.Draft.Length > 0 || talkState != TalkState.Thinking || heard != "hi") throw new Exception("Sending did not start thinking: " + talkState + " draft '" + typer.Draft + "'");
                            RenderFrame();
                            if (!box.Visible || box.State != TalkState.Thinking) throw new Exception("Speech box did not open on THINKING.");
                            typer.Hide();
                            checks.Add("PASS pill under her, pencil opens the typing box above her head, Enter sends, THINKING shows and the typing box stays open");
                            backdrop = new Form();
                            backdrop.Text = "Capri clean test desktop";
                            backdrop.FormBorderStyle = FormBorderStyle.None;
                            backdrop.ShowInTaskbar = false;
                            backdrop.StartPosition = FormStartPosition.Manual;
                            Rectangle area = Screen.PrimaryScreen.WorkingArea;
                            backdrop.Bounds = new Rectangle(area.Left + Math.Max(0, (area.Width - 920) / 2), area.Top + Math.Max(0, (area.Height - 560) / 2), Math.Min(920, area.Width), Math.Min(560, area.Height));
                            backdrop.BackColor = Color.FromArgb(237, 239, 242);
                            backdrop.TopMost = true;
                            backdrop.Paint += delegate(object sender, PaintEventArgs paint)
                            {
                                using (Pen line = new Pen(Color.FromArgb(225, 228, 232)))
                                    for (int x = 32; x < backdrop.Width; x += 32) for (int y = 32; y < backdrop.Height; y += 32) paint.Graphics.DrawEllipse(line, x, y, 1, 1);
                                using (Font label = new Font("Segoe UI", 10))
                                using (Brush ink = new SolidBrush(Color.FromArgb(110, 116, 125)))
                                    paint.Graphics.DrawString("CAPRI  /  WINDOWS DESKTOP COMPANION", label, ink, 32, 28);
                                using (Brush bar = new SolidBrush(Color.FromArgb(249, 250, 251))) paint.Graphics.FillRectangle(bar, 0, backdrop.Height - 42, backdrop.Width, 42);
                                using (Font label = new Font("Segoe UI", 9))
                                using (Brush ink = new SolidBrush(Color.FromArgb(126, 131, 139)))
                                    paint.Graphics.DrawString("Clean test desktop · actual native app", label, ink, 24, backdrop.Height - 29);
                            };
                            backdrop.Show();
                            anchor = new Point(backdrop.Left + backdrop.Width / 2 - Width / 2 + 150, backdrop.Top + backdrop.Height / 2 - Height / 2 + 20);
                            Location = anchor;
                            lastForeground = new IntPtr(-1);
                            StayOnTop();
                            bool above = false;
                            for (IntPtr below = Native.GetWindow(Handle, 2); below != IntPtr.Zero; below = Native.GetWindow(below, 2)) if (below == backdrop.Handle) { above = true; break; }
                            if (!above) throw new Exception("Did not rise above a newer topmost window.");
                            checks.Add("PASS rises back above a newer always-on-top window");
                            RenderFrame();
                        }
                        else if (step == 2)
                        {
                            const string expected = "Hey choom! Net's been quiet, too quiet. Time to stir up some trouble.";
                            if (talkState != TalkState.Talking || reply != expected || speech != expected) throw new Exception("Brain reply did not start talking: " + talkState + " " + reply);
                            replyStart = clock.Elapsed.TotalSeconds - 0.9;
                            speechStart = replyStart;
                            RenderFrame();
                            if (box.State != TalkState.Talking || box.Revealed < 20 || box.Revealed >= expected.Length) throw new Exception("Typewriter did not reveal part of the reply.");
                            using (Bitmap typing = new Bitmap(box.Image)) typing.Save(Path.Combine(output, "talk-typing.png"), ImageFormat.Png);
                            replyStart -= 10;
                            speechStart = replyStart;
                            RenderFrame();
                            if (talkState != TalkState.Online || speech != null || box.State != TalkState.Online) throw new Exception("Did not finish talking into ONLINE.");
                            using (Bitmap sheet = new Bitmap(Math.Max(box.Image.Width, pill.Image.Width) + 20, box.Image.Height + pill.Image.Height + 30, PixelFormat.Format32bppArgb))
                            using (Graphics graphics = Graphics.FromImage(sheet))
                            {
                                graphics.Clear(Color.FromArgb(44, 47, 56));
                                graphics.DrawImage(box.Image, 10, 10, box.Image.Width, box.Image.Height);
                                graphics.DrawImage(pill.Image, 10 + (box.Image.Width - pill.Image.Width) / 2, box.Image.Height + 20, pill.Image.Width, pill.Image.Height);
                                sheet.Save(Path.Combine(output, "talk.png"), ImageFormat.Png);
                            }
                            box.Leaving = clock.Elapsed.TotalSeconds - 1;
                            RenderFrame();
                            if (box.Visible || talkState != TalkState.Off) throw new Exception("Dismissing did not close the speech box.");
                            Hear("send nudes");
                            string refused = reply;
                            Hear("i want to kill myself");
                            if (thoughts != 1 || refused == null || refused == expected || talkState != TalkState.Talking || reply != Safety.HelpLine) throw new Exception("Safety lines reached the brain.");
                            Think = null;
                            brain.Model = null;
                            Hear("hello");
                            if (talkState != TalkState.Dormant || reply != Brain.Offline) throw new Exception("Missing brain did not go DORMANT.");
                            replyStart -= 10;
                            speechStart = replyStart;
                            RenderFrame();
                            box.Opened = clock.Elapsed.TotalSeconds - 1;
                            RenderFrame();
                            pill.Press(1);
                            typer.Draft = "nice, what are you up to?";
                            typer.Refresh();
                            if (!typer.Visible || !box.Visible || typer.Bottom > box.PanelOnScreen.Top || typer.Left != box.PanelOnScreen.Left) throw new Exception("Typing box did not sit above her message.");
                            using (Bitmap pair = new Bitmap(box.Width + 20, box.Bottom - typer.Top + 20, PixelFormat.Format32bppArgb))
                            using (Graphics graphics = Graphics.FromImage(pair))
                            {
                                graphics.Clear(Color.FromArgb(44, 47, 56));
                                try { graphics.CopyFromScreen(typer.Left, typer.Top, 10 + typer.Left - box.Left, 10, typer.Size); } catch { }
                                graphics.DrawImage(box.Image, 10, 10 + box.Top - typer.Top, box.Image.Width, box.Image.Height);
                                pair.Save(Path.Combine(output, "reply-above.png"), ImageFormat.Png);
                            }
                            typer.Hide();
                            checks.Add("PASS talk loop: TALKING types out with her mouth, ONLINE, dismiss closes; refusal and help line skip the brain; no brain shows DORMANT; reply box sits above her message");
                            Speak = delegate(string words) { voiced = words; return tone; };
                            heard = "voice check";
                            Answer(++asking, "[GLITCH] Voice link online. Miss me, choom?", TalkState.Talking);
                            if (talkState != TalkState.Thinking || reply != null) throw new Exception("Voice did not hold THINKING while she found her words.");
                        }
                        else if (step == 3)
                        {
                            const string line = "[GLITCH] Voice link online. Miss me, choom?";
                            if (voiced != "Voice link online. Miss me, choom?") throw new Exception("Voice was sent the wrong words: " + voiced);
                            if (talkState != TalkState.Talking || reply != line || mouths == null || mouths.Length != 12 || Math.Abs(pace - line.Length / Voice.Seconds(tone)) > 0.01) throw new Exception("Voice reply did not start in time with the audio: " + talkState + " " + pace);
                            double now = clock.Elapsed.TotalSeconds;
                            speechStart = now - 0.1;
                            if (MouthCell(now, idle) != -1) throw new Exception("Mouth moved during silence.");
                            speechStart = now - 0.75;
                            if (MouthCell(now, idle) < 0) throw new Exception("Mouth stayed shut while she spoke.");
                            replyStart = now - 0.5;
                            RenderFrame();
                            if (box.Revealed < line.Length / 2 - 2 || box.Revealed > line.Length / 2 + 2) throw new Exception("Typing did not keep pace with the audio: " + box.Revealed);
                            speechStart = now - 1.1;
                            if (MouthCell(now, idle) != -1 || speech != null) throw new Exception("Mouth kept going after the audio ended.");
                            Speak = delegate(string words) { throw new VoiceException("Out of ElevenLabs credits."); };
                            Answer(++asking, "Still here.", TalkState.Talking);
                        }
                        else if (step == 4)
                        {
                            if ((talkState != TalkState.Talking && talkState != TalkState.Online) || reply != "Still here." || mouths != null || pace != LettersPerSecond || mainframe.VoiceStatus.IndexOf("Out of ElevenLabs credits.", StringComparison.Ordinal) != 0) throw new Exception("Voice failure did not fall back to text: " + mainframe.VoiceStatus);
                            checks.Add("PASS voice reply: THINKING while it speaks, tags not read aloud, typing paced to the audio, mouth follows the audio, failure falls back to text and shows in the Mainframe");
                            Speak = null;
                            CloseTalk();
                            pill.Press(2);
                            if (pill.Chatting || chatting || reply != "Plug my voice in through the Mainframe first, choom.") throw new Exception("Wave without a key did not point to the Mainframe.");
                            mainframe.WindowState = FormWindowState.Minimized;
                            if (TalkPill.Render(1, 0).Width != TalkPill.Measure(1).Width) throw new Exception("Mic button showed outside voice chat.");
                            System.Net.Sockets.TcpListener free = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
                            free.Start();
                            int port = ((IPEndPoint)free.LocalEndpoint).Port;
                            free.Stop();
                            fakeAgent = new HttpListener();
                            fakeAgent.Prefixes.Add("http://localhost:" + port + "/");
                            fakeAgent.Start();
                            short[] line = new short[24000];
                            for (int i = 0; i < line.Length; i++) line[i] = (short)(Math.Sin(i * 0.25) * 9000);
                            byte[] lineBytes = new byte[line.Length * 2];
                            Buffer.BlockCopy(line, 0, lineBytes, 0, lineBytes.Length);
                            string lineAudio = Convert.ToBase64String(lineBytes);
                            HttpListener server = fakeAgent;
                            ThreadPool.QueueUserWorkItem(delegate
                            {
                                try
                                {
                                    Action<HttpListenerContext, string> answer = delegate(HttpListenerContext context, string json)
                                    {
                                        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
                                        context.Response.ContentType = "application/json";
                                        context.Response.ContentLength64 = bytes.Length;
                                        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                                        context.Response.Close();
                                    };
                                    System.Net.WebSockets.WebSocket socket = null;
                                    Func<string> receive = delegate
                                    {
                                        byte[] buffer = new byte[65536];
                                        using (MemoryStream message = new MemoryStream())
                                            while (true)
                                            {
                                                System.Net.WebSockets.WebSocketReceiveResult got = socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).Result;
                                                if (got.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) return null;
                                                message.Write(buffer, 0, got.Count);
                                                if (got.EndOfMessage) return System.Text.Encoding.UTF8.GetString(message.ToArray());
                                            }
                                    };
                                    Action<string> send = delegate(string json) { socket.SendAsync(new ArraySegment<byte>(System.Text.Encoding.UTF8.GetBytes(json)), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None).Wait(); };
                                    Func<string, string> chunk = delegate(string text)
                                    {
                                        int at = text.IndexOf("\"user_audio_chunk\":\"", StringComparison.Ordinal);
                                        if (at < 0) return null;
                                        at += 20;
                                        byte[] audio = Convert.FromBase64String(text.Substring(at, text.IndexOf('"', at) - at));
                                        foreach (byte value in audio) if (value != 0) return "loud";
                                        return "silent";
                                    };
                                    for (int conversation = 1; ; conversation++)
                                    {
                                        HttpListenerContext talk;
                                        while (true)
                                        {
                                            HttpListenerContext request = server.GetContext();
                                            if (request.Request.IsWebSocketRequest) { talk = request; break; }
                                            string body;
                                            using (StreamReader reader = new StreamReader(request.Request.InputStream)) body = reader.ReadToEnd();
                                            string path = request.Request.Url.PathAndQuery;
                                            lock (asked) asked.Add("http " + request.Request.HttpMethod + " " + path + " " + request.Request.Headers["xi-api-key"] + " " + body);
                                            answer(request, path.IndexOf("get-signed-url", StringComparison.Ordinal) >= 0 ? "{\"signed_url\":\"ws://localhost:" + port + "/talk?token=t\"}" : "{\"agent_id\":\"agent_test\"}");
                                        }
                                        socket = talk.AcceptWebSocketAsync(null).Result.WebSocket;
                                        lock (asked) asked.Add("init " + receive());
                                        send("{\"type\":\"conversation_initiation_metadata\",\"conversation_initiation_metadata_event\":{\"conversation_id\":\"conv_test\",\"agent_output_audio_format\":\"pcm_16000\",\"user_input_audio_format\":\"pcm_16000\"}}");
                                        if (conversation > 1)
                                        {
                                            int woke = 0;
                                            while (woke < 3)
                                            {
                                                string text = receive();
                                                if (text == null) break;
                                                string kind = chunk(text);
                                                if (kind == null) continue;
                                                lock (asked) asked.Add("woke " + kind);
                                                if (kind == "loud") woke++;
                                            }
                                            socket.CloseOutputAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait();
                                            continue;
                                        }
                                        send("{\"type\":\"ping\",\"ping_event\":{\"event_id\":7,\"ping_ms\":20}}");
                                        bool ponged = false;
                                        int chunks = 0;
                                        while (!ponged || chunks < 3)
                                        {
                                            string text = receive();
                                            if (text == null) return;
                                            if (text.IndexOf("\"pong\"", StringComparison.Ordinal) >= 0) { ponged = true; lock (asked) asked.Add("pong " + text); }
                                            string kind = chunk(text);
                                            if (kind != null) { chunks++; lock (asked) asked.Add("chunk " + kind); }
                                        }
                                        send("{\"type\":\"user_transcript\",\"user_transcription_event\":{\"user_transcript\":\"hi\"}}");
                                        send("{\"type\":\"agent_response\",\"agent_response_event\":{\"agent_response\":\"[GLITCH] Loud and clear, choom.\"}}");
                                        send("{\"type\":\"audio\",\"audio_event\":{\"audio_base_64\":\"" + lineAudio + "\",\"event_id\":1,\"alignment\":{\"chars\":[\"L\"],\"char_durations_ms\":[40],\"char_start_times_ms\":[0]}}}");
                                        try
                                        {
                                            while (true)
                                            {
                                                string text = receive();
                                                if (text == null) break;
                                                string kind = chunk(text);
                                                if (kind != null) lock (asked) asked.Add("late " + kind);
                                            }
                                        }
                                        catch (Exception) { }
                                    }
                                }
                                catch (Exception error) { lock (asked) asked.Add("server " + error.GetType().Name); }
                            });
                            agentHost = Voice.Host;
                            Voice.Host = "http://localhost:" + port;
                            voice.Key = "sk_test_capri_1234";
                            if (Agent.Create(voice.Key, Voice.Default) != "agent_test") throw new Exception("ElevenLabs agent was not made.");
                            preferences.AgentId = "agent_old";
                            preferences.AgentVersion = 1;
                            CloseTalk();
                            pill.Press(2);
                            if (!pill.Chatting || pill.Muted || !chatting || TalkPill.Render(1, 0, true).Width <= TalkPill.Measure(1).Width + 30) throw new Exception("Wave did not start voice chat with the mic button apart on the right.");
                            Bitmap[] chips = { TalkPill.Render(2, 0, true, false, 2), TalkPill.Render(2, 3, true, false, 6), TalkPill.Render(2, 0, true, true), TalkPill.Render(2, 0) };
                            using (Bitmap sheet = new Bitmap(chips[0].Width + 40, chips.Length * (chips[0].Height + 16) + 16, PixelFormat.Format32bppArgb))
                            using (Graphics graphics = Graphics.FromImage(sheet))
                            {
                                graphics.Clear(Color.FromArgb(214, 220, 228));
                                for (int i = 0; i < chips.Length; i++) graphics.DrawImage(chips[i], 20, 16 + i * (chips[0].Height + 16), chips[i].Width, chips[i].Height);
                                sheet.Save(Path.Combine(output, "pill-mic.png"), ImageFormat.Png);
                            }
                            foreach (Bitmap chip in chips) chip.Dispose();
                        }
                        else if (step == 5)
                        {
                            string log;
                            lock (asked) log = string.Join(" | ", asked.ToArray());
                            if (agent == null || !agent.Ready || preferences.AgentId != "agent_old" || preferences.AgentVersion != Agent.Version || mainframe.VoiceStatus.IndexOf("Voice chat on", StringComparison.Ordinal) != 0) throw new Exception("Voice chat did not open the agent link: " + mainframe.VoiceStatus + " / " + log);
                            short[] loud = new short[Ears.Chunk];
                            for (int i = 0; i < loud.Length; i++) loud[i] = (short)(Math.Sin(i * 0.3) * 9000);
                            pill.Press(3);
                            if (!pill.Muted || !ears.Muted) throw new Exception("Mic button did not mute the mic.");
                            ears.Feed(loud);
                            pill.Press(3);
                            if (pill.Muted || ears.Muted) throw new Exception("Mic button did not unmute the mic.");
                            ears.Feed(loud);
                            ears.Feed(loud);
                        }
                        else if (step == 6)
                        {
                            string log;
                            lock (asked) log = string.Join(" | ", asked.ToArray());
                            if (heard != "hi" || reply != "Loud and clear, choom." || (talkState != TalkState.Talking && talkState != TalkState.Online) || !speaker.Busy) throw new Exception("Voice chat did not hear and answer: " + heard + " / " + reply + " / " + talkState + " / " + log);
                            Play(idle);
                            if (MouthCell(clock.Elapsed.TotalSeconds, idle) < 0) throw new Exception("Her mouth did not follow the agent's audio.");
                            string[] wanted = { "http POST /v1/convai/agents/create sk_test_capri_1234 {\"name\":\"Capri\"", "\"turn\":{\"turn_timeout\":15,\"silence_end_call_timeout\":45}", "http PATCH /v1/convai/agents/agent_old sk_test_capri_1234 {\"conversation_config\":{\"turn\":{\"turn_timeout\":15,\"silence_end_call_timeout\":45}}}", "check in once", "Don\u0027t beg them to talk", "\"prompt\":{\"prompt\":\"You are Capri", "\"llm\":\"gemini-2.5-flash\"", "\"tts\":{\"voice_id\":\"" + Voice.Default + "\"}", "\"overrides\":{\"conversation_config_override\":{\"agent\":{\"prompt\":{\"prompt\":true}},\"tts\":{\"voice_id\":true}}}", "http GET /v1/convai/conversation/get-signed-url?agent_id=agent_old sk_test_capri_1234", "init {\"type\":\"conversation_initiation_client_data\",\"conversation_config_override\":{\"agent\":{\"prompt\":{\"prompt\":\"You are Capri", "pong {\"type\":\"pong\",\"event_id\":7}", "chunk silent | chunk loud | chunk loud" };
                            foreach (string piece in wanted) if (log.IndexOf(piece, StringComparison.Ordinal) < 0) throw new Exception("Agent traffic was missing " + piece + ": " + log);
                            short[] loud = new short[Ears.Chunk];
                            for (int i = 0; i < loud.Length; i++) loud[i] = (short)(Math.Sin(i * 0.3) * 9000);
                            ears.Feed(loud);
                        }
                        else if (step == 7)
                        {
                            string log;
                            lock (asked) log = string.Join(" | ", asked.ToArray());
                            if (log.IndexOf("late silent", StringComparison.Ordinal) < 0 || log.IndexOf("late loud", StringComparison.Ordinal) >= 0) throw new Exception("Mic was not silenced while she talked: " + log);
                            ChatHeard("send nudes");
                            if (!dropping || reply == null || reply == "Loud and clear, choom.") throw new Exception("Voice chat let a blocked line through.");
                            ChatReplied("Sure thing.");
                            if (reply == "Sure thing.") throw new Exception("The agent's reply to a blocked line got through.");
                            ChatHeard("hi again");
                            ChatReplied("well shit, choom");
                            if (!dropping || reply == "well shit, choom") throw new Exception("A rude agent reply got through.");
                            ChatReplied("Still there, choom?");
                            if (!checkedIn) throw new Exception("A line she said unprompted was not taken as a check-in.");
                            speaker.Clear();
                            double quiet = clock.Elapsed.TotalSeconds;
                            CheckChat(quiet);
                            CheckChat(quiet + 0.6);
                            if (agent == null) throw new Exception("She hung up before her check-in had time to land.");
                            CheckChat(quiet + 1.3);
                            if (agent != null || !chatting || !resting || !pill.Chatting || mainframe.VoiceStatus.IndexOf("Voice chat resting", StringComparison.Ordinal) != 0) throw new Exception("She did not hang up and rest after one check-in: " + mainframe.VoiceStatus);
                            short[] speech = new short[Ears.Chunk];
                            for (int i = 0; i < speech.Length; i++) speech[i] = (short)(Math.Sin(i * 0.3) * 9000);
                            ears.Feed(new short[Ears.Chunk]);
                            ears.Feed(speech);
                            if (!resting) throw new Exception("One loud blip woke her.");
                            ears.Feed(speech);
                            if (resting) throw new Exception("Talking did not wake her.");
                            ears.Feed(speech);
                        }
                        else if (step == 8)
                        {
                        }
                        else if (step == 9)
                        {
                            string log;
                            lock (asked) log = string.Join(" | ", asked.ToArray());
                            if (System.Text.RegularExpressions.Regex.Matches(log, "get-signed-url").Count < 2 || System.Text.RegularExpressions.Regex.Matches(log, "init \\{").Count < 2 || log.IndexOf("woke silent | woke loud | woke loud | woke loud", StringComparison.Ordinal) < 0) throw new Exception("Talking did not reopen the link and replay what she missed: " + log);
                            if (agent != null || !chatting || !resting || mainframe.VoiceStatus.IndexOf("Voice chat resting", StringComparison.Ordinal) != 0) throw new Exception("A clean hang-up from ElevenLabs did not leave her resting: " + mainframe.VoiceStatus + " / " + log);
                            pill.Press(2);
                            if (pill.Chatting || chatting || agent != null || ears.Listening || speaker.Busy || resting) throw new Exception("Wave did not end voice chat.");
                            Voice.Host = agentHost;
                            fakeAgent.Stop();
                            voice.Key = null;
                            ShowVoice();
                            checks.Add("PASS voice chat: makes an ElevenLabs agent with her persona and voice that checks in once after 15 s of quiet, patches an older agent once, opens a signed live link, answers pings, streams the mic, mic button sits apart on the right only while it's on and mutes the mic, her mouth follows the agent's audio, the mic goes quiet while she talks, blocked lines and rude replies are caught, one check-in then she hangs up and rests, talking wakes her and replays the first words, a clean hang-up from ElevenLabs rests too, wave ends it");
                            Speak = null;
                            heard = "Hello Capri.";
                            Answer(++asking, "Hey choom! Net's been quiet, too quiet. Time to stir up some trouble.", TalkState.Talking);
                            replyStart -= 10;
                            speechStart = replyStart;
                            RenderFrame();
                        }
                        else if (step == 10)
                        {
                            using (Bitmap capture = new Bitmap(backdrop.Width, backdrop.Height))
                            using (Graphics graphics = Graphics.FromImage(capture))
                            {
                                try { graphics.CopyFromScreen(backdrop.Location, Point.Empty, backdrop.Size); } catch { }
                                capture.Save(Path.Combine(output, "desktop.png"), ImageFormat.Png);
                            }
                            checks.Add("PASS actual native app captured over app-owned neutral test backdrop");

                            string r1 = DesktopActions.TryHandle(brain, "Search mode");
                            string r2 = DesktopActions.TryHandle(brain, "I really want to stay at your house");
                            string yUrl = DesktopActions.LastOpenedUrl;
                            string r3 = DesktopActions.TryHandle(brain, "When was Astral Spiff born?");
                            string r4 = DesktopActions.TryHandle(brain, "relax");
                            string r5 = DesktopActions.TryHandle(brain, "Why did you open images of cats?");
                            string r6 = DesktopActions.TryHandle(brain, "No");
                            string r7 = DesktopActions.TryHandle(brain, "reset memory");
                            if (r1 != "Search mode. What are we looking for?" || r2 != "Searching YouTube for I really want to stay at your house." || yUrl.IndexOf("youtube.com", StringComparison.OrdinalIgnoreCase) < 0)
                                throw new Exception("Video 2 Search Mode failed: " + r1 + " / " + r2);
                            if (r3 == null || r3.IndexOf("I was born before him", StringComparison.Ordinal) < 0)
                                throw new Exception("Video 2 birthdate query failed: " + r3);
                            if (r4 == null || r4.IndexOf("Relax? You're telling me to relax?", StringComparison.Ordinal) < 0 || r5 == null || r5.IndexOf("Can I get some more eddies now", StringComparison.Ordinal) < 0 || r6 == null || r6.IndexOf("Scrapped it", StringComparison.Ordinal) < 0)
                                throw new Exception("Video 3 cat mischief flow failed: " + r4 + " / " + r5 + " / " + r6);
                            if (r7 == null || r7.IndexOf("start from scratch, choom", StringComparison.Ordinal) < 0)
                                throw new Exception("Video 3 memory reset failed: " + r7);
                            checks.Add("PASS Video 2 & Video 3 interactions: Search mode YouTube, Google birthdate search, autonomous cat mischief, eddies banter, and memory reset");

                            brain.Model = null;
                            brain.AllowReflexFallback = true;
                            string reflex = brain.Ask("Who are you?");
                            if (string.IsNullOrEmpty(reflex) || reflex.IndexOf("Capri", StringComparison.OrdinalIgnoreCase) < 0)
                                throw new Exception("Offline Reflex Brain failed: " + reflex);
                            checks.Add("PASS Offline Zero-RAM Reflex Brain fallback works without any LLM loaded");

                            byte[] offlineWav = voice.SpeakOffline("Preem, choom!");
                            if (offlineWav == null || Voice.Seconds(offlineWav) < 0.25 || Voice.Mouths(offlineWav, MouthRate).Length == 0)
                                throw new Exception("Offline Biochip Voice synthesis or lip-sync failed.");
                            checks.Add("PASS Offline Biochip Voice synthesis (24kHz WAV + Cyberpunk DSP pitch/chorus + 5-frame mouth lip-sync)");

                            ToggleHoloFrame();
                            if (!preferences.HoloFrame || frame == null) throw new Exception("Holo-Frame mode did not render.");
                            frame.Save(Path.Combine(output, "holo-frame.png"), ImageFormat.Png);
                            CloseTalk();
                            SetBoxMode(2);
                            if (preferences.BoxMode != 2 || frame == null) throw new Exception("Eye-Cut Banner Box mode did not render.");
                            frame.Save(Path.Combine(output, "eye-cut-box.png"), ImageFormat.Png);
                            SetBoxMode(3);
                            if (preferences.BoxMode != 3 || frame == null) throw new Exception("Project Capricorn Hi Box mode did not render.");
                            frame.Save(Path.Combine(output, "hi-box.png"), ImageFormat.Png);
                            ToggleDormantEye();
                            if (!preferences.DormantEye || frame == null) throw new Exception("Dormant Eye mode did not render.");
                            frame.Save(Path.Combine(output, "dormant-eye.png"), ImageFormat.Png);
                            checks.Add("PASS Cyberpunk Holo-Frame, Edgerunners Eye-Cut Box, Project Capricorn Hi Box, and Dormant Cyber-Eye modes rendered");

                            checks.Add("LIMIT handler-driven integration test; physical mouse input and tray UI require a manual check");
                            checks.Add("PASS quit menu requested application exit");
                            File.WriteAllLines(Path.Combine(output, "smoke-test.txt"), checks);
                            test.Stop();
                            test.Dispose();
                            backdrop.Close();
                            ((ToolStripMenuItem)menu.Items[menu.Items.Count - 1]).PerformClick();
                        }
                        step++;
                    }
                    catch (Exception error)
                    {
                        TestFailed = true;
                        File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
                        test.Stop();
                        test.Dispose();
                        if (backdrop != null) backdrop.Close();
                        Close();
                    }
                };
                test.Start();
            };
        }
    }

    internal sealed class TakeoverOverlay : Form
    {
        private readonly Companion companion;
        private readonly Clip clip;
        private readonly System.Windows.Forms.Timer timer;
        private readonly Stopwatch clock;
        private readonly Rectangle screenBounds;
        private Bitmap surface;
        private const double Duration = 2.8;

        internal TakeoverOverlay(Companion owner, Clip takeoverClip)
        {
            companion = owner;
            clip = takeoverClip;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            screenBounds = Screen.FromPoint(Cursor.Position).Bounds;
            Bounds = screenBounds;
            clock = Stopwatch.StartNew();
            timer = new System.Windows.Forms.Timer { Interval = 33 };
            timer.Tick += OnTick;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x8 | 0x00080000;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            timer.Start();
        }

        private void OnTick(object sender, EventArgs e)
        {
            double elapsed = clock.Elapsed.TotalSeconds;
            if (elapsed >= Duration)
            {
                timer.Stop();
                Close();
                Dispose();
                companion.Teleport();
                return;
            }

            int frameIdx = (int)(elapsed * 10.0);
            if (frameIdx >= clip.Count) frameIdx = clip.Count - 1;

            if (surface == null || surface.Width != screenBounds.Width || surface.Height != screenBounds.Height)
            {
                if (surface != null) surface.Dispose();
                surface = new Bitmap(screenBounds.Width, screenBounds.Height, PixelFormat.Format32bppPArgb);
            }

            using (Graphics g = Graphics.FromImage(surface))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;

                int frameW = clip.Strip.Width / clip.Count;
                int frameH = clip.Strip.Height;
                Rectangle src = new Rectangle(frameIdx * frameW, 0, frameW, frameH);

                float scale = Math.Max(screenBounds.Width / (float)frameW, (screenBounds.Height * 0.9f) / (float)frameH);
                float drawW = frameW * scale;
                float drawH = frameH * scale;
                float dx = (screenBounds.Width - drawW) / 2f;
                float dy = screenBounds.Height - drawH;

                g.DrawImage(clip.Strip, new RectangleF(dx, dy, drawW, drawH), src, GraphicsUnit.Pixel);
            }
            Native.DrawLayered(Handle, surface, screenBounds.Location);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                if (surface != null) surface.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class Shelf : Form
    {
        private readonly int extra;

        internal Shelf(int extended = 0)
        {
            extra = extended;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(40, 44, 52);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= 0x08000000 | 0x8 | extra;
                return value;
            }
        }
    }

    internal sealed class CapriMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(26, 27, 31); } }
        public override Color ImageMarginGradientBegin { get { return ToolStripDropDownBackground; } }
        public override Color ImageMarginGradientEnd { get { return ToolStripDropDownBackground; } }
        public override Color MenuItemSelected { get { return Color.FromArgb(65, 69, 53); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(167, 245, 88); } }
        public override Color CheckBackground { get { return Color.FromArgb(65, 69, 53); } }
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; internal Point(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] internal struct Size { internal int Width, Height; internal Size(int width, int height) { Width = width; Height = height; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] internal struct Blend { internal byte Operation, Flags, Alpha, Format; }
        [StructLayout(LayoutKind.Sequential)] private struct InputInfo { internal int Size; internal uint Time; }
        [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref InputInfo info);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr window, int command);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, int flags);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] internal static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr window, int id, int modifiers, int key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")] internal static extern int GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool AllowSetForegroundWindow(int process);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string lParam);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen, ref Point destination, ref Size size, IntPtr source, ref Point origin, int colorKey, ref Blend blend, int flags);

        internal static long IdleMilliseconds()
        {
            InputInfo info = new InputInfo { Size = Marshal.SizeOf(typeof(InputInfo)) };
            return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.Time) : 0;
        }

        internal static void DrawLayered(IntPtr window, Bitmap bitmap, System.Drawing.Point location)
        {
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr memory = CreateCompatibleDC(screen);
            IntPtr nativeBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            IntPtr previous = SelectObject(memory, nativeBitmap);
            try
            {
                Point destination = new Point(location.X, location.Y);
                Point origin = new Point(0, 0);
                Size size = new Size(bitmap.Width, bitmap.Height);
                Blend blend = new Blend { Operation = 0, Flags = 0, Alpha = 255, Format = 1 };
                if (!UpdateLayeredWindow(window, screen, ref destination, ref size, memory, ref origin, 0, ref blend, 2)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                SelectObject(memory, previous);
                DeleteObject(nativeBitmap);
                DeleteDC(memory);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }
}
