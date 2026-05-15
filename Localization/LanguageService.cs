// ============================================================================
//  LanguageService.cs  —  Servei de localització en temps d'execució
//
//  Responsabilitats:
//    · Mantenir l'idioma actiu (ca / es / en).
//    · Intercanviar el ResourceDictionary d'strings a Application.Current.
//    · Persistir la preferència a %AppData%\E3G\BIMTools\settings.json.
//    · Disparar LanguageChanged perquè els controls subscriguin i refresquin.
//
//  Ús ràpid:
//      LanguageService.Set("es");            // canvi immediat
//      LanguageService.LanguageChanged += () => ApplyLanguage();
//      string cur = LanguageService.Current; // "ca" | "es" | "en"
//
//  Notes:
//    · El diccionari s'insereix/substitueix a MergedDictionaries de
//      Application.Current.Resources, de manera que tots els controls que
//      usen {DynamicResource Str.Xxx} reaccionen automàticament.
//    · S'ha de cridar des del fil de la UI (Dispatcher).
// ============================================================================
using System;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace E3G.Saneamiento.Localization
{
    public static class LanguageService
    {
        // ── Constants ────────────────────────────────────────────────────
        public const string Ca = "ca";
        public const string Es = "es";
        public const string En = "en";

        private const string SettingsFolder = "E3G\\BIMTools";
        private const string SettingsFile   = "settings.json";

        // ── Estat ────────────────────────────────────────────────────────
        private static string _current = Ca;
        private static ResourceDictionary _activeLangDict;

        /// <summary>Idioma actual: "ca", "es" o "en".</summary>
        public static string Current => _current;

        /// <summary>Es dispara just DESPRÉS d'aplicar el nou diccionari.</summary>
        public static event Action LanguageChanged;

        // ── API pública ──────────────────────────────────────────────────

        /// <summary>
        /// Canvia l'idioma de tota l'aplicació i persisteix la preferència.
        /// Si <paramref name="lang"/> ja és l'actiu, no fa res.
        /// </summary>
        public static void Set(string lang)
        {
            lang = Normalize(lang);
            if (lang == _current && _activeLangDict != null) return;

            _current = lang;
            LoadDictionary(lang);
            Save(lang);
            LanguageChanged?.Invoke();
        }

        /// <summary>
        /// Carrega l'idioma guardat a AppData (o "ca" per defecte) i
        /// l'aplica sense disparar LanguageChanged.  Cridar des del
        /// constructor de la finestra principal / App.xaml.cs.
        /// </summary>
        public static void Init()
        {
            var saved = Load();
            _current = Normalize(saved);
            LoadDictionary(_current);
            // NO disparem LanguageChanged aquí: els controls encara no estan
            // creats del tot (es cridarà des del constructor just abans de
            // InitializeComponent o just després de Loaded).
        }

        // ── Internos ─────────────────────────────────────────────────────

        private static void LoadDictionary(string lang)
        {
            try
            {
                var uri = new Uri(
                    $"pack://application:,,,/E3GBIMTools;component/Localization/Strings.{lang}.xaml",
                    UriKind.Absolute);

                var newDict = new ResourceDictionary { Source = uri };

                var merged = Application.Current?.Resources?.MergedDictionaries;
                if (merged == null) return;

                // Eliminar qualsevol diccionari de strings anterior.
                if (_activeLangDict != null && merged.Contains(_activeLangDict))
                    merged.Remove(_activeLangDict);

                merged.Add(newDict);
                _activeLangDict = newDict;
            }
            catch (Exception ex)
            {
                // Fallback silenciós: si el fitxer no es troba (e.g. primera
                // compilació) no trenquem l'app.
                System.Diagnostics.Debug.WriteLine($"[LanguageService] Error carregant {lang}: {ex.Message}");
            }
        }

        // ── Persistència ─────────────────────────────────────────────────

        private static string SettingsPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, SettingsFolder, SettingsFile);
        }

        private static void Save(string lang)
        {
            try
            {
                var path = SettingsPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                // Llegim el JSON existent per no sobreescriure altres claus.
                var dict = new System.Collections.Generic.Dictionary<string, string>();
                if (File.Exists(path))
                {
                    var raw = File.ReadAllText(path);
                    try { dict = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(raw)
                              ?? dict; }
                    catch { /* JSON malmès → partir de zero */ }
                }
                dict["IdiomaPlugin"] = lang;
                File.WriteAllText(path, JsonSerializer.Serialize(dict,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LanguageService] Error guardant: {ex.Message}");
            }
        }

        private static string Load()
        {
            try
            {
                var path = SettingsPath();
                if (!File.Exists(path)) return Ca;
                var raw  = File.ReadAllText(path);
                var dict = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(raw);
                return dict != null && dict.TryGetValue("IdiomaPlugin", out var v) ? v : Ca;
            }
            catch { return Ca; }
        }

        private static string Normalize(string lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return Ca;
            return lang.Trim().ToLowerInvariant() switch
            {
                "ca" or "català" or "catala"    => Ca,
                "es" or "español" or "espanol"  => Es,
                "en" or "english"               => En,
                _                               => Ca,
            };
        }
    }
}
