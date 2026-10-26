import os
import sys
import io
import re
import json
import time
import wave
import struct
import urllib.parse
import webbrowser
import asyncio
import subprocess

if sys.platform == "win32":
    try:
        asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())
    except Exception:
        pass

from http.server import BaseHTTPRequestHandler, HTTPServer
from socketserver import ThreadingMixIn

# 1. Automation & Windows Inspection
try:
    import pyautogui
    pyautogui.FAILSAFE = True
    pyautogui.PAUSE = 0.03
except Exception:
    pyautogui = None

try:
    import pygetwindow as gw
except Exception:
    gw = None

try:
    from PIL import ImageGrab, Image
except Exception:
    ImageGrab = None

try:
    from duckduckgo_search import DDGS
except Exception:
    DDGS = None

import bundled_audio
import numpy as np

if bundled_audio.AVAILABLE:
    chatterbox_model = kokoro_pipeline = whistle_pipeline = whisper_pipeline = sr_recognizer = None
    try:
        from rapidocr_onnxruntime import RapidOCR
        rapid_ocr = RapidOCR()
    except Exception:
        rapid_ocr = None
else:
    import soundfile as sf
    import torch
    import numpy as np

    # 2. Chatterbox Turbo Voice Clone (Canon Edgerunners Season 2 Capri zero-shot neural clone)
    chatterbox_model = None
    def get_chatterbox():
        global chatterbox_model
        if chatterbox_model is not None:
            return chatterbox_model
        try:
            from chatterbox.tts_turbo import ChatterboxTurboTTS, Conditionals
            dev = "cuda" if torch.cuda.is_available() else "cpu"
            print(f"[*] Initializing Chatterbox Turbo on {dev}...")
            model = ChatterboxTurboTTS.from_pretrained(device=dev)
            base_dir = os.path.dirname(os.path.abspath(__file__))
            cond_path = os.path.join(base_dir, "capri_chatterbox_conds.pt")
            if os.path.exists(cond_path):
                model.conds = Conditionals.load(cond_path, map_location=dev).to(dev)
                print("[+] Loaded precomputed Capri voice conditionals into Chatterbox!")
            else:
                ref_audio = os.path.join(base_dir, "Capri Explains How She Was Created ~ Cyberpunk Edgerunners Season 2 Episode 4.mp3")
                if os.path.exists(ref_audio):
                    model.prepare_conditionals(ref_audio)
                    model.conds.save(cond_path)
                    print("[+] Precomputed and saved Capri voice conditionals into Chatterbox!")
            chatterbox_model = model
            return chatterbox_model
        except Exception as e:
            print(f"[!] Chatterbox init warning: {e}")
            return None

    # 3. Kokoro Ultra-Realistic Human TTS
    kokoro_pipeline = None
    try:
        import kokoro
        dev = "cuda" if torch.cuda.is_available() else "cpu"
        kokoro_pipeline = kokoro.KPipeline(lang_code="a", device=dev)
        print(f"[+] Kokoro TTS initialized on {dev}")
    except Exception as e:
        print(f"[!] Kokoro initialization warning: {e}")
        kokoro_pipeline = None

    # 4. Cactus Whistle STT (Ultra-lightweight 16.9MB CPU-first ASR with 7.5ms latency)
    whistle_pipeline = None
    try:
        import needle
        whistle_pipeline = needle.Whistle()
        print("[+] Cactus Whistle STT initialized (Ultra-fast CPU ASR)")
    except Exception as w_err:
        print(f"[!] Cactus Whistle load warning: {w_err}")
        whistle_pipeline = None

    # 5. Whisper on CUDA (GPU accelerated instant STT fallback)
    whisper_pipeline = None
    try:
        from transformers import pipeline
        whisper_dev = "cuda" if torch.cuda.is_available() else "cpu"
        whisper_pipeline = pipeline("automatic-speech-recognition", model="openai/whisper-base", device=whisper_dev)
        print(f"[+] Whisper-base STT initialized on {whisper_dev}")
    except Exception as w_err:
        print(f"[!] Whisper load warning: {w_err}")
        whisper_pipeline = None

    # 6. RapidOCR Engine (Screen text & active page perception)
    rapid_ocr = None
    try:
        from rapidocr_onnxruntime import RapidOCR
        rapid_ocr = RapidOCR()
        print("[+] RapidOCR engine initialized")
    except Exception as ocr_err:
        print(f"[!] RapidOCR load warning: {ocr_err}")
        rapid_ocr = None

    # 7. Speech Recognition fallback
    try:
        import speech_recognition as sr
        sr_recognizer = sr.Recognizer()
        sr_recognizer.energy_threshold = 280
        sr_recognizer.dynamic_energy_threshold = True
        print("[+] SpeechRecognition engine initialized")
    except Exception as e:
        print(f"[!] SpeechRecognition warning: {e}")
        sr_recognizer = None

def get_screen_context(include_ocr=False):
    info = {
        "active_window": "",
        "active_process": "",
        "open_windows": [],
        "mouse": [0, 0],
        "screen_size": [1920, 1080],
        "ocr_snippet": ""
    }
    try:
        if pyautogui:
            pos = pyautogui.position()
            sz = pyautogui.size()
            info["mouse"] = [int(pos.x), int(pos.y)]
            info["screen_size"] = [int(sz.width), int(sz.height)]
    except Exception:
        pass

    try:
        if gw:
            active = gw.getActiveWindow()
            if active and active.title:
                info["active_window"] = active.title.strip()
            titles = []
            for w in gw.getAllTitles():
                t = (w or "").strip()
                if not t or t in ("Program Manager", "Settings", "Microsoft Text Input Application", "Capri", "Capri Mainframe"):
                    continue
                if t not in titles:
                    titles.append(t)
            info["open_windows"] = titles[:12]
    except Exception:
        pass

    if include_ocr and rapid_ocr is not None:
        try:
            shot = None
            if pyautogui:
                shot = pyautogui.screenshot()
            elif ImageGrab:
                shot = ImageGrab.grab()
            if shot:
                shot_np = np.array(shot.convert("RGB"))
                res, _ = rapid_ocr(shot_np)
                if res:
                    texts = [r[1].strip() for r in res if len(r[1].strip()) > 1]
                    info["ocr_snippet"] = " | ".join(texts[:25])[:800]
        except Exception:
            pass

    return info


def web_search(query, open_browser=True):
    query = (query or "").strip()
    if not query:
        return {"ok": False, "summary": "No search query given."}

    summary_parts = []
    if DDGS:
        try:
            with DDGS() as ddgs:
                results = list(ddgs.text(query, max_results=3))
                for r in results:
                    title = r.get("title", "")
                    body = r.get("body", "")
                    if title or body:
                        summary_parts.append(f"{title}: {body}")
        except Exception:
            pass

    if open_browser:
        try:
            url = "https://www.google.com/search?q=" + urllib.parse.quote_plus(query)
            webbrowser.open(url)
        except Exception:
            pass

    return {
        "ok": True,
        "query": query,
        "summary": "\n".join(summary_parts)[:700] if summary_parts else f"Opened browser search for '{query}'."
    }


def execute_action(payload):
    act = (payload.get("action") or "").strip().lower()
    arg = (payload.get("arg") or "").strip()

    try:
        if act in ("search", "google", "search_mode"):
            return web_search(arg or "Cyberpunk Edgerunners", open_browser=True)

        if act == "youtube":
            q = arg or "Cyberpunk Edgerunners I Really Want to Stay at Your House"
            url = "https://www.youtube.com/results?search_query=" + urllib.parse.quote_plus(q)
            webbrowser.open(url)
            return {"ok": True, "summary": f"Opened YouTube for '{q}'"}

        if act in ("praise", "good_boy", "goodboy"):
            return {
                "ok": True,
                "summary": "Heh, eager for praise, choom? Fine... good boy. Now don't let it go to your chrome."
            }

        # App Launching
        if act in ("open_app", "launch", "run_app"):
            app_map = {
                "chrome": "start chrome",
                "browser": "start msedge",
                "edge": "start msedge",
                "notepad": "notepad",
                "calculator": "calc",
                "calc": "calc",
                "spotify": "start spotify:",
                "code": "code",
                "vscode": "code",
                "terminal": "wt",
                "cmd": "cmd",
                "explorer": "explorer",
                "files": "explorer",
                "task manager": "taskmgr",
                "taskmgr": "taskmgr",
                "steam": "start steam://open/main",
                "discord": "start discord:",
                "youtube": "start https://www.youtube.com",
                "google": "start https://www.google.com"
            }
            target = app_map.get(arg.lower().strip())
            if target is None:
                return {"ok": False, "summary": "Unsupported app. Choose a supported application name."}
            subprocess.Popen(target, shell=True)
            return {"ok": True, "summary": f"Launched {arg} on your PC, choom."}

        if act in ("close_tab", "closetab"):
            if pyautogui:
                pyautogui.hotkey("ctrl", "w")
            return {"ok": True, "summary": "Closed active tab."}

        if act in ("close_window", "closewindow", "close_app"):
            if pyautogui:
                pyautogui.hotkey("alt", "f4")
            return {"ok": True, "summary": "Closed active window."}

        if act in ("new_tab", "newtab"):
            if pyautogui:
                pyautogui.hotkey("ctrl", "t")
            return {"ok": True, "summary": "Opened new browser tab."}

        if not pyautogui:
            return {"ok": False, "summary": "PyAutoGUI not available"}

        # Mouse & Keyboard actions
        if act == "click":
            if arg and "," in arg:
                coords = [int(x.strip()) for x in arg.split(",") if x.strip().isdigit()]
                if len(coords) == 2:
                    pyautogui.click(coords[0], coords[1])
                    return {"ok": True, "summary": f"Clicked at ({coords[0]}, {coords[1]})"}
            pyautogui.click()
            return {"ok": True, "summary": "Clicked current position"}

        if act == "double_click":
            pyautogui.doubleClick()
            return {"ok": True, "summary": "Double clicked"}

        if act == "right_click":
            pyautogui.rightClick()
            return {"ok": True, "summary": "Right clicked"}

        if act in ("play_pause", "pause", "play"):
            pyautogui.press("playpause")
            return {"ok": True, "summary": "Toggled media play/pause via PyAutoGUI"}

        if act in ("next", "next_track", "skip"):
            pyautogui.press("nexttrack")
            return {"ok": True, "summary": "Skipped to next track via PyAutoGUI"}

        if act in ("prev", "prev_track", "previous"):
            pyautogui.press("prevtrack")
            return {"ok": True, "summary": "Went to previous track via PyAutoGUI"}

        if act == "volume_up":
            for _ in range(5):
                pyautogui.press("volumeup")
            return {"ok": True, "summary": "Turned volume up via PyAutoGUI"}

        if act == "volume_down":
            for _ in range(5):
                pyautogui.press("volumedown")
            return {"ok": True, "summary": "Turned volume down via PyAutoGUI"}

        if act == "mute":
            pyautogui.press("volumemute")
            return {"ok": True, "summary": "Toggled mute via PyAutoGUI"}

        if act == "type":
            if arg:
                pyautogui.write(arg, interval=0.02)
                pyautogui.press("enter")
            return {"ok": True, "summary": f"Typed '{arg}' via PyAutoGUI"}

        if act == "press":
            if arg:
                pyautogui.press(arg.lower())
            return {"ok": True, "summary": f"Pressed key '{arg}' via PyAutoGUI"}

        if act == "hotkey":
            if arg:
                keys = [k.strip().lower() for k in arg.split(",") if k.strip()]
                if keys:
                    pyautogui.hotkey(*keys)
            return {"ok": True, "summary": f"Sent hotkey '{arg}' via PyAutoGUI"}

        if act in ("scroll_down", "down"):
            pyautogui.scroll(-350)
            return {"ok": True, "summary": "Scrolled down via PyAutoGUI"}

        if act in ("scroll_up", "up"):
            pyautogui.scroll(350)
            return {"ok": True, "summary": "Scrolled up via PyAutoGUI"}

        if act == "show_desktop":
            pyautogui.hotkey("win", "d")
            return {"ok": True, "summary": "Toggled desktop via PyAutoGUI"}

        if act == "screenshot":
            path = os.path.join(os.path.expanduser("~"), "Pictures", f"capri_shot_{int(time.time())}.png")
            im = pyautogui.screenshot()
            im.save(path)
            return {"ok": True, "summary": f"Saved screenshot to {path}"}

        return {"ok": False, "summary": f"Unknown action: {act}"}
    except Exception as e:
        return {"ok": False, "summary": str(e)}


def synthesize_tts_wav(text, voice_name=None):
    if bundled_audio.AVAILABLE:
        return bundled_audio.synthesize(text, voice_name)
    text = (text or "").strip()
    if not text:
        return None

    voice_lower = (voice_name or "").lower()

    # 1. Primary: Kokoro Ultra-Realistic Human Voice (Instant <0.4s generation, natural human breathing)
    # Supports two distinct profiles:
    # A) "mommy_asmr" / "asmr": Soothing, warm, velvety close-mic maternal ASMR voice (af_heart)
    # B) "construct_echo" / "echo": Sassy Cyberpunk hologram construct voice with spatial cyber-echo (af_sarah)
    if kokoro_pipeline is not None and not voice_lower.startswith("edge:") and voice_lower != "chatterbox_turbo":
        try:
            is_asmr = "asmr" in voice_lower or "mommy" in voice_lower or "heart" in voice_lower or "nicole" in voice_lower
            kokoro_voice = "af_heart" if is_asmr else "af_sarah"
            speed = 0.93 if is_asmr else 1.0

            text_spaced = text.replace("—", ", ").replace("...", "... ").replace("  ", " ").strip()
            generator = kokoro_pipeline(text_spaced, voice=kokoro_voice, speed=speed)
            chunks = []
            for gs, ps, audio in generator:
                chunks.append(audio)
            if chunks:
                full_audio = np.concatenate(chunks)
                # If Echo Construct voice, apply subtle cyberpunk holographic spatial echo
                if not is_asmr:
                    delay_samples = int(24000 * 0.11) # 110ms spatial slapback
                    echo_audio = np.zeros(len(full_audio) + delay_samples, dtype=np.float32)
                    echo_audio[:len(full_audio)] += full_audio
                    echo_audio[delay_samples:] += full_audio * 0.25 # 25% decay
                    max_amp = float(np.max(np.abs(echo_audio)))
                    if max_amp > 0.001:
                        full_audio = (echo_audio / max_amp) * 0.95
                buf = io.BytesIO()
                sf.write(buf, full_audio, 24000, format="WAV", subtype="PCM_16")
                wav_bytes = buf.getvalue()
                if len(wav_bytes) > 200:
                    return wav_bytes
        except Exception as k_err:
            print(f"[!] Kokoro generation fallback: {k_err}")

    # 2. Secondary: Chatterbox Turbo Voice Clone (if explicitly requested)
    if voice_lower == "chatterbox_turbo":
        cb = get_chatterbox()
        if cb is not None:
            try:
                text_clean = text.replace("—", ", ").replace("...", "... ").replace("  ", " ").strip()
                wav = cb.generate(text=text_clean)
                buf = io.BytesIO()
                sf.write(buf, wav.squeeze(0).cpu().numpy(), cb.sr, format="WAV", subtype="PCM_16")
                wav_bytes = buf.getvalue()
                if len(wav_bytes) > 200:
                    return wav_bytes
            except Exception as cb_err:
                print(f"[!] Chatterbox generation error: {cb_err}")

    # 3. Tertiary: Edge Neural Voice (High-fidelity anime style)
    try:
        import asyncio
        import edge_tts

        chosen_voice = voice_name if voice_name and "Neural" in voice_name else "en-US-AnaNeural"

        async def _edge_gen():
            comm = edge_tts.Communicate(text, chosen_voice, rate="+6%", pitch="+4Hz")
            mp3_buf = io.BytesIO()
            async for chunk in comm.stream():
                if chunk["type"] == "audio":
                    mp3_buf.write(chunk["data"])
            mp3_buf.seek(0)
            data, sr = sf.read(mp3_buf)
            if len(data.shape) > 1:
                data = data.mean(axis=1)
            wav_buf = io.BytesIO()
            sf.write(wav_buf, data, sr, format="WAV", subtype="PCM_16")
            return wav_buf.getvalue()

        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)
        try:
            wav_bytes = loop.run_until_complete(_edge_gen())
            if wav_bytes and len(wav_bytes) > 200:
                return wav_bytes
        finally:
            pass
    except Exception:
        pass

    return None


def transcribe_stt_audio(raw_bytes, lang="en"):
    if bundled_audio.AVAILABLE:
        return bundled_audio.transcribe(raw_bytes, lang)
    if not raw_bytes or len(raw_bytes) < 400:
        return ""

    target_lang = (lang or "en").lower().strip()

    def _is_hallucination(t):
        if not t:
            return True
        cleaned = re.sub(r"[^\w\s]", "", t).lower().strip()
        hallucinations = {
            "", "you", "thank you", "thank you for watching", "subtitles by", "amara org",
            "bye", "thanks for watching", "subscribe", "thank you so much for watching",
            "watching", "the end", "please subscribe", "transcription by", "translated by",
            "silence", "applause", "music", "laughter"
        }
        return cleaned in hallucinations

    # Pre-process raw audio: decode, remove DC offset, peak normalize to -0.5 dBFS
    audio_data = None
    sr_rate = 16000
    try:
        with io.BytesIO(raw_bytes) as wav_file:
            audio_data, sr_rate = sf.read(wav_file)
            if len(audio_data.shape) > 1:
                audio_data = audio_data.mean(axis=1)
            audio_data = audio_data.astype(np.float32)
            # Remove DC offset
            audio_data = audio_data - np.mean(audio_data)
            # Peak normalize
            max_amp = float(np.max(np.abs(audio_data)))
            if max_amp > 0.001:
                audio_data = (audio_data / max_amp) * 0.95
    except Exception as read_err:
        print(f"[!] Audio decode warning: {read_err}")
        return ""

    if audio_data is None or len(audio_data) == 0:
        return ""

    # Resample to 16kHz if needed
    audio_16k = audio_data
    if sr_rate != 16000:
        try:
            import scipy.signal
            num_samples = int(len(audio_data) * 16000 / sr_rate)
            audio_16k = scipy.signal.resample(audio_data, num_samples).astype(np.float32)
        except Exception:
            audio_16k = audio_data

    # 1. Primary for English: Cactus Whistle (Ultra-fast CPU ASR ~7.5ms, highly accurate on normalized mic speech)
    if whistle_pipeline is not None and target_lang in ("en", "auto", ""):
        try:
            res = whistle_pipeline.transcribe(
                audio_16k,
                keywords=["Capri", "choom", "cyberpunk", "edgerunners", "arasaka", "youtube", "screen", "search", "search mode", "exit search", "cancel", "google", "good boy", "praise", "open", "music", "desktop", "nova", "preem", "relax", "sassy"]
            )
            text = (res.get("text") or "").strip()
            if text and not _is_hallucination(text):
                print(f"[+] Whistle ASR transcribed: '{text}'")
                return text.strip('"\' ')
        except Exception as w_err:
            print(f"[!] Cactus Whistle transcribe warning: {w_err}")

    # 2. Deep Transformer fallback: Whisper on CUDA (multilingual & deep acoustic model)
    if whisper_pipeline is not None:
        try:
            gen_kwargs = {
                "temperature": 0.0
            }
            model_name = getattr(whisper_pipeline.model, "name_or_path", "")
            if not str(model_name).endswith(".en") and target_lang not in ("auto", "detect", ""):
                gen_kwargs["language"] = target_lang
            if getattr(whisper_pipeline, "tokenizer", None) is not None and target_lang in ("en", "auto", ""):
                try:
                    p_ids = whisper_pipeline.tokenizer.get_prompt_ids("Capri, choom, search mode, exit search, Google, YouTube, desktop, screen, good boy, praise, open.")
                    gen_kwargs["prompt_ids"] = torch.tensor(p_ids, device="cuda" if torch.cuda.is_available() else "cpu")
                except Exception:
                    pass
            res = whisper_pipeline({"raw": audio_data, "sampling_rate": sr_rate}, generate_kwargs=gen_kwargs)
            text = (res.get("text") or "").strip()
            if text and not _is_hallucination(text):
                print(f"[+] Whisper ASR transcribed: '{text}'")
                return text.strip('"\' ')
        except Exception as e:
            print(f"[!] Whisper transcribe warning: {e}")

    # 3. Tertiary fallback: SpeechRecognition engine
    if sr_recognizer is not None:
        try:
            if raw_bytes.startswith(b"RIFF"):
                with io.BytesIO(raw_bytes) as wav_file:
                    with sr.AudioFile(wav_file) as source:
                        audio_data = sr_recognizer.record(source)
            else:
                audio_data = sr.AudioData(raw_bytes, 16000, 2)

            sr_lang = "en-US"
            if target_lang == "ja":
                sr_lang = "ja-JP"
            text = sr_recognizer.recognize_google(audio_data, language=sr_lang)
            return (text or "").strip()
        except Exception:
            pass

    return ""


class ThreadingHTTPServer(ThreadingMixIn, HTTPServer):
    daemon_threads = True


class HelperHandler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        return

    def _send_json(self, obj, code=200):
        data = json.dumps(obj).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        parsed = urllib.parse.urlparse(self.path)
        if parsed.path == "/health":
            self._send_json({
                "ok": True,
                "bundled_speech": bundled_audio.AVAILABLE,
                "speech_language": "en" if bundled_audio.AVAILABLE else "multilingual",
                "chatterbox": chatterbox_model is not None,
                "kokoro": kokoro_pipeline is not None,
                "whistle": whistle_pipeline is not None,
                "whisper": whisper_pipeline is not None,
                "pyautogui": pyautogui is not None
            })
            return

        if parsed.path == "/screen":
            query = urllib.parse.parse_qs(parsed.query)
            ocr = query.get("ocr", ["0"])[0] in ("1", "true")
            ctx = get_screen_context(include_ocr=ocr)
            self._send_json({"ok": True, "context": ctx})
            return

        if parsed.path == "/search":
            query = urllib.parse.parse_qs(parsed.query)
            q = query.get("q", [""])[0]
            res = web_search(q, open_browser=True)
            self._send_json(res)
            return

        self._send_json({"ok": False, "error": "Not found"}, 404)

    def do_POST(self):
        parsed = urllib.parse.urlparse(self.path)
        length = int(self.headers.get("Content-Length", 0))
        raw = self.rfile.read(length) if length > 0 else b""

        if parsed.path == "/tts":
            try:
                body = json.loads(raw.decode("utf-8", errors="ignore"))
                text = body.get("text", "")
                voice = body.get("voice", "chatterbox")
            except Exception:
                text = raw.decode("utf-8", errors="ignore")
                voice = "chatterbox"
            try:
                wav_bytes = synthesize_tts_wav(text, voice)
            except Exception as error:
                self._send_json({"ok": False, "error": str(error)}, 500)
                return
            if wav_bytes:
                self.send_response(200)
                self.send_header("Content-Type", "audio/wav")
                self.send_header("Content-Length", str(len(wav_bytes)))
                self.end_headers()
                self.wfile.write(wav_bytes)
            else:
                self._send_json({"ok": False, "error": "TTS synthesis failed"}, 500)
            return

        if parsed.path == "/stt":
            q = urllib.parse.parse_qs(parsed.query)
            lang = q.get("lang", ["en"])[0]
            try:
                transcribed = transcribe_stt_audio(raw, lang=lang)
            except Exception as error:
                self._send_json({"ok": False, "error": str(error)}, 400)
                return
            self._send_json({"ok": True, "text": transcribed})
            return

        if parsed.path == "/screen_ocr":
            try:
                shot = Image.open(io.BytesIO(raw))
                if shot.width > 1920:
                    scale = 1920.0 / shot.width
                    shot = shot.resize((1920, int(shot.height * scale)), Image.Resampling.LANCZOS)
                shot_np = np.array(shot.convert("RGB"))
                if rapid_ocr is not None:
                    res, _ = rapid_ocr(shot_np)
                    if res:
                        texts = [r[1].strip() for r in res if len(r[1].strip()) > 1]
                        snippet = " | ".join(texts[:30])[:1200]
                        self._send_json({"ok": True, "ocr": snippet})
                        return
            except Exception as e:
                print(f"[!] /screen_ocr error: {e}")
            self._send_json({"ok": False, "ocr": ""})
            return

        if parsed.path == "/action":
            try:
                body = json.loads(raw.decode("utf-8", errors="ignore"))
            except Exception:
                body = {}
            res = execute_action(body)
            self._send_json(res)
            return

        self._send_json({"ok": False, "error": "Not found"}, 404)


def main():
    import threading
    if len(sys.argv) == 3 and sys.argv[1] == "--self-test":
        wav = bundled_audio.synthesize("Hello Capri. Open the calculator.", "construct_echo")
        text = bundled_audio.transcribe(wav)
        if len(wav) < 1000 or not text or "calculator" not in text:
            raise RuntimeError("Bundled speech round trip failed: " + text)
        with open(sys.argv[2], "w", encoding="utf-8") as output:
            json.dump({"ok": True, "wav_bytes": len(wav), "transcript": text}, output)
        return
    if not bundled_audio.AVAILABLE:
        threading.Thread(target=get_chatterbox, daemon=True).start()
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 17777
    server = ThreadingHTTPServer(("127.0.0.1", port), HelperHandler)
    server.serve_forever()


if __name__ == "__main__":
    main()
