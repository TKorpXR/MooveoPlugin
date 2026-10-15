using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Debug = UnityEngine.Debug;

/// <summary>
/// Met la configuration de SteamVR en accord avec le profil matériel : pilote null sans casque
/// (Vive Trackers) ou vrai casque (PICO). Mêmes règles que <c>tools/steamvr/Set-SteamVRProfile.ps1</c>.
/// N'utilise aucune API Unity hors des journaux : peut tourner hors du fil principal.
/// </summary>
public static class SteamVRConfig
{
    /// <summary>Mode sans casque : pilote null, trackers seuls.</summary>
    public const string ModeNoHeadset = "NoHeadset";

    /// <summary>Mode avec un vrai casque.</summary>
    public const string ModeHeadset = "Headset";

    /// <summary>Univers de suivi du casque factice, exigé dans la configuration de pièce.</summary>
    public const string NullDriverUniverseId = "2";

    /// <summary>Processus de SteamVR, tous fermés avant d'écrire (SteamVR réécrit ses réglages en quittant).</summary>
    public static readonly string[] ProcessNames =
        { "vrmonitor", "vrserver", "vrcompositor", "vrdashboard", "vrwebhelper", "vrstartup", "vrserverhelper" };

    private const string DefaultSteamRoot = @"C:\Program Files (x86)\Steam";
    private const int KeptBackups = 10;

    /// <summary>Fichiers de configuration de SteamVR sur ce poste.</summary>
    public sealed class Paths
    {
        /// <summary>Réglages utilisateur, ceux qui font foi (dossier <c>config</c> de <c>openvrpaths.vrpath</c>).</summary>
        public string UserSettings;
        /// <summary>Défauts du pilote null.</summary>
        public string NullDriverDefaults;
        /// <summary>Défauts de SteamVR, écrasés à chaque mise à jour.</summary>
        public string SteamVRDefaults;
        /// <summary>Configuration de pièce lue par SteamVR.</summary>
        public string ChaperoneInfo;
        /// <summary>Ancienne configuration de pièce (<c>Steam/config</c>), source de l'univers de production.</summary>
        public string LegacyChaperoneInfo;
        /// <summary>Dossier d'installation de SteamVR.</summary>
        public string SteamVRDirectory;
        /// <summary>Exécutable de vrmonitor.</summary>
        public string VrMonitorExe;
    }

    /// <summary>Une valeur à corriger : fichier, clé, valeur actuelle et valeur attendue.</summary>
    public sealed class Difference
    {
        public string File;
        public string Key;
        public string Current;
        public string Expected;
        public override string ToString() => $"{Key} = {Current} (attendu : {Expected})";
    }

    /// <summary>Issue d'une vérification / correction.</summary>
    public enum Outcome { AlreadyConfigured, Applied, SteamVRRunning, Failed }

    /// <summary>Résultat de <see cref="Ensure"/>.</summary>
    public sealed class Result
    {
        public Outcome Outcome;
        public string Message;
        public List<Difference> Fixed = new List<Difference>();
        public bool ClosedSteamVR;
        public string BackupDirectory;
        public bool Ok => Outcome == Outcome.AlreadyConfigured || Outcome == Outcome.Applied;
    }

    // ---------------------------------------------------------------------------------------------
    // Localisation
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Trouve les fichiers via <c>%LOCALAPPDATA%/openvr/openvrpaths.vrpath</c>, puis
    /// <paramref name="vrMonitorHint"/>, puis l'installation Steam par défaut.
    /// </summary>
    public static bool TryLocate(string vrMonitorHint, out Paths paths, out string error)
    {
        paths = null;
        error = null;

        string steamVrDir = null;
        var configDirs = new List<string>();

        try
        {
            string registry = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "openvr", "openvrpaths.vrpath");
            if (File.Exists(registry))
            {
                JObject json = JObject.Parse(File.ReadAllText(registry));
                foreach (JToken dir in json["config"] ?? new JArray()) configDirs.Add((string)dir);
                foreach (JToken dir in json["runtime"] ?? new JArray())
                {
                    string candidate = (string)dir;
                    if (Directory.Exists(candidate)) { steamVrDir = candidate; break; }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SteamVRConfig] openvrpaths.vrpath illisible, repli sur les chemins connus : {e.Message}");
        }

        if (steamVrDir == null && !string.IsNullOrEmpty(vrMonitorHint))
        {
            string fromHint = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(vrMonitorHint) ?? "", "..", ".."));
            if (Directory.Exists(fromHint)) steamVrDir = fromHint;
        }
        if (steamVrDir == null)
        {
            string fromDefault = Path.Combine(DefaultSteamRoot, "steamapps", "common", "SteamVR");
            if (Directory.Exists(fromDefault)) steamVrDir = fromDefault;
        }
        if (steamVrDir == null)
        {
            error = "Dossier de SteamVR introuvable (ni openvrpaths.vrpath, ni SteamVREXEPath, ni " +
                    $"{DefaultSteamRoot}). SteamVR est-il installé ?";
            return false;
        }

        string legacyConfig = Path.GetFullPath(Path.Combine(steamVrDir, "..", "..", "..", "config"));
        configDirs.Add(legacyConfig);
        string configDir = configDirs.FirstOrDefault(d => File.Exists(Path.Combine(d, "steamvr.vrsettings")))
                           ?? configDirs.First();

        paths = new Paths
        {
            SteamVRDirectory = steamVrDir,
            UserSettings = Path.Combine(configDir, "steamvr.vrsettings"),
            ChaperoneInfo = Path.Combine(configDir, "chaperone_info.vrchap"),
            LegacyChaperoneInfo = Path.Combine(legacyConfig, "chaperone_info.vrchap"),
            NullDriverDefaults = Path.Combine(steamVrDir, "drivers", "null", "resources", "settings", "default.vrsettings"),
            SteamVRDefaults = Path.Combine(steamVrDir, "resources", "settings", "default.vrsettings"),
            VrMonitorExe = Path.Combine(steamVrDir, "bin", "win64", "vrmonitor.exe")
        };
        return true;
    }

    // ---------------------------------------------------------------------------------------------
    // Valeurs attendues
    // ---------------------------------------------------------------------------------------------

    /// <summary>Vrai pour le mode sans casque.</summary>
    private static bool IsNoHeadset(string mode) =>
        string.Equals(mode, ModeNoHeadset, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Réglages utilisateur attendus pour le mode. Une valeur <c>null</c> retire la clé (défaut SteamVR).
    /// Sans casque : clés SteamVRNoHeadset et configuration kiosque de production.
    /// </summary>
    private static IEnumerable<(string section, string key, JToken value)> ExpectedUserSettings(string mode)
    {
        bool noHeadset = IsNoHeadset(mode);
        yield return ("steamvr", "requireHmd", new JValue(!noHeadset));
        yield return ("steamvr", "forcedDriver", new JValue(noHeadset ? "null" : ""));
        yield return ("steamvr", "activateMultipleDrivers", new JValue(noHeadset));
        yield return ("driver_null", "enable", new JValue(noHeadset));

        if (noHeadset)
        {
            yield return ("driver_null", "renderWidth", new JValue(16));
            yield return ("driver_null", "renderHeight", new JValue(16));
            yield return ("driver_null", "displayFrequency", new JValue(60));
            yield return ("steamvr", "motionSmoothing", new JValue(false));
            yield return ("steamvr", "supersampleManualOverride", new JValue(true));
            yield return ("steamvr", "supersampleScale", new JValue(0.5));
            yield return ("steamvr", "hasSeenRoomSetup", new JValue(true));
        }
        else
        {
            yield return ("steamvr", "motionSmoothing", null);
            yield return ("steamvr", "supersampleManualOverride", null);
            yield return ("steamvr", "supersampleScale", null);
        }
    }

    /// <summary>Les trois clés SteamVRNoHeadset dans les défauts de SteamVR (valeurs de Valve en mode casque).</summary>
    private static IEnumerable<(string key, JToken value)> ExpectedSteamVRDefaults(string mode)
    {
        bool noHeadset = IsNoHeadset(mode);
        yield return ("requireHmd", new JValue(!noHeadset));
        yield return ("forcedDriver", new JValue(noHeadset ? "null" : ""));
        yield return ("activateMultipleDrivers", new JValue(noHeadset));
    }

    /// <summary>Valeur <c>enable</c> du fichier de défauts du pilote null.</summary>
    private static readonly Regex NullEnable = new Regex("(\"enable\"\\s*:\\s*)(true|false)", RegexOptions.IgnoreCase);

    /// <summary>Valeur (booléen ou chaîne) d'une clé JSON, pour un remplacement en place.</summary>
    private static Regex ValueOf(string key) =>
        new Regex("(\"" + Regex.Escape(key) + "\"\\s*:\\s*)(true|false|\"[^\"]*\")");

    /// <summary>Égalité JSON, nombres comparés par valeur (60 = 60.0).</summary>
    private static bool Same(JToken a, JToken b)
    {
        if (a == null || b == null) return a == null && b == null;
        bool numA = a.Type == JTokenType.Integer || a.Type == JTokenType.Float;
        bool numB = b.Type == JTokenType.Integer || b.Type == JTokenType.Float;
        if (numA && numB) return Math.Abs(a.Value<double>() - b.Value<double>()) < 1e-6;
        return JToken.DeepEquals(a, b);
    }

    /// <summary>Valeur lisible pour un message.</summary>
    private static string Show(JToken value) => value == null ? "(absent)" : value.ToString(Formatting.None);

    // ---------------------------------------------------------------------------------------------
    // Inspection
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Écarts bloquants entre la configuration effective (utilisateur, sinon défauts) et le mode.
    /// Une clé absente mais égale au défaut n'est pas un écart. Liste vide = rien à faire.
    /// </summary>
    public static List<Difference> Inspect(Paths paths, string mode)
    {
        var differences = new List<Difference>();

        JObject user = ReadJson(paths.UserSettings);
        JObject steamDefaults = ReadJsonQuietly(paths.SteamVRDefaults);
        JObject nullDefaults = ReadJsonQuietly(paths.NullDriverDefaults);

        foreach (var (section, key, expected) in ExpectedUserSettings(mode))
        {
            JToken current = user[section]?[key];
            JToken fallback = (section == "driver_null" ? nullDefaults[section]?[key] : null)
                              ?? steamDefaults[section]?[key];

            bool wrong;
            string shown, wanted;
            if (expected == null)
            {
                wrong = current != null && !Same(current, fallback);
                shown = Show(current);
                wanted = "(valeur par défaut de SteamVR)";
            }
            else
            {
                JToken effective = current ?? fallback;
                wrong = effective == null || !Same(effective, expected);
                shown = current != null ? Show(current) : $"(absent, défaut {Show(fallback)})";
                wanted = Show(expected);
            }

            if (wrong)
                differences.Add(new Difference
                {
                    File = paths.UserSettings,
                    Key = $"{section}.{key}",
                    Current = shown,
                    Expected = wanted
                });
        }

        if (File.Exists(paths.NullDriverDefaults))
        {
            Match match = NullEnable.Match(File.ReadAllText(paths.NullDriverDefaults));
            string expected = IsNoHeadset(mode) ? "true" : "false";
            if (match.Success && !string.Equals(match.Groups[2].Value, expected, StringComparison.OrdinalIgnoreCase))
                differences.Add(new Difference
                {
                    File = paths.NullDriverDefaults,
                    Key = "driver_null.enable (défauts du pilote null)",
                    Current = match.Groups[2].Value,
                    Expected = expected
                });
        }

        if (IsNoHeadset(mode) && !HasNullDriverUniverse(paths))
            differences.Add(new Difference
            {
                File = paths.ChaperoneInfo,
                Key = $"configuration de pièce, univers {NullDriverUniverseId} (casque factice)",
                Current = "(absent)",
                Expected = "présent"
            });

        return differences;
    }

    /// <summary>Écarts dans les défauts de SteamVR. Jamais bloquants.</summary>
    public static List<Difference> InspectSteamVRDefaults(Paths paths, string mode)
    {
        var differences = new List<Difference>();
        if (!File.Exists(paths.SteamVRDefaults)) return differences;

        JObject defaults = ReadJson(paths.SteamVRDefaults);
        foreach (var (key, expected) in ExpectedSteamVRDefaults(mode))
        {
            JToken current = defaults["steamvr"]?[key];
            if (current != null && !Same(current, expected))
                differences.Add(new Difference
                {
                    File = paths.SteamVRDefaults,
                    Key = $"steamvr.{key} (défauts de SteamVR)",
                    Current = Show(current),
                    Expected = Show(expected)
                });
        }
        return differences;
    }

    /// <summary>Vrai si la configuration de pièce contient l'univers du casque factice (ou est illisible : jamais écrasée).</summary>
    private static bool HasNullDriverUniverse(Paths paths)
    {
        if (!File.Exists(paths.ChaperoneInfo)) return false;
        try
        {
            return FindUniverse(ReadJson(paths.ChaperoneInfo), NullDriverUniverseId) != null;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SteamVRConfig] {paths.ChaperoneInfo} illisible, laissé tel quel : {e.Message}");
            return true;
        }
    }

    /// <summary>Entrée d'un univers dans une configuration de pièce, ou null.</summary>
    private static JObject FindUniverse(JObject chaperone, string universeId) =>
        (chaperone["universes"] as JArray)?.OfType<JObject>()
            .FirstOrDefault(u => string.Equals((string)u["universeID"], universeId, StringComparison.Ordinal));

    /// <summary>Lit un fichier JSON ; absent ou vide = objet vide.</summary>
    private static JObject ReadJson(string path)
    {
        if (!File.Exists(path)) return new JObject();
        string text = File.ReadAllText(path);
        return string.IsNullOrWhiteSpace(text) ? new JObject() : JObject.Parse(text);
    }

    /// <summary>Comme <see cref="ReadJson"/>, mais un fichier illisible donne un objet vide.</summary>
    private static JObject ReadJsonQuietly(string path)
    {
        try { return ReadJson(path); }
        catch (Exception e)
        {
            Debug.LogWarning($"[SteamVRConfig] {path} illisible, ignoré : {e.Message}");
            return new JObject();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Processus
    // ---------------------------------------------------------------------------------------------

    /// <summary>Vrai si un processus de SteamVR tourne.</summary>
    public static bool IsRunning() => ProcessNames.Any(IsProcessRunning);

    /// <summary>Vrai si un processus de ce nom tourne.</summary>
    private static bool IsProcessRunning(string name)
    {
        try
        {
            Process[] found = Process.GetProcessesByName(name);
            foreach (Process p in found) p.Dispose();
            return found.Length > 0;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Ferme SteamVR (poliment, puis de force). Vrai si plus rien ne tourne à l'échéance.</summary>
    public static bool Close(int timeoutMs = 10000)
    {
        var watch = Stopwatch.StartNew();

        foreach (Process monitor in SafeGet("vrmonitor"))
        {
            try { monitor.CloseMainWindow(); } catch (Exception) { }
            monitor.Dispose();
        }
        if (WaitUntilClosed(timeoutMs / 2, watch)) return true;

        foreach (string name in ProcessNames)
        foreach (Process p in SafeGet(name))
        {
            try { if (!p.HasExited) p.Kill(); }
            catch (Exception e) { Debug.LogWarning($"[SteamVRConfig] Impossible d'arrêter {name} : {e.Message}"); }
            p.Dispose();
        }
        return WaitUntilClosed(timeoutMs, watch);
    }

    /// <summary>Attend la fin de tous les processus de SteamVR jusqu'à l'échéance.</summary>
    private static bool WaitUntilClosed(int untilMs, Stopwatch watch)
    {
        while (watch.ElapsedMilliseconds < untilMs)
        {
            if (!IsRunning()) return true;
            System.Threading.Thread.Sleep(200);
        }
        return !IsRunning();
    }

    /// <summary>Processus de ce nom, sans lever.</summary>
    private static Process[] SafeGet(string name)
    {
        try { return Process.GetProcessesByName(name); }
        catch (Exception) { return Array.Empty<Process>(); }
    }

    // ---------------------------------------------------------------------------------------------
    // Écriture
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Écrit le mode dans le fichier utilisateur, les défauts du pilote null et, sans casque, la
    /// configuration de pièce. SteamVR doit être fermé. Lève en cas d'échec.
    /// </summary>
    public static void Apply(Paths paths, string mode)
    {
        JObject user = ReadJson(paths.UserSettings);
        foreach (var (section, key, value) in ExpectedUserSettings(mode))
        {
            if (value == null)
            {
                (user[section] as JObject)?.Remove(key);
                continue;
            }
            if (!(user[section] is JObject block))
            {
                block = new JObject();
                user[section] = block;
            }
            block[key] = value;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(paths.UserSettings) ?? ".");
        WriteUtf8NoBom(paths.UserSettings, user.ToString(Formatting.Indented));

        if (File.Exists(paths.NullDriverDefaults))
        {
            string text = File.ReadAllText(paths.NullDriverDefaults);
            string replaced = NullEnable.Replace(text, m => m.Groups[1].Value + (IsNoHeadset(mode) ? "true" : "false"), 1);
            if (replaced != text) WriteUtf8NoBom(paths.NullDriverDefaults, replaced);
        }

        if (IsNoHeadset(mode) && !HasNullDriverUniverse(paths)) AddNullDriverUniverse(paths);
    }

    /// <summary>
    /// Écrit en place les trois clés SteamVRNoHeadset dans les défauts de SteamVR. Sans danger
    /// SteamVR ouvert (il ne réécrit jamais ce fichier). Lève en cas d'échec.
    /// </summary>
    public static void ApplySteamVRDefaults(Paths paths, string mode)
    {
        if (!File.Exists(paths.SteamVRDefaults)) return;
        string text = File.ReadAllText(paths.SteamVRDefaults);
        string replaced = text;
        foreach (var (key, value) in ExpectedSteamVRDefaults(mode))
            replaced = ValueOf(key).Replace(replaced, m => m.Groups[1].Value + value.ToString(Formatting.None), 1);
        if (replaced == text) return;

        JObject.Parse(replaced);
        WriteUtf8NoBom(paths.SteamVRDefaults, replaced);
    }

    /// <summary>
    /// Ajoute l'univers du casque factice à la configuration de pièce, sans toucher aux autres.
    /// Reprend l'entrée de l'ancienne configuration si elle existe, sinon une pièce neutre.
    /// </summary>
    private static void AddNullDriverUniverse(Paths paths)
    {
        JObject chaperone = File.Exists(paths.ChaperoneInfo)
            ? ReadJson(paths.ChaperoneInfo)
            : new JObject { ["jsonid"] = "chaperone_info", ["version"] = 5 };
        if (!(chaperone["universes"] is JArray universes))
        {
            universes = new JArray();
            chaperone["universes"] = universes;
        }

        JObject entry = null;
        try
        {
            if (File.Exists(paths.LegacyChaperoneInfo))
                entry = FindUniverse(ReadJson(paths.LegacyChaperoneInfo), NullDriverUniverseId)?.DeepClone() as JObject;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SteamVRConfig] Ancienne configuration de pièce illisible, pièce neutre utilisée : {e.Message}");
        }
        universes.Add(entry ?? NeutralUniverse(NullDriverUniverseId));

        Directory.CreateDirectory(Path.GetDirectoryName(paths.ChaperoneInfo) ?? ".");
        WriteUtf8NoBom(paths.ChaperoneInfo, chaperone.ToString(Formatting.Indented));
    }

    /// <summary>Pièce neutre au format de SteamVR : 3 × 2,25 m, murs de 2,43 m, debout à l'origine.</summary>
    private static JObject NeutralUniverse(string universeId)
    {
        const float halfX = 1.5f, halfZ = 1.125f, height = 2.43f;
        var corners = new[] { (-halfX, -halfZ), (halfX, -halfZ), (halfX, halfZ), (-halfX, halfZ) };
        var walls = new JArray();
        for (int i = 0; i < corners.Length; i++)
        {
            var (x0, z0) = corners[i];
            var (x1, z1) = corners[(i + 1) % corners.Length];
            walls.Add(new JArray(
                new JArray(x0, 0f, z0), new JArray(x0, height, z0),
                new JArray(x1, height, z1), new JArray(x1, 0f, z1)));
        }
        return new JObject
        {
            ["collision_bounds"] = walls,
            ["play_area"] = new JArray(halfX * 2f, halfZ * 2f),
            ["seated"] = new JObject { ["translation"] = new JArray(0f, 1.2f, 0f), ["yaw"] = 0 },
            ["standing"] = new JObject { ["translation"] = new JArray(0f, 0f, 0f), ["yaw"] = 0 },
            ["time"] = DateTime.Now.ToString("ddd MMM dd HH:mm:ss yyyy", System.Globalization.CultureInfo.InvariantCulture),
            ["universeID"] = universeId
        };
    }

    /// <summary>Écrit en UTF-8 sans BOM (format attendu par SteamVR).</summary>
    private static void WriteUtf8NoBom(string path, string text) =>
        File.WriteAllText(path, text, new UTF8Encoding(false));

    /// <summary>Sauvegarde les fichiers touchés dans un dossier horodaté ; garde les plus récentes.</summary>
    private static string Backup(Paths paths, string backupRoot)
    {
        if (string.IsNullOrEmpty(backupRoot)) return null;
        string dir = Path.Combine(backupRoot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        Directory.CreateDirectory(dir);
        if (File.Exists(paths.UserSettings))
            File.Copy(paths.UserSettings, Path.Combine(dir, "steamvr.vrsettings"), true);
        if (File.Exists(paths.NullDriverDefaults))
            File.Copy(paths.NullDriverDefaults, Path.Combine(dir, "null_driver_default.vrsettings"), true);
        if (File.Exists(paths.SteamVRDefaults))
            File.Copy(paths.SteamVRDefaults, Path.Combine(dir, "steamvr_default.vrsettings"), true);
        if (File.Exists(paths.ChaperoneInfo))
            File.Copy(paths.ChaperoneInfo, Path.Combine(dir, "chaperone_info.vrchap"), true);
        File.WriteAllText(Path.Combine(dir, "LISEZMOI.txt"),
            "Sauvegarde faite par " + MooveoGameHooks.AppName + " avant de modifier la configuration SteamVR.\r\n" +
            "Restauration (SteamVR fermé) :\r\n" +
            $"  copy steamvr.vrsettings             \"{paths.UserSettings}\"\r\n" +
            $"  copy null_driver_default.vrsettings \"{paths.NullDriverDefaults}\"\r\n" +
            $"  copy steamvr_default.vrsettings     \"{paths.SteamVRDefaults}\"\r\n" +
            $"  copy chaperone_info.vrchap          \"{paths.ChaperoneInfo}\"\r\n");

        foreach (string old in Directory.GetDirectories(backupRoot).OrderByDescending(d => d).Skip(KeptBackups))
        {
            try { Directory.Delete(old, true); } catch (Exception) { }
        }
        return dir;
    }

    // ---------------------------------------------------------------------------------------------
    // Séquence complète
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Vérifie et corrige la configuration pour <paramref name="mode"/>. Ferme SteamVR s'il tourne
    /// (<paramref name="allowClose"/>), échoue s'il est relancé pendant l'écriture. Ne le relance pas.
    /// </summary>
    public static Result Ensure(string mode, string vrMonitorHint, string backupRoot, bool allowClose)
    {
        var result = new Result();
        try
        {
            if (!TryLocate(vrMonitorHint, out Paths paths, out string error))
                return Fail(result, error);

            List<Difference> differences = Inspect(paths, mode);
            List<Difference> defaults = InspectSteamVRDefaultsQuietly(paths, mode);
            if (differences.Count == 0 && defaults.Count == 0)
            {
                result.Outcome = Outcome.AlreadyConfigured;
                result.Message = $"Configuration SteamVR déjà en mode {mode}.";
                return result;
            }

            if (differences.Count > 0)
            {
                if (IsRunning())
                {
                    if (!allowClose)
                    {
                        result.Outcome = Outcome.SteamVRRunning;
                        result.Message = "SteamVR tourne avec une configuration qui ne correspond pas au matériel : " +
                                         "il doit être fermé pour la corriger.";
                        result.Fixed = differences;
                        return result;
                    }
                    Debug.Log("[SteamVRConfig] SteamVR tourne avec une configuration inadaptée : fermeture.");
                    if (!Close())
                        return Fail(result, "SteamVR n'a pas pu être fermé : sa configuration n'a pas été modifiée. " +
                                            "Fermez-le à la main puis relancez les vérifications.");
                    result.ClosedSteamVR = true;
                }

                if (IsRunning())
                    return Fail(result, RestartedMessage("avant la correction : rien n'a été modifié"));

                result.BackupDirectory = Backup(paths, backupRoot);
                Apply(paths, mode);

                List<Difference> remaining = Inspect(paths, mode);
                if (remaining.Count > 0)
                    return Fail(result, "Configuration SteamVR réécrite, mais toujours incorrecte : " +
                                        string.Join(" ; ", remaining));

                if (IsRunning())
                    return Fail(result, RestartedMessage("pendant la correction : elle risque d'être écrasée à sa fermeture"));

                result.Fixed.AddRange(differences);
            }

            string defaultsWarning = null;
            List<Difference> defaultsLeft = InspectSteamVRDefaultsQuietly(paths, mode);
            if (defaultsLeft.Count > 0)
            {
                try
                {
                    if (result.BackupDirectory == null) result.BackupDirectory = Backup(paths, backupRoot);
                    ApplySteamVRDefaults(paths, mode);
                    result.Fixed.AddRange(defaultsLeft);
                }
                catch (Exception e)
                {
                    defaultsWarning = $"Défauts de SteamVR non modifiés ({paths.SteamVRDefaults}) : {e.Message}. " +
                                      "Sans conséquence tant que le fichier utilisateur porte les réglages.";
                    Debug.LogWarning($"[SteamVRConfig] {defaultsWarning}");
                }
            }

            result.Outcome = result.Fixed.Count > 0 ? Outcome.Applied : Outcome.AlreadyConfigured;
            result.Message = result.Fixed.Count > 0
                ? $"Configuration SteamVR passée en mode {mode} ({result.Fixed.Count} réglage(s)). " +
                  $"Sauvegarde : {result.BackupDirectory}"
                : $"Configuration SteamVR déjà en mode {mode}.";
            if (defaultsWarning != null) result.Message += " " + defaultsWarning;
            if (result.Fixed.Count > 0)
                Debug.Log($"[SteamVRConfig] {result.Message}\n  " + string.Join("\n  ", result.Fixed));
            return result;
        }
        catch (UnauthorizedAccessException e)
        {
            return Fail(result, "Écriture refusée dans les fichiers de SteamVR (droits insuffisants sur le " +
                                $"dossier Steam) : {e.Message}");
        }
        catch (Exception e)
        {
            return Fail(result, $"Configuration SteamVR impossible à vérifier ou corriger : {e.Message}");
        }
    }

    /// <summary>Comme <see cref="InspectSteamVRDefaults"/>, mais un fichier illisible donne une liste vide.</summary>
    private static List<Difference> InspectSteamVRDefaultsQuietly(Paths paths, string mode)
    {
        try { return InspectSteamVRDefaults(paths, mode); }
        catch (Exception e)
        {
            Debug.LogWarning($"[SteamVRConfig] Défauts de SteamVR illisibles, ignorés : {e.Message}");
            return new List<Difference>();
        }
    }

    /// <summary>Message d'échec quand SteamVR a été relancé par un autre logiciel.</summary>
    private static string RestartedMessage(string when) =>
        $"SteamVR a été relancé {when}. Un autre logiciel le démarre peut-être (PICO Connect…) : " +
        "fermez-le, fermez SteamVR, puis relancez les vérifications.";

    /// <summary>Marque le résultat en échec et le journalise.</summary>
    private static Result Fail(Result result, string message)
    {
        result.Outcome = Outcome.Failed;
        result.Message = message;
        Debug.LogError($"[SteamVRConfig] {message}");
        return result;
    }
}
