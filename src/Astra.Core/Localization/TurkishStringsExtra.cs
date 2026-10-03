namespace Astra.Core.Localization;

/// <summary>Turkish strings for the assistant, overlays, voice, onboarding and tools (added after the settings pages).</summary>
internal static class TurkishStringsExtra
{
    public static readonly Dictionary<string, string> Map = new()
    {
        // Displays
        ["Display {0}"] = "Ekran {0}", ["Primary"] = "Ana ekran", ["Primary display (default)"] = "Ana ekran (varsayılan)",

        // Voice page
        ["Test"] = "Test", ["Stop"] = "Durdur", ["Windows default"] = "Windows varsayılanı", ["Auto (Turkish / English)"] = "Otomatik (Türkçe / İngilizce)",
        ["Input device for the wake word and voice commands."] = "Uyandırma kelimesi ve sesli komutlar için giriş aygıtı.",
        ["Astra only reacts to commands that start with this word."] = "Astra yalnızca bu kelimeyle başlayan komutlara yanıt verir.",
        ["Hold to speak, release to send. Works in both activation modes."] = "Konuşmak için basılı tut, göndermek için bırak. Her iki etkinleştirme modunda da çalışır.",
        ["Change…"] = "Değiştir…",
        ["Speech model"] = "Konuşma modeli",
        ["Runs on this PC with Whisper; your voice never leaves it. Download once."] = "Whisper ile bu bilgisayarda çalışır; sesin bilgisayarından çıkmaz. Bir kez indirilir.",
        ["Recognition language"] = "Tanıma dili",
        ["Auto understands Turkish and English and replies in the language you spoke."] = "Otomatik mod Türkçe ve İngilizceyi anlar ve konuştuğun dilde yanıt verir.",
        ["Windows voices run locally. OpenAI voices need your OpenAI key and send the reply text to OpenAI."] = "Windows sesleri yerelde çalışır. OpenAI sesleri OpenAI anahtarını gerektirir ve yanıt metnini OpenAI'a gönderir.",
        ["Astra switches to a Turkish or English voice automatically to match the reply language."] = "Astra, yanıt diline uymak için otomatik olarak Türkçe veya İngilizce sese geçer.",
        ["Windows voices (local)"] = "Windows sesleri (yerel)", ["OpenAI voices (external)"] = "OpenAI sesleri (harici)",
        ["Installed ✓"] = "Yüklü ✓", ["Download ({0} MB)"] = "İndir ({0} MB)",
        ["Speech model ready. Astra can hear you now."] = "Konuşma modeli hazır. Astra artık seni duyabilir.",
        ["Download failed: {0}"] = "İndirme başarısız: {0}", ["Problem"] = "Sorun",
        ["Press the keys you want to hold…"] = "Basılı tutmak istediğin tuşlara bas…", ["Use this"] = "Bunu kullan",
        ["Could not open the microphone: {0}"] = "Mikrofon açılamadı: {0}",
        ["Tiny"] = "Minik", ["Base"] = "Temel", ["Small"] = "Küçük",
        ["Fastest, lowest accuracy"] = "En hızlı, en düşük doğruluk", ["Balanced"] = "Dengeli", ["Best accuracy, recommended (default)"] = "En iyi doğruluk, önerilen (varsayılan)",
        ["The speech model is not downloaded yet (Settings ▸ Voice)."] = "Konuşma modeli henüz indirilmedi (Ayarlar ▸ Ses).",
        ["Speech model"] = "Konuşma modeli",

        // AI page extras
        ["Vision model"] = "Görüntü modeli", ["Model used for screen analysis. Leave empty for a sensible default."] = "Ekran analizi için kullanılan model. Uygun bir varsayılan için boş bırak.",
        ["Automatic"] = "Otomatik",

        // Appearance
        ["Astra icon"] = "Astra simgesi", ["Preview overlays"] = "Katmanları önizle", ["Preview"] = "Önizle",
        ["Shows the HUD, AI cursor, notification, speech bubble and visualizer with sample content."] = "HUD'u, yapay zekâ imlecini, bildirimi, konuşma balonunu ve görselleştiriciyi örnek içerikle gösterir.",
        ["HUD display"] = "HUD ekranı",
        ["Which screen the HUD, speech bubble, visualizer and permission prompts appear on. Default is the primary display."] = "HUD'un, konuşma balonunun, görselleştiricinin ve izin istemlerinin hangi ekranda görüneceği. Varsayılan ana ekrandır.",
        ["Speech bubble"] = "Konuşma balonu", ["Shows what Astra says in a bubble on screen. Off by default."] = "Astra'nın söylediklerini ekranda bir balonda gösterir. Varsayılan olarak kapalı.",
        ["Bubble position"] = "Balon konumu",
        ["Spectrum that moves with Astra’s voice in real time. Off by default; sits at the top right of the screen."] = "Astra'nın sesiyle gerçek zamanlı hareket eden spektrum. Varsayılan olarak kapalı; ekranın sağ üstünde durur.",
        ["Notifications"] = "Bildirimler", ["Astra’s own pop-ups when a task completes or fails."] = "Bir görev tamamlandığında veya başarısız olduğunda Astra'nın kendi bildirimleri.",
        ["Notification display"] = "Bildirim ekranı", ["Which screen notifications appear on. Default is the primary display."] = "Bildirimlerin hangi ekranda görüneceği. Varsayılan ana ekrandır.",
        ["Notification position"] = "Bildirim konumu",
        ["Browser opened"] = "Tarayıcı açıldı", ["Finding “Misery” on YouTube…"] = "YouTube'da “Misery” bulunuyor…",
        ["Open the browser and play Misery"] = "Tarayıcıyı aç ve Misery'yi çal",
        ["This is how notifications look."] = "Bildirimler böyle görünür.", ["This is how Astra's speech bubble looks."] = "Astra'nın konuşma balonu böyle görünür.",

        // Home & status
        ["Type a command…"] = "Bir komut yaz…", ["Listening"] = "Dinliyor", ["Start listening"] = "Dinlemeye başla",
        ["Say “{0}” followed by a command, hold {1} to talk, or type below."] = "“{0}” deyip komutunu söyle, konuşmak için {1} tuşlarını basılı tut veya aşağıya yaz.",
        ["Listening…"] = "Dinliyor…", ["Thinking…"] = "Düşünüyor…", ["Working…"] = "Çalışıyor…", ["Speaking…"] = "Konuşuyor…", ["Understanding…"] = "Anlıyor…",
        ["Done"] = "Tamamlandı", ["Something went wrong"] = "Bir şeyler ters gitti", ["Ready"] = "Hazır", ["Paused"] = "Duraklatıldı",
        ["Ready — say “{0}”"] = "Hazır — “{0}” de",
        ["Open Notepad"] = "Not Defteri'ni aç", ["Play Misery on YouTube"] = "YouTube'da Misery'yi çal", ["Find my PDF files in Downloads"] = "İndirilenler'deki PDF dosyalarımı bul", ["What can you do?"] = "Neler yapabilirsin?",
        ["Choose an AI model"] = "Bir yapay zekâ modeli seç", ["Pick a model so Astra can handle requests that need reasoning."] = "Astra'nın akıl yürütme gerektiren isteklere yanıt verebilmesi için bir model seç.",
        ["Add your {0} API key, or switch to Ollama to run locally."] = "{0} API anahtarını ekle veya yerelde çalışmak için Ollama'ya geç.",
        ["Download the speech model"] = "Konuşma modelini indir", ["Astra listens with a local model. It is downloaded once and nothing leaves your PC."] = "Astra yerel bir modelle dinler. Bir kez indirilir ve hiçbir şey bilgisayarından çıkmaz.",
        ["Let Astra index your apps and files so it can open and find them instantly."] = "Astra'nın uygulamalarını ve dosyalarını dizinlemesine izin ver; böylece onları anında açıp bulabilir.",
        ["Set up"] = "Kur",

        // Index page
        ["Astra needs to learn about your computer."] = "Astra'nın bilgisayarını tanıması gerekiyor.",
        ["You can scan your Windows installation so Astra can find applications, files and folders quickly. Only names, types, sizes and dates are indexed, and the index never leaves this PC."] =
            "Astra'nın uygulamaları, dosyaları ve klasörleri hızlıca bulabilmesi için Windows kurulumunu tarayabilirsin. Yalnızca adlar, türler, boyutlar ve tarihler dizinlenir; dizin bu bilgisayardan asla çıkmaz.",
        ["Rescan Windows"] = "Windows'u yeniden tara", ["Scanning Windows…"] = "Windows taranıyor…", ["Building search index…"] = "Arama dizini oluşturuluyor…",
        ["{0} apps · {1} folders · {2} files found so far"] = "{0} uygulama · {1} klasör · {2} dosya bulundu",
        ["{0} apps · {1} folders · {2} files{3}"] = "{0} uygulama · {1} klasör · {2} dosya{3}", ["scanned {0}"] = "{0} tarihinde tarandı",
        ["Delete the index"] = "Dizini sil", ["Removes the local index. Astra can’t find apps and files until you scan again."] = "Yerel dizini kaldırır. Yeniden tarayana kadar Astra uygulamaları ve dosyaları bulamaz.",
        ["Delete…"] = "Sil…", ["Delete the index?"] = "Dizin silinsin mi?", ["Astra will not be able to find apps and files until you scan again."] = "Yeniden tarayana kadar Astra uygulamaları ve dosyaları bulamayacak.",
        ["Index status"] = "Dizin durumu", ["Applies on the next scan."] = "Bir sonraki taramada uygulanır.",
        ["Index file metadata"] = "Dosya meta verilerini dizinle",
        ["Names, types, sizes and dates only. File contents are never read unless a task needs them. Turn off to index apps and folders only."] = "Yalnızca adlar, türler, boyutlar ve tarihler. Bir görev gerektirmedikçe dosya içerikleri asla okunmaz. Yalnızca uygulamaları ve klasörleri dizinlemek için kapat.",
        ["Anything inside these folders is invisible to search. Applies on the next scan."] = "Bu klasörlerin içindeki her şey aramada görünmez. Bir sonraki taramada uygulanır.",
        ["Data"] = "Veri", ["Windows scan complete"] = "Windows taraması tamamlandı", ["Windows scan failed"] = "Windows taraması başarısız",
        ["{0} apps, {1} folders and {2} files are ready to search."] = "{0} uygulama, {1} klasör ve {2} dosya aramaya hazır.",

        // Memory page
        ["Add a memory"] = "Anı ekle", ["Or just tell Astra “remember that…”. Passwords, keys and card numbers are never saved."] = "Veya Astra'ya “şunu hatırla…” de. Parolalar, anahtarlar ve kart numaraları asla kaydedilmez.",
        ["e.g. My preferred browser is Avast Secure Browser"] = "örn. Tercih ettiğim tarayıcı Avast Secure Browser", ["Add"] = "Ekle",
        ["Nothing saved yet."] = "Henüz kayıtlı bir şey yok.", ["Forget everything"] = "Her şeyi unut", ["Delete every saved memory."] = "Kayıtlı tüm anıları sil.", ["Clear all…"] = "Tümünü temizle…",
        ["Saved"] = "Kaydedildi", ["Not saved"] = "Kaydedilmedi", ["Forget everything?"] = "Her şey unutulsun mu?", ["Every saved memory will be deleted."] = "Kayıtlı her anı silinecek.", ["Delete all"] = "Tümünü sil",
        ["Delete"] = "Sil",
        ["Memory is turned off in settings."] = "Bellek ayarlardan kapatılmış.", ["Nothing to remember."] = "Hatırlanacak bir şey yok.",
        ["That looks sensitive (password, key, card or ID number), so it was not saved."] = "Bu hassas görünüyor (parola, anahtar, kart veya kimlik numarası); bu yüzden kaydedilmedi.",
        ["Saved."] = "Kaydedildi.", ["Already remembered."] = "Zaten hatırlıyorum.",

        // Onboarding
        ["Welcome to Astra"] = "Astra'ya hoş geldin", ["Back"] = "Geri", ["Next"] = "İleri", ["Skip"] = "Atla", ["Get started"] = "Başla", ["Start using Astra"] = "Astra'yı kullanmaya başla",
        ["Choose your microphone, wake word and speech engine."] = "Mikrofonunu, uyandırma kelimeni ve konuşma motorunu seç.",
        ["Connect a cloud provider, or run models locally with Ollama."] = "Bir bulut sağlayıcısına bağlan veya Ollama ile modelleri yerelde çalıştır.",
        ["Let Astra learn your apps and files — locally, never sent to an AI model."] = "Astra'nın uygulamalarını ve dosyalarını öğrenmesine izin ver — yerelde, hiçbir zaman bir yapay zekâ modeline gönderilmez.",
        ["Decide what Astra may do without asking."] = "Astra'nın sormadan neler yapabileceğine karar ver.", ["Pick a theme and what Astra shows on screen."] = "Bir tema seç ve Astra'nın ekranda neler göstereceğini belirle.",
        ["You're all set"] = "Her şey hazır",
        ["Astra is your natural-language interface to Windows. Talk to it, and it opens apps, finds files, browses the web and gets things done — asking you first when it matters. This quick setup takes about two minutes, and every choice can be changed later in Settings."] =
            "Astra, Windows'a doğal dille erişim arayüzün. Onunla konuş; uygulamaları açar, dosyaları bulur, web'de gezer ve işleri halleder — önemli olduğunda önce sana sorar. Bu hızlı kurulum yaklaşık iki dakika sürer ve her seçim daha sonra Ayarlar'dan değiştirilebilir.",
        ["Astra is ready. Say its wake word, hold the push-to-talk key, or type in the console. You can change anything in Settings at any time."] =
            "Astra hazır. Uyandırma kelimesini söyle, bas-konuş tuşunu basılı tut veya konsola yaz. Her şeyi istediğin zaman Ayarlar'dan değiştirebilirsin.",

        // Tools (HUD lines)
        ["Opening {0}…"] = "{0} açılıyor…", ["Closing {0}…"] = "{0} kapatılıyor…", ["Looking for {0}…"] = "{0} aranıyor…",
        ["Searching files: {0}"] = "Dosyalar aranıyor: {0}", ["Searching folders: {0}"] = "Klasörler aranıyor: {0}", ["Checking running processes…"] = "Çalışan işlemler kontrol ediliyor…",
        ["Looking at open windows…"] = "Açık pencerelere bakılıyor…", ["Window: {0} {1}"] = "Pencere: {0} {1}", ["Finding the browser…"] = "Tarayıcı bulunuyor…", ["Reading system information…"] = "Sistem bilgisi okunuyor…",
        ["Searching for “{0}”…"] = "“{0}” aranıyor…", ["Finding “{0}” on YouTube…"] = "YouTube'da “{0}” bulunuyor…", ["Reading the page…"] = "Sayfa okunuyor…", ["Clicking {0}…"] = "{0} tıklanıyor…",
        ["Typing in the page…"] = "Sayfaya yazılıyor…", ["Managing tabs…"] = "Sekmeler yönetiliyor…", ["Looking at the page…"] = "Sayfaya bakılıyor…", ["Clicking…"] = "Tıklanıyor…",
        ["Double-clicking…"] = "Çift tıklanıyor…", ["Moving the mouse…"] = "Fare hareket ettiriliyor…", ["Scrolling…"] = "Kaydırılıyor…", ["Typing…"] = "Yazılıyor…", ["Pressing {0}…"] = "{0} tuşuna basılıyor…",
        ["Taking a screenshot…"] = "Ekran görüntüsü alınıyor…", ["Looking at the screen…"] = "Ekrana bakılıyor…", ["Reading the clipboard…"] = "Pano okunuyor…", ["Copying to the clipboard…"] = "Panoya kopyalanıyor…",
        ["Creating {0}…"] = "{0} oluşturuluyor…", ["Reading {0}…"] = "{0} okunuyor…", ["Listing {0}…"] = "{0} listeleniyor…", ["Moving {0}…"] = "{0} taşınıyor…", ["Copying {0}…"] = "{0} kopyalanıyor…",
        ["Deleting {0}…"] = "{0} siliniyor…", ["Moving files to {0}…"] = "Dosyalar {0} konumuna taşınıyor…", ["Deleting files in {0}…"] = "{0} içindeki dosyalar siliniyor…",
        ["Running a command…"] = "Komut çalıştırılıyor…", ["Volume {0}…"] = "Ses: {0}…", ["Power: {0}…"] = "Güç: {0}…", ["Saving to memory…"] = "Belleğe kaydediliyor…", ["Updating memory…"] = "Bellek güncelleniyor…", ["Waiting…"] = "Bekleniyor…",

        // Confirmations
        ["Astra wants to force-close {0}. Unsaved work may be lost."] = "Astra {0} uygulamasını zorla kapatmak istiyor. Kaydedilmemiş işler kaybolabilir.",
        ["Astra wants to delete {0}."] = "Astra {0} öğesini silmek istiyor.", ["Astra wants to move {0} files from {1} to {2}."] = "Astra {0} dosyayı {1} konumundan {2} konumuna taşımak istiyor.",
        ["Astra wants to delete {0} files."] = "Astra {0} dosyayı silmek istiyor.", ["Astra wants to run this command: {0}"] = "Astra şu komutu çalıştırmak istiyor: {0}",
        ["Astra wants to lock this computer."] = "Astra bilgisayarı kilitlemek istiyor.", ["Astra wants to put this computer to sleep."] = "Astra bilgisayarı uyku moduna almak istiyor.",
        ["Astra wants to sign you out."] = "Astra oturumunu kapatmak istiyor.", ["Astra wants to restart this computer."] = "Astra bilgisayarı yeniden başlatmak istiyor.",
        ["Astra wants to shut down this computer."] = "Astra bilgisayarı kapatmak istiyor.",
        ["Permission: {0}"] = "İzin: {0}", ["Astra needs your approval"] = "Astra'nın onayına ihtiyacı var", ["Allow"] = "İzin ver", ["This cannot be undone."] = "Bu işlem geri alınamaz.",

        // Assistant / provider messages
        ["Astra is still working on the previous request."] = "Astra önceki istek üzerinde hâlâ çalışıyor.", ["Cancelled."] = "İptal edildi.", ["Task complete"] = "Görev tamamlandı", ["Task failed"] = "Görev başarısız",
        ["Open {0}"] = "{0} uygulamasını aç", ["Start Listening"] = "Dinlemeye başla", ["Stop Listening"] = "Dinlemeyi durdur", ["Pause Assistant"] = "Asistanı duraklat", ["Exit"] = "Çıkış",
        ["The provider rejected the API key ({0})."] = "Sağlayıcı API anahtarını reddetti ({0}).", ["The provider is rate limiting requests: {0}"] = "Sağlayıcı istekleri sınırlıyor: {0}",
        ["The provider returned {0}: {1}"] = "Sağlayıcı {0} döndürdü: {1}", ["The model took too long to respond."] = "Model yanıt vermekte çok gecikti.", ["Could not reach the model: {0}"] = "Modele ulaşılamadı: {0}",
        ["No model is selected. Choose one under Settings ▸ AI model."] = "Model seçilmemiş. Ayarlar ▸ Yapay zekâ modeli bölümünden birini seç.",
        ["No API key is saved for {0}. Add it under Settings ▸ AI model."] = "{0} için kayıtlı API anahtarı yok. Ayarlar ▸ Yapay zekâ modeli bölümünden ekle.",
        ["The model “{0}” does not support tool calling. Pick another Ollama model (for example qwen3 or llama3.1)."] = "“{0}” modeli araç çağırmayı desteklemiyor. Başka bir Ollama modeli seç (örneğin qwen3 veya llama3.1).",
        ["The current model “{0}” cannot see images. Choose a vision-capable model or a separate vision provider under Settings ▸ AI model."] = "Mevcut model “{0}” görüntüleri göremiyor. Ayarlar ▸ Yapay zekâ modeli bölümünden görüntü destekli bir model veya ayrı bir görüntü sağlayıcısı seç.",
        ["No vision-capable Ollama model is installed (for example gemma3 or llava)."] = "Görüntü destekli bir Ollama modeli kurulu değil (örneğin gemma3 veya llava).",
        ["Choose a vision model under Settings ▸ AI model."] = "Ayarlar ▸ Yapay zekâ modeli bölümünden bir görüntü modeli seç.",
    };
}
