using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Mooveo.Editor
{
    /// <summary>
    /// Outil de publication d'un jeu vers la plateforme Mooveo.
    ///
    /// Pipeline :
    ///   1. Declare Game     -> POST /api/admin/games        (création de l'entrée minimale)
    ///   2. Build Windows64  -> BuildPipeline.BuildPlayer    (StandaloneWindows64)
    ///   3. Package & Upload -> ZIP du build, calcul checksum, upload via URLs S3 pre-signées
    ///   4. Finalize         -> POST /api/admin/games/new-version (publie la version)
    /// </summary>
    public class GamePublisher : EditorWindow
    {
        // ----- Prefs keys -----
        private const string PREF_BACKEND_URL = "Mooveo.Publisher.BackendUrl";
        private const string PREF_ADMIN_KEY   = "Mooveo.Publisher.AdminKey";
        private const string PREF_GAME_ID     = "Mooveo.Publisher.GameId";
        private const string PREF_GAME_NAME   = "Mooveo.Publisher.GameName";
        private const string PREF_VERSION     = "Mooveo.Publisher.Version";
        private const string PREF_BUILD_DIR   = "Mooveo.Publisher.BuildDir";
        private const string PREF_LAUNCH_EXE  = "Mooveo.Publisher.LaunchExe";
        private const string PREF_MIN_LAUNCHER= "Mooveo.Publisher.MinLauncher";

        // ----- UI state -----
        private string backendUrl;
        private string adminKey;
        private string gameId;
        private string gameName;
        private string version;
        private string buildDir;
        private string launchExe;
        private string minLauncherVersion;
        private Texture2D coverTexture;
        private string coverPath;

        private bool busy;
        private string statusMessage = "";
        private MessageType statusType = MessageType.None;
        private Vector2 scroll;

        // shared HTTP client
        private static readonly HttpClient http = new HttpClient();

        [MenuItem("Mooveo/Publish Game...", priority = 100)]
        public static void ShowWindow()
        {
            var w = GetWindow<GamePublisher>("Mooveo Publisher");
            w.minSize = new Vector2(480, 520);
        }

        private void OnEnable()
        {
            backendUrl         = EditorPrefs.GetString(PREF_BACKEND_URL, "https://preprod.mooveo.tkorp.com");
            adminKey           = EditorPrefs.GetString(PREF_ADMIN_KEY, "");
            gameId             = EditorPrefs.GetString(PREF_GAME_ID, "");
            gameName           = EditorPrefs.GetString(PREF_GAME_NAME, Application.productName);
            version            = EditorPrefs.GetString(PREF_VERSION, "0.1.0");
            buildDir           = EditorPrefs.GetString(PREF_BUILD_DIR, "");
            launchExe          = EditorPrefs.GetString(PREF_LAUNCH_EXE, "");
            minLauncherVersion = EditorPrefs.GetString(PREF_MIN_LAUNCHER, "1.0.0");
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField("Mooveo Game Publisher", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Pipeline : Declare → Build → Package → Upload → Publish.\n" +
                "Renseigne d'abord la connexion backend, puis suis les étapes dans l'ordre.",
                MessageType.None);
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(busy))
            {
                DrawConnectionSection();
                EditorGUILayout.Space();
                DrawGameInfoSection();
                EditorGUILayout.Space();
                DrawStepsSection();
            }

            EditorGUILayout.Space();
            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, statusType);
            }

            EditorGUILayout.EndScrollView();
        }

        // -------------------------------------------------------------------
        // UI sections
        // -------------------------------------------------------------------

        private void DrawConnectionSection()
        {
            EditorGUILayout.LabelField("Backend", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            backendUrl = EditorGUILayout.TextField("API URL", backendUrl);
            adminKey   = EditorGUILayout.PasswordField("Admin Key (X-Admin-Key)", adminKey);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(PREF_BACKEND_URL, backendUrl);
                EditorPrefs.SetString(PREF_ADMIN_KEY, adminKey);
            }
        }

        private void DrawGameInfoSection()
        {
            EditorGUILayout.LabelField("Game", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            gameId   = EditorGUILayout.TextField(new GUIContent("Game ID (slug)", "a-z, 0-9, tirets uniquement"), gameId);
            gameName = EditorGUILayout.TextField("Display name", gameName);
            version  = EditorGUILayout.TextField("Version", version);
            EditorGUILayout.LabelField("Cover image (optionnelle)");
            coverTexture = (Texture2D)EditorGUILayout.ObjectField(coverTexture, typeof(Texture2D), false, GUILayout.Height(60));
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(PREF_GAME_ID, gameId);
                EditorPrefs.SetString(PREF_GAME_NAME, gameName);
                EditorPrefs.SetString(PREF_VERSION, version);
            }
        }

        private void DrawStepsSection()
        {
            EditorGUILayout.LabelField("Pipeline", EditorStyles.boldLabel);

            // ---- Step 1: Declare ----
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(gameName)))
            {
                if (GUILayout.Button("1. Declare Game"))
                {
                    _ = DeclareGameAsync();
                }
            }

            EditorGUILayout.Space(4);

            // ---- Step 2: Build ----
            if (GUILayout.Button("2. Build Windows x64"))
            {
                BuildPlayerSync();
            }
            buildDir  = EditorGUILayout.TextField("Build output dir", buildDir);
            launchExe = EditorGUILayout.TextField("Launch .exe", launchExe);
            minLauncherVersion = EditorGUILayout.TextField("Min launcher version", minLauncherVersion);

            EditorGUILayout.Space(4);

            // ---- Step 3: Package + Upload + Publish ----
            bool canPublish = !string.IsNullOrEmpty(gameId)
                              && !string.IsNullOrEmpty(version)
                              && !string.IsNullOrEmpty(buildDir)
                              && Directory.Exists(buildDir)
                              && !string.IsNullOrEmpty(launchExe);
            using (new EditorGUI.DisabledScope(!canPublish))
            {
                if (GUILayout.Button("3. Package & Publish"))
                {
                    _ = PackageAndPublishAsync();
                }
            }
        }

        // -------------------------------------------------------------------
        // Step 1 — Declare Game
        // -------------------------------------------------------------------

        private async Task DeclareGameAsync()
        {
            if (!ValidateConnection() || !ValidateSlug()) return;

            busy = true;
            SetStatus("Déclaration du jeu...", MessageType.Info);
            try
            {
                string coverUrl = "";

                // 1. Si on a une cover, l'uploader d'abord pour avoir une URL
                if (coverTexture != null)
                {
                    coverPath = AssetDatabase.GetAssetPath(coverTexture);
                    if (!string.IsNullOrEmpty(coverPath) && File.Exists(coverPath))
                    {
                        SetStatus("Upload de la cover...", MessageType.Info);
                        coverUrl = await UploadAssetAsync("cover", null, coverPath, GuessContentType(coverPath));
                    }
                }

                // 2. POST /api/admin/games
                var body = new Dictionary<string, string>
                {
                    { "game_id", gameId },
                    { "name", gameName },
                };
                if (!string.IsNullOrEmpty(coverUrl)) body["cover_url"] = coverUrl;

                var resp = await PostJsonAsync<Dictionary<string, object>>("/api/admin/games", body);
                if (resp == null)
                {
                    SetStatus("Échec de la déclaration : réponse vide.", MessageType.Error);
                    return;
                }

                SetStatus($"Jeu déclaré : {gameId}" + (coverUrl != "" ? $"\nCover: {coverUrl}" : ""), MessageType.Info);
            }
            catch (Exception e)
            {
                SetStatus($"Erreur : {e.Message}", MessageType.Error);
            }
            finally { busy = false; Repaint(); }
        }

        // -------------------------------------------------------------------
        // Step 2 — Build
        // -------------------------------------------------------------------

        private void BuildPlayerSync()
        {
            if (!ValidateSlug()) return;

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                SetStatus("Aucune scène dans Build Settings. Ajoute au moins une scène.", MessageType.Error);
                return;
            }

            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var outDir = Path.Combine(projectRoot, "Builds", $"{gameId}-{version}");
            Directory.CreateDirectory(outDir);

            var exeName = $"{gameId}.exe";
            var exePath = Path.Combine(outDir, exeName);

            SetStatus("Build en cours...", MessageType.Info);

            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(opts);
            if (report.summary.result == BuildResult.Succeeded)
            {
                buildDir = outDir;
                launchExe = exeName;
                EditorPrefs.SetString(PREF_BUILD_DIR, buildDir);
                EditorPrefs.SetString(PREF_LAUNCH_EXE, launchExe);
                EditorPrefs.SetString(PREF_MIN_LAUNCHER, minLauncherVersion);
                SetStatus($"Build OK : {exePath}\nTaille totale: {FormatBytes((long)report.summary.totalSize)}", MessageType.Info);
            }
            else
            {
                SetStatus($"Build échoué : {report.summary.result}", MessageType.Error);
            }
        }

        // -------------------------------------------------------------------
        // Step 3 — Package, upload, publish
        // -------------------------------------------------------------------

        private async Task PackageAndPublishAsync()
        {
            if (!ValidateConnection() || !ValidateSlug()) return;

            busy = true;
            try
            {
                // 1. Zipper le build
                SetStatus("Création du ZIP...", MessageType.Info);
                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                var artifactsDir = Path.Combine(projectRoot, "Builds", "_artifacts");
                Directory.CreateDirectory(artifactsDir);
                var zipName = $"{gameId}.zip";
                var zipPath = Path.Combine(artifactsDir, zipName);
                if (File.Exists(zipPath)) File.Delete(zipPath);
                ZipFile.CreateFromDirectory(buildDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

                // 2. Calculer checksum + lister fichiers
                SetStatus("Calcul des checksums...", MessageType.Info);
                var zipChecksum = "sha256:" + Sha256OfFile(zipPath);

                var fileList = new List<Dictionary<string, object>>();
                foreach (var f in Directory.GetFiles(buildDir, "*", SearchOption.AllDirectories))
                {
                    var rel = f.Substring(buildDir.Length + 1).Replace('\\', '/');
                    fileList.Add(new Dictionary<string, object>
                    {
                        { "path", rel },
                        { "hash", "sha256:" + Sha256OfFile(f) },
                        { "size", new FileInfo(f).Length },
                    });
                }

                // 3. Upload du zip
                SetStatus("Upload du ZIP...", MessageType.Info);
                var zipUrl = await UploadAssetAsync("zip", version, zipPath, "application/zip");

                // 4. Construire et uploader le manifest.json
                SetStatus("Génération du manifest...", MessageType.Info);
                var manifest = new Dictionary<string, object>
                {
                    { "game_id", gameId },
                    { "version", version },
                    { "published_at", DateTime.UtcNow.ToString("o") },
                    { "download_url", zipUrl },
                    { "checksum", zipChecksum },
                    { "launch_exe", launchExe },
                    { "files", fileList },
                    { "dependencies", new List<object>() },
                    { "min_launcher_version", minLauncherVersion },
                };
                var manifestPath = Path.Combine(artifactsDir, "manifest.json");
                File.WriteAllText(manifestPath, Newtonsoft.Json.JsonConvert.SerializeObject(manifest), new UTF8Encoding(false));

                SetStatus("Upload du manifest...", MessageType.Info);
                var manifestUrl = await UploadAssetAsync("manifest", version, manifestPath, "application/json");

                // 5. Publish
                SetStatus("Finalisation...", MessageType.Info);
                await PostJsonAsync<Dictionary<string, object>>("/api/admin/games/new-version", new Dictionary<string, string>
                {
                    { "game_id", gameId },
                    { "name", gameName },
                    { "version", version },
                    { "manifest_url", manifestUrl },
                });

                SetStatus($"Publié !\nVersion: {version}\nManifest: {manifestUrl}\nDownload: {zipUrl}", MessageType.Info);
            }
            catch (Exception e)
            {
                SetStatus($"Erreur publication : {e.Message}", MessageType.Error);
                Debug.LogException(e);
            }
            finally { busy = false; Repaint(); }
        }

        // -------------------------------------------------------------------
        // Upload helper : demande une URL signée puis fait le PUT
        // -------------------------------------------------------------------

        private async Task<string> UploadAssetAsync(string kind, string assetVersion, string filePath, string contentType)
        {
            var fileName = Path.GetFileName(filePath);
            var body = new Dictionary<string, string>
            {
                { "kind", kind },
                { "filename", fileName },
                { "content_type", contentType },
            };
            if (!string.IsNullOrEmpty(assetVersion)) body["version"] = assetVersion;

            var presigned = await PostJsonAsync<Dictionary<string, object>>($"/api/admin/games/{gameId}/upload-url", body);
            if (presigned == null) throw new Exception("upload-url returned no body");
            if (!presigned.TryGetValue("upload_url", out var u) || !presigned.TryGetValue("public_url", out var p))
                throw new Exception("upload-url missing fields");

            using var stream = File.OpenRead(filePath);
            using var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            var req = new HttpRequestMessage(HttpMethod.Put, u.ToString()) { Content = content };
            var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync();
                throw new Exception($"S3 PUT {fileName} failed: {(int)resp.StatusCode} {err}");
            }
            return p.ToString();
        }

        // -------------------------------------------------------------------
        // HTTP / JSON helpers
        // -------------------------------------------------------------------

        private async Task<T> PostJsonAsync<T>(string path, object body)
        {
            var url = backendUrl.TrimEnd('/') + path;
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(body);
            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            req.Headers.TryAddWithoutValidation("X-Admin-Key", adminKey);

            var resp = await http.SendAsync(req);
            var text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                throw new Exception($"POST {path} failed: {(int)resp.StatusCode} {text}");
            }
            var envelope = Newtonsoft.Json.Linq.JObject.Parse(text);
            var success = envelope.Value<bool?>("success");
            if (success == false)
            {
                var msg = envelope.Value<string>("error") ?? "unknown";
                throw new Exception($"API error on {path} : {msg}");
            }
            var data = envelope["data"];
            if (data == null) return default;
            return data.ToObject<T>();
        }

        // -------------------------------------------------------------------
        // Validation / helpers
        // -------------------------------------------------------------------

        private bool ValidateConnection()
        {
            if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(adminKey))
            {
                SetStatus("Renseigne d'abord l'API URL et l'Admin Key.", MessageType.Error);
                return false;
            }
            return true;
        }

        private bool ValidateSlug()
        {
            if (string.IsNullOrEmpty(gameId) || !Regex.IsMatch(gameId, "^[a-z0-9-]+$"))
            {
                SetStatus("game_id doit être un slug : a-z, 0-9, tirets.", MessageType.Error);
                return false;
            }
            return true;
        }

        private void SetStatus(string msg, MessageType type)
        {
            statusMessage = msg;
            statusType = type;
            Repaint();
        }

        private static string Sha256OfFile(string path)
        {
            using var sha = SHA256.Create();
            using var s = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }

        private static string GuessContentType(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".zip" => "application/zip",
                ".json" => "application/json",
                _ => "application/octet-stream",
            };
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double s = bytes; int i = 0;
            while (s >= 1024 && i < units.Length - 1) { s /= 1024; i++; }
            return $"{s:0.##} {units[i]}";
        }
    }
}
