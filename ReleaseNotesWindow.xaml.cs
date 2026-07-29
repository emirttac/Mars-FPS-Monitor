using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FPSOverlay
{
    public partial class ReleaseNotesWindow : Window
    {
        private readonly OverlayConfig _config;
        private readonly Dictionary<string, string> _langLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["EN"] = "English",
            ["TR"] = "Türkçe",
            ["DE"] = "Deutsch",
            ["ES"] = "Español",
            ["FR"] = "Français",
            ["PT"] = "Português",
            ["BR"] = "Português (BR)",
            ["RU"] = "Русский",
            ["AZ"] = "Azərbaycan",
            ["ZH"] = "中文",
        };

        public ReleaseNotesWindow(OverlayConfig config)
        {
            InitializeComponent();
            _config = config;

            try { Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/app.ico")); } catch { }

            MouseLeftButtonDown += (_, __) => { try { DragMove(); } catch { } };

            foreach (var kv in _langLabels)
                CmbLanguage.Items.Add(new ComboBoxItem { Content = kv.Value, Tag = kv.Key });

            string lang = string.IsNullOrWhiteSpace(_config.Language) ? "EN" : _config.Language;
            SelectLanguage(lang);
            ApplyNotesLanguage(lang);

            ChkAck.Checked += (_, __) => UpdateContinueEnabled();
            ChkAck.Unchecked += (_, __) => UpdateContinueEnabled();
            UpdateContinueEnabled();
        }

        private void SelectLanguage(string code)
        {
            for (int i = 0; i < CmbLanguage.Items.Count; i++)
            {
                if (CmbLanguage.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag as string, code, StringComparison.OrdinalIgnoreCase))
                {
                    CmbLanguage.SelectedIndex = i;
                    return;
                }
            }
            CmbLanguage.SelectedIndex = 0;
        }

        private string CurrentLangCode()
        {
            if (CmbLanguage.SelectedItem is ComboBoxItem item && item.Tag is string code)
                return code;
            return "EN";
        }

        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            ApplyNotesLanguage(CurrentLangCode());
        }

        private void ApplyNotesLanguage(string lang)
        {
            var n = ReleaseNotesCopy.For(lang);
            Title = n.WindowTitle;
            LblTitle.Text = n.Title;
            LblFeaturesHeader.Text = n.FeaturesHeader;
            LblBugsHeader.Text = n.BugsHeader;
            TxtFeatures.Text = n.FeaturesBody;
            TxtBugs.Text = n.BugsBody;
            ChkAck.Content = n.AckCheckbox;
            BtnContinue.Content = n.ContinueButton;
            LblLang.Text = n.LanguageLabel;
        }

        private void UpdateContinueEnabled()
        {
            BtnContinue.IsEnabled = ChkAck.IsChecked == true;
            BtnContinue.Opacity = BtnContinue.IsEnabled ? 1.0 : 0.45;
        }

        private void BtnContinue_Click(object sender, RoutedEventArgs e)
        {
            if (ChkAck.IsChecked != true) return;

            string lang = CurrentLangCode();
            if (!string.Equals(_config.Language, lang, StringComparison.OrdinalIgnoreCase))
            {
                _config.Language = lang;
                try { _config.Save(); } catch { }
            }

            DialogResult = true;
            Close();
        }
    }

    /// <summary>v2.0 what's-new copy for every UI language.</summary>
    public sealed class ReleaseNotesCopy
    {
        public string WindowTitle { get; init; } = "Mars FPS Monitor — What's New";
        public string Title { get; init; } = "Mars FPS Monitor v2.0 Release Notes";
        public string LanguageLabel { get; init; } = "Language";
        public string FeaturesHeader { get; init; } = "Feature updates";
        public string BugsHeader { get; init; } = "Bug fixes";
        public string FeaturesBody { get; init; } = "";
        public string BugsBody { get; init; } = "";
        public string AckCheckbox { get; init; } = "I have read and understand these notes";
        public string ContinueButton { get; init; } = "Continue";

        public static ReleaseNotesCopy For(string? lang) => (lang ?? "EN").ToUpperInvariant() switch
        {
            "TR" => Tr(),
            "DE" => De(),
            "ES" => Es(),
            "FR" => Fr(),
            "PT" => Pt(),
            "BR" => Br(),
            "RU" => Ru(),
            "AZ" => Az(),
            "ZH" => Zh(),
            _ => En()
        };

        public static ReleaseNotesCopy En() => new()
        {
            WindowTitle = "Mars FPS Monitor — What's New",
            Title = "Mars FPS Monitor v2.0 Release Notes",
            LanguageLabel = "Language",
            FeaturesHeader = "Feature updates",
            BugsHeader = "Bug fixes",
            FeaturesBody =
                "• A new Home tab was added with a Gauges-style UI for CPU/GPU temperatures and RAM usage.\n\n" +
                "• A Library tab was added. For now it is a preview: you can view and launch your active games. Future updates will add per-game settings and richer info UI.\n\n" +
                "• Overlay gained RTSS and exclusive-fullscreen support. As Mars FPS Monitor we chose not to inject DLLs into game folders for fullscreen tracking. In exclusive fullscreen, HUD support now comes through RTSS.\n\n" +
                "• Smart Overclock evolved into fully automatic Smart Overclock. Previously the engine could enable OC even with no game/engine process running. The new Mars Overclock Engine detects whether a game is running (GPU 3D Load > 30%) and enables OC only while you play, turning it off when you are idle — so your system is not forced into OC at rest.\n\n" +
                "• The UI feels smoother and more responsive.",
            BugsBody =
                "• Fixed a crash triggered by WPF mouse click/move quirks when rapidly shaking the Color Picker panel on the Display tab.",
            AckCheckbox = "I have read and understand these notes",
            ContinueButton = "Continue"
        };

        public static ReleaseNotesCopy Tr() => new()
        {
            WindowTitle = "Mars FPS Monitor — Yenilikler",
            Title = "Mars FPS Monitor v2.0 Güncelleme Notları",
            LanguageLabel = "Dil",
            FeaturesHeader = "Özellik Güncellemeleri",
            BugsHeader = "Hata Düzeltmeleri",
            FeaturesBody =
                "• Yeni Ana Ekran sekmesi eklendi ve bu sekmede \"Gauges\" temasında CPU/GPU sıcaklıkları ve RAM kullanımını gösteren bir UI eklendi.\n\n" +
                "• Kütüphanem isimli bir sekme eklendi. Şu anlık ön gösterim amaçlı sadece aktif oyunlarınızı görüntüleyip başlatabiliyorsunuz. Gelecek yeni güncellemelerde her oyuna özel çok daha özel ayar ve bilgilendirme UI'u eklenecek.\n\n" +
                "• Overlay kısmında RTSS ve tam ekran desteği eklendi. Mars FPS Monitor olarak siz oyuncuların tam ekranda Overlay takibi için oyun dosyalarının içerisine DLL enjekte etmemeyi tercih ettik. Bundan dolayı tam ekran oyun modunda HUD desteğini artık RTSS sayesinde alabileceksiniz.\n\n" +
                "• Akıllı Overclock desteği artık Tam Akıllı Overclock desteğine evrildi. Önceden Akıllı Overclock Motoru arkada herhangi bir oyun veya oyun motoru işlemi açık olmasa bile OC modunu açabiliyordu. Ancak yeni Mars Overclock Motoru, sizin herhangi bir oyun işlemi çalıştırıp çalıştırmadığınızı (GPU 3D Load > %30) algoritma ile anlayarak sadece oyun oynadığınız anlarda OC başlatıp oynamadığınız anlarda OC kapatır. Sisteminizi durgun anlarda OC yapmaya zorlamaz. Artık Akıllı OC Sistemi tam otomatik.\n\n" +
                "• Arayüz daha akıcı hale getirildi.",
            BugsBody =
                "• Görünüm sekmesindeki Renk Seçici panelini açıp hızlı şekilde salladığınız zaman WPF'nin mouse tıklama ve hareket bugunu tetikleyip yazılımın çökmesine neden olan hata giderildi.",
            AckCheckbox = "Okudum ve anladım",
            ContinueButton = "Devam et"
        };

        public static ReleaseNotesCopy De() => new()
        {
            WindowTitle = "Mars FPS Monitor — Neuigkeiten",
            Title = "Mars FPS Monitor v2.0 Versionshinweise",
            LanguageLabel = "Sprache",
            FeaturesHeader = "Funktionsupdates",
            BugsHeader = "Fehlerbehebungen",
            FeaturesBody =
                "• Neuer Start-Tab mit Gauges-UI für CPU-/GPU-Temperaturen und RAM-Auslastung.\n\n" +
                "• Bibliothek-Tab hinzugefügt (Vorschau): aktive Spiele anzeigen und starten. Später folgen spielspezifische Einstellungen und Infos.\n\n" +
                "• Overlay: RTSS- und Exclusive-Fullscreen-Unterstützung. Keine DLL-Injektion in Spieldateien — Fullscreen-HUD über RTSS.\n\n" +
                "• Smart Overclock ist vollautomatisch: erkennt Spielbetrieb (GPU-3D-Last > 30 %) und aktiviert OC nur während des Spielens.\n\n" +
                "• Die Oberfläche reagiert flüssiger.",
            BugsBody =
                "• Absturz behoben, der durch schnelles Bewegen der Farbauswahl unter Darstellung (WPF-Mausbug) ausgelöst wurde.",
            AckCheckbox = "Ich habe die Hinweise gelesen und verstanden",
            ContinueButton = "Weiter"
        };

        public static ReleaseNotesCopy Es() => new()
        {
            WindowTitle = "Mars FPS Monitor — Novedades",
            Title = "Notas de la versión Mars FPS Monitor v2.0",
            LanguageLabel = "Idioma",
            FeaturesHeader = "Actualizaciones de funciones",
            BugsHeader = "Corrección de errores",
            FeaturesBody =
                "• Nueva pestaña Inicio con UI tipo Gauges para temperaturas de CPU/GPU y uso de RAM.\n\n" +
                "• Pestaña Biblioteca (vista previa): ver y lanzar juegos activos. Próximamente ajustes e info por juego.\n\n" +
                "• Overlay con soporte RTSS y pantalla completa exclusiva, sin inyectar DLL en los juegos.\n\n" +
                "• Smart Overclock totalmente automático: detecta juego (carga GPU 3D > 30 %) y activa OC solo mientras juegas.\n\n" +
                "• Interfaz más fluida.",
            BugsBody =
                "• Se corrigió un cierre inesperado al agitar rápidamente el selector de color en Visualización (error de ratón de WPF).",
            AckCheckbox = "He leído y entiendo estas notas",
            ContinueButton = "Continuar"
        };

        public static ReleaseNotesCopy Fr() => new()
        {
            WindowTitle = "Mars FPS Monitor — Nouveautés",
            Title = "Notes de version Mars FPS Monitor v2.0",
            LanguageLabel = "Langue",
            FeaturesHeader = "Mises à jour des fonctionnalités",
            BugsHeader = "Corrections de bugs",
            FeaturesBody =
                "• Nouvel onglet Accueil avec UI type Gauges pour températures CPU/GPU et usage RAM.\n\n" +
                "• Onglet Bibliothèque (aperçu) : afficher et lancer vos jeux actifs. Réglages par jeu à venir.\n\n" +
                "• Overlay : support RTSS et plein écran exclusif, sans injection DLL dans les jeux.\n\n" +
                "• Smart Overclock entièrement automatique : détecte un jeu (charge GPU 3D > 30 %) et n’active l’OC que pendant le jeu.\n\n" +
                "• Interface plus fluide.",
            BugsBody =
                "• Correction d’un plantage lors d’un mouvement rapide du sélecteur de couleur (onglet Affichage, bug souris WPF).",
            AckCheckbox = "J’ai lu et compris ces notes",
            ContinueButton = "Continuer"
        };

        public static ReleaseNotesCopy Pt() => new()
        {
            WindowTitle = "Mars FPS Monitor — Novidades",
            Title = "Notas de atualização Mars FPS Monitor v2.0",
            LanguageLabel = "Idioma",
            FeaturesHeader = "Atualizações de funcionalidades",
            BugsHeader = "Correções de erros",
            FeaturesBody =
                "• Novo separador Início com UI estilo Gauges para temperaturas CPU/GPU e uso de RAM.\n\n" +
                "• Separador Biblioteca (pré-visualização): ver e iniciar jogos ativos. Definições por jogo em breve.\n\n" +
                "• Overlay com RTSS e ecrã inteiro exclusivo, sem injetar DLL nos jogos.\n\n" +
                "• Smart Overclock totalmente automático: deteta jogo (carga GPU 3D > 30 %) e ativa OC só enquanto joga.\n\n" +
                "• Interface mais fluida.",
            BugsBody =
                "• Corrigida falha ao agitar rapidamente o seletor de cor no separador Aspeto (bug do rato WPF).",
            AckCheckbox = "Li e compreendi estas notas",
            ContinueButton = "Continuar"
        };

        public static ReleaseNotesCopy Br() => new()
        {
            WindowTitle = "Mars FPS Monitor — Novidades",
            Title = "Notas de atualização Mars FPS Monitor v2.0",
            LanguageLabel = "Idioma",
            FeaturesHeader = "Atualizações de recursos",
            BugsHeader = "Correções de bugs",
            FeaturesBody =
                "• Nova aba Início com UI estilo Gauges para temperaturas de CPU/GPU e uso de RAM.\n\n" +
                "• Aba Biblioteca (prévia): ver e iniciar jogos ativos. Ajustes por jogo em atualizações futuras.\n\n" +
                "• Overlay com suporte a RTSS e tela cheia exclusiva, sem injetar DLL nos jogos.\n\n" +
                "• Smart Overclock totalmente automático: detecta jogo (carga GPU 3D > 30 %) e ativa OC só durante o jogo.\n\n" +
                "• Interface mais fluida.",
            BugsBody =
                "• Corrigida falha ao balançar rapidamente o seletor de cores na aba Aparência (bug de mouse do WPF).",
            AckCheckbox = "Li e entendi estas notas",
            ContinueButton = "Continuar"
        };

        public static ReleaseNotesCopy Ru() => new()
        {
            WindowTitle = "Mars FPS Monitor — Что нового",
            Title = "Заметки о выпуске Mars FPS Monitor v2.0",
            LanguageLabel = "Язык",
            FeaturesHeader = "Обновления функций",
            BugsHeader = "Исправления ошибок",
            FeaturesBody =
                "• Новая вкладка «Главная» с UI в стиле Gauges: температуры CPU/GPU и использование RAM.\n\n" +
                "• Вкладка «Библиотека» (превью): просмотр и запуск активных игр. Позже — настройки по играм.\n\n" +
                "• Overlay: поддержка RTSS и exclusive fullscreen без DLL-инъекций в игры.\n\n" +
                "• Smart Overclock полностью автоматический: определяет игру (GPU 3D Load > 30 %) и включает OC только во время игры.\n\n" +
                "• Интерфейс стал плавнее.",
            BugsBody =
                "• Исправлен сбой при быстром движении панели выбора цвета во вкладке «Вид» (баг мыши WPF).",
            AckCheckbox = "Я прочитал(а) и понял(а) эти заметки",
            ContinueButton = "Продолжить"
        };

        public static ReleaseNotesCopy Az() => new()
        {
            WindowTitle = "Mars FPS Monitor — Yeniliklər",
            Title = "Mars FPS Monitor v2.0 Yeniləmə Qeydləri",
            LanguageLabel = "Dil",
            FeaturesHeader = "Xüsusiyyət yeniləmələri",
            BugsHeader = "Xəta düzəlişləri",
            FeaturesBody =
                "• Yeni Ana Ekran tabı əlavə olundu: Gauges üslubunda CPU/GPU temperaturu və RAM istifadəsi.\n\n" +
                "• Kitabxanam tabı (ön baxış): aktiv oyunları görüb başlada bilərsiniz. Gələcəkdə oyunəxas ayarlar.\n\n" +
                "• Overlay-də RTSS və tam ekran dəstəyi — oyunlara DLL inject etmədən.\n\n" +
                "• Smart Overclock tam avtomatikdir: oyun aşkarlayır (GPU 3D Load > 30 %) və yalnız oyun zamanı OC açır.\n\n" +
                "• İnterfeys daha axıcıdır.",
            BugsBody =
                "• Görünüş tabında Rəng Seçicini sürətlə silkələyəndə WPF siçan bug-u ilə çökmə düzəldildi.",
            AckCheckbox = "Oxudum və başa düşdüm",
            ContinueButton = "Davam et"
        };

        public static ReleaseNotesCopy Zh() => new()
        {
            WindowTitle = "Mars FPS Monitor — 更新说明",
            Title = "Mars FPS Monitor v2.0 更新说明",
            LanguageLabel = "语言",
            FeaturesHeader = "功能更新",
            BugsHeader = "错误修复",
            FeaturesBody =
                "• 新增主页选项卡，以 Gauges 风格显示 CPU/GPU 温度与内存占用。\n\n" +
                "• 新增“我的库”选项卡（预览）：可查看并启动已安装游戏；后续将提供按游戏的设置与信息界面。\n\n" +
                "• Overlay 支持 RTSS 与独占全屏；我们选择不向游戏目录注入 DLL，全屏 HUD 由 RTSS 提供。\n\n" +
                "• 智能超频升级为全自动：通过算法判断是否在游戏中（GPU 3D Load > 30%），仅在游戏时开启 OC，空闲时关闭。\n\n" +
                "• 界面更流畅。",
            BugsBody =
                "• 修复在“外观”选项卡快速晃动颜色选择器时，因 WPF 鼠标点击/移动问题导致的崩溃。",
            AckCheckbox = "我已阅读并理解以上说明",
            ContinueButton = "继续"
        };
    }
}
