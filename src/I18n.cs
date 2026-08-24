// Localization. To add a language: copy a block, translate the values, append it to Catalog.
// Nothing else to wire up - the language menu and the settings file are both driven by Catalog.
// Keep Short5h / Short7d very short; they render in a narrow column.
// Save this file as UTF-8 (the build passes /codepage:65001).
using System;

namespace Vibespan
{
    public class Strings
    {
        public string Code, Native;

        // status
        public string Loading, Offline, FrozenFor, Updated, ResetsIn, Colon;
        public string DayUnit, HourUnit, MinuteUnit;
        public string SourceLive, SourcePolled, ActiveLimit;

        // errors
        public string ErrNotSignedIn, ErrBadResponse, ErrRateLimited;

        // menu - top level
        public string MenuRefresh, MenuMetrics, MenuSize, MenuAppearance, MenuAlerts,
                      MenuLanguage, MenuQuit, MenuOpenLog, MenuOpenSettings,
                      MenuAlwaysOnTop, MenuClickThrough, MenuStartWithWindows,
                      MenuUseLiveFeed, MenuBringToCentre, MenuBehaviour;

        // menu - appearance
        public string MenuTheme, MenuOpacity, MenuOrientation, MenuHorizontal, MenuVertical,
                      MenuShowLogo, MenuShowBorder, MenuShowBackground, MenuResetAppearance, MenuResetSize,
                      MenuHideFullScreen, MenuStyle, MenuFont, MenuMoreFonts, MenuBarStyle,
                      MenuMark, MenuShowLabel, MenuRefreshEvery, MenuMinutes, MenuFeedHint,
                      MenuMode, MenuModeWidget, MenuModeHairline, MenuEdge, MenuEdgeBottom,
                      MenuEdgeTop, MenuEdgeLeft, MenuEdgeRight, MenuThickness, MenuHairlineHint;

        // menu - rows
        public string MenuColour, MenuMoreColours, MenuShowRemaining, MenuRemaining,
                      MenuCountdown, MenuClock, MenuOff, MenuMoveUp, MenuMoveDown,
                      MenuUseThemeColour;

        // menu - alerts
        public string MenuNotifyAt, MenuPlaySound, MenuMuteOneHour, MenuMuted;

        // alerts
        public string AlertTitle, AlertBody;

        // live feed
        public string FeedBusy, FeedEnabled, FeedDisabled;
    }

    public static class I18n
    {
        public static readonly Strings[] Catalog = { English(), French(), Spanish(), German() };
        public static Strings T = Catalog[0];

        public static void Use(string code)
        {
            foreach (Strings s in Catalog) if (s.Code == code) { T = s; return; }
            T = Catalog[0];
        }

        static Strings English()
        {
            return new Strings
            {
                Code = "en", Native = "English",
                Loading = "loading...", Offline = "Offline: {0}",
                FrozenFor = "Frozen for {0}: {1}", Updated = "updated {0}",
                ResetsIn = "resets in {0}", Colon = ": ",
                DayUnit = "d", HourUnit = "h", MinuteUnit = "min",
                SourceLive = "live from Claude Code", SourcePolled = "polled",
                ActiveLimit = "currently the binding limit",
                ErrNotSignedIn = "Claude Code is not signed in (run it once)",
                ErrBadResponse = "Unreadable API response",
                ErrRateLimited = "Rate limited - backing off",
                MenuRefresh = "Refresh now", MenuMetrics = "Metrics", MenuSize = "Size",
                MenuAppearance = "Appearance", MenuAlerts = "Alerts", MenuLanguage = "Language",
                MenuQuit = "Quit", MenuOpenLog = "Open log", MenuOpenSettings = "Open settings file",
                MenuBehaviour = "Behaviour", MenuAlwaysOnTop = "Always on top", MenuClickThrough = "Click-through",
                MenuStartWithWindows = "Start with Windows",
                MenuUseLiveFeed = "Use Claude Code live feed",
                MenuBringToCentre = "Bring to centre",
                MenuTheme = "Theme", MenuOpacity = "Opacity", MenuOrientation = "Orientation",
                MenuHorizontal = "Horizontal", MenuVertical = "Vertical",
                MenuShowLogo = "Show logo", MenuShowBorder = "Show border",
                MenuShowBackground = "Show background",
                MenuResetAppearance = "Reset appearance", MenuResetSize = "Reset size",
                MenuHideFullScreen = "Hide in full-screen apps",
                MenuMode = "Display", MenuModeWidget = "Widget",
                MenuModeHairline = "Screen-edge hairline", MenuEdge = "Edge",
                MenuEdgeBottom = "Bottom", MenuEdgeTop = "Top",
                MenuEdgeLeft = "Left", MenuEdgeRight = "Right",
                MenuThickness = "Line thickness", MenuHairlineHint = "No text in this mode - hover for the numbers, or use the tray icon",
                MenuStyle = "Style", MenuFont = "Font",
                MenuMoreFonts = "More fonts...", MenuBarStyle = "Bar style",
                MenuMark = "Mark", MenuShowLabel = "Show label",
                MenuRefreshEvery = "Refresh every", MenuMinutes = "{0} min",
                MenuFeedHint = "Faster than this risks rate limits - use the live feed instead",
                MenuColour = "Colour", MenuMoreColours = "More colours...",
                MenuShowRemaining = "Show remaining instead of used",
                MenuRemaining = "Remaining", MenuCountdown = "Countdown", MenuClock = "Clock time",
                MenuOff = "Off", MenuMoveUp = "Move up", MenuMoveDown = "Move down",
                MenuUseThemeColour = "Use theme colour",
                MenuNotifyAt = "Notify at {0}%", MenuPlaySound = "Play sound",
                MenuMuteOneHour = "Mute for 1 hour", MenuMuted = "Muted until {0}",
                AlertTitle = "Vibespan",
                AlertBody = "{0}: {1}% used - resets in {2}",
                FeedBusy = "Claude Code already has a status line configured",
                FeedEnabled = "Live feed enabled", FeedDisabled = "Live feed disabled"
            };
        }

        static Strings French()
        {
            return new Strings
            {
                Code = "fr", Native = "Français",
                Loading = "chargement...", Offline = "Hors ligne : {0}",
                FrozenFor = "Figé depuis {0} : {1}", Updated = "maj {0}",
                ResetsIn = "reset dans {0}", Colon = " : ",
                DayUnit = "j", HourUnit = "h", MinuteUnit = "min",
                SourceLive = "en direct de Claude Code", SourcePolled = "interrogé",
                ActiveLimit = "limite actuellement contraignante",
                ErrNotSignedIn = "Claude Code n'est pas connecté (lance-le une fois)",
                ErrBadResponse = "Réponse de l'API illisible",
                ErrRateLimited = "Trop de requêtes - mise en pause",
                MenuRefresh = "Actualiser", MenuMetrics = "Mesures", MenuSize = "Taille",
                MenuAppearance = "Apparence", MenuAlerts = "Alertes", MenuLanguage = "Langue",
                MenuQuit = "Quitter", MenuOpenLog = "Ouvrir le journal",
                MenuOpenSettings = "Ouvrir le fichier de réglages",
                MenuBehaviour = "Comportement", MenuAlwaysOnTop = "Toujours au premier plan", MenuClickThrough = "Clics traversants",
                MenuStartWithWindows = "Lancer au démarrage de Windows",
                MenuUseLiveFeed = "Utiliser le flux direct de Claude Code",
                MenuBringToCentre = "Ramener au centre",
                MenuTheme = "Thème", MenuOpacity = "Opacité", MenuOrientation = "Orientation",
                MenuHorizontal = "Horizontale", MenuVertical = "Verticale",
                MenuShowLogo = "Afficher le logo", MenuShowBorder = "Afficher la bordure",
                MenuShowBackground = "Afficher le fond",
                MenuResetAppearance = "Réinitialiser l'apparence", MenuResetSize = "Réinitialiser la taille",
                MenuHideFullScreen = "Masquer en plein écran",
                MenuMode = "Affichage", MenuModeWidget = "Widget",
                MenuModeHairline = "Filet au bord de l’écran", MenuEdge = "Bord",
                MenuEdgeBottom = "Bas", MenuEdgeTop = "Haut",
                MenuEdgeLeft = "Gauche", MenuEdgeRight = "Droite",
                MenuThickness = "Épaisseur du trait", MenuHairlineHint = "Aucun texte ici - survolez pour les chiffres, ou utilisez l’icône",
                MenuStyle = "Style", MenuFont = "Police",
                MenuMoreFonts = "Autres polices...", MenuBarStyle = "Style de barre",
                MenuMark = "Marque", MenuShowLabel = "Afficher le titre",
                MenuRefreshEvery = "Actualiser toutes les", MenuMinutes = "{0} min",
                MenuFeedHint = "Plus rapide risque une limitation - preferez le flux direct",
                MenuColour = "Couleur", MenuMoreColours = "Plus de couleurs...",
                MenuShowRemaining = "Afficher le restant plutôt que l'utilisé",
                MenuRemaining = "Restant", MenuCountdown = "Compte à rebours", MenuClock = "Heure",
                MenuOff = "Masqué", MenuMoveUp = "Monter", MenuMoveDown = "Descendre",
                MenuUseThemeColour = "Couleur du thème",
                MenuNotifyAt = "Alerter à {0} %", MenuPlaySound = "Jouer un son",
                MenuMuteOneHour = "Couper 1 heure", MenuMuted = "Coupé jusqu'à {0}",
                AlertTitle = "Vibespan",
                AlertBody = "{0} : {1} % utilisés - reset dans {2}",
                FeedBusy = "Claude Code a déjà une status line configurée",
                FeedEnabled = "Flux direct activé", FeedDisabled = "Flux direct désactivé"
            };
        }

        static Strings Spanish()
        {
            return new Strings
            {
                Code = "es", Native = "Español",
                Loading = "cargando...", Offline = "Sin conexión: {0}",
                FrozenFor = "Congelado desde hace {0}: {1}", Updated = "act. {0}",
                ResetsIn = "se reinicia en {0}", Colon = ": ",
                DayUnit = "d", HourUnit = "h", MinuteUnit = "min",
                SourceLive = "en directo desde Claude Code", SourcePolled = "consultado",
                ActiveLimit = "límite vinculante actualmente",
                ErrNotSignedIn = "Claude Code no ha iniciado sesión (ejecútalo una vez)",
                ErrBadResponse = "Respuesta de la API ilegible",
                ErrRateLimited = "Demasiadas peticiones - pausando",
                MenuRefresh = "Actualizar", MenuMetrics = "Métricas", MenuSize = "Tamaño",
                MenuAppearance = "Apariencia", MenuAlerts = "Alertas", MenuLanguage = "Idioma",
                MenuQuit = "Salir", MenuOpenLog = "Abrir el registro",
                MenuOpenSettings = "Abrir el archivo de ajustes",
                MenuBehaviour = "Comportamiento", MenuAlwaysOnTop = "Siempre visible", MenuClickThrough = "Clics atravesables",
                MenuStartWithWindows = "Iniciar con Windows",
                MenuUseLiveFeed = "Usar el flujo en directo de Claude Code",
                MenuBringToCentre = "Traer al centro",
                MenuTheme = "Tema", MenuOpacity = "Opacidad", MenuOrientation = "Orientación",
                MenuHorizontal = "Horizontal", MenuVertical = "Vertical",
                MenuShowLogo = "Mostrar el logo", MenuShowBorder = "Mostrar el borde",
                MenuShowBackground = "Mostrar el fondo",
                MenuResetAppearance = "Restablecer la apariencia", MenuResetSize = "Restablecer el tamaño",
                MenuHideFullScreen = "Ocultar en pantalla completa",
                MenuMode = "Visualización", MenuModeWidget = "Widget",
                MenuModeHairline = "Filete en el borde", MenuEdge = "Borde",
                MenuEdgeBottom = "Abajo", MenuEdgeTop = "Arriba",
                MenuEdgeLeft = "Izquierda", MenuEdgeRight = "Derecha",
                MenuThickness = "Grosor de la línea", MenuHairlineHint = "Sin texto aquí - pasa el ratón o usa el icono de la bandeja",
                MenuStyle = "Estilo", MenuFont = "Fuente",
                MenuMoreFonts = "Mas fuentes...", MenuBarStyle = "Estilo de barra",
                MenuMark = "Marca", MenuShowLabel = "Mostrar el titulo",
                MenuRefreshEvery = "Actualizar cada", MenuMinutes = "{0} min",
                MenuFeedHint = "Mas rapido arriesga limites - usa el flujo en directo",
                MenuColour = "Color", MenuMoreColours = "Más colores...",
                MenuShowRemaining = "Mostrar lo restante en vez de lo usado",
                MenuRemaining = "Restante", MenuCountdown = "Cuenta atrás", MenuClock = "Hora",
                MenuOff = "Oculto", MenuMoveUp = "Subir", MenuMoveDown = "Bajar",
                MenuUseThemeColour = "Color del tema",
                MenuNotifyAt = "Avisar al {0} %", MenuPlaySound = "Reproducir un sonido",
                MenuMuteOneHour = "Silenciar 1 hora", MenuMuted = "Silenciado hasta {0}",
                AlertTitle = "Vibespan",
                AlertBody = "{0}: {1} % usado - se reinicia en {2}",
                FeedBusy = "Claude Code ya tiene una status line configurada",
                FeedEnabled = "Flujo en directo activado", FeedDisabled = "Flujo en directo desactivado"
            };
        }

        static Strings German()
        {
            return new Strings
            {
                Code = "de", Native = "Deutsch",
                Loading = "lädt...", Offline = "Offline: {0}",
                FrozenFor = "Eingefroren seit {0}: {1}", Updated = "akt. {0}",
                ResetsIn = "zurückgesetzt in {0}", Colon = ": ",
                DayUnit = "T", HourUnit = "h", MinuteUnit = "Min",
                SourceLive = "live von Claude Code", SourcePolled = "abgefragt",
                ActiveLimit = "derzeit bindendes Limit",
                ErrNotSignedIn = "Claude Code ist nicht angemeldet (einmal starten)",
                ErrBadResponse = "Unlesbare API-Antwort",
                ErrRateLimited = "Zu viele Anfragen - Pause",
                MenuRefresh = "Aktualisieren", MenuMetrics = "Messwerte", MenuSize = "Größe",
                MenuAppearance = "Aussehen", MenuAlerts = "Warnungen", MenuLanguage = "Sprache",
                MenuQuit = "Beenden", MenuOpenLog = "Protokoll öffnen",
                MenuOpenSettings = "Einstellungsdatei öffnen",
                MenuBehaviour = "Verhalten", MenuAlwaysOnTop = "Immer im Vordergrund", MenuClickThrough = "Klicks durchlassen",
                MenuStartWithWindows = "Mit Windows starten",
                MenuUseLiveFeed = "Live-Feed von Claude Code nutzen",
                MenuBringToCentre = "In die Mitte holen",
                MenuTheme = "Thema", MenuOpacity = "Deckkraft", MenuOrientation = "Ausrichtung",
                MenuHorizontal = "Waagerecht", MenuVertical = "Senkrecht",
                MenuShowLogo = "Logo anzeigen", MenuShowBorder = "Rahmen anzeigen",
                MenuShowBackground = "Hintergrund anzeigen",
                MenuResetAppearance = "Aussehen zurücksetzen", MenuResetSize = "Größe zurücksetzen",
                MenuHideFullScreen = "Bei Vollbild ausblenden",
                MenuMode = "Darstellung", MenuModeWidget = "Widget",
                MenuModeHairline = "Bildschirmkante", MenuEdge = "Kante",
                MenuEdgeBottom = "Unten", MenuEdgeTop = "Oben",
                MenuEdgeLeft = "Links", MenuEdgeRight = "Rechts",
                MenuThickness = "Linienstärke", MenuHairlineHint = "Kein Text - fahre drüber oder nutze das Infobereich-Symbol",
                MenuStyle = "Stil", MenuFont = "Schrift",
                MenuMoreFonts = "Weitere Schriften...", MenuBarStyle = "Balkenstil",
                MenuMark = "Markierung", MenuShowLabel = "Titel anzeigen",
                MenuRefreshEvery = "Aktualisieren alle", MenuMinutes = "{0} Min",
                MenuFeedHint = "Schneller riskiert Limits - nutze den Live-Feed",
                MenuColour = "Farbe", MenuMoreColours = "Weitere Farben...",
                MenuShowRemaining = "Rest statt Verbrauch anzeigen",
                MenuRemaining = "Rest", MenuCountdown = "Countdown", MenuClock = "Uhrzeit",
                MenuOff = "Aus", MenuMoveUp = "Nach oben", MenuMoveDown = "Nach unten",
                MenuUseThemeColour = "Themenfarbe",
                MenuNotifyAt = "Warnen bei {0} %", MenuPlaySound = "Ton abspielen",
                MenuMuteOneHour = "1 Stunde stumm", MenuMuted = "Stumm bis {0}",
                AlertTitle = "Vibespan",
                AlertBody = "{0}: {1} % verbraucht - Reset in {2}",
                FeedBusy = "Claude Code hat bereits eine Statuszeile konfiguriert",
                FeedEnabled = "Live-Feed aktiviert", FeedDisabled = "Live-Feed deaktiviert"
            };
        }
    }

    /// <summary>Duration and clock formatting, shared by the gauge and the tooltip.</summary>
    public static class Fmt
    {
        static Strings L { get { return I18n.T; } }

        /// <summary>"4h53", "2d 3h", "12 min" - or "" once the window has already reset.</summary>
        public static string Countdown(DateTimeOffset? resetsAt)
        {
            if (!resetsAt.HasValue) return "";
            TimeSpan t = resetsAt.Value - DateTimeOffset.UtcNow;
            if (t.TotalSeconds <= 0) return "";
            if (t.TotalHours >= 24)
                return string.Format("{0}{1} {2}{3}", (int)Math.Floor(t.TotalDays), L.DayUnit, t.Hours, L.HourUnit);
            if (t.TotalHours >= 1)
                return string.Format("{0}{1}{2:00}", (int)Math.Floor(t.TotalHours), L.HourUnit, t.Minutes);
            return string.Format("{0} {1}", (int)Math.Ceiling(t.TotalMinutes), L.MinuteUnit);
        }

        public static string Clock(DateTimeOffset? resetsAt)
        {
            if (!resetsAt.HasValue) return "";
            return resetsAt.Value.ToLocalTime().ToString("HH:mm");
        }

        public static string Age(TimeSpan t)
        {
            if (t.TotalHours >= 24) return string.Format("{0} {1}", (int)Math.Floor(t.TotalDays), L.DayUnit);
            if (t.TotalHours >= 1) return string.Format("{0}{1}{2:00}", (int)Math.Floor(t.TotalHours), L.HourUnit, t.Minutes);
            return string.Format("{0} {1}", Math.Max(1, (int)Math.Floor(t.TotalMinutes)), L.MinuteUnit);
        }

        public static string ErrorText(string code)
        {
            if (code == "notSignedIn") return L.ErrNotSignedIn;
            if (code == "badResponse") return L.ErrBadResponse;
            if (code == "rateLimited") return L.ErrRateLimited;
            return code;
        }
    }
}
