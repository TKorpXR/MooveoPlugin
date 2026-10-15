using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// Capture les logs Unity : erreurs dans un fichier « erreurs », tous les niveaux dans un fichier « complet »,
/// et les événements de <see cref="SessionSummary"/> dans un fichier « résumé » (dossier Export/Logs).
/// Les trois sont aussi gardés en mémoire pour l'onglet Console.
/// </summary>
[Preserve]
public static class ErrorLogWriter
{
    /// <summary>Une erreur gardée en mémoire ; <see cref="Count"/> grandit quand elle se répète.</summary>
    public readonly struct Entry
    {
        public readonly long Id;
        public readonly DateTime Time;
        public readonly string Level;
        public readonly string Message;
        public readonly string StackTrace;
        public readonly int Count;

        public Entry(long id, DateTime time, string level, string message, string stackTrace, int count)
        {
            Id = id;
            Time = time;
            Level = level;
            Message = message;
            StackTrace = stackTrace;
            Count = count;
        }
    }

    private sealed class Item
    {
        public long Id;
        public DateTime Time;
        public string Level;
        public string Message;
        public string StackTrace;
        public int Count;
    }

    /// <summary>Les dernières entrées en mémoire ; une entrée identique à la précédente est seulement comptée.</summary>
    private sealed class Ring
    {
        public readonly List<Item> Items = new List<Item>();
        private readonly int _max;
        private readonly bool _collapse;
        private long _nextId;
        private string _lastKey;
        private Item _last;

        public Ring(int max, bool collapseRepeats = true)
        {
            _max = max;
            _collapse = collapseRepeats;
        }

        public void Clear()
        {
            Items.Clear();
            _lastKey = null;
            _last = null;
        }

        public void Add(DateTime now, string level, string message, string stackTrace)
        {
            string key = level + "|" + message + "|" + stackTrace;
            if (_collapse && key == _lastKey && _last != null)
            {
                _last.Count++;
                return;
            }

            _lastKey = key;
            _last = new Item
            {
                Id = ++_nextId,
                Time = now,
                Level = level,
                Message = Cap(message, MaxMessageChars),
                StackTrace = Cap(stackTrace, MaxStackChars),
                Count = 1
            };
            Items.Add(_last);
            if (Items.Count > _max) Items.RemoveAt(0);
        }
    }

    /// <summary>Un fichier .log du jour : ajout, rotation, regroupement des répétitions.</summary>
    private sealed class LogFile
    {
        private readonly string _prefix;
        private readonly bool _collapse;
        private readonly string _extension;
        private readonly bool _writeHeader;
        private StreamWriter _writer;
        private string _directory;
        private string _header;
        private DateTime _openDate;
        private int _part;
        private long _bytes;
        private string _lastKey;
        private int _repeatCount;
        private DateTime _lastRepeatFlush;

        public string Path { get; private set; }
        public bool IsOpen => _writer != null;

        public LogFile(string prefix, bool collapseRepeats = true, string extension = ".log", bool writeHeader = true)
        {
            _prefix = prefix;
            _collapse = collapseRepeats;
            _extension = extension;
            _writeHeader = writeHeader;
        }

        /// <summary>Ouvre le fichier du jour ; renvoie faux (fichier abandonné) en cas d'échec.</summary>
        public bool Open(DateTime now, string directory, string header)
        {
            _directory = directory;
            _header = header;
            _part = 0;
            _lastKey = null;
            _repeatCount = 0;
            try
            {
                OpenFile(now);
                return true;
            }
            catch
            {
                Drop();
                return false;
            }
        }

        /// <summary>Écrit une entrée horodatée, avec le contexte s'il y en a ; une entrée identique à la précédente est seulement comptée.</summary>
        public void Write(DateTime now, string level, string message, string stackTrace, string context = null)
        {
            if (_writer == null) return;
            try
            {
                string key = level + "|" + message + "|" + stackTrace;
                if (_collapse && key == _lastKey)
                {
                    _repeatCount++;
                    if ((now - _lastRepeatFlush).TotalSeconds >= RepeatSummarySeconds) FlushRepeat(now);
                    return;
                }

                FlushRepeat(now);
                _lastKey = key;
                _lastRepeatFlush = now;

                if (now.Date != _openDate || _bytes >= MaxFileBytes) Roll(now);

                var sb = new StringBuilder();
                sb.Append('[').Append(now.ToString("HH:mm:ss.fff")).Append("] ")
                  .Append(level.PadRight(9)).Append(' ')
                  .Append(string.IsNullOrEmpty(context) ? "" : "{" + context + "} ")
                  .Append(Indent(message, 4)).Append("\r\n");
                if (!string.IsNullOrWhiteSpace(stackTrace))
                    sb.Append("    ").Append(Indent(stackTrace.TrimEnd(), 4)).Append("\r\n");
                WriteRaw(sb.ToString());
            }
            catch
            {
                Drop();
            }
        }

        /// <summary>Écrit une ligne telle quelle (JSON Lines), avec la même rotation.</summary>
        public void WriteLine(DateTime now, string line)
        {
            if (_writer == null) return;
            try
            {
                if (now.Date != _openDate || _bytes >= MaxFileBytes) Roll(now);
                WriteRaw(line + "\r\n");
            }
            catch
            {
                Drop();
            }
        }

        /// <summary>Écrit la ligne de fin de session si demandé, puis ferme le fichier.</summary>
        public void Close(bool writeEnd)
        {
            if (_writer == null) return;
            try
            {
                FlushRepeat(DateTime.Now);
                if (writeEnd) WriteRaw($"===== Fin de session {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====\r\n");
                _writer.Dispose();
            }
            catch { }
            _writer = null;
        }

        /// <summary>Écrit le nombre de répétitions de l'entrée en cours.</summary>
        private void FlushRepeat(DateTime now)
        {
            if (_repeatCount > 0 && _writer != null)
                WriteRaw($"    (répétée {_repeatCount} fois, jusqu'à {now:HH:mm:ss})\r\n");
            _repeatCount = 0;
            _lastRepeatFlush = now;
        }

        private void Roll(DateTime now)
        {
            _part = now.Date != _openDate ? 0 : _part + 1;
            _writer.Dispose();
            _writer = null;
            OpenFile(now);
        }

        /// <summary>Ouvre en ajout, lecture partagée, et écrit l'en-tête de session.</summary>
        private void OpenFile(DateTime now)
        {
            _openDate = now.Date;
            string name = _prefix + now.ToString("yyyy-MM-dd") + (_part > 0 ? "_" + (_part + 1) : "") + _extension;
            Path = System.IO.Path.Combine(_directory, name);

            var stream = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _bytes = stream.Length;
            _writer = new StreamWriter(stream, Utf8Bom) { AutoFlush = true };

            if (!_writeHeader) return;
            string header = _header.Replace("{time}", now.ToString("yyyy-MM-dd HH:mm:ss"));
            WriteRaw((_bytes > 0 ? "\r\n" : "") + header + "\r\n");
        }

        private void WriteRaw(string text)
        {
            _writer.Write(text);
            _bytes += Utf8Bom.GetByteCount(text);
        }

        private void Drop()
        {
            try { _writer?.Dispose(); } catch { }
            _writer = null;
            Path = null;
        }
    }

    /// <summary>Clé EditorPrefs activant l'écriture des fichiers en éditeur.</summary>
    public const string EditorEnabledKey = "GraffWall.ErrorLogInEditor";

    private const string ErrorsPrefix = "GraffWall_erreurs_";
    private const string FullPrefix = "GraffWall_complet_";
    private const string SummaryPrefix = "GraffWall_resume_";
    private const int RetentionDays = 30;
    private const long MaxFileBytes = 10L * 1024 * 1024;
    private const double RepeatSummarySeconds = 60.0;
    private const int MaxErrorEntries = 300;
    private const int MaxAllEntries = 500;
    private const int MaxSummaryEntries = 500;
    private const int MaxMessageChars = 2000;
    private const int MaxStackChars = 6000;

    private static readonly object Gate = new object();
    private static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true);
    private static readonly Ring ErrorsRing = new Ring(MaxErrorEntries);
    private static readonly Ring AllRing = new Ring(MaxAllEntries);
    private static readonly LogFile ErrorsFile = new LogFile(ErrorsPrefix);
    private static readonly LogFile FullFile = new LogFile(FullPrefix);
    private static readonly Ring SummaryRing = new Ring(MaxSummaryEntries, collapseRepeats: false);
    private static readonly LogFile SummaryFile = new LogFile(SummaryPrefix, collapseRepeats: false);
    private static readonly LogFile SummaryJsonFile =
        new LogFile(SummaryPrefix, collapseRepeats: false, extension: ".jsonl", writeHeader: false);

    private static string _directory;
    private static bool _started;
    private static int _version;
    private static volatile string _context = "";
    private static volatile string _gameMode = "";
    private static string _sessionId = "";
    private static string _machine = "";

    [ThreadStatic] private static bool _writing;

    /// <summary>Fichier « erreurs » en cours d'écriture, ou null.</summary>
    public static string CurrentFilePath => ErrorsFile.Path;

    /// <summary>Fichier « complet » en cours d'écriture, ou null.</summary>
    public static string CurrentFullFilePath => FullFile.Path;

    /// <summary>Dossier des journaux.</summary>
    public static string LogsDirectory => _directory;

    /// <summary>Vrai si au moins un fichier est en cours d'écriture.</summary>
    public static bool IsWritingFile => ErrorsFile.IsOpen || FullFile.IsOpen || SummaryFile.IsOpen || SummaryJsonFile.IsOpen;

    /// <summary>Change à chaque nouvelle erreur ou répétition ; sert à savoir si l'affichage est à jour.</summary>
    public static int Version => Volatile.Read(ref _version);

    /// <summary>Ce que la Console peut afficher.</summary>
    public enum LogView { Errors, All, Summary }

    /// <summary>Copie les entrées en mémoire de la vue demandée, de la plus ancienne à la plus récente.</summary>
    public static void Read(List<Entry> into, LogView view)
    {
        Ring ring = view == LogView.Errors ? ErrorsRing : view == LogView.All ? AllRing : SummaryRing;
        into.Clear();
        lock (Gate)
        {
            foreach (Item item in ring.Items)
                into.Add(new Entry(item.Id, item.Time, item.Level, item.Message, item.StackTrace, item.Count));
        }
    }

    /// <summary>Démarre la capture et, hors éditeur (ou si activé), ouvre les fichiers du jour.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Start()
    {
        Stop(writeEnd: false);

        bool toFile = true;
#if UNITY_EDITOR
        toFile = UnityEditor.EditorPrefs.GetBool(EditorEnabledKey, false);
#endif
        _sessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
        _machine = Environment.MachineName;

        lock (Gate)
        {
            ErrorsRing.Clear();
            AllRing.Clear();
            SummaryRing.Clear();
        }

        if (toFile)
        {
            try
            {
                _directory = ResolveDirectory();
                Directory.CreateDirectory(_directory);
                PurgeOldFiles(_directory);

                string header = BuildSessionHeader();
                DateTime now = DateTime.Now;
                ErrorsFile.Open(now, _directory, header);
                FullFile.Open(now, _directory, header);
                SummaryFile.Open(now, _directory, header);
                SummaryJsonFile.Open(now, _directory, header);
            }
            catch { }
        }

        Application.logMessageReceivedThreaded += OnLogMessage;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        Application.quitting += OnQuitting;
        _started = true;
    }

    /// <summary>Se désabonne et ferme les fichiers.</summary>
    private static void Stop(bool writeEnd)
    {
        if (_started)
        {
            Application.logMessageReceivedThreaded -= OnLogMessage;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
            Application.quitting -= OnQuitting;
            _started = false;
        }

        lock (Gate)
        {
            ErrorsFile.Close(writeEnd);
            FullFile.Close(writeEnd);
            SummaryFile.Close(writeEnd);
            SummaryJsonFile.Close(writeEnd);
        }
    }

    private static void OnQuitting() => Stop(writeEnd: true);

    /// <summary>Reçoit tous les logs Unity, quel que soit le fil.</summary>
    private static void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        switch (type)
        {
            case LogType.Error: Append("ERROR", condition, stackTrace, true); break;
            case LogType.Exception: Append("EXCEPTION", condition, stackTrace, true); break;
            case LogType.Assert: Append("ASSERT", condition, stackTrace, true); break;
            case LogType.Warning: Append("WARNING", condition, null, false); break;
            default: Append("LOG", condition, null, false); break;
        }
    }

    /// <summary>Reçoit les exceptions non gérées.</summary>
    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        Append("FATAL", ex != null ? ex.GetType().Name + ": " + ex.Message : Convert.ToString(e.ExceptionObject),
               ex?.StackTrace, true);
    }

    /// <summary>Reçoit les tâches en échec dont le résultat n'a jamais été lu.</summary>
    private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
    {
        Exception ex = e.Exception != null ? e.Exception.GetBaseException() : null;
        Append("TASK", ex != null ? ex.GetType().Name + ": " + ex.Message : "Tâche en échec",
               ex?.StackTrace, true);
    }

    /// <summary>Envoie l'entrée au fichier complet ; si c'est une erreur, aussi au fichier erreurs et à la mémoire.</summary>
    private static void Append(string level, string message, string stackTrace, bool isError)
    {
        if (_writing) return;
        _writing = true;
        try
        {
            lock (Gate)
            {
                DateTime now = DateTime.Now;
                string context = isError ? _context : null;
                AllRing.Add(now, level, message, stackTrace);
                if (isError) ErrorsRing.Add(now, level, message, stackTrace);
                Interlocked.Increment(ref _version);

                FullFile.Write(now, level, message, stackTrace, context);
                if (isError) ErrorsFile.Write(now, level, message, stackTrace, context);
            }
        }
        catch { }
        finally { _writing = false; }
    }

    /// <summary>
    /// Ajoute un événement au résumé de session : fichier texte, fichier JSON Lines (avec les paires
    /// nom/valeur de <paramref name="data"/>, pour l'analyse) et vue Résumé de la Console.
    /// <paramref name="category"/> est un mot court en majuscules : SESSION, MODE, CLIC, OUTIL, NOTIF,
    /// IMPRESSION, SAUVEGARDE.
    /// </summary>
    public static void Summary(string category, string text, params (string key, object value)[] data)
    {
        try
        {
            lock (Gate)
            {
                DateTime now = DateTime.Now;
                SummaryRing.Add(now, category, text, null);
                Interlocked.Increment(ref _version);
                SummaryFile.Write(now, category, text, null);
                if (SummaryJsonFile.IsOpen) SummaryJsonFile.WriteLine(now, BuildJson(now, category, text, data));
            }
        }
        catch { }
    }

    /// <summary>Mode de jeu actif, ajouté à chaque ligne du fichier JSON.</summary>
    public static void SetGameMode(string mode) => _gameMode = mode ?? "";

    /// <summary>Construit une ligne JSON : date complète, session, poste, mode de jeu, catégorie, texte et données.</summary>
    private static string BuildJson(DateTime now, string category, string text, (string key, object value)[] data)
    {
        var sb = new StringBuilder(192);
        sb.Append("{\"t\":");
        AppendQuoted(sb, now.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture));
        AppendField(sb, "sid", _sessionId);
        AppendField(sb, "machine", _machine);
        AppendField(sb, "mode", _gameMode);
        AppendField(sb, "cat", category);
        AppendField(sb, "text", text);

        if (data != null && data.Length > 0)
        {
            sb.Append(",\"data\":{");
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0) sb.Append(',');
                AppendQuoted(sb, data[i].key);
                sb.Append(':');
                AppendValue(sb, data[i].value);
            }
            sb.Append('}');
        }
        return sb.Append('}').ToString();
    }

    private static void AppendField(StringBuilder sb, string key, string value)
    {
        sb.Append(',');
        AppendQuoted(sb, key);
        sb.Append(':');
        AppendQuoted(sb, value);
    }

    /// <summary>Écrit une valeur JSON : booléen et nombres tels quels, tout le reste en chaîne.</summary>
    private static void AppendValue(StringBuilder sb, object value)
    {
        switch (value)
        {
            case null: sb.Append("null"); break;
            case bool b: sb.Append(b ? "true" : "false"); break;
            case int _:
            case long _:
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
            case float f when !float.IsNaN(f) && !float.IsInfinity(f):
                sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                break;
            case double d when !double.IsNaN(d) && !double.IsInfinity(d):
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                break;
            default:
                AppendQuoted(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    /// <summary>Écrit une chaîne JSON échappée (guillemets, barre oblique inverse, caractères de contrôle).</summary>
    private static void AppendQuoted(StringBuilder sb, string text)
    {
        sb.Append('"');
        foreach (char c in text ?? "")
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    /// <summary>Contexte ajouté aux lignes d'erreur (mode de jeu, outils des joueurs) ; vide pour n'en mettre aucun.</summary>
    public static void SetContext(string context) => _context = context ?? "";

    /// <summary>Décale les lignes suivant la première.</summary>
    private static string Indent(string text, int pad)
    {
        string prefix = new string(' ', pad);
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append("\r\n").Append(prefix);
            sb.Append(lines[i].TrimEnd());
        }
        return sb.ToString();
    }

    private static string Cap(string text, int max) =>
        string.IsNullOrEmpty(text) || text.Length <= max ? text : text.Substring(0, max) + "…";

    /// <summary>Dossier de sortie : <c>LogsPath</c> s'il est renseigné, sinon <c>Export/Logs</c>.</summary>
    private static string ResolveDirectory()
    {
        string configured = GlobalSettingsFile.Read("LogsPath");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(GlobalSettings.Core.GlobalSettings.AppRoot, "Export", "Logs")
            : configured;
    }

    /// <summary>Construit l'en-tête de session : version, système, matériel.</summary>
    private static string BuildSessionHeader()
    {
        string hardware = GlobalSettingsFile.Read("TrackingHardware");
        return "===== Session {time} — GraffWall " + Application.version + " — " + SystemInfo.operatingSystem +
               " — Matériel : " + (string.IsNullOrEmpty(hardware) ? "non mémorisé" : hardware) + " =====";
    }

    /// <summary>Supprime les journaux de plus de 30 jours.</summary>
    private static void PurgeOldFiles(string directory)
    {
        DateTime limit = DateTime.Now.AddDays(-RetentionDays);
        foreach (string pattern in new[] { ErrorsPrefix + "*.log", FullPrefix + "*.log", SummaryPrefix + "*.log", SummaryPrefix + "*.jsonl" })
        {
            foreach (string file in Directory.GetFiles(directory, pattern))
            {
                try { if (File.GetLastWriteTime(file) < limit) File.Delete(file); }
                catch { }
            }
        }
    }
}
