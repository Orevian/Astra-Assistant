# ✦ Astra v1.0.1

**EN:** A voice-first AI desktop agent for Windows. Say *"Astra, open Chrome and play Misery on YouTube"* and it does it, then verifies the result. Speech recognition and the file/app index run **locally**.
**TR:** Windows için sesle çalışan AI masaüstü ajanı. *"Astra, Chrome'u aç ve YouTube'da Misery'yi çal"* de, yapar ve sonucu doğrular. Konuşma tanıma ve dosya/uygulama dizini **yerelde** çalışır.

## 📦 Download / İndir

| File / Dosya | What it is / Nedir |
|---|---|
| `Astra-Setup-1.0.1.exe` | Installer (recommended) / Kurulum paketi (önerilen) |
| `Astra-1.0.1-win-x64.zip` | Portable folder, just run `Astra.exe` / Taşınabilir klasör, `Astra.exe`'yi çalıştır |

Requires **Windows 10 (19041+) or Windows 11, 64-bit**. Nothing else to install.
**Windows 10 (19041+) veya Windows 11, 64-bit** gerekir. Başka bir şey kurmana gerek yok.

## 🚀 First run / İlk açılış

1. Run the installer, then follow the setup wizard (mic → wake word → AI → *Scan Windows* → permissions → look).
   Kurulumu çalıştır, sihirbazı izle (mikrofon → uyandırma kelimesi → yapay zekâ → *Windows'u tara* → izinler → görünüm).
2. The speech model (~466 MB) downloads once. / Konuşma modeli (~466 MB) bir kez indirilir.
3. Say **"Astra"**, hold **Ctrl+Alt+Space**, or type in the console. / **"Astra"** de, **Ctrl+Alt+Space** tuşlarını basılı tut veya konsola yaz.

100% offline? Install [Ollama](https://ollama.com), pull `qwen3:8b`, choose *Ollama* in *Settings ▸ AI model*.
Tamamen çevrimdışı mı? [Ollama](https://ollama.com) kur, `qwen3:8b` indir, *Ayarlar ▸ Yapay zekâ modeli*'nde *Ollama*'yı seç.

## ✨ What's included / İçindekiler

- 🎙️ Wake word, push-to-talk, local Whisper STT, Turkish + English / Uyandırma kelimesi, bas-konuş, yerel Whisper, Türkçe + İngilizce
- ⚡ Simple commands run without calling an AI model / Basit komutlar yapay zekâ çağrılmadan çalışır
- 🛡️ Asks before risky actions; deletes go to the Recycle Bin / Riskli işlerden önce sorar; silinenler Geri Dönüşüm Kutusu'na gider
- 🔌 OpenAI, Gemini, Anthropic, Mistral, DeepSeek, Ollama, OpenAI-compatible
- 👁️ Screen understanding, browser control, 35+ tools / Ekran anlama, tarayıcı kontrolü, 35+ araç
- 🎨 HUD, AI cursor, speech bubble, audio visualizer, multi-monitor / HUD, AI imleci, konuşma balonu, ses görselleştirici, çoklu monitör

## 🔧 Fixed in this release / Bu sürümde düzelenler

- "Listening" notification no longer appears for ordinary room talk, only after the wake word or push-to-talk.
  "Dinliyor" bildirimi artık odadaki sıradan konuşmada çıkmıyor; yalnızca uyandırma kelimesi veya bas-konuş sonrası çıkıyor.
- Better recognition of spoken app names ("VS Code", "krom", "diskord").
  Söylenen uygulama adları daha iyi tanınıyor ("VS Code", "krom", "diskord").

## ⚠️ Please read / Lütfen oku

**EN:** Astra is **not malware**: it has no hidden functionality or telemetry, and the full source is in this repository. Because it simulates mouse/keyboard input, uses a global hotkey and is **not code-signed**, Windows SmartScreen or your antivirus may warn about it (false positive). If unsure, build it from source or check the file on [VirusTotal](https://www.virustotal.com). Provided **"as is"**, without warranty; use at your own risk. AI can make mistakes, so review confirmation prompts and keep backups.

**TR:** Astra **zararlı yazılım değildir**: gizli işlev veya telemetri içermez, kaynak kodun tamamı bu depoda. Fare/klavye girdisi simüle ettiği, genel kısayol kullandığı ve **kod imzalı olmadığı** için Windows SmartScreen veya antivirüsün uyarı vermesi (yanlış pozitif) mümkündür. Emin değilsen kaynaktan derle veya dosyayı [VirusTotal](https://www.virustotal.com)'da kontrol et. **"Olduğu gibi"** sunulur, garanti yoktur; sorumluluk sana aittir. Yapay zekâ hata yapabilir; onay istemlerini oku ve yedek al.

---
Full documentation: see the main [README](README.md) · Tam dokümantasyon için ana [README](README.md) dosyasına bak.
