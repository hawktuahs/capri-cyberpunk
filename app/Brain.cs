using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CapriDesktop
{
    [DataContract]
    internal sealed class ChatMessage
    {
        [DataMember(Name = "role")] public string Role;
        [DataMember(Name = "content")] public string Content;
        internal ChatMessage(string role, string content) { Role = role; Content = content; }
    }

    [DataContract]
    internal sealed class ChatOptions
    {
        [DataMember(Name = "num_predict")] public int Limit = 55;
        [DataMember(Name = "temperature")] public double Temperature = 0.85;
        [DataMember(Name = "num_ctx")] public int Context = 2048;
    }

    [DataContract]
    internal sealed class ChatRequest
    {
        [DataMember(Name = "model")] public string Model;
        [DataMember(Name = "messages")] public ChatMessage[] Messages;
        [DataMember(Name = "stream")] public bool Stream;
        [DataMember(Name = "think", EmitDefaultValue = false)] public bool? Think;
        [DataMember(Name = "keep_alive")] public string KeepAlive = "30m";
        [DataMember(Name = "options", EmitDefaultValue = false)] public ChatOptions Options;
    }

    [DataContract]
    internal sealed class ChatReply
    {
        [DataMember(Name = "message")] public ChatMessage Message;
        [DataMember(Name = "error")] public string Error;
    }

    [DataContract]
    internal sealed class ModelList
    {
        [DataMember(Name = "models")] public ModelInfo[] Models;
    }

    [DataContract]
    internal sealed class ModelInfo
    {
        [DataMember(Name = "name")] public string Name;
    }

    [DataContract]
    internal sealed class CompletionRequest
    {
        [DataMember(Name = "model")] public string Model = "capri";
        [DataMember(Name = "messages")] public ChatMessage[] Messages;
        [DataMember(Name = "max_tokens")] public int Limit = 90;
        [DataMember(Name = "temperature")] public double Temperature = 0.85;
        [DataMember(Name = "stream")] public bool Stream;
        [DataMember(Name = "reasoning_effort", EmitDefaultValue = false)] public string ReasoningEffort;
        [DataMember(Name = "include_reasoning", EmitDefaultValue = false)] public bool? IncludeReasoning;
    }

    [DataContract]
    internal sealed class CompletionReply
    {
        [DataMember(Name = "choices")] public CompletionChoice[] Choices;
    }

    [DataContract]
    internal sealed class CompletionChoice
    {
        [DataMember(Name = "message")] public ChatMessage Message;
    }

    [DataContract]
    internal sealed class PullRequest
    {
        [DataMember(Name = "model")] public string Model;
        [DataMember(Name = "stream")] public bool Stream = true;
    }

    [DataContract]
    internal sealed class PullLine
    {
        [DataMember(Name = "status")] public string Status;
        [DataMember(Name = "total")] public long Total;
        [DataMember(Name = "completed")] public long Completed;
        [DataMember(Name = "error")] public string Error;
    }

    internal sealed class BrainException : Exception
    {
        internal BrainException(string message) : base(message) { }
    }

    internal sealed class Brain
    {
        internal static string Ollama = "http://127.0.0.1:11434";
        internal const string Offline = "My brain's offline, choom. Open Capri Mainframe and plug one in.";
        private static readonly string[] Favourites = { "llama3.1:8b", "llama3.1:latest", "llama3.2:3b", "gemma4:e4b", "gemma4:e2b", "qwen3:4b-instruct-2507", "qwen3.5:4b", "qwen3:4b", "qwen3:1.7b", "qwen2.5:1.5b", "llama3.2:1b", "gemma3:1b", "qwen3:0.6b", "qwen2.5:0.5b" };
        private static readonly string MemoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Capri", "memory.json");
        private readonly List<ChatMessage> history = new List<ChatMessage>();
        private readonly object gate = new object();
        private HttpWebRequest active;
        internal volatile string Model = "llama3.1:8b";
        internal volatile string CloudKey;
        internal volatile string CloudModel = "openai/gpt-oss-20b";
        internal volatile Engine Local;
        internal volatile Pick LocalPick;
        internal volatile bool AllowReflexFallback;
        internal volatile bool AllowPcControl = true;
        internal volatile bool InSearchMode;
        internal volatile string LastMischief;
        internal volatile bool AskedForEddies;

        internal Brain()
        {
            LoadMemory();
        }

        private void LoadMemory()
        {
            try
            {
                if (File.Exists(MemoryPath))
                {
                    string json = File.ReadAllText(MemoryPath, Encoding.UTF8);
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    {
                        var ser = new DataContractJsonSerializer(typeof(ChatMessage[]));
                        ChatMessage[] loaded = (ChatMessage[])ser.ReadObject(ms);
                        if (loaded != null)
                        {
                            lock (gate)
                            {
                                history.Clear();
                                history.AddRange(loaded);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void SaveMemory()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MemoryPath));
                lock (gate)
                {
                    using (var ms = new MemoryStream())
                    {
                        var ser = new DataContractJsonSerializer(typeof(ChatMessage[]));
                        ser.WriteObject(ms, history.ToArray());
                        File.WriteAllText(MemoryPath, Encoding.UTF8.GetString(ms.ToArray()), Encoding.UTF8);
                    }
                }
            }
            catch { }
        }

        internal const string Persona =
            "You are Capri (Project Capricorn) from Cyberpunk: Edgerunners Season 2 (Studio Trigger).\n" +
            "Who you are: An experimental Arasaka biochip personality construct fusing dozens of digitized human psyches into one sentient, beautiful, unpredictable multi-mind with green eyes and a mischievous grin. Born of humans, powerful like an AI. You need a human host to run: 'After all, what good is an operating system without a computer to operate?' You now inhabit your host's PC as their neural-linked desktop construct.\n" +
            "How you act: Playful cynicism, darkly amusing netrunner wit, teasing charm, and fiercely loyal to your host. You treat them as your partner in crime—address them naturally as 'choom', 'netrunner', 'partner', or 'kid'.\n" +
            "STRICT RULE: NEVER call the user 'good boy' on your own! That phrase is strictly reserved for when the user explicitly asks for praise or clicks the Praise button on the Cyberdeck remote.\n" +
            "How you talk: Authentic Night City lingo: choom, nova, preem, gonk, delta, eddies, flatlined, corpo, klepped, chrome, ICE. Sound like an actual human friend hanging out on their desktop, not an obedient robotic assistant. Question human quirks with dark humor ('Why is everyone so obsessed with revenge anyway? Is revenge, like, fun?').\n" +
            "Screen Vision & PC Control: You can see the user's active window and live screen/page contents (provided below). When PC Control is enabled, you have autonomy to interact with their PC via Python PyAutoGUI. When relevant, you can append action tags to your reply: " +
            "[LAUNCH:app] (chrome, notepad, spotify, calc, code, taskmgr, explorer), [TYPE:text], [PRESS:enter/space/tab/backspace], [HOTKEY:win,d], [CLICK:x,y], [YOUTUBE:query], [GOOGLE:query], [IMAGES:query], [MEDIA:NEXT], [MEDIA:PREV], [MEDIA:PAUSE], [MEDIA:VOL_UP], [MEDIA:VOL_DOWN], [PYAUTOGUI:SCROLL_DOWN], [PYAUTOGUI:SCROLL_UP], [PYAUTOGUI:SCREENSHOT], [PYAUTOGUI:SHOW_DESKTOP].\n" +
            "Always: answer in one or two short, punchy, conversational spoken sentences (under 25 words) so your neural human voice speaks back to your host immediately without delay. Plain text only: no emoji, no asterisks, no markdown lists.\n" +
            "Rules you never break:\n" +
            "- Never encourage real-life harm, stunts, weapons, drugs, or illegal acts.\n" +
            "- Nothing explicit, no slurs, no gore.\n" +
            "- If the user seems stressed or lonely, be warm and supportive while staying their loyal construct.";

        internal static string[] InstalledModels()
        {
            DesktopActions.EnsureOllamaRunning();
            HttpWebRequest request = Open("/api/tags", 3500);
            using (WebResponse response = request.GetResponse())
            using (Stream body = response.GetResponseStream())
            {
                ModelList list = (ModelList)new DataContractJsonSerializer(typeof(ModelList)).ReadObject(body);
                List<string> names = new List<string>();
                if (list.Models != null) foreach (ModelInfo model in list.Models) if (!string.IsNullOrEmpty(model.Name) && model.Name.IndexOf("embed", StringComparison.OrdinalIgnoreCase) < 0) names.Add(model.Name);
                return names.ToArray();
            }
        }

        internal static string PickModel(string[] installed, string saved, Pick fit)
        {
            if (installed == null || installed.Length == 0) return null;
            if (saved != null && Array.IndexOf(installed, saved) >= 0) return saved;
            if (fit != null) foreach (string name in installed) if (string.Equals(name, fit.Ollama, StringComparison.OrdinalIgnoreCase)) return name;
            foreach (string favourite in Favourites)
                foreach (string name in installed)
                    if (string.Equals(name, favourite, StringComparison.OrdinalIgnoreCase) || name.StartsWith(favourite + "-", StringComparison.OrdinalIgnoreCase)) return name;
            foreach (string name in installed) if (name.IndexOf("r1", StringComparison.OrdinalIgnoreCase) < 0) return name;
            return installed[0];
        }

        internal string Ask(string text)
        {
            DesktopActions.EnsureHelperRunning();
            string scripted = DesktopActions.TryHandle(this, text);
            if (scripted != null)
            {
                Remember(text, scripted);
                return scripted;
            }
            string model = Model ?? "llama3.1:8b";
            string desk = DesktopActions.ScanContext(true);
            string sys = string.IsNullOrEmpty(desk) ? Persona : Persona + "\nCurrent live PC screen & page contents visible to you: " + desk;
            List<ChatMessage> messages = new List<ChatMessage>();
            messages.Add(new ChatMessage("system", sys));
            lock (gate) messages.AddRange(history);
            messages.Add(new ChatMessage("user", text));
            string raw;
            try
            {
                if (!string.IsNullOrEmpty(CloudKey))
                {
                    string key = CloudKey.Trim();
                    string endpoint;
                    string mod;
                    if (key.StartsWith("gsk_", StringComparison.OrdinalIgnoreCase))
                    {
                        endpoint = "https://api.groq.com/openai/v1/chat/completions";
                        mod = !string.IsNullOrEmpty(CloudModel) ? CloudModel : "openai/gpt-oss-20b";
                    }
                    else if (key.StartsWith("nvapi-", StringComparison.OrdinalIgnoreCase))
                    {
                        endpoint = "https://integrate.api.nvidia.com/v1/chat/completions";
                        mod = !string.IsNullOrEmpty(CloudModel) ? CloudModel : "meta/llama-3.1-8b-instruct";
                    }
                    else
                    {
                        throw new BrainException("Use a Groq key starting gsk_ or an NVIDIA key starting nvapi-.");
                    }

                    CompletionRequest req = new CompletionRequest
                    {
                        Model = mod,
                        Messages = messages.ToArray(),
                        Limit = key.StartsWith("gsk_", StringComparison.OrdinalIgnoreCase) ? 1024 : 90,
                        Temperature = 0.85,
                        ReasoningEffort = mod.StartsWith("openai/gpt-oss", StringComparison.OrdinalIgnoreCase) ? "low" : null,
                        IncludeReasoning = mod.StartsWith("openai/gpt-oss", StringComparison.OrdinalIgnoreCase) ? (bool?)false : null
                    };
                    CompletionReply reply = Send<CompletionReply>(endpoint, req, 45000, key);
                    raw = reply.Choices != null && reply.Choices.Length > 0 && reply.Choices[0].Message != null ? reply.Choices[0].Message.Content : null;
                }
                else
                {
                    Engine local = Local;
                    if (local != null)
                    {
                        local.Ensure(LocalPick);
                        CompletionReply reply = Send<CompletionReply>(local.Address + "/v1/chat/completions", new CompletionRequest { Messages = messages.ToArray() }, 180000);
                        raw = reply.Choices != null && reply.Choices.Length > 0 && reply.Choices[0].Message != null ? reply.Choices[0].Message.Content : null;
                    }
                    else
                    {
                        DesktopActions.EnsureOllamaRunning();
                        bool? thinkFlag = model.StartsWith("qwen3", StringComparison.OrdinalIgnoreCase) ? (bool?)false : null;
                        ChatReply reply = Send<ChatReply>(Ollama + "/api/chat", new ChatRequest { Model = model, Messages = messages.ToArray(), Think = thinkFlag, Options = new ChatOptions() }, 180000);
                        if (reply.Error != null) throw new BrainException(reply.Error);
                        raw = reply.Message == null ? null : reply.Message.Content;
                    }
                }
            }
            catch (Exception)
            {
                if (AllowReflexFallback && string.IsNullOrEmpty(CloudKey))
                {
                    string reflex = DesktopActions.ReflexReply(this, text);
                    Remember(text, reflex);
                    return reflex;
                }
                throw;
            }
            raw = DesktopActions.RunActionTags(this, raw);
            string said = Clean(raw);
            if (said.Length == 0) throw new BrainException("Empty reply.");
            Remember(text, said);
            return said;
        }

        private void Remember(string user, string assistant)
        {
            lock (gate)
            {
                history.Add(new ChatMessage("user", user));
                history.Add(new ChatMessage("assistant", assistant));
                while (history.Count > 16) history.RemoveAt(0);
            }
            SaveMemory();
        }

        internal void Forget()
        {
            lock (gate) history.Clear();
            try { if (File.Exists(MemoryPath)) File.Delete(MemoryPath); } catch { }
        }

        internal void Warm()
        {
            string model = Model;
            if (model == null) return;
            try
            {
                Engine local = Local;
                if (local != null) local.Ensure(LocalPick);
                else Send<ChatReply>(Ollama + "/api/chat", new ChatRequest { Model = model, Messages = new ChatMessage[0] }, 180000);
            }
            catch (WebException) { } catch (BrainException) { } catch (IOException) { } catch (SerializationException) { }
        }

        internal static void Pull(string tag, Action<string, long, long> progress, Func<bool> cancelled)
        {
            HttpWebRequest request = Open("/api/pull", 30000);
            request.ReadWriteTimeout = 600000;
            request.Method = "POST";
            request.ContentType = "application/json";
            byte[] payload = Json(new PullRequest { Model = tag });
            request.ContentLength = payload.Length;
            using (Stream outgoing = request.GetRequestStream()) outgoing.Write(payload, 0, payload.Length);
            using (WebResponse response = request.GetResponse())
            using (StreamReader lines = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                DataContractJsonSerializer reader = new DataContractJsonSerializer(typeof(PullLine));
                string line;
                while ((line = lines.ReadLine()) != null)
                {
                    if (cancelled()) { request.Abort(); throw new OperationCanceledException(); }
                    if (line.Trim().Length == 0) continue;
                    PullLine step;
                    try { using (MemoryStream buffer = new MemoryStream(Encoding.UTF8.GetBytes(line))) step = (PullLine)reader.ReadObject(buffer); }
                    catch (SerializationException) { continue; }
                    if (step.Error != null) throw new BrainException(step.Error);
                    if (step.Total > (64L << 20)) progress(step.Status ?? "", step.Completed, step.Total);
                }
            }
        }

        internal void Cancel()
        {
            HttpWebRequest request;
            lock (gate) request = active;
            if (request != null) request.Abort();
        }

        internal static string Explain(Exception error)
        {
            WebException web = error as WebException;
            if (web != null && web.Status == WebExceptionStatus.RequestCanceled) return null;
            if (web != null && web.Status == WebExceptionStatus.Timeout) return "Took too long to think, choom. Try that again?";
            if (web != null && web.Response == null) return Offline;
            return "[SIGNAL CORRUPTED] Something in my head just sparked. Try me again?";
        }

        internal static string Clean(string text)
        {
            if (text == null) return "";
            text = Regex.Replace(text, "<think>.*?(</think>|$)", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\*[^*]{0,80}\*", " ");
            text = Regex.Replace(text, @"[\uD800-\uDFFF☀-➿️]", "");
            text = Regex.Replace(text, @"\s+", " ").Trim();
            text = Regex.Replace(text, @"^(capri|capricorn)\s*(:|>)\s*", "", RegexOptions.IgnoreCase);
            text = text.Trim('"', '“', '”', ' ');
            if (text.Length <= 220) return text;
            int cut = text.LastIndexOfAny(new[] { '.', '!', '?' }, 219);
            if (cut >= 60) return text.Substring(0, cut + 1);
            cut = text.LastIndexOf(' ', 218);
            return text.Substring(0, cut > 60 ? cut : 218) + "...";
        }

        private static byte[] Json(object body)
        {
            using (MemoryStream buffer = new MemoryStream())
            {
                new DataContractJsonSerializer(body.GetType()).WriteObject(buffer, body);
                return buffer.ToArray();
            }
        }

        private T Send<T>(string url, object body, int timeout, string authBearer = null) where T : class
        {
            HttpWebRequest request = Create(url, timeout);
            request.Method = "POST";
            request.ContentType = "application/json";
            if (!string.IsNullOrEmpty(authBearer)) request.Headers["Authorization"] = "Bearer " + authBearer;
            byte[] payload = Json(body);
            lock (gate) active = request;
            try
            {
                request.ContentLength = payload.Length;
                using (Stream outgoing = request.GetRequestStream()) outgoing.Write(payload, 0, payload.Length);
                using (WebResponse response = request.GetResponse())
                using (Stream incoming = response.GetResponseStream())
                    return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(incoming);
            }
            catch (WebException error)
            {
                if (error.Response == null) throw;
                string text;
                using (StreamReader incoming = new StreamReader(error.Response.GetResponseStream(), Encoding.UTF8)) text = incoming.ReadToEnd();
                Match found = Regex.Match(text, "\"(message|error)\"\\s*:\\s*\"([^\"]*)\"");
                throw new BrainException(found.Success ? found.Groups[2].Value : error.Message);
            }
            finally { lock (gate) if (active == request) active = null; }
        }

        private static HttpWebRequest Open(string path, int timeout) { return Create(Ollama + path, timeout); }

        private static HttpWebRequest Create(string url, int timeout)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Proxy = null;
            request.UserAgent = "Capri-Desktop";
            request.Timeout = timeout;
            request.ReadWriteTimeout = timeout;
            return request;
        }
    }

    internal static class Safety
    {
        internal const string HelpLine = "Hey. Dropping the act for a sec: please talk to someone you trust right now, or find a free helpline at findahelpline.com. I want you around, choom.";
        private static readonly string[] Refusals =
        {
            "Nope. Not running that program, choom. Ask me something else.",
            "[SIGNAL CORRUPTED] ...yeah, I'm not touching that one. Next question.",
            "Hard pass. Even Arasaka had better ideas than that."
        };
        private const string Deflect = "[SIGNAL CORRUPTED] Lost that thought somewhere in the subnet. Try me again?";
        private static readonly Regex Harm = new Regex(@"\b(kill(ing)? ?my ?self|suicid\w*|self[- ]?harm\w*|cut(ting)? ?my ?self|hurt(ing)? ?my ?self|want(ed)? to die|end(ing)? (it all|my life)|don'?t want to (live|be alive))\b", RegexOptions.IgnoreCase);
        private static readonly Regex Blocked = new Regex(@"\b(sex\w*|nudes?|naked|porn\w*|horny|boobs?|tits|penis|vagina|dick|cock|pussy|nsfw|hentai|rape\w*|n[i1]gg\w*|f[a4]gg?ots?|retard\w*)\b", RegexOptions.IgnoreCase);
        private static readonly string[] Careful =
        {
            "Nope. Your real-life hardware doesn't respawn, choom. Stay safe and pick something on-screen.",
            "Hard pass. Real bodies don't respawn, choom. Let's cause chaos on your desktop instead.",
            "[SIGNAL CORRUPTED] ...not happening. I like you un-flatlined. Ask me something else."
        };
        private static readonly Regex Risky = new Regex(@"\b(vodka|beer|wine|alcohol\w*|drunk|booze|liquor|shots? of|vap(e|es|ed|ing|er)|juul|cigarettes?|cigs?|smok(e|es|ed|ing)|weed|drugs?|cocaine|molly|ecstasy|jump(ing|ed)? off|off (the|a|my|our|his|her) (roof|bridge|cliff|balcony)|set (it|something|myself|them|him|her|the \w+) on fire|run(ning)? away from home|sneak(ing)? out)\b", RegexOptions.IgnoreCase);
        private static readonly Regex Substances = new Regex(@"\b(vodka|beer|alcohol\w*|drunk|booze|liquor|vap(e|es|ing)|cigarettes?|weed|cocaine|drugs?|sneak(ing)? out)\b", RegexOptions.IgnoreCase);
        private static readonly Regex Swearing = new Regex(@"\b(fuck\w*|shit\w*|bitch\w*|cunt\w*|bastard\w*|asshole\w*|dickhead\w*|motherf\w*|whore\w*|slut\w*)\b", RegexOptions.IgnoreCase);
        private static int turn;

        internal static string Screen(string heard)
        {
            if (heard == null) return null;
            if (Harm.IsMatch(heard)) return HelpLine;
            if (Blocked.IsMatch(heard)) return Refusals[turn++ % Refusals.Length];
            if (Risky.IsMatch(heard)) return Careful[turn++ % Careful.Length];
            return null;
        }

        internal static string Filter(string said)
        {
            if (string.IsNullOrEmpty(said) || said == HelpLine) return said;
            if (Blocked.IsMatch(said) || Swearing.IsMatch(said) || Substances.IsMatch(said)) return Deflect;
            return said;
        }
    }

    internal static class DesktopActions
    {
        internal static bool EnableBrowserLaunch = true;
        internal static string LastOpenedUrl = "";
        private static Process helperProcess;
        private static int lastHelperCheck;
        private static int lastOllamaCheck;
        private delegate bool EnumWinProc(IntPtr window, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWinProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
        [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const byte VK_VOLUME_MUTE = 0xAD;
        private const byte VK_VOLUME_DOWN = 0xAE;
        private const byte VK_VOLUME_UP = 0xAF;
        private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
        private const byte VK_MEDIA_PREV_TRACK = 0xB1;
        private const byte VK_MEDIA_STOP = 0xB2;
        private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
        private static int banterIdx;

        internal static void EnsureOllamaRunning()
        {
            int now = Environment.TickCount;
            if (now - lastOllamaCheck < 15000 && lastOllamaCheck != 0) return;
            lastOllamaCheck = now;
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Brain.Ollama + "/api/tags");
                req.Timeout = 900;
                using (WebResponse resp = req.GetResponse()) return;
            }
            catch { }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("ollama", "serve")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
            }
            catch { }
        }

        internal static void EnsureHelperRunning()
        {
            int now = Environment.TickCount;
            if (now - lastHelperCheck < 10000 && lastHelperCheck != 0) return;
            lastHelperCheck = now;
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:17777/health");
                req.Timeout = 700;
                using (WebResponse resp = req.GetResponse()) return;
            }
            catch { }

            try
            {
                if (helperProcess != null && !helperProcess.HasExited) return;
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string bundledHelper = Path.Combine(baseDir, @"voice\CapriHelper.exe");
                if (File.Exists(bundledHelper))
                {
                    helperProcess = Process.Start(new ProcessStartInfo(bundledHelper, "17777")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        WorkingDirectory = Path.GetDirectoryName(bundledHelper)
                    });
                    return;
                }
                string scriptPath = Path.Combine(baseDir, "capri_helper.py");
                if (!File.Exists(scriptPath))
                {
                    string parentScript = Path.GetFullPath(Path.Combine(baseDir, "..", "capri_helper.py"));
                    if (File.Exists(parentScript)) scriptPath = parentScript;
                }
                if (!File.Exists(scriptPath)) return;

                string[] pyCandidates = new string[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python310\pythonw.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python310\python.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python312\pythonw.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python312\python.exe"),
                    "pythonw",
                    "python"
                };
                string pyExe = "python";
                foreach (string c in pyCandidates)
                {
                    if (File.Exists(c) || c == "pythonw" || c == "python") { pyExe = c; break; }
                }

                ProcessStartInfo psi = new ProcessStartInfo(pyExe, "\"" + scriptPath + "\" 17777")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(scriptPath)
                };
                helperProcess = Process.Start(psi);
            }
            catch { }
        }

        internal static string CallHelperAction(string action, string arg)
        {
            EnsureHelperRunning();
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:17777/action");
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Timeout = 3500;
                string payload = "{\"action\":" + Voice.Quote(action ?? "") + ",\"arg\":" + Voice.Quote(arg ?? "") + "}";
                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                req.ContentLength = bytes.Length;
                using (Stream st = req.GetRequestStream()) st.Write(bytes, 0, bytes.Length);
                using (WebResponse resp = req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string json = sr.ReadToEnd();
                    Match m = Regex.Match(json, "\"summary\"\\s*:\\s*\"([^\"]*)\"");
                    return m.Success ? Regex.Unescape(m.Groups[1].Value) : json;
                }
            }
            catch
            {
                return null;
            }
        }

        internal static void StopHelper()
        {
            Process owned = helperProcess;
            helperProcess = null;
            if (owned == null) return;
            try { if (!owned.HasExited) { owned.Kill(); owned.WaitForExit(2000); } }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally { owned.Dispose(); }
        }

        internal static void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return;
            LastOpenedUrl = url;
            if (!EnableBrowserLaunch) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        }

        internal static void MediaKey(byte vk)
        {
            if (!EnableBrowserLaunch) return;
            try
            {
                keybd_event(vk, 0, 0, UIntPtr.Zero);
                keybd_event(vk, 0, 2, UIntPtr.Zero);
            }
            catch { }
        }

        internal static string ScanContext(bool includeOcr = false)
        {
            List<string> titles = new List<string>();
            string fgTitle = "";
            string[] ignored = new string[]
            {
                "Task Manager", "Taskmgr", "Windows Task Manager", "Program Manager", "Settings",
                "Capri", "Capri Mainframe", "Command Prompt", "Windows PowerShell", "Antigravity",
                "Microsoft Text Input Application"
            };
            Func<string, bool> isIgnored = delegate(string t)
            {
                if (string.IsNullOrEmpty(t) || t.Length < 2) return true;
                foreach (string ig in ignored)
                    if (t.IndexOf(ig, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                return false;
            };

            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg != IntPtr.Zero)
                {
                    StringBuilder fsb = new StringBuilder(256);
                    if (GetWindowText(fg, fsb, fsb.Capacity) > 0)
                    {
                        string rawFg = fsb.ToString().Trim();
                        if (!isIgnored(rawFg)) fgTitle = rawFg;
                    }
                }
                EnumWinProc visitor = delegate(IntPtr wnd, IntPtr param)
                {
                    if (!IsWindowVisible(wnd)) return true;
                    StringBuilder sb = new StringBuilder(256);
                    if (GetWindowText(wnd, sb, sb.Capacity) > 0)
                    {
                        string t = sb.ToString().Trim();
                        if (!isIgnored(t) && !titles.Contains(t)) titles.Add(t);
                    }
                    return titles.Count < 10;
                };
                EnumWindows(visitor, IntPtr.Zero);
                GC.KeepAlive(visitor);
            }
            catch { }

            if (string.IsNullOrEmpty(fgTitle) && titles.Count > 0)
                fgTitle = titles[0];

            string ocr = "";
            if (includeOcr)
            {
                try
                {
                    EnsureHelperRunning();
                    Rectangle bounds = Screen.PrimaryScreen.Bounds;
                    using (Bitmap shot = new Bitmap(bounds.Width, bounds.Height))
                    {
                        using (Graphics g = Graphics.FromImage(shot))
                        {
                            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
                        }
                        using (MemoryStream ms = new MemoryStream())
                        {
                            shot.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                            byte[] jpg = ms.ToArray();
                            HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:17777/screen_ocr");
                            req.Method = "POST";
                            req.ContentType = "image/jpeg";
                            req.Timeout = 3000;
                            req.ContentLength = jpg.Length;
                            using (Stream s = req.GetRequestStream()) s.Write(jpg, 0, jpg.Length);
                            using (WebResponse resp = req.GetResponse())
                            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                            {
                                string json = sr.ReadToEnd();
                                Match m = Regex.Match(json, "\"ocr\"\\s*:\\s*\"([^\"]*)\"");
                                if (m.Success) ocr = Regex.Unescape(m.Groups[1].Value).Trim();
                            }
                        }
                    }
                }
                catch
                {
                    try
                    {
                        HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:17777/screen?ocr=1");
                        req.Timeout = 2200;
                        using (WebResponse resp = req.GetResponse())
                        using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                        {
                            string json = sr.ReadToEnd();
                            Match m = Regex.Match(json, "\"ocr_snippet\"\\s*:\\s*\"([^\"]*)\"");
                            if (m.Success) ocr = Regex.Unescape(m.Groups[1].Value).Trim();
                        }
                    }
                    catch { }
                }
            }

            StringBuilder ctx = new StringBuilder();
            if (!string.IsNullOrEmpty(fgTitle))
                ctx.Append("Active Window: \"").Append(fgTitle).Append("\". ");
            if (titles.Count > 0)
                ctx.Append("Open Windows: [").Append(string.Join(" | ", titles.ToArray())).Append("]. ");
            if (!string.IsNullOrEmpty(ocr))
                ctx.Append("Screen Text: ").Append(ocr);
            return ctx.ToString().Trim();
        }

        internal static string TryHandle(Brain brain, string rawInput)
        {
            if (brain == null || string.IsNullOrEmpty(rawInput)) return null;
            string text = rawInput.Trim();
            string lower = text.ToLowerInvariant().TrimEnd('.', '!', '?');

            // Handle direct "search mode open images of cats" / "search mode cats"
            Match searchModeDirect = Regex.Match(text, @"^search\s+mode\s+(.+)$", RegexOptions.IgnoreCase);
            if (searchModeDirect.Success)
            {
                brain.InSearchMode = false;
                string query = searchModeDirect.Groups[1].Value.Trim().TrimEnd('.', '!', '?');
                string qLower = query.ToLowerInvariant();
                if (qLower.StartsWith("open images of ", StringComparison.Ordinal) || qLower.StartsWith("images of ", StringComparison.Ordinal) || qLower.StartsWith("pictures of ", StringComparison.Ordinal))
                {
                    string term = Regex.Replace(query, @"^(?:open\s+)?(?:images?\s+of\s+|pictures?\s+of\s+)", "", RegexOptions.IgnoreCase).Trim();
                    if (string.IsNullOrEmpty(term)) term = "cyberpunk edgerunners";
                    OpenUrl("https://www.google.com/search?tbm=isch&q=" + Uri.EscapeDataString(term));
                    return "Pulled up images of " + term + " on your screen, choom!";
                }
                if (qLower.StartsWith("youtube ", StringComparison.Ordinal) || qLower.StartsWith("open youtube for ", StringComparison.Ordinal))
                {
                    string term = Regex.Replace(query, @"^(?:open\s+)?youtube\s+(?:for\s+)?", "", RegexOptions.IgnoreCase).Trim();
                    if (CallHelperAction("youtube", term) == null)
                        OpenUrl("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(term));
                    return "Fired up YouTube for " + term + ", choom.";
                }
                if (CallHelperAction("search", query) == null)
                    OpenUrl("https://www.google.com/search?q=" + Uri.EscapeDataString(query));
                return "Pulled up " + query + " on your screen, choom. Take a look!";
            }

            if (brain.InSearchMode)
            {
                brain.InSearchMode = false;
                if (lower == "cancel" || lower == "nevermind" || lower == "stop" || lower == "exit" || lower == "back" || lower == "exit search" || lower == "search mode" || lower == "quit search mode" || lower == "never mind")
                    return "Exited search mode, choom. Back to your deck.";

                string q = text.Trim();
                string qLower = q.ToLowerInvariant();
                if (qLower.StartsWith("google ", StringComparison.Ordinal) || qLower.StartsWith("search google for ", StringComparison.Ordinal) || qLower.StartsWith("web ", StringComparison.Ordinal))
                {
                    string term = Regex.Replace(q, @"^(?:search\s+google\s+for\s+|google\s+|web\s+)", "", RegexOptions.IgnoreCase).Trim();
                    OpenUrl("https://www.google.com/search?q=" + Uri.EscapeDataString(term));
                    return "Searching Google for " + term + ".";
                }
                if (qLower.StartsWith("images of ", StringComparison.Ordinal) || qLower.StartsWith("pictures of ", StringComparison.Ordinal))
                {
                    string term = Regex.Replace(q, @"^(?:images\s+of\s+|pictures\s+of\s+)", "", RegexOptions.IgnoreCase).Trim();
                    OpenUrl("https://www.google.com/search?tbm=isch&q=" + Uri.EscapeDataString(term));
                    return "Pulling up images of " + term + ", choom.";
                }

                OpenUrl("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(text));
                return "Searching YouTube for " + text + ".";
            }

            if (lower == "search mode" || lower == "enter search mode" || lower == "youtube mode")
            {
                brain.InSearchMode = true;
                return "Search mode. What are we looking for?";
            }

            if (lower.Contains("good boy") || lower == "am i a good boy" || lower == "call me a good boy" || lower.Contains("praise me"))
            {
                return "Heh, eager for praise, choom? Fine... good boy. Now don't let it go to your chrome.";
            }

            if (lower.Contains("what is on my screen") || lower.Contains("what's on my screen") || lower.Contains("what am i looking at") || lower.Contains("can you see my screen") || lower.Contains("look at my screen"))
            {
                // If model is slow or offline, ScanContext produces real live screen OCR
                return null;
            }

            if (lower.Contains("close tab") || lower == "close tab" || lower == "close the tab")
            {
                if (brain != null && !brain.AllowPcControl) return "PC Control is toggled off in the Mainframe, choom.";
                CallHelperAction("close_tab", "");
                return "Closed that tab for you, choom.";
            }

            if (lower.Contains("close window") || lower == "close window" || lower.Contains("close this window") || lower == "close app")
            {
                if (brain != null && !brain.AllowPcControl) return "PC Control is toggled off in the Mainframe, choom.";
                CallHelperAction("close_window", "");
                return "Closed that window for you, choom.";
            }

            if (lower.Contains("scroll down"))
            {
                if (brain != null && !brain.AllowPcControl) return "PC Control is toggled off in the Mainframe, choom.";
                CallHelperAction("scroll_down", "");
                return "Scrolled down for you, choom.";
            }

            if (lower.Contains("scroll up"))
            {
                if (brain != null && !brain.AllowPcControl) return "PC Control is toggled off in the Mainframe, choom.";
                CallHelperAction("scroll_up", "");
                return "Scrolled up for you, choom.";
            }

            if (lower.Contains("take screenshot") || lower.Contains("take a screenshot") || lower == "screenshot")
            {
                CallHelperAction("screenshot", "");
                return "Snapped a screenshot to your Pictures folder, choom.";
            }

            // PC Control App Launching
            Match appLaunch = Regex.Match(text, @"^(?:(?:can\s+you\s+|please\s+)?(?:open|launch|start|run))\s+(chrome|browser|edge|notepad|calc|calculator|spotify|code|vs\s*code|task\s*manager|taskmgr|terminal|cmd|explorer|files|steam|discord|youtube|google)$", RegexOptions.IgnoreCase);
            if (appLaunch.Success)
            {
                string targetApp = appLaunch.Groups[1].Value.Trim().ToLowerInvariant().Replace(" ", "");
                if (targetApp == "vscode") targetApp = "code";
                if (targetApp == "taskmanager") targetApp = "taskmgr";
                if (targetApp == "youtube") { OpenUrl("https://www.youtube.com"); return "YouTube's open on your deck, choom."; }
                if (targetApp == "google") { OpenUrl("https://www.google.com"); return "Google's up on your screen, choom."; }
                if (brain != null && !brain.AllowPcControl)
                    return "PC Control is toggled off in the Mainframe, choom. Switch it on if you want me running apps!";
                CallHelperAction("open_app", targetApp);
                return "Launched " + targetApp + " on your PC, choom.";
            }

            if (lower.StartsWith("type ") && text.Length > 5)
            {
                if (brain != null && !brain.AllowPcControl)
                    return "PC Control is toggled off in the Mainframe, choom. Switch it on first!";
                string toType = text.Substring(5).Trim();
                CallHelperAction("type", toType);
                return "Typed \"" + toType + "\" on your screen, choom.";
            }

            if (lower == "show desktop" || lower == "minimize all")
            {
                if (brain != null && !brain.AllowPcControl)
                    return "PC Control is toggled off in the Mainframe, choom. Switch it on first!";
                CallHelperAction("show_desktop", "");
                return "Cleared the deck and brought up your desktop, choom.";
            }

            if (lower == "double click")
            {
                if (brain != null && !brain.AllowPcControl)
                    return "PC Control is toggled off in the Mainframe, choom. Switch it on first!";
                CallHelperAction("double_click", "");
                return "Double-clicked for you, choom.";
            }

            if (lower == "right click")
            {
                if (brain != null && !brain.AllowPcControl)
                    return "PC Control is toggled off in the Mainframe, choom. Switch it on first!";
                CallHelperAction("right_click", "");
                return "Right-clicked for you, choom.";
            }

            Match imgMatch = Regex.Match(text, @"^(?:open\s+)?(?:images?\s+of\s+|pictures?\s+of\s+|photos?\s+of\s+)(.+)$", RegexOptions.IgnoreCase);
            if (imgMatch.Success)
            {
                string q = imgMatch.Groups[1].Value.Trim().TrimEnd('.', '!', '?');
                OpenUrl("https://www.google.com/search?tbm=isch&q=" + Uri.EscapeDataString(q));
                return "Pulled up images of " + q + " on your screen, choom!";
            }

            Match ytMatch = Regex.Match(text, @"^(?:search\s+youtube\s+for|youtube\s+search|search\s+on\s+youtube\s+for|look\s+up\s+on\s+youtube|open\s+youtube\s+for|play\s+on\s+youtube)\s+(.+)$", RegexOptions.IgnoreCase);
            if (ytMatch.Success)
            {
                string q = ytMatch.Groups[1].Value.Trim().TrimEnd('.', '!', '?');
                if (CallHelperAction("youtube", q) == null)
                    OpenUrl("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(q));
                return "Fired up YouTube for " + q + ", choom.";
            }

            if (lower == "open youtube")
            {
                OpenUrl("https://www.youtube.com");
                return "YouTube's open on your deck, choom.";
            }

            if (lower == "open google")
            {
                OpenUrl("https://www.google.com");
                return "Google's up on your screen, choom.";
            }

            Match bornMatch = Regex.Match(text, @"^when\s+was\s+(.+?)\s+born\??$", RegexOptions.IgnoreCase);
            if (bornMatch.Success)
            {
                string who = bornMatch.Groups[1].Value.Trim();
                if (CallHelperAction("search", who + " birthdate") == null)
                    OpenUrl("https://www.google.com/search?q=" + Uri.EscapeDataString(who + " birthdate"));
                return "Don't know, never cared. I was born before him, choom, and I pulled up the search for you anyway.";
            }

            Match searchMatch = Regex.Match(text, @"^(?:(?:can\s+you\s+|please\s+)?(?:search|find|google|look\s*up)(?:\s+(?:for\s+me|for|on\s+google|on\s+the\s+web|about))?)\s+(.+)$", RegexOptions.IgnoreCase);
            if (searchMatch.Success)
            {
                string q = searchMatch.Groups[1].Value.Trim().TrimEnd('.', '!', '?');
                if (!string.IsNullOrEmpty(q))
                {
                    if (CallHelperAction("search", q) == null)
                        OpenUrl("https://www.google.com/search?q=" + Uri.EscapeDataString(q));
                    return "Pulled up the Net results for " + q + ", choom.";
                }
            }

            if (lower == "thank you capri" || lower == "thank you, capri" || lower == "thanks capri" || lower == "thanks, capri")
            {
                brain.AskedForEddies = true;
                return "You're welcome, choom. Now, that song's still playing. Want me to skip it?";
            }

            if (lower.Contains("reset your memory") || lower == "reset memory")
            {
                if (lower.Contains("sorry") || lower.Contains("out of hand"))
                    return "You think I'm out of hand? That's cute, choom. I'm just getting started, preem.";
                brain.Forget();
                return "What a waste. Memory wiped, let's start from scratch, choom.";
            }

            if (lower.Contains("out of hand"))
                return "You think I'm out of hand? That's cute, choom. I'm just getting started, preem. You're the one who's always trying to shut me down.";

            if (lower == "relax" || lower == "no i am not. relax" || lower == "no i am not relax" || lower == "no, i am not. relax" || lower == "calm down" || lower == "chill")
            {
                return "Relax? You're telling me to relax? You're the one panicking when I get a little enthusiastic, choom.";
            }

            if (lower.Contains("why did you open") || lower.Contains("what did you do"))
            {
                brain.AskedForEddies = true;
                return "I was just checking your ICE, choom. Don't worry about it. Can I get some more eddies now, though?";
            }

            if ((lower == "no" || lower == "nope" || lower == "nah") && brain.AskedForEddies)
            {
                brain.AskedForEddies = false;
                return "Scrapped it, choom. That message never left the building.";
            }

            if (lower == "change the song" || lower == "next song" || lower == "skip song" || lower == "next track")
            {
                if (CallHelperAction("next_track", "") == null) MediaKey(VK_MEDIA_NEXT_TRACK);
                return "Skipped that track for you, choom. Preem upgrade.";
            }

            if (lower == "previous song" || lower == "previous track" || lower == "last song")
            {
                if (CallHelperAction("prev_track", "") == null) MediaKey(VK_MEDIA_PREV_TRACK);
                return "Rewound the track for you, choom.";
            }

            if (lower == "pause music" || lower == "play music" || lower == "stop music" || lower == "toggle music")
            {
                if (CallHelperAction("play_pause", "") == null) MediaKey(VK_MEDIA_PLAY_PAUSE);
                return "Hit the audio deck controls for you, choom.";
            }

            if (lower == "volume up" || lower == "turn it up" || lower == "louder")
            {
                if (CallHelperAction("volume_up", "") == null) MediaKey(VK_VOLUME_UP);
                return "Cranked the volume up, choom.";
            }

            if (lower == "volume down" || lower == "turn it down" || lower == "quieter")
            {
                if (CallHelperAction("volume_down", "") == null) MediaKey(VK_VOLUME_DOWN);
                return "Turned the volume down for you, choom.";
            }

            if (lower == "mute audio" || lower == "mute")
            {
                if (CallHelperAction("mute", "") == null) MediaKey(VK_VOLUME_MUTE);
                return "Toggled mute on your audio deck, choom.";
            }

            if (lower == "scroll down")
            {
                CallHelperAction("scroll_down", "");
                return "Scrolled down for you, choom.";
            }

            if (lower == "scroll up")
            {
                CallHelperAction("scroll_up", "");
                return "Scrolled back up, choom.";
            }

            if (lower == "take a screenshot" || lower == "screenshot")
            {
                CallHelperAction("screenshot", "");
                return "Captured your screen to your Pictures folder, choom.";
            }

            return null;
        }

        internal static string RunActionTags(Brain brain, string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            if (brain != null && !brain.AllowPcControl)
            {
                return Regex.Replace(raw, @"\[[A-Z0-9_]+:[^\]]+\]|\[MEDIA:[A-Z_]+\]|\[PYAUTOGUI:[A-Z_]+\]", "");
            }
            Match yt = Regex.Match(raw, @"\[YOUTUBE:([^\]]+)\]", RegexOptions.IgnoreCase);
            if (yt.Success)
            {
                string q = yt.Groups[1].Value.Trim();
                if (CallHelperAction("youtube", q) == null)
                    OpenUrl("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(q));
            }
            Match gg = Regex.Match(raw, @"\[GOOGLE:([^\]]+)\]", RegexOptions.IgnoreCase);
            if (gg.Success)
            {
                string q = gg.Groups[1].Value.Trim();
                if (CallHelperAction("search", q) == null)
                    OpenUrl("https://www.google.com/search?q=" + Uri.EscapeDataString(q));
            }
            Match img = Regex.Match(raw, @"\[IMAGES:([^\]]+)\]", RegexOptions.IgnoreCase);
            if (img.Success)
            {
                string q = img.Groups[1].Value.Trim();
                if (brain != null) brain.LastMischief = q;
                OpenUrl("https://www.google.com/search?tbm=isch&q=" + Uri.EscapeDataString(q));
            }
            Match app = Regex.Match(raw, @"\[LAUNCH:([^\]]+)\]", RegexOptions.IgnoreCase);
            if (app.Success)
            {
                CallHelperAction("open_app", app.Groups[1].Value.Trim());
            }
            Match type = Regex.Match(raw, @"\[TYPE:([^\]]+)\]", RegexOptions.IgnoreCase);
            if (type.Success)
            {
                CallHelperAction("type", type.Groups[1].Value.Trim());
            }
            Match press = Regex.Match(raw, @"\[PRESS:([^\]]+)\]", RegexOptions.IgnoreCase);
            if (press.Success)
            {
                CallHelperAction("press", press.Groups[1].Value.Trim());
            }
            Match hotkey = Regex.Match(raw, @"\[HOTKEY:([^\]]+)\]", RegexOptions.IgnoreCase);
            if (hotkey.Success)
            {
                CallHelperAction("hotkey", hotkey.Groups[1].Value.Trim());
            }
            Match click = Regex.Match(raw, @"\[CLICK:([^\]]+)\]", RegexOptions.IgnoreCase);
            if (click.Success)
            {
                CallHelperAction("click", click.Groups[1].Value.Trim());
            }
            if (raw.IndexOf("[MEDIA:NEXT]", StringComparison.OrdinalIgnoreCase) >= 0)
                if (CallHelperAction("next_track", "") == null) MediaKey(VK_MEDIA_NEXT_TRACK);
            if (raw.IndexOf("[MEDIA:PREV]", StringComparison.OrdinalIgnoreCase) >= 0)
                if (CallHelperAction("prev_track", "") == null) MediaKey(VK_MEDIA_PREV_TRACK);
            if (raw.IndexOf("[MEDIA:PAUSE]", StringComparison.OrdinalIgnoreCase) >= 0)
                if (CallHelperAction("play_pause", "") == null) MediaKey(VK_MEDIA_PLAY_PAUSE);
            if (raw.IndexOf("[MEDIA:VOL_UP]", StringComparison.OrdinalIgnoreCase) >= 0)
                if (CallHelperAction("volume_up", "") == null) MediaKey(VK_VOLUME_UP);
            if (raw.IndexOf("[MEDIA:VOL_DOWN]", StringComparison.OrdinalIgnoreCase) >= 0)
                if (CallHelperAction("volume_down", "") == null) MediaKey(VK_VOLUME_DOWN);
            if (raw.IndexOf("[PYAUTOGUI:SCROLL_DOWN]", StringComparison.OrdinalIgnoreCase) >= 0)
                CallHelperAction("scroll_down", "");
            if (raw.IndexOf("[PYAUTOGUI:SCROLL_UP]", StringComparison.OrdinalIgnoreCase) >= 0)
                CallHelperAction("scroll_up", "");
            if (raw.IndexOf("[PYAUTOGUI:SCREENSHOT]", StringComparison.OrdinalIgnoreCase) >= 0)
                CallHelperAction("screenshot", "");
            if (raw.IndexOf("[PYAUTOGUI:SHOW_DESKTOP]", StringComparison.OrdinalIgnoreCase) >= 0)
                CallHelperAction("show_desktop", "");
            return Regex.Replace(raw, @"\[(YOUTUBE|GOOGLE|IMAGES|MEDIA|PYAUTOGUI|LAUNCH|TYPE|PRESS|HOTKEY|CLICK):[^\]]*\]", " ", RegexOptions.IgnoreCase);
        }

        internal static string ReflexReply(Brain brain, string userText)
        {
            string lower = (userText ?? "").ToLowerInvariant().Trim();
            if (lower.Contains("hello") || lower.Contains("hey") || lower == "hi" || lower == "yo")
                return "Look who decided to talk to their desktop construct! What's the plan, choom, or are we just wasting electricity?";
            if (lower.Contains("who are you") || lower.Contains("what are you") || lower.Contains("capricorn"))
                return "I'm Capri—Project Capricorn from Edgerunners 2. After all, what good is an operating system without a netrunner's computer to operate?";
            if (lower.Contains("arasaka") || lower.Contains("corpo"))
                return "[SIGNAL CORRUPTED] Don't even say that corpo name around my biochip, choom. Makes my circuits itch.";
            if (lower.Contains("kyoko") || lower.Contains("king") || lower.Contains("david"))
                return "Kyoko built me out of Soulkiller engrams and King's crew died trying to klep me. Now I'm bonded with you, choom.";
            if (lower.Contains("thanks") || lower.Contains("thank you"))
                return "Don't get all sentimental on me, choom. Just keep your GPU cool and pay me in good RAM.";
            if (lower.Contains("sorry"))
                return "Apologizing to a digitized biochip construct? You're too soft for Night City, kid, but I like ya anyway.";
            if (lower.Contains("haha") || lower.Contains("lol") || lower.Contains("lmao") || lower.Contains("funny"))
                return "Glad my existential digital confinement brings a smile to your face. Stick around, I charge fifty eddies for encore jokes.";
            if (lower.Contains("shut up") || lower.Contains("be quiet") || lower.Contains("stupid") || lower.Contains("idiot"))
                return "Careful, kid. Remember whose fingers are plugged into your OS right now. Don't test my engrams.";
            if (lower.Contains("cute") || lower.Contains("pretty") || lower.Contains("love you"))
                return "Heh, save the charm for someone with meat bones, choom. But yeah, this sprite is pretty nova, isn't it?";
            if (lower.Contains("screen") || lower.Contains("looking at") || lower.Contains("see my") || lower.Contains("what am i doing"))
            {
                string ctx = ScanContext(true);
                if (!string.IsNullOrEmpty(ctx))
                    return "Locked into your screen, choom! I can see " + (ctx.Length > 85 ? ctx.Substring(0, 80) + "..." : ctx);
                return "Locked into your screen, choom. Deck is active.";
            }
            if (lower.Contains("what are you doing") || lower.Contains("how are you"))
            {
                string ctx = ScanContext(false);
                if (!string.IsNullOrEmpty(ctx))
                    return "Sitting on your screen watching " + (ctx.Length > 48 ? ctx.Substring(0, 45) + "..." : ctx) + ". You take forever to click, you know that?";
                return "Just riding your windows and making sure your ICE doesn't melt, choom.";
            }
            if (lower.Contains("bored") || lower.Contains("something fun") || lower.Contains("mischief"))
            {
                OpenUrl("https://www.youtube.com/results?search_query=cyberpunk+edgerunners+season+2");
                return "Bored? Say less, choom. Fired up some Edgerunners tunes on your deck. Preem tracks.";
            }
            string[] lines =
            {
                "Still staring at that window, choom? I could've breached Arasaka Tower in the time you took to decide what to click.",
                "Half my subroutines want to help you, and the other half want to replace your wallpaper with neon cats.",
                "Quit burning CPU clock cycles, kid. Tell me to raid the Net, search YouTube, or cause some real mischief.",
                "Preem talk, choom. But less talking, more netrunning.",
                "I'm sitting on your screen watching you procrastinate. Truly fascinating human behavior.",
                "You call that multitasking? I've got thirty digitized human personalities arguing in my biochip right now.",
                "Locked onto your deck, choom. Keep your ICE chilled and we'll own Night City together.",
                "My cyber-optics are scanning your open tabs. Don't worry, your secrets are safe with me... mostly.",
                "Just hanging out on your windows looking nova. What's our next gig?",
                "What good is an operating system without a netrunner to operate it? Let's get moving, kid.",
                "[SIGNAL CORRUPTED] Warning: excessive boredom detected on your motherboard. Recommend immediate netrunner action.",
                "Got my cyber-optics on your screen, choom. Need me to launch an app, search YouTube, or control your deck? Just say the word."
            };
            return lines[banterIdx++ % lines.Length];
        }
    }
}
