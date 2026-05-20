using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
    /// Pipeline guidé :
    ///   0. Connexion backend         (API URL + Admin Key)
    ///   1. Identité du jeu           (slug + nom + cover)
    ///   2. Déclaration                -> POST /api/admin/games
    ///   3. Build Windows x64
    ///   4. Package + Upload + Publish -> POST /api/admin/games/new-version
    /// </summary>
    public class GamePublisher : EditorWindow
    {
        // ----- Prefs keys -----
        private const string PREF_BACKEND_URL  = "Mooveo.Publisher.BackendUrl";
        private const string PREF_ADMIN_KEY    = "Mooveo.Publisher.AdminKey";
        private const string PREF_GAME_ID      = "Mooveo.Publisher.GameId";
        private const string PREF_GAME_NAME    = "Mooveo.Publisher.GameName";
        private const string PREF_VERSION      = "Mooveo.Publisher.Version";
        private const string PREF_BUILD_DIR    = "Mooveo.Publisher.BuildDir";
        private const string PREF_LAUNCH_EXE   = "Mooveo.Publisher.LaunchExe";
        private const string PREF_COVER_PATH   = "Mooveo.Publisher.CoverPath";

        // Valeur écrite dans le manifest. Non exposée dans l'UI : à incrémenter
        // ici si un futur build dépend d'une nouvelle feature du launcher.
        private const string DEFAULT_MIN_LAUNCHER_VERSION = "1.0.0";

        // ----- UI state -----
        private string backendUrl;
        private string adminKey;
        private string gameId;
        private string gameName;
        private string version;
        private string buildDir;
        private string launchExe;

        // Cover (depuis l'explorateur Windows, pas un asset projet)
        private string coverPath;
        private Texture2D coverPreview;

        // Remote state du jeu, rafraîchi par CheckGameStatus
        private enum RemoteState { Unknown, NotDeclared, Declared, Published }
        private RemoteState remoteState = RemoteState.Unknown;
        private string remoteCurrentVersion = "";
        private string remoteCoverUrl = "";
        private string remoteName = "";
        private string lastCheckedSlug = "";
        // Marqué true quand l'utilisateur choisit une nouvelle cover locale
        // après une vérif (donc à pousser même si une cover_url existe déjà côté backend).
        private bool coverDirty;

        private bool busy;
        private string statusMessage = "";
        private MessageType statusType = MessageType.None;
        private Vector2 scroll;

        private static readonly HttpClient http = new HttpClient();

        [MenuItem("Mooveo/Publish Game...", priority = 100)]
        public static void ShowWindow()
        {
            var w = GetWindow<GamePublisher>("Mooveo Publisher");
            w.minSize = new Vector2(520, 620);
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
            coverPath          = EditorPrefs.GetString(PREF_COVER_PATH, "");
            LoadCoverPreview();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField("Mooveo Game Publisher", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Suis les étapes dans l'ordre. Chaque section se débloque quand la précédente est OK.",
                MessageType.None);
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(busy))
            {
                DrawStep0_Connection();
                EditorGUILayout.Space();
                DrawStep1_GameInfo();
                EditorGUILayout.Space();
                DrawStep2_Declare();
                EditorGUILayout.Space();
                DrawStep3_Build();
                EditorGUILayout.Space();
                DrawStep4_Publish();
            }

            EditorGUILayout.Space();
            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, statusType);
            }

            EditorGUILayout.EndScrollView();
        }

        // -------------------------------------------------------------------
        // Step 0 — Connexion
        // -------------------------------------------------------------------
        private void DrawStep0_Connection()
        {
            DrawStepHeader("0", "Connexion backend", ConnectionOk());

            EditorGUI.BeginChangeCheck();
            backendUrl = EditorGUILayout.TextField("API URL", backendUrl);
            adminKey   = EditorGUILayout.PasswordField("Admin Key (X-Admin-Key)", adminKey);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(PREF_BACKEND_URL, backendUrl);
                EditorPrefs.SetString(PREF_ADMIN_KEY, adminKey);
            }

            if (!ConnectionOk())
                EditorGUILayout.HelpBox("Renseigne l'URL du backend et la clé Admin pour continuer.", MessageType.Info);
        }

        // -------------------------------------------------------------------
        // Step 1 — Identité du jeu
        // -------------------------------------------------------------------
        private void DrawStep1_GameInfo()
        {
            using (new EditorGUI.DisabledScope(!ConnectionOk()))
            {
                DrawStepHeader("1", "Identité du jeu", GameInfoOk());

                EditorGUI.BeginChangeCheck();
                gameId   = EditorGUILayout.TextField(new GUIContent("Game ID (slug)", "a-z, 0-9, tirets uniquement"), gameId);
                gameName = EditorGUILayout.TextField("Nom affiché", gameName);
                if (EditorGUI.EndChangeCheck())
                {
                    EditorPrefs.SetString(PREF_GAME_ID, gameId);
                    EditorPrefs.SetString(PREF_GAME_NAME, gameName);
                    // Slug a changé → on invalide l'état remote
                    if (gameId != lastCheckedSlug) remoteState = RemoteState.Unknown;
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Cover image (optionnelle)", EditorStyles.miniBoldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    // Preview
                    var previewRect = GUILayoutUtility.GetRect(96, 96, GUILayout.Width(96), GUILayout.Height(96));
                    if (coverPreview != null)
                        EditorGUI.DrawPreviewTexture(previewRect, coverPreview, null, ScaleMode.ScaleToFit);
                    else
                        EditorGUI.DrawRect(previewRect, new Color(0.15f, 0.15f, 0.15f));

                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField(
                            string.IsNullOrEmpty(coverPath) ? "(aucune)" : coverPath,
                            EditorStyles.wordWrappedMiniLabel);

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("Choisir une image…", GUILayout.Height(22)))
                            {
                                PickCoverFromExplorer();
                            }
                            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(coverPath)))
                            {
                                if (GUILayout.Button("Retirer", GUILayout.Width(70), GUILayout.Height(22)))
                                {
                                    coverPath = "";
                                    coverPreview = null;
                                    coverDirty = false;
                                    EditorPrefs.SetString(PREF_COVER_PATH, "");
                                }
                            }
                        }
                        EditorGUILayout.LabelField("PNG / JPG / WEBP — depuis ton disque", EditorStyles.miniLabel);
                    }
                }
            }
        }

        private void PickCoverFromExplorer()
        {
            // OpenFilePanelWithFilters affiche un vrai explorateur Windows
            var filters = new[] { "Images", "png,jpg,jpeg,webp", "All files", "*" };
            var picked = EditorUtility.OpenFilePanelWithFilters(
                "Choisir la cover du jeu",
                string.IsNullOrEmpty(coverPath) ? "" : Path.GetDirectoryName(coverPath),
                filters);

            if (string.IsNullOrEmpty(picked)) return;
            if (!File.Exists(picked))
            {
                SetStatus("Fichier introuvable.", MessageType.Error);
                return;
            }

            coverPath = picked;
            EditorPrefs.SetString(PREF_COVER_PATH, coverPath);
            coverDirty = true;
            LoadCoverPreview();
        }

        private void LoadCoverPreview()
        {
            coverPreview = null;
            if (string.IsNullOrEmpty(coverPath) || !File.Exists(coverPath)) return;
            try
            {
                var bytes = File.ReadAllBytes(coverPath);
                var tex = new Texture2D(2, 2);
                if (tex.LoadImage(bytes)) coverPreview = tex;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Mooveo] Cover preview load failed: {e.Message}");
            }
        }

        // -------------------------------------------------------------------
        // Step 2 — Déclaration / vérification
        // -------------------------------------------------------------------
        private void DrawStep2_Declare()
        {
            using (new EditorGUI.DisabledScope(!ConnectionOk() || !GameInfoOk()))
            {
                DrawStepHeader("2", "Déclarer le jeu", remoteState == RemoteState.Declared || remoteState == RemoteState.Published);

                // Bandeau d'état remote
                switch (remoteState)
                {
                    case RemoteState.Unknown:
                        EditorGUILayout.HelpBox(
                            "État inconnu. Clique « Vérifier » pour savoir si le slug est déjà déclaré sur le backend.",
                            MessageType.Info);
                        break;
                    case RemoteState.NotDeclared:
                        EditorGUILayout.HelpBox(
                            $"« {gameId} » n'est pas encore déclaré. Clique « Déclarer » pour créer l'entrée.",
                            MessageType.Warning);
                        break;
                    case RemoteState.Declared:
                        EditorGUILayout.HelpBox(
                            $"« {gameId} » est déclaré mais aucune version n'a encore été publiée.",
                            MessageType.Info);
                        break;
                    case RemoteState.Published:
                        EditorGUILayout.HelpBox(
                            $"« {gameId} » est publié (version courante : {remoteCurrentVersion}). Tu vas pousser la version {version}.",
                            MessageType.Info);
                        break;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Vérifier l'état", GUILayout.Height(24)))
                    {
                        _ = CheckGameStatusAsync();
                    }

                    using (new EditorGUI.DisabledScope(remoteState == RemoteState.Declared || remoteState == RemoteState.Published))
                    {
                        if (GUILayout.Button(
                                remoteState == RemoteState.NotDeclared ? "Déclarer maintenant" : "Déclarer",
                                GUILayout.Height(24)))
                        {
                            _ = DeclareGameAsync();
                        }
                    }
                }

                // Mise à jour d'identité (sans nouvelle version)
                if (remoteState == RemoteState.Declared || remoteState == RemoteState.Published)
                {
                    bool nameChanged = gameName != remoteName;
                    bool coverChanged = coverDirty && !string.IsNullOrEmpty(coverPath);

                    if (nameChanged || coverChanged)
                    {
                        var diffs = new List<string>();
                        if (nameChanged)  diffs.Add($"nom : « {remoteName} » → « {gameName} »");
                        if (coverChanged) diffs.Add("nouvelle cover");
                        EditorGUILayout.HelpBox(
                            "Modifications détectées sans nouvelle version :\n• " + string.Join("\n• ", diffs),
                            MessageType.Warning);

                        if (GUILayout.Button("Mettre à jour l'identité (sans publier)", GUILayout.Height(24)))
                        {
                            _ = UpdateIdentityAsync(nameChanged, coverChanged);
                        }
                    }
                }
            }
        }

        private async Task UpdateIdentityAsync(bool nameChanged, bool coverChanged)
        {
            if (!ValidateConnection() || !ValidateSlug()) return;

            busy = true;
            SetStatus("Mise à jour de l'identité…", MessageType.Info);
            try
            {
                var body = new Dictionary<string, string>();
                if (nameChanged) body["name"] = gameName;

                if (coverChanged)
                {
                    SetStatus("Upload de la nouvelle cover…", MessageType.Info);
                    var coverUrl = await UploadAssetAsync("cover", null, coverPath, GuessContentType(coverPath));
                    body["cover_url"] = coverUrl;
                    remoteCoverUrl = coverUrl;
                }

                if (body.Count == 0)
                {
                    SetStatus("Aucun changement à pousser.", MessageType.Info);
                    return;
                }

                await PatchJsonAsync<Dictionary<string, object>>($"/api/admin/games/{gameId}", body);

                if (nameChanged) remoteName = gameName;
                coverDirty = false;
                SetStatus("Identité mise à jour.", MessageType.Info);
            }
            catch (Exception e)
            {
                SetStatus($"Erreur : {e.Message}", MessageType.Error);
            }
            finally { busy = false; Repaint(); }
        }

        // -------------------------------------------------------------------
        // Step 3 — Build
        // -------------------------------------------------------------------
        private void DrawStep3_Build()
        {
            bool canBuild = ConnectionOk() && GameInfoOk()
                            && (remoteState == RemoteState.Declared || remoteState == RemoteState.Published);

            using (new EditorGUI.DisabledScope(!canBuild))
            {
                DrawStepHeader("3", "Build Windows x64", BuildOk());

                if (!canBuild)
                {
                    EditorGUILayout.HelpBox("Déclare d'abord le jeu (étape 2) avant de builder.", MessageType.Info);
                }

                EditorGUI.BeginChangeCheck();
                version = EditorGUILayout.TextField(new GUIContent("Version à builder", "Ex: 0.1.0 — détermine le dossier de build et la version publiée"), version);
                if (EditorGUI.EndChangeCheck())
                {
                    EditorPrefs.SetString(PREF_VERSION, version);
                }

                if (remoteState == RemoteState.Published && version == remoteCurrentVersion)
                {
                    EditorGUILayout.HelpBox(
                        $"La version « {version} » est déjà publiée. Incrémente-la avant de re-builder.",
                        MessageType.Warning);
                }

                if (GUILayout.Button("Lancer le build", GUILayout.Height(26)))
                {
                    BuildPlayerSync();
                }

                EditorGUI.BeginChangeCheck();
                buildDir  = EditorGUILayout.TextField("Dossier du build", buildDir);
                launchExe = EditorGUILayout.TextField("Exécutable de lancement", launchExe);
                if (EditorGUI.EndChangeCheck())
                {
                    EditorPrefs.SetString(PREF_BUILD_DIR, buildDir);
                    EditorPrefs.SetString(PREF_LAUNCH_EXE, launchExe);
                }
            }
        }

        // -------------------------------------------------------------------
        // Step 4 — Package & Publish
        // -------------------------------------------------------------------
        private void DrawStep4_Publish()
        {
            bool canPublish = ConnectionOk() && GameInfoOk()
                              && (remoteState == RemoteState.Declared || remoteState == RemoteState.Published)
                              && BuildOk();

            using (new EditorGUI.DisabledScope(!canPublish))
            {
                DrawStepHeader("4", "Packager & publier", false);

                if (!canPublish)
                {
                    EditorGUILayout.HelpBox("Termine d'abord les étapes 1 → 3.", MessageType.Info);
                }

                if (GUILayout.Button("Packager, uploader & publier", GUILayout.Height(28)))
                {
                    _ = PackageAndPublishAsync();
                }
            }
        }

        // -------------------------------------------------------------------
        // Step header avec puce d'état
        // -------------------------------------------------------------------
        private void DrawStepHeader(string num, string title, bool done)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var bullet = done ? "✔" : "•";
                var color = done ? new Color(0.4f, 0.8f, 0.4f) : new Color(0.8f, 0.7f, 0.3f);
                var prev = GUI.color;
                GUI.color = color;
                EditorGUILayout.LabelField($"{bullet}  Étape {num} — {title}", EditorStyles.boldLabel);
                GUI.color = prev;
            }
        }

        // -------------------------------------------------------------------
        // Step 2 helpers — Check status + Declare
        // -------------------------------------------------------------------
        private async Task CheckGameStatusAsync()
        {
            if (!ValidateConnection() || !ValidateSlug()) return;

            busy = true;
            SetStatus($"Vérification de l'état de « {gameId} »…", MessageType.Info);
            try
            {
                var url = backendUrl.TrimEnd('/') + $"/api/admin/games/{gameId}";
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("X-Admin-Key", adminKey);
                var resp = await http.SendAsync(req);
                var text = await resp.Content.ReadAsStringAsync();

                lastCheckedSlug = gameId;

                if ((int)resp.StatusCode == 404)
                {
                    remoteState = RemoteState.NotDeclared;
                    remoteCurrentVersion = "";
                    remoteCoverUrl = "";
                    SetStatus($"Jeu « {gameId} » non déclaré sur le backend.", MessageType.Warning);
                    return;
                }
                if (!resp.IsSuccessStatusCode)
                {
                    SetStatus($"Erreur backend ({(int)resp.StatusCode}) : {text}", MessageType.Error);
                    remoteState = RemoteState.Unknown;
                    return;
                }

                var env = Newtonsoft.Json.Linq.JObject.Parse(text);
                var data = env["data"];
                remoteCurrentVersion = data?.Value<string>("current_version") ?? "";
                remoteCoverUrl       = data?.Value<string>("cover_url") ?? "";
                remoteName           = data?.Value<string>("name") ?? "";
                remoteState = string.IsNullOrEmpty(remoteCurrentVersion) ? RemoteState.Declared : RemoteState.Published;
                coverDirty = false;

                SetStatus(
                    remoteState == RemoteState.Published
                        ? $"Jeu trouvé. Version courante : {remoteCurrentVersion}."
                        : "Jeu déclaré (aucune version publiée).",
                    MessageType.Info);
            }
            catch (Exception e)
            {
                SetStatus($"Erreur réseau : {e.Message}", MessageType.Error);
                remoteState = RemoteState.Unknown;
            }
            finally { busy = false; Repaint(); }
        }

        private async Task DeclareGameAsync()
        {
            if (!ValidateConnection() || !ValidateSlug()) return;

            busy = true;
            SetStatus("Déclaration du jeu…", MessageType.Info);
            try
            {
                string coverUrl = "";

                if (!string.IsNullOrEmpty(coverPath) && File.Exists(coverPath))
                {
                    SetStatus("Upload de la cover…", MessageType.Info);
                    coverUrl = await UploadAssetAsync("cover", null, coverPath, GuessContentType(coverPath));
                }

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

                remoteState = RemoteState.Declared;
                lastCheckedSlug = gameId;
                remoteName = gameName;
                if (!string.IsNullOrEmpty(coverUrl)) remoteCoverUrl = coverUrl;
                coverDirty = false;

                SetStatus($"Jeu déclaré : {gameId}" + (coverUrl != "" ? $"\nCover: {coverUrl}" : ""), MessageType.Info);
            }
            catch (Exception e)
            {
                SetStatus($"Erreur : {e.Message}", MessageType.Error);
            }
            finally { busy = false; Repaint(); }
        }

        // -------------------------------------------------------------------
        // Step 3 — Build
        // -------------------------------------------------------------------
        private void BuildPlayerSync()
        {
            if (!ValidateSlug()) return;

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                SetStatus("Aucune scène activée dans Build Settings. Ajoutes-en au moins une.", MessageType.Error);
                return;
            }

            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var outDir = Path.Combine(projectRoot, "Builds", $"{gameId}-{version}");
            Directory.CreateDirectory(outDir);

            var exeName = $"{gameId}.exe";
            var exePath = Path.Combine(outDir, exeName);

            SetStatus("Build en cours…", MessageType.Info);

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
                SetStatus($"Build OK : {exePath}\nTaille totale : {FormatBytes((long)report.summary.totalSize)}", MessageType.Info);
            }
            else
            {
                SetStatus($"Build échoué : {report.summary.result}", MessageType.Error);
            }
        }

        // -------------------------------------------------------------------
        // Step 4 — Package, upload, publish
        // -------------------------------------------------------------------
        private async Task PackageAndPublishAsync()
        {
            if (!ValidateConnection() || !ValidateSlug()) return;

            busy = true;
            try
            {
                SetStatus("Création du ZIP…", MessageType.Info);
                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                var artifactsDir = Path.Combine(projectRoot, "Builds", "_artifacts");
                Directory.CreateDirectory(artifactsDir);
                var zipName = $"{gameId}.zip";
                var zipPath = Path.Combine(artifactsDir, zipName);
                if (File.Exists(zipPath)) File.Delete(zipPath);
                ZipFile.CreateFromDirectory(buildDir, zipPath, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);

                SetStatus("Calcul des checksums…", MessageType.Info);
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

                SetStatus("Upload du ZIP…", MessageType.Info);
                var zipUrl = await UploadAssetAsync("zip", version, zipPath, "application/zip");

                // Cover (si choisie et pas encore uploadée pour cette session)
                string coverUrl = remoteCoverUrl;
                if (string.IsNullOrEmpty(coverUrl) && !string.IsNullOrEmpty(coverPath) && File.Exists(coverPath))
                {
                    SetStatus("Upload de la cover…", MessageType.Info);
                    coverUrl = await UploadAssetAsync("cover", null, coverPath, GuessContentType(coverPath));
                    remoteCoverUrl = coverUrl;
                }

                SetStatus("Génération du manifest…", MessageType.Info);
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
                    { "min_launcher_version", DEFAULT_MIN_LAUNCHER_VERSION },
                };
                var manifestPath = Path.Combine(artifactsDir, "manifest.json");
                File.WriteAllText(manifestPath, Newtonsoft.Json.JsonConvert.SerializeObject(manifest), new UTF8Encoding(false));

                SetStatus("Upload du manifest…", MessageType.Info);
                var manifestUrl = await UploadAssetAsync("manifest", version, manifestPath, "application/json");

                SetStatus("Finalisation…", MessageType.Info);
                var publishBody = new Dictionary<string, string>
                {
                    { "game_id", gameId },
                    { "name", gameName },
                    { "version", version },
                    { "manifest_url", manifestUrl },
                };
                if (!string.IsNullOrEmpty(coverUrl)) publishBody["cover_url"] = coverUrl;

                await PostJsonAsync<Dictionary<string, object>>("/api/admin/games/new-version", publishBody);

                remoteState = RemoteState.Published;
                remoteCurrentVersion = version;
                remoteName = gameName;
                coverDirty = false;

                SetStatus($"Publié !\nVersion : {version}\nManifest : {manifestUrl}\nDownload : {zipUrl}", MessageType.Info);
            }
            catch (Exception e)
            {
                SetStatus($"Erreur publication : {e.Message}", MessageType.Error);
                Debug.LogException(e);
            }
            finally { busy = false; Repaint(); }
        }

        // -------------------------------------------------------------------
        // Upload helper
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
        private Task<T> PostJsonAsync<T>(string path, object body) => SendJsonAsync<T>(HttpMethod.Post, path, body);
        private Task<T> PatchJsonAsync<T>(string path, object body) => SendJsonAsync<T>(new HttpMethod("PATCH"), path, body);

        private async Task<T> SendJsonAsync<T>(HttpMethod method, string path, object body)
        {
            var url = backendUrl.TrimEnd('/') + path;
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(body);
            var req = new HttpRequestMessage(method, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            req.Headers.TryAddWithoutValidation("X-Admin-Key", adminKey);

            var resp = await http.SendAsync(req);
            var text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                throw new Exception($"{method.Method} {path} failed: {(int)resp.StatusCode} {text}");
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
        // Conditions / Validation
        // -------------------------------------------------------------------
        private bool ConnectionOk() => !string.IsNullOrEmpty(backendUrl) && !string.IsNullOrEmpty(adminKey);
        private bool GameInfoOk()   => !string.IsNullOrEmpty(gameId)
                                       && Regex.IsMatch(gameId ?? "", "^[a-z0-9-]+$")
                                       && !string.IsNullOrEmpty(gameName);
        private bool BuildOk()      => !string.IsNullOrEmpty(version)
                                       && !string.IsNullOrEmpty(buildDir)
                                       && Directory.Exists(buildDir)
                                       && !string.IsNullOrEmpty(launchExe);

        private bool ValidateConnection()
        {
            if (!ConnectionOk())
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
