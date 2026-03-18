using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace Mooveo.Editor
{
    public class PackageBuilder : EditorWindow
    {
        private string packagePath = "Assets/Mooveo";
        private string exportPath = "Mooveo_Package.unitypackage";
        private bool includeDependencies = true;
        private bool includePackages = true;
        private bool createUPMPackage = false;
        private string upmExportPath = "Packages/Mooveo";
        private Vector2 scrollPosition;
        private List<string> dependencyPaths = new List<string>();
        private List<string> packageDependencies = new List<string>();
        private bool showDependencies = false;
        private bool showPackages = false;

        [MenuItem("Mooveo/Build Full Package")]
        public static void ShowWindow()
        {
            GetWindow<PackageBuilder>("Mooveo Package Builder");
        }

        private void BuildUPMPackage()
        {
            if (!Directory.Exists(packagePath))
            {
                Debug.LogError($"Package path does not exist: {packagePath}");
                return;
            }

            // Scanner les dépendances si nécessaire
            if (includeDependencies && dependencyPaths.Count == 0)
            {
                ScanAssetDependencies();
            }

            if (includePackages && packageDependencies.Count == 0)
            {
                ScanPackageDependencies();
            }

            // Filtrer et valider les dépendances finales
            var validatedDependencies = ValidateAndFilterDependencies();

            try
            {
                CreateUPMPackageStructure(validatedDependencies);
                Debug.Log($"UPM Package built successfully at: {upmExportPath}");
                Debug.Log($"Package includes automatic dependency installation");
                
                // Ouvrir le dossier contenant le package
                EditorUtility.RevealInFinder(upmExportPath);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to build UPM Package: {e.Message}");
            }
        }

        private void CreateUPMPackageStructure(List<string> assetDependencies)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var fullUPMPath = Path.Combine(projectRoot, upmExportPath);
            
            // Créer la structure du package UPM
            if (Directory.Exists(fullUPMPath))
            {
                Directory.Delete(fullUPMPath, true);
            }
            Directory.CreateDirectory(fullUPMPath);

            // Copier les fichiers de Mooveo
            var sourceMooveoPath = Path.Combine(projectRoot, packagePath);
            var targetMooveoPath = Path.Combine(fullUPMPath, "Runtime");
            CopyDirectory(sourceMooveoPath, targetMooveoPath);

            // Supprimer l'asmdef existant pour éviter le conflit
            var existingAsmdef = Path.Combine(targetMooveoPath, "Mooveo.Runtime.asmdef");
            if (File.Exists(existingAsmdef))
            {
                File.Delete(existingAsmdef);
                Debug.Log($"Removed conflicting assembly definition: {existingAsmdef}");
            }

            // Copier les dépendances d'assets
            var dependenciesPath = Path.Combine(fullUPMPath, "Dependencies");
            if (assetDependencies.Count > 0)
            {
                Directory.CreateDirectory(dependenciesPath);
                foreach (var dep in assetDependencies)
                {
                    var relativePath = dep.Replace("Assets/", "");
                    var targetDepPath = Path.Combine(dependenciesPath, relativePath);
                    var targetDepDir = Path.GetDirectoryName(targetDepPath);
                    
                    if (!Directory.Exists(targetDepDir))
                    {
                        Directory.CreateDirectory(targetDepDir);
                    }
                    
                    File.Copy(dep, targetDepPath, true);
                    
                    // Copier aussi le .meta file
                    var metaPath = dep + ".meta";
                    if (File.Exists(metaPath))
                    {
                        File.Copy(metaPath, targetDepPath + ".meta", true);
                    }
                }
            }

            // Créer le package.json UPM
            CreateUPMPackageJson(fullUPMPath);

            // Créer l'assembly definition pour le package
            CreateUPMAssemblyDefinition(fullUPMPath);

            Debug.Log($"UPM Package structure created at: {fullUPMPath}");
        }

        private void CreateUPMPackageJson(string packagePath)
        {
            var packageJson = new
            {
                name = "com.bl0bli.mooveo",
                version = "1.0.0",
                displayName = "Mooveo SDK",
                description = "Système de tracking et calibration pour espaces interactifs 2D/3D.",
                unity = "2021.3",
                unityRelease = "21f1",
                documentationUrl = "https://github.com/bl0bli/mooveo",
                dependencies = new Dictionary<string, string>
                {
                    {"com.unity.inputsystem", "1.7.0"},
                    {"com.unity.textmeshpro", "3.0.6"},
                    {"com.unity.xr.management", "4.4.0"},
                    {"com.unity.xr.interaction.toolkit", "2.5.2"},
                    {"com.unity.xr.openxr", "1.10.0"},
                    {"com.dbrizov.naughtyattributes", "3.2.0"}
                },
                keywords = new[] { "vr", "tracking", "calibration", "xr", "interaction" },
                author = new
                {
                    name = "Enzo Bossé",
                    email = "bosseenzo6@gmail.com"
                }
            };

            var jsonPath = Path.Combine(packagePath, "package.json");
            File.WriteAllText(jsonPath, Newtonsoft.Json.JsonConvert.SerializeObject(packageJson, Newtonsoft.Json.Formatting.Indented));
            Debug.Log($"Created UPM package.json at: {jsonPath}");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Mooveo Package Builder", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            packagePath = EditorGUILayout.TextField("Package Path:", packagePath);
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Export Options", EditorStyles.boldLabel);
            createUPMPackage = EditorGUILayout.Toggle("Create UPM Package", createUPMPackage);
            
            if (createUPMPackage)
            {
                upmExportPath = EditorGUILayout.TextField("UPM Export Path:", upmExportPath);
                EditorGUILayout.HelpBox("UPM Package will include all dependencies automatically", MessageType.Info);
            }
            else
            {
                exportPath = EditorGUILayout.TextField("Export Name:", exportPath);
                EditorGUILayout.HelpBox("Unity Package requires manual dependency installation", MessageType.Info);
            }
            
            EditorGUILayout.Space();
            includeDependencies = EditorGUILayout.Toggle("Include Asset Dependencies", includeDependencies);
            includePackages = EditorGUILayout.Toggle("Include Package Dependencies", includePackages);
            
            EditorGUILayout.Space();

            if (GUILayout.Button("Scan Dependencies"))
            {
                ScanAllDependencies();
            }

            if (GUILayout.Button(createUPMPackage ? "Build UPM Package" : "Build Unity Package"))
            {
                if (createUPMPackage)
                {
                    BuildUPMPackage();
                }
                else
                {
                    BuildPackage();
                }
            }

            EditorGUILayout.Space();

            if (dependencyPaths.Count > 0)
            {
                showDependencies = EditorGUILayout.Foldout(showDependencies, $"Asset Dependencies ({dependencyPaths.Count})");
                if (showDependencies)
                {
                    scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
                    foreach (var path in dependencyPaths)
                    {
                        EditorGUILayout.LabelField($"  {path}", EditorStyles.miniLabel);
                    }
                    EditorGUILayout.EndScrollView();
                }
            }

            if (packageDependencies.Count > 0)
            {
                showPackages = EditorGUILayout.Foldout(showPackages, $"Package Dependencies ({packageDependencies.Count})");
                if (showPackages)
                {
                    foreach (var pkg in packageDependencies)
                    {
                        EditorGUILayout.LabelField($"  {pkg}", EditorStyles.miniLabel);
                    }
                }
            }
        }

        private void ScanAllDependencies()
        {
            dependencyPaths.Clear();
            packageDependencies.Clear();

            if (!Directory.Exists(packagePath))
            {
                Debug.LogError($"Package path does not exist: {packagePath}");
                return;
            }

            // Scanner les dépendances d'assets
            if (includeDependencies)
            {
                ScanAssetDependencies();
            }

            // Scanner les dépendances de packages
            if (includePackages)
            {
                ScanPackageDependencies();
            }

            // AJOUTER les dépendances critiques manquantes
            AddCriticalMissingDependencies();

            Debug.Log($"Found {dependencyPaths.Count} asset dependencies and {packageDependencies.Count} package dependencies");
        }

        private void AddCriticalMissingDependencies()
        {
            // Forcer l'inclusion des dépendances critiques basées sur les erreurs observées
            
            // GlobalSettings (référencé par CalibrationCursorUI.cs)
            var globalSettingsAssets = AssetDatabase.FindAssets("GlobalSettings t:script");
            foreach (var guid in globalSettingsAssets)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (File.Exists(path) && path.StartsWith("Assets/"))
                {
                    dependencyPaths.Add(path);
                    Debug.Log($"Added critical dependency: {path}");
                }
            }

            // NaughtyAttributes (utilisé par UIAnimator.cs)
            var naughtyAssets = AssetDatabase.FindAssets("NaughtyAttributes");
            foreach (var guid in naughtyAssets)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (File.Exists(path) && path.StartsWith("Assets/"))
                {
                    dependencyPaths.Add(path);
                    Debug.Log($"Added NaughtyAttributes dependency: {path}");
                }
            }

            // S'assurer que les packages XR sont bien dans la liste
            var requiredPackages = new[]
            {
                "com.unity.xr.management",
                "com.unity.xr.interaction.toolkit", 
                "com.unity.xr.openxr",
                "com.unity.inputsystem",
                "com.unity.textmeshpro",
                "com.dbrizov.naughtyattributes",
                "com.bl0bli.globalsettings"
            };

            foreach (var pkg in requiredPackages)
            {
                if (!packageDependencies.Contains(pkg))
                {
                    packageDependencies.Add(pkg);
                    Debug.Log($"Added critical package dependency: {pkg}");
                }
            }
        }

        private void ScanAssetDependencies()
        {
            var assetPaths = Directory.GetFiles(packagePath, "*", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith(".meta") && !p.EndsWith(".cs"))
                .Select(p => p.Replace('\\', '/'))
                .ToList();

            var allDependencies = new HashSet<string>();

            foreach (var assetPath in assetPaths)
            {
                var dependencies = AssetDatabase.GetDependencies(assetPath, true);
                foreach (var dep in dependencies)
                {
                    if (!dep.StartsWith(packagePath) && File.Exists(dep) && IsEssentialDependency(dep))
                    {
                        allDependencies.Add(dep);
                    }
                }
            }

            // Analyser les scripts pour les références directes uniquement
            var scriptFiles = Directory.GetFiles(packagePath, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith(".meta"))
                .ToList();
            foreach (var scriptPath in scriptFiles)
            {
                var scriptDependencies = AnalyzeScriptDependencies(scriptPath);
                foreach (var dep in scriptDependencies)
                {
                    if (File.Exists(dep) && IsEssentialDependency(dep))
                    {
                        allDependencies.Add(dep);
                    }
                }
            }

            dependencyPaths = allDependencies.ToList();
            dependencyPaths.Sort();
        }

        private bool IsEssentialDependency(string path)
        {
            // INCLURE les dépendances critiques pour Mooveo
            if (path.StartsWith("Assets/GlobalSettings/") || 
                path.Contains("GlobalSettings.Core"))
            {
                return true; // GlobalSettings est ESSENTIEL
            }

            // INCLURE les packages XR requis
            if (path.Contains("OpenXR") || 
                path.Contains("XR.Interaction.Toolkit") ||
                path.Contains("XR.Management"))
            {
                return true; // Packages XR sont essentiels pour Mooveo
            }

            // INCLURE NaughtyAttributes (utilisé par UIAnimator)
            if (path.Contains("NaughtyAttributes"))
            {
                return true; // Utilisé par UIAnimator
            }

            // Exclure les assets non essentiels
            if (path.StartsWith("Assets/Plugins/CW/") || 
                path.Contains("PaintIn3D") || 
                path.Contains("PaintIn2D") ||
                (path.Contains("CW.Common") && !path.Contains("GlobalSettings")))
            {
                return false;
            }

            // Exclure les assets de test et démo
            if (path.Contains("/Demo/") || 
                path.Contains("/Test/") || 
                path.Contains("/Samples/") ||
                path.Contains("/Documentation/"))
            {
                return false;
            }

            // Exclure les assets par défaut Unity non critiques
            if (path.Contains("Default") && 
                (path.Contains("Material") || path.Contains("Texture") || path.Contains("Shader")))
            {
                return false;
            }

            return true;
        }

        private List<string> AnalyzeScriptDependencies(string scriptPath)
        {
            var dependencies = new HashSet<string>();
            var content = File.ReadAllText(scriptPath);

            // Trouver les références à des classes spécifiques et critiques uniquement
            var criticalTypes = new[] { "GlobalSettings", "MooveoConfig", "InputConfig", "CalibrationManager" };
            
            foreach (var criticalType in criticalTypes)
            {
                if (content.Contains(criticalType))
                {
                    var guids = AssetDatabase.FindAssets($"t:script {criticalType}");
                    foreach (var guid in guids)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        if (File.Exists(path) && IsEssentialDependency(path))
                        {
                            dependencies.Add(path);
                        }
                    }
                }
            }

            // Trouver les références à des assets via Resources.Load (uniquement si critique)
            var resourceReferences = Regex.Matches(content, @"Resources\.Load\s*<\s*([^\s>]+)\s*>\s*\(\s*[""']([^""']+)[""']\s*\)");
            foreach (Match match in resourceReferences)
            {
                var assetType = match.Groups[1].Value;
                var assetName = match.Groups[2].Value;
                
                // Inclure uniquement les Resources critiques
                if (assetName.Contains("Mooveo") || assetName.Contains("Calibration") || assetName.Contains("Config"))
                {
                    var guids = AssetDatabase.FindAssets($"{assetName} t:{assetType}");
                    foreach (var guid in guids)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        if (File.Exists(path) && IsEssentialDependency(path))
                        {
                            dependencies.Add(path);
                        }
                    }
                }
            }

            return dependencies.ToList();
        }

        private void ScanPackageDependencies()
        {
            var scriptFiles = Directory.GetFiles(packagePath, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith(".meta"))
                .ToList();

            var namespaces = new HashSet<string>();
            var unityPackages = new HashSet<string>();

            foreach (var scriptPath in scriptFiles)
            {
                var content = File.ReadAllText(scriptPath);
                
                // Trouver tous les using statements
                var usingMatches = Regex.Matches(content, @"using\s+([^;]+);");
                foreach (Match match in usingMatches)
                {
                    var ns = match.Groups[1].Value.Trim();
                    namespaces.Add(ns);
                }
            }

            // Analyser les namespaces pour identifier les packages Unity
            foreach (var ns in namespaces)
            {
                if (ns.StartsWith("Unity."))
                {
                    var packageName = ConvertNamespaceToPackage(ns);
                    if (!string.IsNullOrEmpty(packageName))
                    {
                        unityPackages.Add(packageName);
                    }
                }
                else if (ns.StartsWith("UnityEngine."))
                {
                    // Modules Unity inclus par défaut
                    var moduleName = ns.Replace("UnityEngine.", "");
                    if (IsUnityModulePackage(moduleName))
                    {
                        unityPackages.Add($"com.unity.modules.{moduleName.ToLower()}");
                    }
                }
                else if (ns.StartsWith("GlobalSettings"))
                {
                    // Forcer l'inclusion de GlobalSettings comme dépendance de package
                    unityPackages.Add("com.bl0bli.globalsettings");
                }
                else if (ns.Contains("NaughtyAttributes"))
                {
                    // Forcer l'inclusion de NaughtyAttributes
                    unityPackages.Add("com.dbrizov.naughtyattributes");
                }
            }

            packageDependencies = unityPackages.ToList();
            packageDependencies.Sort();
        }

        private string ConvertNamespaceToPackage(string namespaceStr)
        {
            var mapping = new Dictionary<string, string>
            {
                {"Unity.ProBuilder", "com.unity.probuilder"},
                {"Unity.ProBuilder.Stl", "com.unity.probuilder"},
                {"Unity.TextMeshPro", "com.unity.textmeshpro"},
                {"Unity.PostProcessing", "com.unity.postprocessing"},
                {"Unity.Cinemachine", "com.unity.cinemachine"},
                {"Unity.Timeline", "com.unity.timeline"},
                {"Unity.Addressables", "com.unity.addressables"},
                {"Unity.Cloud.Build", "com.unity.cloud.build"},
                {"Unity.Cloud.Diagnostics", "com.unity.cloud.diagnostics"},
                {"Unity.Cloud.Downloads", "com.unity.cloud.downloads"},
                {"Unity.Cloud.Production", "com.unity.cloud.production"},
                {"Unity.Cloud.Share", "com.unity.cloud.share"},
                {"Unity.Cloud.Subscription", "com.unity.cloud.subscription"},
                {"Unity.Cloud", "com.unity.cloud"},
                {"Unity.AI.Planner", "com.unity.ai.planner"},
                {"Unity.AI.Navigation", "com.unity.ai.navigation"},
                {"Unity.AI.MachineLearning", "com.unity.ai.ml-agents"},
                {"Unity.VisualScripting", "com.unity.visualscripting"},
                {"Unity.RenderPipelines.Core", "com.unity.render-pipelines.core"},
                {"Unity.RenderPipelines.Universal", "com.unity.render-pipelines.universal"},
                {"Unity.RenderPipelines.HighDefinition", "com.unity.render-pipelines.high-definition"},
                {"Unity.XR.CoreUtils", "com.unity.xr.core-utils"},
                {"Unity.XR.Interaction.Toolkit", "com.unity.xr.interaction.toolkit"},
                {"Unity.XR.Management", "com.unity.xr.management"},
                {"Unity.XR.OpenXR", "com.unity.xr.openxr"},
            };

            foreach (var kvp in mapping)
            {
                if (namespaceStr.StartsWith(kvp.Key))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        private bool IsUnityModulePackage(string moduleName)
        {
            var validModules = new HashSet<string>
            {
                "ai", "androidjni", "animation", "assetbundle", "audio", "video", "vr", "webgl", "webrequest",
                "wheelcollider", "wind", "ui", "uie", "uielementsnative", "terrain", "terrainphysics",
                "tilemap", "textcore", "textcoretextengine", "textrendering", "tween", "umbra",
                "unityanalytics", "unityconnect", "unitywebrequest", "unitywebrequestassetbundle",
                "unitywebrequestaudio", "unitywebrequesttexture", "vehicles", "video", "vr", "wind",
                "xr", "ui", "imgui", "physx", "physics2d", "particlesystem", "perception",
                "profiler", "scenevisibility", "screenrecorder", "splines", "streaming",
                "subsystems", "terrain", "terrainphysics", "tilemap", "tls", "ui", "uie",
                "umbra", "vehicles", "video", "virtualreality", "visualscripting", "vr",
                "vfx", "web", "webrequest", "wind", "xr"
            };

            return validModules.Contains(moduleName.ToLower());
        }

        private void BuildPackage()
        {
            if (!Directory.Exists(packagePath))
            {
                Debug.LogError($"Package path does not exist: {packagePath}");
                return;
            }

            // Scanner les dépendances si nécessaire
            if (includeDependencies && dependencyPaths.Count == 0)
            {
                ScanAssetDependencies();
            }

            if (includePackages && packageDependencies.Count == 0)
            {
                ScanPackageDependencies();
            }

            // Filtrer et valider les dépendances finales
            var validatedDependencies = ValidateAndFilterDependencies();

            var allPaths = new List<string>();
            
            // Ajouter le dossier principal
            allPaths.Add(packagePath);
            
            // Ajouter uniquement les dépendances validées
            if (includeDependencies)
            {
                allPaths.AddRange(validatedDependencies);
            }

            // Créer le package
            var exportPathFull = Path.Combine(Application.dataPath, "..", exportPath);
            AssetDatabase.ExportPackage(allPaths.ToArray(), exportPathFull, ExportPackageOptions.Recurse);

            Debug.Log($"Package built successfully: {exportPathFull}");
            Debug.Log($"Included {allPaths.Count} paths ({validatedDependencies.Count} dependencies)");

            // Afficher les dépendances de packages dans la console
            if (includePackages && packageDependencies.Count > 0)
            {
                Debug.Log("Package dependencies detected (these should be installed in the target project):");
                foreach (var pkg in packageDependencies)
                {
                    Debug.Log($"  - {pkg}");
                }
            }

            // Afficher les dépendances exclues pour transparence
            var excludedCount = dependencyPaths.Count - validatedDependencies.Count;
            if (excludedCount > 0)
            {
                Debug.Log($"Excluded {excludedCount} non-essential dependencies to keep package minimal");
            }

            // Ouvrir le dossier contenant le package
            EditorUtility.RevealInFinder(exportPathFull);
        }

        private List<string> ValidateAndFilterDependencies()
        {
            var validated = new List<string>();
            
            foreach (var dep in dependencyPaths)
            {
                if (IsEssentialDependency(dep) && File.Exists(dep))
                {
                    validated.Add(dep);
                }
                else
                {
                    Debug.Log($"Excluded non-essential dependency: {dep}");
                }
            }
            
            return validated;
        }

        private void CreateUPMAssemblyDefinition(string packagePath)
        {
            var asmdef = new
            {
                name = "Mooveo.UPM",
                rootNamespace = "",
                references = new[] { "Unity.InputSystem", "Unity.TextMeshPro", "UnityEngine.XR" },
                includePlatforms = new string[0],
                excludePlatforms = new string[0],
                allowUnsafeCode = false,
                overrideReferences = false,
                precompiledReferences = new string[0],
                autoReferenced = true,
                defineConstraints = new string[0],
                versionDefines = new object[0],
                noEngineReferences = false
            };

            var asmdefPath = Path.Combine(packagePath, "Runtime", "Mooveo.UPM.asmdef");
            File.WriteAllText(asmdefPath, Newtonsoft.Json.JsonConvert.SerializeObject(asmdef, Newtonsoft.Json.Formatting.Indented));
            Debug.Log($"Created UPM assembly definition at: {asmdefPath}");
        }

        private void CopyDirectory(string sourceDir, string targetDir)
        {
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // Copier tous les fichiers et sous-dossiers
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var fileName = Path.GetFileName(file);
                var targetFile = Path.Combine(targetDir, fileName);
                File.Copy(file, targetFile, true);
            }

            foreach (var directory in Directory.GetDirectories(sourceDir))
            {
                var dirName = Path.GetFileName(directory);
                var targetDirectory = Path.Combine(targetDir, dirName);
                CopyDirectory(directory, targetDirectory);
            }
        }
    }
}