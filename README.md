<div align="center">

# ✦ Astra

**Talk to your PC. It listens, understands, and actually does it.**<br>
**Bilgisayarınla konuş. Dinler, anlar ve gerçekten yapar.**

`C# · .NET 10 · WinUI 3` &nbsp;·&nbsp; `100% native` &nbsp;·&nbsp; `local-first` &nbsp;·&nbsp; `🇹🇷 Türkçe + 🇬🇧 English`

[🇬🇧 English](#-english) &nbsp;|&nbsp; [🇹🇷 Türkçe](#-türkçe)

</div>

---

# 🇬🇧 English

Astra is **not a chatbot**. It's a voice-first desktop agent for Windows that controls your computer in natural language, checks that each step really worked, and asks before doing anything risky.

> *"Astra, open Avast Secure Browser and play Misery on YouTube."*
> *"Astra, move the PDFs in Downloads to Documents."*
> *"Astra, close Discord and open Spotify."*
> *"Astra, click the red button on the screen."*

It opens the browser, finds the video, opens it, and confirms the page actually changed. Then it tells you, in your language.

## ✨ Highlights

- 🎙️ **Truly hands-free.** Say the wake word ("Astra"), hold a push-to-talk hotkey, or just type. Speech recognition runs **locally** with Whisper (GPU-accelerated), so your voice never leaves your PC.
- 🌍 **Speaks your language.** Understands Turkish and English, replies in the language you spoke, and switches TTS voice to match.
- ⚡ **Simple things are instant.** *"Open Chrome"* is resolved on your machine in about half a second, **without calling any AI model**. The LLM only wakes up for ambiguous, multi-step, or visual tasks.
- 🧠 **The model never sees your whole PC.** Astra keeps a local index (SQLite + FTS5) of apps, games, browsers, files and folders. The AI asks small scoped questions (`search_applications`, `search_files`…) and gets back only the few rows it needs. Nothing is bulk-uploaded.
- 🛡️ **You stay in control.** Seven permission categories (ask / allow / deny). Destructive actions *always* ask: *"Astra wants to delete 147 files."* Deletes go to the Recycle Bin, and system folders are protected.
- 👁️ **Can see the screen.** "Click the red button" → screenshot → vision model → click → verify the screen changed.
- 🔌 **Bring your own AI.** OpenAI, Google Gemini, Anthropic, Mistral, DeepSeek, **Ollama (fully local)** and any OpenAI-compatible API. API keys live in **Windows Credential Manager**, never in a config file.
- 🧩 **35+ tools:** launch/close apps, windows, mouse & keyboard, Windows UI Automation, clipboard, files (bulk move/delete), shell commands, browser control (including reading, clicking and typing in pages), screen analysis, long-term memory.
- 🎨 **A UI that feels like Windows 11.** Mica, Fluent, 7 themes, your own `icon.ico`, plus optional overlays:
  - **HUD:** what Astra hears, thinks and is doing right now
  - **AI cursor** with six states (idle, listening, thinking, executing, speaking, error)
  - **Speech bubble** (off by default)
  - **Audio visualizer**, six styles, top-right, reacts to Astra's voice in real time (off by default)
  - **Multi-monitor:** pick which display shows the HUD and notifications (default: primary)
- 🧰 **Tray, first-run wizard, Start with Windows, full Turkish/English interface.**

## 🚀 Get started

1. Download `Astra-Setup-x.y.z.exe` from **Releases** (or build it yourself, below) and run it.
2. Follow the first-run wizard: microphone → wake word → AI provider → *Scan Windows* → permissions → look & feel.
3. Say **"Astra"**, hold **Ctrl+Alt+Space**, or type in the console.

**Want 100% local?** Install [Ollama](https://ollama.com), pull a model with tool support (e.g. `qwen3:8b`), pick *Ollama* in *Settings ▸ AI model*. Astra detects it automatically.

## 🔨 Build from source

Requirements: Windows 10 19041+ / Windows 11, [.NET 10 SDK](https://dotnet.microsoft.com/download), optionally [Inno Setup](https://jrsoftware.org/isinfo.php) for the installer.

```powershell
.\build.ps1                  # publish + installer  -> dist\Astra-Setup-<version>.exe
.\build.ps1 -SkipInstaller   # self-contained folder -> dist\Astra\Astra.exe
.\build.ps1 -Test            # run the unit tests first
```

Your icon: put an `icon.ico` in `src\Astra.App\` (it replaces the placeholder) and rebuild. It becomes the exe, window, tray, installer, HUD and cursor icon.

## 🏗️ Architecture

```
src/Astra.Core   Settings · Providers · Llm · Index (SQLite/FTS5) · Control (Win32/UIA) · Browser (CDP)
                 Tools · Agent · Memory · Voice (Whisper/VAD/TTS) · Assistant (core, vision)
src/Astra.App    WinUI 3: windows, settings, overlays, tray, onboarding
tests/           unit tests
installer/       Inno Setup script
```

Design rule: **the LLM reasons about the task; it doesn't store a map of your computer.**

## 🔒 Privacy at a glance

- Speech-to-text, wake word and the file/app index are **local**.
- Only your request and the minimal result of each tool call go to the AI provider you chose (nothing, if you use Ollama).
- Memories are local, viewable, deletable, and Astra refuses to store passwords, keys or card numbers.
- Data lives in `%AppData%\Astra`.

## 🤝 Contributing

Issues and pull requests are welcome. Please run `.\build.ps1 -Test` before submitting.

## ⚠️ Disclaimer & security notes

- **Not malware.** Astra contains no hidden functionality, telemetry, miner, keylogger or remote-access code. The complete source is in this repository so you can read it, and you're encouraged to build it yourself instead of trusting a binary.
- **Why antivirus or SmartScreen may complain.** Astra legitimately does things that security tools watch closely: it simulates mouse and keyboard input, registers a global push-to-talk hotkey, reads the screen on request, launches processes, and can run commands. The installer is **not code-signed**, so Windows SmartScreen may show an "unknown publisher" warning. False positives are possible. If in doubt, build from source or scan the file with an antivirus or [VirusTotal](https://www.virustotal.com).
- **No warranty.** This software is provided **"as is"**, without warranty of any kind. You use it at your own risk. The authors are not liable for any damage, data loss or other consequences, including actions taken by an AI model.
- **AI can be wrong.** Models make mistakes and misunderstand requests. Astra verifies actions and asks before risky ones, but keep important data backed up, and review confirmation prompts instead of approving them blindly. Voice recognition can mishear.
- **Powerful by design.** Astra can control your computer. Review *Settings ▸ Security* and only grant the permissions you're comfortable with. Don't enable "always allow" for Terminal or Power unless you understand the risk.
- **Third-party services.** If you choose a cloud AI or the OpenAI voice, the relevant text, and screenshots when you use screen analysis, are sent to that provider under *its* terms and pricing. You are responsible for your API usage and costs.
- **Trademarks.** Product names (Windows, Chrome, Spotify, Discord, Valorant, OpenAI, Anthropic, Google, Mistral, DeepSeek, Ollama…) belong to their owners. Astra is an independent project and is not affiliated with or endorsed by them.

---

# 🇹🇷 Türkçe

Astra bir **chatbot değil**. Windows için, sesle çalışan bir masaüstü ajanı: bilgisayarını doğal dille kontrol eder, her adımın gerçekten işe yaradığını doğrular ve riskli bir şey yapmadan önce sana sorar.

> *"Astra, Avast Secure Browser'ı aç ve YouTube'da Misery şarkısını aç."*
> *"Astra, İndirilenler'deki PDF'leri Belgeler'e taşı."*
> *"Astra, Discord'u kapat ve Spotify'ı aç."*
> *"Astra, ekrandaki kırmızı butona tıkla."*

Tarayıcıyı açar, videoyu bulur, açar ve sayfanın gerçekten değiştiğini doğrular. Sonra sana kendi dilinde haber verir.

## ✨ Öne çıkanlar

- 🎙️ **Gerçekten eller serbest.** Uyandırma kelimesini söyle ("Astra"), bas-konuş tuşunu basılı tut veya yaz. Konuşma tanıma **bilgisayarında** Whisper ile çalışır (GPU destekli); sesin PC'nden çıkmaz.
- 🌍 **Senin dilini konuşur.** Türkçe ve İngilizceyi anlar, konuştuğun dilde yanıt verir, TTS sesini de buna göre değiştirir.
- ⚡ **Basit işler anında.** *"Chrome'u aç"* gibi komutlar **hiçbir yapay zekâ modeli çağrılmadan** yaklaşık yarım saniyede çözülür. LLM yalnızca belirsiz, çok adımlı veya görsel işlerde devreye girer.
- 🧠 **Model tüm bilgisayarını görmez.** Astra uygulamalar, oyunlar, tarayıcılar, dosyalar ve klasörler için yerel bir dizin tutar (SQLite + FTS5). Yapay zekâ küçük, kapsamlı sorular sorar (`search_applications`, `search_files`…) ve yalnızca ihtiyacı olan birkaç satırı alır. Hiçbir şey toplu gönderilmez.
- 🛡️ **Kontrol sende.** Yedi izin kategorisi (sor / izin ver / reddet). Yıkıcı işlemler *her zaman* sorar: *"Astra 147 dosyayı silmek istiyor."* Silinenler Geri Dönüşüm Kutusu'na gider, sistem klasörleri korunur.
- 👁️ **Ekranı görebilir.** "Kırmızı butona tıkla" → ekran görüntüsü → görüntü modeli → tıkla → ekranın değiştiğini doğrula.
- 🔌 **İstediğin yapay zekâyı kullan.** OpenAI, Google Gemini, Anthropic, Mistral, DeepSeek, **Ollama (tamamen yerel)** ve OpenAI uyumlu her API. API anahtarların ayar dosyasında değil, **Windows Kimlik Bilgisi Yöneticisi**'nde durur.
- 🧩 **35+ araç:** uygulama aç/kapat, pencereler, fare ve klavye, Windows UI Automation, pano, dosyalar (toplu taşıma/silme), komut çalıştırma, tarayıcı kontrolü (sayfa okuma, tıklama, yazma dahil), ekran analizi, uzun süreli bellek.
- 🎨 **Windows 11 hissi veren arayüz.** Mica, Fluent, 7 tema, kendi `icon.ico` dosyan ve isteğe bağlı katmanlar:
  - **HUD:** Astra'nın ne duyduğu, ne düşündüğü ve şu an ne yaptığı
  - Altı durumlu **AI imleci** (boşta, dinliyor, düşünüyor, yürütüyor, konuşuyor, hata)
  - **Konuşma balonu** (varsayılan kapalı)
  - **Ses görselleştirici:** altı stil, sağ üstte, Astra'nın sesine göre gerçek zamanlı hareket eder (varsayılan kapalı)
  - **Çoklu monitör:** HUD ve bildirimlerin hangi ekranda görüneceğini seç (varsayılan: ana ekran)
- 🧰 **Sistem tepsisi, ilk açılış sihirbazı, Windows ile başlatma, tamamen Türkçe/İngilizce arayüz.**

## 🚀 Başlarken

1. **Releases** sayfasından `Astra-Setup-x.y.z.exe` dosyasını indir (veya aşağıdan kendin derle) ve çalıştır.
2. İlk açılış sihirbazını izle: mikrofon → uyandırma kelimesi → yapay zekâ sağlayıcısı → *Windows'u tara* → izinler → görünüm.
3. **"Astra"** de, **Ctrl+Alt+Space** tuşlarını basılı tut veya konsola yaz.

**%100 yerel mi istiyorsun?** [Ollama](https://ollama.com) kur, araç çağırmayı destekleyen bir model indir (örn. `qwen3:8b`) ve *Ayarlar ▸ Yapay zekâ modeli*'nde *Ollama*'yı seç. Astra onu otomatik algılar.

## 🔨 Kaynaktan derleme

Gereksinimler: Windows 10 19041+ / Windows 11, [.NET 10 SDK](https://dotnet.microsoft.com/download), kurulum paketi için isteğe bağlı [Inno Setup](https://jrsoftware.org/isinfo.php).

```powershell
.\build.ps1                  # yayınla + kurulum paketi -> dist\Astra-Setup-<sürüm>.exe
.\build.ps1 -SkipInstaller   # kendi içinde çalışan klasör -> dist\Astra\Astra.exe
.\build.ps1 -Test            # önce birim testlerini çalıştır
```

Kendi ikonun: `icon.ico` dosyanı `src\Astra.App\` içine koy (yer tutucunun üzerine yazar) ve yeniden derle. Exe, pencere, tepsi, kurulum paketi, HUD ve imleç ikonu olur.

## 🏗️ Mimari

```
src/Astra.Core   Ayarlar · Sağlayıcılar · Llm · Dizin (SQLite/FTS5) · Kontrol (Win32/UIA) · Tarayıcı (CDP)
                 Araçlar · Ajan · Bellek · Ses (Whisper/VAD/TTS) · Asistan (çekirdek, görüntü)
src/Astra.App    WinUI 3: pencereler, ayarlar, katmanlar, tepsi, sihirbaz
tests/           birim testleri
installer/       Inno Setup betiği
```

Tasarım kuralı: **LLM görev hakkında akıl yürütür; bilgisayarının haritasını tutmaz.**

## 🔒 Gizlilik özeti

- Konuşma tanıma, uyandırma kelimesi ve dosya/uygulama dizini **yereldir**.
- Seçtiğin yapay zekâ sağlayıcısına yalnızca isteğin ve her araç çağrısının asgari sonucu gider (Ollama kullanırsan hiçbir şey gitmez).
- Anılar yereldir, görüntülenebilir ve silinebilir; Astra parola, anahtar veya kart numarası saklamayı reddeder.
- Veriler `%AppData%\Astra` içinde durur.

## 🤝 Katkı

Sorunlar (issue) ve pull request'ler memnuniyetle karşılanır. Göndermeden önce `.\build.ps1 -Test` çalıştırın.

## ⚠️ Sorumluluk reddi ve güvenlik notları

- **Zararlı yazılım değildir.** Astra gizli işlev, telemetri, madenci, keylogger veya uzaktan erişim kodu içermez. Kaynak kodun tamamı bu depoda; okuyabilirsin. Hazır bir dosyaya güvenmek yerine kendin derlemen teşvik edilir.
- **Antivirüs veya SmartScreen neden uyarabilir?** Astra, güvenlik araçlarının yakından izlediği şeyleri meşru olarak yapar: fare ve klavye girdisi simüle eder, genel bir bas-konuş kısayolu kaydeder, istendiğinde ekranı okur, işlem başlatır ve komut çalıştırabilir. Kurulum paketi **kod imzalı değildir**; bu yüzden Windows SmartScreen "bilinmeyen yayıncı" uyarısı gösterebilir. Yanlış pozitifler olabilir. Şüphen varsa kaynaktan derle veya dosyayı bir antivirüsle ya da [VirusTotal](https://www.virustotal.com) ile tara.
- **Garanti yoktur.** Bu yazılım **"olduğu gibi"** sunulur, hiçbir türde garanti verilmez. Kullanım riski sana aittir. Yapay zekâ modelinin yaptığı eylemler dahil, oluşabilecek hiçbir zarar, veri kaybı veya sonuçtan yazarlar sorumlu değildir.
- **Yapay zekâ yanılabilir.** Modeller hata yapar ve isteği yanlış anlayabilir. Astra eylemleri doğrular ve riskli olanlardan önce sorar; yine de önemli verilerini yedekle ve onay isteklerini gözü kapalı onaylamak yerine oku. Ses tanıma yanlış duyabilir.
- **Tasarımı gereği güçlüdür.** Astra bilgisayarını kontrol edebilir. *Ayarlar ▸ Güvenlik*'i incele ve yalnızca rahat olduğun izinleri ver. Riskini anlamadıkça Terminal veya Güç için "her zaman izin ver"i açma.
- **Üçüncü taraf hizmetler.** Bulut yapay zekâsı veya OpenAI sesi seçersen ilgili metin, ve ekran analizi kullandığında ekran görüntüleri, o sağlayıcıya **onun** koşulları ve fiyatlandırmasıyla gönderilir. API kullanımından ve maliyetinden sen sorumlusun.
- **Ticari markalar.** Ürün adları (Windows, Chrome, Spotify, Discord, Valorant, OpenAI, Anthropic, Google, Mistral, DeepSeek, Ollama…) sahiplerine aittir. Astra bağımsız bir projedir; bunlarla bağlantılı değildir ve onlar tarafından desteklenmez.
