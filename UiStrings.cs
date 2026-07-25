namespace FPSOverlay
{
    /// <summary>All the UI strings for every language we support. one big vibes dictionary.</summary>
    public sealed class UiStrings
    {
        public string LanguageCode { get; init; } = "EN";

        // the window chrome / shell bits
        public string Title { get; init; } = "Mars FPS Monitor - Control Panel";
        public string BrandSub { get; init; } = "FPS Monitor · Control Panel";
        public string NavHeader { get; init; } = "SETTINGS";
        public string NavOverlay { get; init; } = "Overlay";
        public string NavSensors { get; init; } = "Sensors";
        public string NavDisplay { get; init; } = "Display";
        public string NavOverclock { get; init; } = "Overclock";
        public string NavAbout { get; init; } = "About";

        // page titles + blurbs
        public string PageOverlay { get; init; } = "Overlay";
        public string PageOverlayDesc { get; init; } = "Choose how the in-game overlay looks and behaves.";
        public string PageSensors { get; init; } = "Sensors";
        public string PageSensorsDesc { get; init; } = "Pick which hardware metrics appear on the overlay.";
        public string PageDisplay { get; init; } = "Display";
        public string PageDisplayDesc { get; init; } = "Color, size, and on-screen placement.";
        public string PageOverclock { get; init; } = "Overclock";
        public string PageOverclockDesc { get; init; } = "Control mode, live status, AI suggestions, and your profiles.";
        public string PageAbout { get; init; } = "About";
        public string PageAboutDesc { get; init; } = "Product info and community.";
        public string AboutBody { get; init; } = "A lightweight, injection-free performance overlay for Windows.";
        public string AboutVersion { get; init; } = "v1.0";
        public string BrandName { get; init; } = "Mars FPS Monitor";
        public string CheckUpdates { get; init; } = "Check for updates";
        public string UpdateChecking { get; init; } = "Checking for updates…";
        public string UpdateLatest { get; init; } = "You're on the latest version.";
        public string UpdateAvailable { get; init; } = "A new version is available: {0}";
        public string UpdateFailed { get; init; } = "Couldn't check for updates.";
        public string UpdateNoRelease { get; init; } = "No releases published yet.";
        public string UpdateOpenRelease { get; init; } = "Open download page";

        // all the everyday toggles people actually spam
        public string Lang { get; init; } = "LANGUAGE";
        public string Profile { get; init; } = "OVERLAY PROFILE";
        public string GpuSelect { get; init; } = "ACTIVE GPU";
        public string Appearance { get; init; } = "APPEARANCE";
        public string OverlayColor { get; init; } = "Overlay color";
        public string CustomColor { get; init; } = "Pick color";
        public string FontSize { get; init; } = "Size";
        public string Position { get; init; } = "POSITION";
        public string Padding { get; init; } = "Padding";
        public string Sensors { get; init; } = "METRICS";
        public string OverlayCtrl { get; init; } = "OVERLAY";
        public string OverlayToggle { get; init; } = "Show / hide on screen";
        public string ShowGpuName { get; init; } = "Show GPU Name";
        public string ShowFps { get; init; } = "FPS";
        public string ShowFrametime { get; init; } = "Frametime";
        public string ShowOnePercent { get; init; } = "1% Low";
        public string ShowCpu { get; init; } = "CPU Temp";
        public string ShowCpuLoad { get; init; } = "CPU Load";
        public string ShowGpu { get; init; } = "GPU Temp";
        public string ShowGpuLoad { get; init; } = "GPU Load";
        public string ShowRam { get; init; } = "RAM Usage";
        public string ShowVram { get; init; } = "VRAM Usage";
        public string ShowOc { get; init; } = "Overclock Status";
        public string ShowClock { get; init; } = "Clock";
        public string PosUnlock { get; init; } = "Unlock position (drag)";
        public string Preview { get; init; } = "LIVE PREVIEW";

        // overclock panel copy — spicy stuff
        public string OcModeHeader { get; init; } = "CONTROL MODE";
        public string OcOff { get; init; } = "Off";
        public string OcAuto { get; init; } = "Auto";
        public string OcManual { get; init; } = "Manual";
        public string OcManualProfile { get; init; } = "Fixed profile";
        public string OcActiveHeader { get; init; } = "ACTIVE NOW";
        public string OcActiveProfile { get; init; } = "Active profile";
        public string OcGpuTemp { get; init; } = "GPU temperature";
        public string OcHotspot { get; init; } = "Hotspot";
        public string OcApplied { get; init; } = "Applied values";
        public string OcCore { get; init; } = "Core";
        public string OcMem { get; init; } = "Memory";
        public string OcPower { get; init; } = "Power";
        public string OcPowerStock { get; init; } = "stock";
        public string OcRestore { get; init; } = "Turn off & restore";

        public string AiHeader { get; init; } = "AI ASSISTANT";
        public string AiDesc { get; init; } = "Conservative Eco / Performance / Extreme suggestions. Nothing is applied until you save.";
        public string AiAsk { get; init; } = "Get AI suggestion";
        public string AiSaveAll { get; init; } = "Save all";
        public string AiSaveOne { get; init; } = "Save";
        public string AiIdle { get; init; } = "Suggestions appear here after you ask.";
        public string AiLoading { get; init; } = "Preparing suggestions…";
        public string AiReady { get; init; } = "Suggestions ready — review and save if you like them.";
        public string AiSavedOne { get; init; } = "Profile saved. Enable Auto or Manual to use it.";
        public string AiSavedAll { get; init; } = "Profiles saved. Enable Auto or Manual to use them.";
        public string AiPrefetched { get; init; } = "AI suggestions ready.";

        public string ProfilesHeader { get; init; } = "MY PROFILES";
        public string EditHeader { get; init; } = "EDIT";
        public string FieldName { get; init; } = "Name";
        public string FieldMin { get; init; } = "Min °C";
        public string FieldMax { get; init; } = "Max °C";
        public string FieldCore { get; init; } = "Core +MHz";
        public string FieldMem { get; init; } = "Mem +MHz";
        public string FieldPower { get; init; } = "Power %";
        public string FieldPowerHint { get; init; } = "empty = stock";
        public string BtnAdd { get; init; } = "Add";
        public string BtnSave { get; init; } = "Save";
        public string BtnDelete { get; init; } = "Delete";
        public string BtnImport { get; init; } = "Import";
        public string BtnExport { get; init; } = "Export";
        public string BtnDefaults { get; init; } = "Defaults";
        public string MsgAdded { get; init; } = "Added";
        public string MsgSaved { get; init; } = "Saved";
        public string MsgImported { get; init; } = "Imported";
        public string MsgExported { get; init; } = "Exported";
        public string ConfirmDelete { get; init; } = "Delete this profile?";
        public string ConfirmDefaults { get; init; } = "Reset to Eco / Performance / Extreme defaults?";
        public string ConfirmImport { get; init; } = "Replace all existing profiles?\nYes = replace, No = merge";

        // splash screen vibes
        public string SplashTitle { get; init; } = "Mars";
        public string SplashSubtitle { get; init; } = "FPS Monitor";
        public string SplashBoot { get; init; } = "Starting…";
        public string SplashSensors { get; init; } = "Reading hardware…";
        public string SplashAi { get; init; } = "Getting AI suggestions…";
        public string SplashReady { get; init; } = "Ready";
        public string ProfileClassic { get; init; } = "Classic Minimalist";
        public string ProfileGamer { get; init; } = "Gamer Panel";
        public string ProfileSteam { get; init; } = "Steam Deck Style";
        public string ProfileAdvanced { get; init; } = "Advanced Performance HUD";
        public string ProfilePill { get; init; } = "Compact Pill";
        public string ProfileNeon { get; init; } = "Neon Glass";
        public string ProfileTower { get; init; } = "Tower";

        public static UiStrings For(string? lang) => (lang ?? "EN").ToUpperInvariant() switch
        {
            "TR" => Tr(),
            "AZ" => Az(),
            "DE" => De(),
            "ES" => Es(),
            "FR" => Fr(),
            "PT" => Pt(),
            "BR" => Br(),
            "RU" => Ru(),
            "ZH" => Zh(),
            _ => En()
        };

        public static UiStrings En() => new() { LanguageCode = "EN" };

        public static UiStrings Tr() => new()
        {
            LanguageCode = "TR",
            Title = "Mars FPS Monitor - Kontrol Paneli",
            BrandSub = "FPS Monitor · Kontrol Paneli",
            BrandName = "Mars FPS Monitor",
            AboutVersion = "v1.0",
            CheckUpdates = "Güncelleştirmeleri denetle",
            UpdateChecking = "Güncellemeler denetleniyor…",
            UpdateLatest = "En güncel sürümü kullanıyorsunuz.",
            UpdateAvailable = "Yeni sürüm mevcut: {0}",
            UpdateFailed = "Güncelleme denetlenemedi.",
            UpdateNoRelease = "Henüz yayınlanmış sürüm yok.",
            UpdateOpenRelease = "İndirme sayfasını aç",
            NavHeader = "AYARLAR",
            NavOverlay = "Overlay", NavSensors = "Sensörler", NavDisplay = "Görünüm", NavOverclock = "Overclock", NavAbout = "Hakkında",
            PageOverlay = "Overlay", PageOverlayDesc = "Oyun içi overlay görünümünü ve davranışını seçin.",
            PageSensors = "Sensörler", PageSensorsDesc = "Overlay’de hangi metriklerin görüneceğini seçin.",
            PageDisplay = "Görünüm", PageDisplayDesc = "Renk, boyut ve ekran yerleşimi.",
            PageOverclock = "Overclock", PageOverclockDesc = "Kontrol modu, anlık durum, AI önerileri ve profilleriniz.",
            PageAbout = "Hakkında", PageAboutDesc = "Ürün bilgisi ve topluluk.",
            AboutBody = "Windows için hafif, inject’siz performans overlay’i.",
            Lang = "DİL", Profile = "OVERLAY PROFİLİ", GpuSelect = "AKTİF GPU", Appearance = "GÖRÜNÜM",
            OverlayColor = "Overlay rengi", CustomColor = "Renk seç", FontSize = "Boyut", Position = "POZİSYON",
            Padding = "Kenar boşluğu", Sensors = "METRİKLER", OverlayCtrl = "OVERLAY", OverlayToggle = "Ekranda göster / gizle",
            ShowGpuName = "GPU adını göster", ShowFps = "FPS", ShowFrametime = "Frametime", ShowOnePercent = "1% Low",
            ShowCpu = "CPU sıcaklığı", ShowCpuLoad = "CPU kullanımı", ShowGpu = "GPU sıcaklığı", ShowGpuLoad = "GPU kullanımı",
            ShowRam = "RAM kullanımı", ShowVram = "VRAM kullanımı", ShowOc = "Overclock durumu", ShowClock = "Saat",
            PosUnlock = "Pozisyon kilidini aç (sürükle)", Preview = "CANLI ÖNİZLEME",
            OcModeHeader = "KONTROL MODU", OcOff = "Kapalı", OcAuto = "Otomatik", OcManual = "Manuel",
            OcManualProfile = "Sabit profil", OcActiveHeader = "ŞU AN AKTİF", OcActiveProfile = "Aktif profil",
            OcGpuTemp = "GPU sıcaklığı", OcHotspot = "Hotspot", OcApplied = "Uygulanan değerler",
            OcCore = "Core", OcMem = "Bellek", OcPower = "Power", OcPowerStock = "stok",
            OcRestore = "Kapat ve geri yükle",
            AiHeader = "AI ASİSTAN", AiDesc = "Muhafazakar Eco / Performance / Extreme önerileri. Kaydetmeden uygulanmaz.",
            AiAsk = "AI önerisi al", AiSaveAll = "Tümünü kaydet", AiSaveOne = "Kaydet",
            AiIdle = "Öneriler burada görünecek.", AiLoading = "Öneriler hazırlanıyor…",
            AiReady = "Öneriler hazır — beğenirseniz kaydedin.",
            AiSavedOne = "Profil kaydedildi. Kullanmak için Otomatik veya Manuel’i açın.",
            AiSavedAll = "Profiller kaydedildi. Kullanmak için Otomatik veya Manuel’i açın.",
            AiPrefetched = "AI önerileri hazır.",
            ProfilesHeader = "PROFİLLERİM", EditHeader = "DÜZENLE",
            FieldName = "Ad", FieldMin = "Min °C", FieldMax = "Max °C", FieldCore = "Core +MHz", FieldMem = "Mem +MHz",
            FieldPower = "Power %", FieldPowerHint = "boş = stok",
            BtnAdd = "Ekle", BtnSave = "Kaydet", BtnDelete = "Sil", BtnImport = "İçe aktar", BtnExport = "Dışa aktar", BtnDefaults = "Varsayılan",
            MsgAdded = "Eklendi", MsgSaved = "Kaydedildi", MsgImported = "İçe aktarıldı", MsgExported = "Dışa aktarıldı",
            ConfirmDelete = "Bu profil silinsin mi?", ConfirmDefaults = "Eco / Performance / Extreme varsayılanlarına dönülsün mü?",
            ConfirmImport = "Mevcut profiller değiştirilsin mi?\nEvet = değiştir, Hayır = birleştir",
            SplashTitle = "Mars", SplashSubtitle = "FPS Monitor", SplashBoot = "Başlatılıyor…",
            SplashSensors = "Donanım okunuyor…", SplashAi = "AI önerileri alınıyor…", SplashReady = "Hazır",
            ProfileClassic = "Klasik Minimal", ProfileGamer = "Gamer Panel", ProfileSteam = "Steam Deck Stili",
            ProfileAdvanced = "Gelişmiş Performans HUD", ProfilePill = "Kompakt Hap", ProfileNeon = "Neon Cam", ProfileTower = "Tower"
        };

        public static UiStrings De() => new()
        {
            LanguageCode = "DE",
            Title = "Mars FPS Monitor - Systemsteuerung", BrandSub = "FPS Monitor · Systemsteuerung",
            BrandName = "Mars FPS Monitor", AboutVersion = "v1.0",
            CheckUpdates = "Nach Updates suchen", UpdateChecking = "Suche nach Updates…",
            UpdateLatest = "Sie verwenden die neueste Version.", UpdateAvailable = "Neue Version verfügbar: {0}",
            UpdateFailed = "Update-Prüfung fehlgeschlagen.", UpdateNoRelease = "Noch keine Releases.",
            UpdateOpenRelease = "Download-Seite öffnen",
            SplashTitle = "Mars", SplashSubtitle = "FPS Monitor",
            NavHeader = "EINSTELLUNGEN", NavSensors = "Sensoren", NavDisplay = "Anzeige", NavAbout = "Info",
            PageSensors = "Sensoren", PageSensorsDesc = "Wählen Sie die angezeigten Hardwarewerte.",
            PageDisplay = "Anzeige", PageDisplayDesc = "Farbe, Größe und Position.",
            PageOverclockDesc = "Steuerungsmodus, Live-Status, KI-Vorschläge und Ihre Profile.",
            PageAbout = "Info", PageAboutDesc = "Produktinfos und Community.",
            AboutBody = "Leichtes Overlay ohne Injection für Windows.",
            Lang = "SPRACHE", Profile = "OVERLAY-PROFIL", GpuSelect = "AKTIVE GPU", Appearance = "ERSCHEINUNG",
            OverlayColor = "Overlay-Farbe", CustomColor = "Farbe wählen", FontSize = "Größe", Position = "POSITION",
            Padding = "Abstand", Sensors = "WERTE", OverlayCtrl = "OVERLAY", OverlayToggle = "Ein-/ausblenden",
            ShowGpuName = "GPU-Namen anzeigen", ShowFps = "FPS", ShowFrametime = "Frametime", ShowOnePercent = "1% Low",
            ShowCpu = "CPU-Temp", ShowCpuLoad = "CPU-Auslastung", ShowGpu = "GPU-Temp", ShowGpuLoad = "GPU-Auslastung",
            ShowRam = "RAM-Nutzung", ShowVram = "VRAM-Nutzung", ShowOc = "Overclock-Status", ShowClock = "Uhrzeit",
            PosUnlock = "Position entsperren", Preview = "LIVE-VORSCHAU",
            OcModeHeader = "STEUERUNG", OcOff = "Aus", OcAuto = "Auto", OcManual = "Manuell",
            OcManualProfile = "Festes Profil", OcActiveHeader = "AKTIV", OcActiveProfile = "Aktives Profil",
            OcGpuTemp = "GPU-Temperatur", OcHotspot = "Hotspot", OcApplied = "Angewendete Werte",
            OcMem = "Speicher", OcPowerStock = "Stock", OcRestore = "Aus & zurücksetzen",
            AiHeader = "KI-ASSISTENT", AiDesc = "Konservative Eco-/Performance-/Extreme-Vorschläge. Speichern zum Übernehmen.",
            AiAsk = "KI-Vorschlag holen", AiSaveAll = "Alle speichern", AiSaveOne = "Speichern",
            AiIdle = "Vorschläge erscheinen hier.", AiLoading = "Vorschläge werden vorbereitet…",
            AiReady = "Vorschläge bereit — bei Bedarf speichern.",
            AiSavedOne = "Profil gespeichert. Aktivieren Sie Auto oder Manuell.",
            AiSavedAll = "Profile gespeichert. Aktivieren Sie Auto oder Manuell.",
            AiPrefetched = "KI-Vorschläge bereit.",
            ProfilesHeader = "MEINE PROFILE", EditHeader = "BEARBEITEN",
            FieldName = "Name", FieldPower = "Power %", FieldPowerHint = "leer = Stock",
            BtnAdd = "Neu", BtnSave = "Speichern", BtnDelete = "Löschen", BtnImport = "Import", BtnExport = "Export", BtnDefaults = "Standard",
            MsgAdded = "Hinzugefügt", MsgSaved = "Gespeichert", MsgImported = "Importiert", MsgExported = "Exportiert",
            ConfirmDelete = "Profil löschen?", ConfirmDefaults = "Auf Eco / Performance / Extreme zurücksetzen?",
            ConfirmImport = "Alle Profile ersetzen?\nJa = ersetzen, Nein = zusammenführen",
            SplashBoot = "Startet…", SplashSensors = "Hardware wird gelesen…",
            SplashAi = "KI-Vorschläge werden geladen…", SplashReady = "Bereit",
            NavOverlay = "Overlay", NavOverclock = "Overclock",
            PageOverlay = "Overlay", PageOverlayDesc = "Legen Sie Aussehen und Verhalten des Overlays fest.",
            PageOverclock = "Overclock",
            OcCore = "Core", OcPower = "Power",
            FieldMin = "Min °C", FieldMax = "Max °C", FieldCore = "Core +MHz", FieldMem = "Mem +MHz",
            ProfileClassic = "Klassisch Minimal", ProfileGamer = "Gamer-Panel", ProfileSteam = "Steam-Deck-Stil",
            ProfileAdvanced = "Erweitertes Performance-HUD", ProfilePill = "Kompakte Pille", ProfileNeon = "Neon-Glas", ProfileTower = "Tower"
        };

        public static UiStrings Es() => new()
        {
            LanguageCode = "ES",
            Title = "Mars FPS Monitor - Panel de control", BrandSub = "FPS Monitor · Panel de control",
            BrandName = "Mars FPS Monitor", AboutVersion = "v1.0",
            CheckUpdates = "Buscar actualizaciones", UpdateChecking = "Buscando actualizaciones…",
            UpdateLatest = "Ya tienes la última versión.", UpdateAvailable = "Nueva versión disponible: {0}",
            UpdateFailed = "No se pudo comprobar actualizaciones.", UpdateNoRelease = "Aún no hay versiones publicadas.",
            UpdateOpenRelease = "Abrir página de descarga",
            SplashTitle = "Mars", SplashSubtitle = "FPS Monitor",
            NavHeader = "AJUSTES", NavSensors = "Sensores", NavDisplay = "Pantalla", NavAbout = "Acerca de",
            PageSensors = "Sensores", PageSensorsDesc = "Elige qué métricas se muestran.",
            PageDisplay = "Pantalla", PageDisplayDesc = "Color, tamaño y posición.",
            PageOverclockDesc = "Modo de control, estado en vivo, sugerencias de IA y tus perfiles.",
            PageAbout = "Acerca de", AboutBody = "Overlay ligero sin inyección para Windows.",
            Lang = "IDIOMA", Profile = "PERFIL DE OVERLAY", GpuSelect = "GPU ACTIVA", Appearance = "APARIENCIA",
            OverlayColor = "Color del overlay", CustomColor = "Elegir color", FontSize = "Tamaño", Position = "POSICIÓN",
            Padding = "Margen", Sensors = "MÉTRICAS", OverlayToggle = "Mostrar / ocultar",
            ShowGpuName = "Mostrar nombre de GPU", ShowFps = "FPS", ShowFrametime = "Frametime", ShowOnePercent = "1% Low",
            ShowCpu = "Temp. CPU", ShowCpuLoad = "Uso de CPU", ShowGpu = "Temp. GPU", ShowGpuLoad = "Uso de GPU",
            ShowRam = "Uso de RAM", ShowVram = "Uso de VRAM", ShowOc = "Estado de Overclock", ShowClock = "Reloj",
            PosUnlock = "Desbloquear posición", Preview = "VISTA PREVIA",
            OcModeHeader = "MODO DE CONTROL", OcOff = "Apagado", OcAuto = "Automático", OcManual = "Manual",
            OcManualProfile = "Perfil fijo", OcActiveHeader = "ACTIVO AHORA", OcActiveProfile = "Perfil activo",
            OcGpuTemp = "Temperatura GPU", OcApplied = "Valores aplicados", OcMem = "Memoria",
            OcPowerStock = "stock", OcRestore = "Apagar y restaurar",
            AiHeader = "ASISTENTE IA", AiDesc = "Sugerencias conservadoras. No se aplican hasta guardar.",
            AiAsk = "Obtener sugerencia IA", AiSaveAll = "Guardar todo", AiSaveOne = "Guardar",
            AiIdle = "Las sugerencias aparecerán aquí.", AiLoading = "Preparando sugerencias…",
            AiReady = "Sugerencias listas — guárdalas si te gustan.",
            AiSavedOne = "Perfil guardado. Activa Auto o Manual.",
            AiSavedAll = "Perfiles guardados. Activa Auto o Manual.",
            AiPrefetched = "Sugerencias de IA listas.",
            ProfilesHeader = "MIS PERFILES", EditHeader = "EDITAR",
            FieldName = "Nombre", FieldPowerHint = "vacío = stock",
            BtnAdd = "Añadir", BtnSave = "Guardar", BtnDelete = "Eliminar", BtnImport = "Importar", BtnExport = "Exportar", BtnDefaults = "Predeterminados",
            MsgAdded = "Añadido", MsgSaved = "Guardado", MsgImported = "Importado", MsgExported = "Exportado",
            ConfirmDelete = "¿Eliminar este perfil?", ConfirmDefaults = "¿Restablecer Eco / Performance / Extreme?",
            ConfirmImport = "¿Reemplazar todos los perfiles?\nSí = reemplazar, No = fusionar",
            SplashBoot = "Iniciando…", SplashSensors = "Leyendo hardware…",
            SplashAi = "Obteniendo sugerencias IA…", SplashReady = "Listo",
            NavOverlay = "Overlay", NavOverclock = "Overclock",
            PageOverlay = "Overlay", PageOverlayDesc = "Elige el aspecto y el comportamiento del overlay.",
            PageOverclock = "Overclock", PageAboutDesc = "Información del producto y comunidad.",
            OverlayCtrl = "OVERLAY", OcCore = "Core", OcPower = "Power", OcHotspot = "Hotspot",
            FieldMin = "Min °C", FieldMax = "Max °C", FieldCore = "Core +MHz", FieldMem = "Mem +MHz",
            ProfileClassic = "Clásico minimalista", ProfileGamer = "Panel gamer", ProfileSteam = "Estilo Steam Deck",
            ProfileAdvanced = "HUD de rendimiento avanzado", ProfilePill = "Píldora compacta", ProfileNeon = "Cristal neón", ProfileTower = "Tower"
        };

        public static UiStrings Fr() => new()
        {
            LanguageCode = "FR",
            Title = "Mars FPS Monitor - Panneau", BrandSub = "FPS Monitor · Panneau",
            BrandName = "Mars FPS Monitor", AboutVersion = "v1.0",
            CheckUpdates = "Vérifier les mises à jour", UpdateChecking = "Vérification…",
            UpdateLatest = "Vous avez la dernière version.", UpdateAvailable = "Nouvelle version disponible : {0}",
            UpdateFailed = "Échec de la vérification.", UpdateNoRelease = "Aucune version publiée.",
            UpdateOpenRelease = "Ouvrir la page de téléchargement",
            SplashTitle = "Mars", SplashSubtitle = "FPS Monitor",
            NavHeader = "RÉGLAGES", NavSensors = "Capteurs", NavDisplay = "Affichage", NavAbout = "À propos",
            PageSensors = "Capteurs", PageDisplay = "Affichage",
            PageOverclockDesc = "Mode de contrôle, état live, suggestions IA et vos profils.",
            PageAbout = "À propos", AboutBody = "Overlay léger sans injection pour Windows.",
            Lang = "LANGUE", Profile = "PROFIL OVERLAY", GpuSelect = "GPU ACTIVE", Appearance = "APPARENCE",
            OverlayColor = "Couleur", CustomColor = "Choisir", FontSize = "Taille", Position = "POSITION",
            Sensors = "MÉTRIQUES", OverlayToggle = "Afficher / masquer",
            ShowGpuName = "Afficher le nom GPU", ShowFps = "FPS", ShowFrametime = "Frametime", ShowOnePercent = "1% Low",
            ShowCpu = "Temp. CPU", ShowCpuLoad = "Charge CPU", ShowGpu = "Temp. GPU", ShowGpuLoad = "Charge GPU",
            ShowRam = "Utilisation RAM", ShowVram = "Utilisation VRAM", ShowOc = "État Overclock", ShowClock = "Horloge",
            PosUnlock = "Déverrouiller la position", Preview = "APERÇU",
            OcModeHeader = "MODE DE CONTRÔLE", OcOff = "Désactivé", OcAuto = "Auto", OcManual = "Manuel",
            OcManualProfile = "Profil fixe", OcActiveHeader = "ACTIF", OcActiveProfile = "Profil actif",
            OcGpuTemp = "Température GPU", OcApplied = "Valeurs appliquées", OcMem = "Mémoire",
            OcPowerStock = "stock", OcRestore = "Désactiver et restaurer",
            AiHeader = "ASSISTANT IA", AiDesc = "Suggestions conservatrices. Rien n’est appliqué avant enregistrement.",
            AiAsk = "Obtenir suggestion IA", AiSaveAll = "Tout enregistrer", AiSaveOne = "Enregistrer",
            AiIdle = "Les suggestions apparaîtront ici.", AiLoading = "Préparation…",
            AiReady = "Suggestions prêtes — enregistrez si vous voulez.",
            AiSavedOne = "Profil enregistré. Activez Auto ou Manuel.",
            AiSavedAll = "Profils enregistrés. Activez Auto ou Manuel.",
            AiPrefetched = "Suggestions IA prêtes.",
            ProfilesHeader = "MES PROFILS", EditHeader = "ÉDITER",
            FieldName = "Nom", FieldPowerHint = "vide = stock",
            BtnAdd = "Ajouter", BtnSave = "Enregistrer", BtnDelete = "Supprimer", BtnImport = "Importer", BtnExport = "Exporter", BtnDefaults = "Défauts",
            MsgAdded = "Ajouté", MsgSaved = "Enregistré", MsgImported = "Importé", MsgExported = "Exporté",
            ConfirmDelete = "Supprimer ce profil ?", ConfirmDefaults = "Réinitialiser Eco / Performance / Extreme ?",
            ConfirmImport = "Remplacer tous les profils ?\nOui = remplacer, Non = fusionner",
            SplashBoot = "Démarrage…", SplashSensors = "Lecture du matériel…",
            SplashAi = "Suggestions IA en cours…", SplashReady = "Prêt",
            NavOverlay = "Overlay", NavOverclock = "Overclock",
            PageOverlay = "Overlay", PageOverlayDesc = "Choisissez l’apparence et le comportement de l’overlay.",
            PageSensorsDesc = "Choisissez les métriques affichées.", PageDisplayDesc = "Couleur, taille et position.",
            PageOverclock = "Overclock", PageAboutDesc = "Infos produit et communauté.",
            OverlayCtrl = "OVERLAY", Padding = "Marge", OcCore = "Core", OcPower = "Power", OcHotspot = "Hotspot",
            FieldMin = "Min °C", FieldMax = "Max °C", FieldCore = "Core +MHz", FieldMem = "Mem +MHz",
            ProfileClassic = "Classique minimaliste", ProfileGamer = "Panneau gamer", ProfileSteam = "Style Steam Deck",
            ProfileAdvanced = "HUD performance avancé", ProfilePill = "Pilule compacte", ProfileNeon = "Verre néon", ProfileTower = "Tower"
        };

        public static UiStrings Pt() => new()
        {
            LanguageCode = "PT",
            Title = "Mars FPS Monitor - Painel", BrandSub = "FPS Monitor · Painel",
            BrandName = "Mars FPS Monitor", AboutVersion = "v1.0",
            CheckUpdates = "Procurar atualizações", UpdateChecking = "A verificar atualizações…",
            UpdateLatest = "Já tem a versão mais recente.", UpdateAvailable = "Nova versão disponível: {0}",
            UpdateFailed = "Falha ao verificar atualizações.", UpdateNoRelease = "Ainda sem versões publicadas.",
            UpdateOpenRelease = "Abrir página de transferência",
            SplashTitle = "Mars", SplashSubtitle = "FPS Monitor",
            NavHeader = "DEFINIÇÕES", NavSensors = "Sensores", NavDisplay = "Ecrã", NavAbout = "Acerca de",
            PageSensors = "Sensores", PageDisplay = "Ecrã",
            PageOverclockDesc = "Modo de controlo, estado em direto, sugestões de IA e os seus perfis.",
            AboutBody = "Overlay leve sem injeção para Windows.",
            Lang = "IDIOMA", Profile = "PERFIL OVERLAY", GpuSelect = "GPU ATIVA", Appearance = "APARÊNCIA",
            OverlayColor = "Cor do overlay", CustomColor = "Escolher cor", FontSize = "Tamanho", Position = "POSIÇÃO",
            Sensors = "MÉTRICAS", OverlayToggle = "Mostrar / ocultar",
            ShowGpuName = "Mostrar nome da GPU", ShowFps = "FPS", ShowFrametime = "Frametime", ShowOnePercent = "1% Low",
            ShowCpu = "Temp. CPU", ShowCpuLoad = "Uso de CPU", ShowGpu = "Temp. GPU", ShowGpuLoad = "Uso de GPU",
            ShowRam = "Uso de RAM", ShowVram = "Uso de VRAM", ShowOc = "Estado de Overclock", ShowClock = "Relógio",
            PosUnlock = "Desbloquear posição", Preview = "PRÉ-VISUALIZAÇÃO",
            OcModeHeader = "MODO DE CONTROLO", OcOff = "Desligado", OcAuto = "Automático", OcManual = "Manual",
            OcManualProfile = "Perfil fixo", OcActiveHeader = "ATIVO AGORA", OcActiveProfile = "Perfil ativo",
            OcGpuTemp = "Temperatura GPU", OcApplied = "Valores aplicados", OcMem = "Memória",
            OcPowerStock = "stock", OcRestore = "Desligar e restaurar",
            AiHeader = "ASSISTENTE IA", AiDesc = "Sugestões conservadoras. Só aplicam após guardar.",
            AiAsk = "Obter sugestão IA", AiSaveAll = "Guardar tudo", AiSaveOne = "Guardar",
            AiIdle = "As sugestões aparecem aqui.", AiLoading = "A preparar sugestões…",
            AiReady = "Sugestões prontas — guarde se gostar.",
            AiSavedOne = "Perfil guardado. Ative Auto ou Manual.",
            AiSavedAll = "Perfis guardados. Ative Auto ou Manual.",
            AiPrefetched = "Sugestões de IA prontas.",
            ProfilesHeader = "OS MEUS PERFIS", EditHeader = "EDITAR",
            FieldName = "Nome", FieldPowerHint = "vazio = stock",
            BtnAdd = "Adicionar", BtnSave = "Guardar", BtnDelete = "Eliminar", BtnImport = "Importar", BtnExport = "Exportar", BtnDefaults = "Predefinições",
            MsgAdded = "Adicionado", MsgSaved = "Guardado", MsgImported = "Importado", MsgExported = "Exportado",
            ConfirmDelete = "Eliminar este perfil?", ConfirmDefaults = "Repor Eco / Performance / Extreme?",
            ConfirmImport = "Substituir todos os perfis?\nSim = substituir, Não = juntar",
            SplashBoot = "A iniciar…", SplashSensors = "A ler hardware…",
            SplashAi = "A obter sugestões IA…", SplashReady = "Pronto",
            NavOverlay = "Overlay", NavOverclock = "Overclock",
            PageOverlay = "Overlay", PageOverlayDesc = "Escolha o aspeto e o comportamento do overlay.",
            PageSensorsDesc = "Escolha as métricas a mostrar.", PageDisplayDesc = "Cor, tamanho e posição.",
            PageOverclock = "Overclock", PageAbout = "Acerca de", PageAboutDesc = "Informação do produto e comunidade.",
            OverlayCtrl = "OVERLAY", Padding = "Margem", OcCore = "Core", OcPower = "Power", OcHotspot = "Hotspot",
            FieldMin = "Min °C", FieldMax = "Max °C", FieldCore = "Core +MHz", FieldMem = "Mem +MHz",
            ProfileClassic = "Clássico minimalista", ProfileGamer = "Painel gamer", ProfileSteam = "Estilo Steam Deck",
            ProfileAdvanced = "HUD de desempenho avançado", ProfilePill = "Pílula compacta", ProfileNeon = "Vidro néon", ProfileTower = "Tower"
        };

        public static UiStrings Br() => new()
        {
            LanguageCode = "BR",
            Title = "Mars FPS Monitor - Painel", BrandSub = "FPS Monitor · Painel",
            BrandName = "Mars FPS Monitor", AboutVersion = "v1.0",
            CheckUpdates = "Verificar atualizações", UpdateChecking = "Verificando atualizações…",
            UpdateLatest = "Você está na versão mais recente.", UpdateAvailable = "Nova versão disponível: {0}",
            UpdateFailed = "Falha ao verificar atualizações.", UpdateNoRelease = "Ainda sem versões publicadas.",
            UpdateOpenRelease = "Abrir página de download",
            SplashTitle = "Mars", SplashSubtitle = "FPS Monitor",
            NavHeader = "CONFIGURAÇÕES", NavSensors = "Sensores", NavDisplay = "Exibição", NavAbout = "Sobre",
            PageSensors = "Sensores", PageDisplay = "Exibição",
            PageOverclockDesc = "Modo de controle, status ao vivo, sugestões de IA e seus perfis.",
            PageAbout = "Sobre", AboutBody = "Overlay leve sem injeção para Windows.",
            Lang = "IDIOMA", Profile = "PERFIL DO OVERLAY", GpuSelect = "GPU ATIVA", Appearance = "APARÊNCIA",
            OverlayColor = "Cor do overlay", CustomColor = "Escolher cor", FontSize = "Tamanho", Position = "POSIÇÃO",
            Sensors = "MÉTRICAS", OverlayToggle = "Mostrar / ocultar",
            ShowGpuName = "Mostrar nome da GPU", ShowFps = "FPS", ShowFrametime = "Frametime", ShowOnePercent = "1% Low",
            ShowCpu = "Temp. CPU", ShowCpuLoad = "Uso de CPU", ShowGpu = "Temp. GPU", ShowGpuLoad = "Uso de GPU",
            ShowRam = "Uso de RAM", ShowVram = "Uso de VRAM", ShowOc = "Status de Overclock", ShowClock = "Relógio",
            PosUnlock = "Desbloquear posição", Preview = "PRÉVIA AO VIVO",
            OcModeHeader = "MODO DE CONTROLE", OcOff = "Desligado", OcAuto = "Automático", OcManual = "Manual",
            OcManualProfile = "Perfil fixo", OcActiveHeader = "ATIVO AGORA", OcActiveProfile = "Perfil ativo",
            OcGpuTemp = "Temperatura da GPU", OcApplied = "Valores aplicados", OcMem = "Memória",
            OcPowerStock = "estoque", OcRestore = "Desligar e restaurar",
            AiHeader = "ASSISTENTE DE IA", AiDesc = "Sugestões conservadoras. Só aplica depois de salvar.",
            AiAsk = "Obter sugestão de IA", AiSaveAll = "Salvar todos", AiSaveOne = "Salvar",
            AiIdle = "As sugestões aparecem aqui.", AiLoading = "Preparando sugestões…",
            AiReady = "Sugestões prontas — salve se quiser.",
            AiSavedOne = "Perfil salvo. Ative Auto ou Manual.",
            AiSavedAll = "Perfis salvos. Ative Auto ou Manual.",
            AiPrefetched = "Sugestões de IA prontas.",
            ProfilesHeader = "MEUS PERFIS", EditHeader = "EDITAR",
            FieldName = "Nome", FieldPowerHint = "vazio = estoque",
            BtnAdd = "Adicionar", BtnSave = "Salvar", BtnDelete = "Excluir", BtnImport = "Importar", BtnExport = "Exportar", BtnDefaults = "Padrões",
            MsgAdded = "Adicionado", MsgSaved = "Salvo", MsgImported = "Importado", MsgExported = "Exportado",
            ConfirmDelete = "Excluir este perfil?", ConfirmDefaults = "Redefinir Eco / Performance / Extreme?",
            ConfirmImport = "Substituir todos os perfis?\nSim = substituir, Não = mesclar",
            SplashBoot = "Iniciando…", SplashSensors = "Lendo hardware…",
            SplashAi = "Obtendo sugestões IA…", SplashReady = "Pronto",
            NavOverlay = "Overlay", NavOverclock = "Overclock",
            PageOverlay = "Overlay", PageOverlayDesc = "Escolha a aparência e o comportamento do overlay.",
            PageSensorsDesc = "Escolha as métricas exibidas.", PageDisplayDesc = "Cor, tamanho e posição.",
            PageOverclock = "Overclock", PageAboutDesc = "Informações do produto e comunidade.",
            OverlayCtrl = "OVERLAY", Padding = "Margem", OcCore = "Core", OcPower = "Power", OcHotspot = "Hotspot",
            FieldMin = "Min °C", FieldMax = "Max °C", FieldCore = "Core +MHz", FieldMem = "Mem +MHz",
            ProfileClassic = "Clássico minimalista", ProfileGamer = "Painel gamer", ProfileSteam = "Estilo Steam Deck",
            ProfileAdvanced = "HUD de desempenho avançado", ProfilePill = "Pílula compacta", ProfileNeon = "Vidro neon", ProfileTower = "Tower"
        };

        public static UiStrings Ru() => new()
        {
            LanguageCode = "RU",
            Title = "Mars FPS Monitor - Панель", BrandSub = "FPS Monitor · Панель",
            BrandName = "Mars FPS Monitor", AboutVersion = "v1.0",
            CheckUpdates = "Проверить обновления", UpdateChecking = "Проверка обновлений…",
            UpdateLatest = "У вас актуальная версия.", UpdateAvailable = "Доступна новая версия: {0}",
            UpdateFailed = "Не удалось проверить обновления.", UpdateNoRelease = "Релизов пока нет.",
            UpdateOpenRelease = "Открыть страницу загрузки",
            SplashTitle = "Mars", SplashSubtitle = "FPS Monitor",
            NavHeader = "НАСТРОЙКИ", NavOverlay = "Оверлей", NavSensors = "Датчики", NavDisplay = "Экран",
            NavOverclock = "Разгон", NavAbout = "О программе",
            PageOverlay = "Оверлей", PageOverlayDesc = "Внешний вид и поведение оверлея.",
            PageSensors = "Датчики", PageSensorsDesc = "Какие метрики показывать.",
            PageDisplay = "Экран", PageDisplayDesc = "Цвет, размер и положение.",
            PageOverclock = "Разгон", PageOverclockDesc = "Режим, статус, ИИ-предложения и ваши профили.",
            PageAbout = "О программе", AboutBody = "Лёгкий оверлей без инъекций для Windows.",
            Lang = "ЯЗЫК", Profile = "ПРОФИЛЬ ОВЕРЛЕЯ", GpuSelect = "АКТИВНЫЙ GPU", Appearance = "ВНЕШНИЙ ВИД",
            OverlayColor = "Цвет оверлея", CustomColor = "Выбрать цвет", FontSize = "Размер", Position = "ПОЗИЦИЯ",
            Padding = "Отступ", Sensors = "МЕТРИКИ", OverlayToggle = "Показать / скрыть",
            ShowGpuName = "Имя GPU", ShowFps = "FPS", ShowFrametime = "Frametime", ShowOnePercent = "1% Low",
            ShowCpu = "Темп. CPU", ShowCpuLoad = "Загрузка CPU", ShowGpu = "Темп. GPU", ShowGpuLoad = "Загрузка GPU",
            ShowRam = "ОЗУ", ShowVram = "VRAM", ShowOc = "Статус разгона", ShowClock = "Часы",
            PosUnlock = "Разблокировать позицию", Preview = "ПРЕДПРОСМОТР",
            OcModeHeader = "РЕЖИМ", OcOff = "Выкл", OcAuto = "Авто", OcManual = "Ручной",
            OcManualProfile = "Фикс. профиль", OcActiveHeader = "СЕЙЧАС АКТИВЕН", OcActiveProfile = "Активный профиль",
            OcGpuTemp = "Температура GPU", OcHotspot = "Hotspot", OcApplied = "Применённые значения",
            OcMem = "Память", OcPowerStock = "сток", OcRestore = "Выключить и сбросить",
            AiHeader = "ИИ-АССИСТЕНТ", AiDesc = "Консервативные Eco / Performance / Extreme. Без сохранения не применяется.",
            AiAsk = "Получить совет ИИ", AiSaveAll = "Сохранить все", AiSaveOne = "Сохранить",
            AiIdle = "Предложения появятся здесь.", AiLoading = "Готовим предложения…",
            AiReady = "Готово — сохраните при желании.",
            AiSavedOne = "Профиль сохранён. Включите Авто или Ручной.",
            AiSavedAll = "Профили сохранены. Включите Авто или Ручной.",
            AiPrefetched = "ИИ-предложения готовы.",
            ProfilesHeader = "МОИ ПРОФИЛИ", EditHeader = "ПРАВКА",
            FieldName = "Имя", FieldPowerHint = "пусто = сток",
            BtnAdd = "Добавить", BtnSave = "Сохранить", BtnDelete = "Удалить", BtnImport = "Импорт", BtnExport = "Экспорт", BtnDefaults = "По умолчанию",
            MsgAdded = "Добавлено", MsgSaved = "Сохранено", MsgImported = "Импортировано", MsgExported = "Экспортировано",
            ConfirmDelete = "Удалить этот профиль?", ConfirmDefaults = "Сбросить Eco / Performance / Extreme?",
            ConfirmImport = "Заменить все профили?\nДа = заменить, Нет = объединить",
            SplashBoot = "Запуск…", SplashSensors = "Чтение оборудования…",
            SplashAi = "Получение ИИ-предложений…", SplashReady = "Готово",
            PageAboutDesc = "Информация о продукте и сообщество.", OverlayCtrl = "ОВЕРЛЕЙ",
            OcCore = "Ядро", OcPower = "Питание",
            FieldMin = "Мин °C", FieldMax = "Макс °C", FieldCore = "Ядро +МГц", FieldMem = "Память +МГц",
            ProfileClassic = "Классический минимализм", ProfileGamer = "Геймер-панель", ProfileSteam = "Стиль Steam Deck",
            ProfileAdvanced = "Продвинутый HUD", ProfilePill = "Компактная капсула", ProfileNeon = "Неоновое стекло", ProfileTower = "Башня"
        };

        public static UiStrings Az() => new()
        {
            LanguageCode = "AZ",
            Title = "Mars FPS Monitor - İdarə paneli", BrandSub = "FPS Monitor · İdarə paneli",
            BrandName = "Mars FPS Monitor", AboutVersion = "v1.0",
            CheckUpdates = "Yeniləmələri yoxla", UpdateChecking = "Yeniləmələr yoxlanılır…",
            UpdateLatest = "Ən son versiyanı istifadə edirsiniz.", UpdateAvailable = "Yeni versiya mövcuddur: {0}",
            UpdateFailed = "Yeniləmə yoxlanılmadı.", UpdateNoRelease = "Hələ buraxılmış versiya yoxdur.",
            UpdateOpenRelease = "Yükləmə səhifəsini aç",
            NavHeader = "AYARLAR", NavOverlay = "Overlay", NavSensors = "Sensorlar", NavDisplay = "Görünüş",
            NavOverclock = "Overclock", NavAbout = "Haqqında",
            PageOverlay = "Overlay", PageOverlayDesc = "Oyun içi overlay görünüşünü və davranışını seçin.",
            PageSensors = "Sensorlar", PageSensorsDesc = "Overlay-də hansı metriklərin görünəcəyini seçin.",
            PageDisplay = "Görünüş", PageDisplayDesc = "Rəng, ölçü və ekran yerləşməsi.",
            PageOverclock = "Overclock", PageOverclockDesc = "İdarə rejimi, canlı status, AI təklifləri və profilləriniz.",
            PageAbout = "Haqqında", PageAboutDesc = "Məhsul məlumatı və icma.",
            AboutBody = "Windows üçün yüngül, inject-siz performans overlay-i.",
            Lang = "DİL", Profile = "OVERLAY PROFİLİ", GpuSelect = "AKTİV GPU", Appearance = "GÖRÜNÜŞ",
            OverlayColor = "Overlay rəngi", CustomColor = "Rəng seç", FontSize = "Ölçü", Position = "MÖVQE",
            Padding = "Kənar boşluq", Sensors = "METRİKLƏR", OverlayCtrl = "OVERLAY", OverlayToggle = "Ekranda göstər / gizlət",
            ShowGpuName = "GPU adını göstər", ShowFps = "FPS", ShowFrametime = "Frametime", ShowOnePercent = "1% Low",
            ShowCpu = "CPU temperaturu", ShowCpuLoad = "CPU istifadəsi", ShowGpu = "GPU temperaturu", ShowGpuLoad = "GPU istifadəsi",
            ShowRam = "RAM istifadəsi", ShowVram = "VRAM istifadəsi", ShowOc = "Overclock statusu", ShowClock = "Saat",
            PosUnlock = "Mövqeni kiliddən çıxar (sürüklə)", Preview = "CANLI ÖNİZLƏMƏ",
            OcModeHeader = "İDARƏ REJİMİ", OcOff = "Bağlı", OcAuto = "Avtomatik", OcManual = "Manual",
            OcManualProfile = "Sabit profil", OcActiveHeader = "İNDİ AKTİV", OcActiveProfile = "Aktiv profil",
            OcGpuTemp = "GPU temperaturu", OcHotspot = "Hotspot", OcApplied = "Tətbiq olunan dəyərlər",
            OcCore = "Core", OcMem = "Yaddaş", OcPower = "Power", OcPowerStock = "stok", OcRestore = "Bağla və bərpa et",
            AiHeader = "AI KÖMƏKÇİ", AiDesc = "Ehtiyatlı Eco / Performance / Extreme təklifləri. Saxlamadan tətbiq olunmur.",
            AiAsk = "AI təklifi al", AiSaveAll = "Hamısını saxla", AiSaveOne = "Saxla",
            AiIdle = "Təkliflər burada görünəcək.", AiLoading = "Təkliflər hazırlanır…",
            AiReady = "Təkliflər hazırdır — bəyənsəniz saxlayın.",
            AiSavedOne = "Profil saxlanıldı. İstifadə üçün Avtomatik və ya Manualı açın.",
            AiSavedAll = "Profillər saxlanıldı. İstifadə üçün Avtomatik və ya Manualı açın.",
            AiPrefetched = "AI təklifləri hazırdır.",
            ProfilesHeader = "PROFİLLƏRİM", EditHeader = "REDAKTƏ",
            FieldName = "Ad", FieldMin = "Min °C", FieldMax = "Max °C", FieldCore = "Core +MHz", FieldMem = "Mem +MHz",
            FieldPower = "Power %", FieldPowerHint = "boş = stok",
            BtnAdd = "Əlavə et", BtnSave = "Saxla", BtnDelete = "Sil", BtnImport = "İdxal", BtnExport = "İxrac", BtnDefaults = "Standart",
            MsgAdded = "Əlavə edildi", MsgSaved = "Saxlanıldı", MsgImported = "İdxal edildi", MsgExported = "İxrac edildi",
            ConfirmDelete = "Bu profil silinsin?", ConfirmDefaults = "Eco / Performance / Extreme standartlarına qayıdılsın?",
            ConfirmImport = "Mövcud profillər dəyişdirilsin?\nBəli = dəyişdir, Xeyr = birləşdir",
            SplashTitle = "Mars", SplashSubtitle = "FPS Monitor", SplashBoot = "Başladılır…",
            SplashSensors = "Aparat oxunur…", SplashAi = "AI təklifləri alınır…", SplashReady = "Hazır",
            ProfileClassic = "Klassik Minimal", ProfileGamer = "Gamer Panel", ProfileSteam = "Steam Deck üslubu",
            ProfileAdvanced = "Qabaqcıl Performans HUD", ProfilePill = "Kompakt Hap", ProfileNeon = "Neon Şüşə", ProfileTower = "Tower"
        };

        public static UiStrings Zh() => new()
        {
            LanguageCode = "ZH",
            Title = "Mars FPS Monitor - 控制面板", BrandSub = "FPS Monitor · 控制面板",
            BrandName = "Mars FPS Monitor", AboutVersion = "v1.0",
            CheckUpdates = "检查更新", UpdateChecking = "正在检查更新…",
            UpdateLatest = "已是最新版本。", UpdateAvailable = "有新版本可用：{0}",
            UpdateFailed = "无法检查更新。", UpdateNoRelease = "尚未发布版本。",
            UpdateOpenRelease = "打开下载页面",
            NavHeader = "设置", NavOverlay = "叠加层", NavSensors = "传感器", NavDisplay = "显示",
            NavOverclock = "超频", NavAbout = "关于",
            PageOverlay = "叠加层", PageOverlayDesc = "选择游戏内叠加层的外观与行为。",
            PageSensors = "传感器", PageSensorsDesc = "选择叠加层显示的硬件指标。",
            PageDisplay = "显示", PageDisplayDesc = "颜色、大小与屏幕位置。",
            PageOverclock = "超频", PageOverclockDesc = "控制模式、实时状态、AI 建议与配置文件。",
            PageAbout = "关于", PageAboutDesc = "产品信息与社区。",
            AboutBody = "适用于 Windows 的轻量、无注入性能叠加层。",
            Lang = "语言", Profile = "叠加层配置", GpuSelect = "当前 GPU", Appearance = "外观",
            OverlayColor = "叠加层颜色", CustomColor = "选择颜色", FontSize = "大小", Position = "位置",
            Padding = "边距", Sensors = "指标", OverlayCtrl = "叠加层", OverlayToggle = "显示 / 隐藏",
            ShowGpuName = "显示 GPU 名称", ShowFps = "FPS", ShowFrametime = "帧时间", ShowOnePercent = "1% Low",
            ShowCpu = "CPU 温度", ShowCpuLoad = "CPU 占用", ShowGpu = "GPU 温度", ShowGpuLoad = "GPU 占用",
            ShowRam = "内存占用", ShowVram = "显存占用", ShowOc = "超频状态", ShowClock = "时钟",
            PosUnlock = "解锁位置（可拖动）", Preview = "实时预览",
            OcModeHeader = "控制模式", OcOff = "关闭", OcAuto = "自动", OcManual = "手动",
            OcManualProfile = "固定配置", OcActiveHeader = "当前激活", OcActiveProfile = "活动配置",
            OcGpuTemp = "GPU 温度", OcHotspot = "热点", OcApplied = "已应用数值",
            OcCore = "核心", OcMem = "显存", OcPower = "功耗", OcPowerStock = "默认", OcRestore = "关闭并恢复",
            AiHeader = "AI 助手", AiDesc = "保守的 Eco / Performance / Extreme 建议。保存前不会应用。",
            AiAsk = "获取 AI 建议", AiSaveAll = "全部保存", AiSaveOne = "保存",
            AiIdle = "建议将显示在这里。", AiLoading = "正在准备建议…",
            AiReady = "建议已就绪 — 可按需保存。",
            AiSavedOne = "配置已保存。请启用自动或手动以使用。",
            AiSavedAll = "配置已保存。请启用自动或手动以使用。",
            AiPrefetched = "AI 建议已就绪。",
            ProfilesHeader = "我的配置", EditHeader = "编辑",
            FieldName = "名称", FieldMin = "最低 °C", FieldMax = "最高 °C", FieldCore = "核心 +MHz", FieldMem = "显存 +MHz",
            FieldPower = "功耗 %", FieldPowerHint = "空 = 默认",
            BtnAdd = "添加", BtnSave = "保存", BtnDelete = "删除", BtnImport = "导入", BtnExport = "导出", BtnDefaults = "默认",
            MsgAdded = "已添加", MsgSaved = "已保存", MsgImported = "已导入", MsgExported = "已导出",
            ConfirmDelete = "删除此配置？", ConfirmDefaults = "恢复 Eco / Performance / Extreme 默认？",
            ConfirmImport = "替换所有现有配置？\n是 = 替换，否 = 合并",
            SplashTitle = "Mars", SplashSubtitle = "FPS Monitor", SplashBoot = "正在启动…",
            SplashSensors = "正在读取硬件…", SplashAi = "正在获取 AI 建议…", SplashReady = "就绪",
            ProfileClassic = "经典极简", ProfileGamer = "玩家面板", ProfileSteam = "Steam Deck 风格",
            ProfileAdvanced = "高级性能 HUD", ProfilePill = "紧凑胶囊", ProfileNeon = "霓虹玻璃", ProfileTower = "塔式"
        };
    }
}
