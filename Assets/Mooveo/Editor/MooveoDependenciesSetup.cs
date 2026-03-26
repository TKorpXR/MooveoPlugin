using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Mooveo.Editor
{
    [InitializeOnLoad]
    public class MooveoDependenciesSetup : EditorWindow
    {
        private static ListRequest _listRequest;
        private AddRequest _addRequest;
        private string _currentInstallTarget = "";

        private bool _needsNaughtyAttributes = false;
        private bool _needsNewtonsoft = false;

        private const string NaughtyAttributesUrl = "https://github.com/dbrizov/NaughtyAttributes.git#upm";
        private const string NewtonsoftPackage = "com.unity.nuget.newtonsoft-json";
        // To install XRI Starter Assets, usually it is done via installing a Sample from the package manager.
        // Doing it via script is complex, but checking for the package itself is easy.

        static MooveoDependenciesSetup()
        {
            // Verify packages shortly after Unity starts
            EditorApplication.delayCall += CheckDependencies;
        }

        [MenuItem("Window/Mooveo/Check Dependencies")]
        public static void ShowWindow()
        {
            CheckDependencies();
        }

        private static void CheckDependencies()
        {
            _listRequest = Client.List();
            EditorApplication.update += ListProgress;
        }

        private static void ListProgress()
        {
            if (_listRequest != null && _listRequest.IsCompleted)
            {
                if (_listRequest.Status == StatusCode.Success)
                {
                    bool hasNaughty = _listRequest.Result.Any(p => p.name == "com.dbrizov.naughtyattributes");
                    bool hasNewtonsoft = _listRequest.Result.Any(p => p.name == "com.unity.nuget.newtonsoft-json");

                    if (!hasNaughty || !hasNewtonsoft)
                    {
                        MooveoDependenciesSetup window = GetWindow<MooveoDependenciesSetup>("Mooveo Setup", true);
                        window._needsNaughtyAttributes = !hasNaughty;
                        window._needsNewtonsoft = !hasNewtonsoft;
                        window.minSize = new Vector2(400, 250);
                        window.ShowUtility();
                    }
                }
                EditorApplication.update -= ListProgress;
            }
        }

        private void OnGUI()
        {
            GUILayout.Label("Mooveo Plugin - Missing Dependencies", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Some packages required by MooveoPlugin are missing from this project. You can install them with one click below.", MessageType.Warning);

            EditorGUI.BeginDisabledGroup(_addRequest != null);

            if (_needsNaughtyAttributes)
            {
                EditorGUILayout.Space();
                GUILayout.Label("- NaughtyAttributes (UI & Inspector tools)");
                if (GUILayout.Button("Install NaughtyAttributes"))
                {
                    InstallPackage(NaughtyAttributesUrl);
                }
            }

            if (_needsNewtonsoft)
            {
                EditorGUILayout.Space();
                GUILayout.Label("- Newtonsoft Json (JSON Serialization)");
                if (GUILayout.Button("Install Newtonsoft Json"))
                {
                    InstallPackage(NewtonsoftPackage);
                }
            }

            EditorGUILayout.Space();
            GUILayout.Label("- XR Interaction Toolkit Starter Assets");
            EditorGUILayout.HelpBox("To install Starter Assets, open Package Manager -> XR Interaction Toolkit -> Samples -> Import 'Starter Assets'.", MessageType.Info);

            EditorGUI.EndDisabledGroup();

            if (_addRequest != null)
            {
                EditorGUILayout.Space();
                GUILayout.Label($"Installing {_currentInstallTarget}... please wait.");
            }
        }

        private void InstallPackage(string packageId)
        {
            _currentInstallTarget = packageId;
            _addRequest = Client.Add(packageId);
            EditorApplication.update += AddProgress;
        }

        private void AddProgress()
        {
            if (_addRequest != null && _addRequest.IsCompleted)
            {
                if (_addRequest.Status == StatusCode.Success)
                {
                    Debug.Log($"Successfully installed {_currentInstallTarget}");
                    // Re-check what is still missing
                    _addRequest = null;
                    EditorApplication.update -= AddProgress;
                    CheckDependencies(); 
                }
                else if (_addRequest.Status >= StatusCode.Failure)
                {
                    Debug.LogError($"Failed to install {_currentInstallTarget}: {_addRequest.Error.message}");
                    _addRequest = null;
                    EditorApplication.update -= AddProgress;
                }
            }
        }
    }
}
