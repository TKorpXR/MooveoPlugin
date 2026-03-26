using UnityEngine;
using Valve.VR;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class ViveTrackerAutoSetup : MonoBehaviour
{
    private ulong legacySetHandle = 0;

    void Awake()
    {
        var system = OpenVR.System;
        var input = OpenVR.Input;
        if (system == null || input == null) return;

        string currentAppKey = "application.generated.unity.mooveo.exe";
        if (OpenVR.Applications != null)
        {
            uint pid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            System.Text.StringBuilder sb = new System.Text.StringBuilder(1024);
            var appErr = OpenVR.Applications.GetApplicationKeyByProcessId(pid, sb, (uint)sb.Capacity);
            if (appErr == EVRApplicationError.None)
            {
                currentAppKey = sb.ToString();
            }
        }

        string folderPath = Path.Combine(Application.persistentDataPath, "SteamVR_Kiosk_Bindings");
        if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
        string manifestPath = Path.Combine(folderPath, "actions.json").Replace("/", "\\");

        // Tous les rôles tracker avec leur user path
        Dictionary<string, string> roles = new Dictionary<string, string>()
        {
            { "vive_tracker_handed",          "/user/hand/left" },
            { "vive_tracker",                 "/user/hand/right" },
            { "vive_tracker_left_foot",       "/user/foot/left" },
            { "vive_tracker_right_foot",      "/user/foot/right" },
            { "vive_tracker_left_knee",       "/user/knee/left" },
            { "vive_tracker_right_knee",      "/user/knee/right" },
            { "vive_tracker_chest",           "/user/chest" },
            { "vive_tracker_waist",           "/user/waist" },
            { "vive_tracker_left_shoulder",   "/user/shoulder/left" },
            { "vive_tracker_right_shoulder",  "/user/shoulder/right" },
            { "vive_tracker_left_elbow",      "/user/elbow/left" },
            { "vive_tracker_right_elbow",     "/user/elbow/right" },
            { "vive_tracker_camera",          "/user/camera" }
        };

        List<string> defaultBindingsList = new List<string>();

        foreach (var role in roles)
        {
            string bindingFileName = $"binding_{role.Key}.json";
            defaultBindingsList.Add($"{{\"controller_type\": \"{role.Key}\", \"binding_url\": \"{bindingFileName}\"}}");

            List<string> sourceBlocks = new List<string>();

            // LE SECRET EST ICI : On écoute le rôle logique (pour l'UI SteamVR) 
            // ET les canaux matériels physiques (Hand Left & Right pour les Pogo Pins)
            string[] targetPaths = { role.Value, "/user/hand/left", "/user/hand/right" };

            foreach (string path in targetPaths)
            {
                // Pogo Pin 3 : Grip
                sourceBlocks.Add($@"{{
                   ""inputs"" : {{ ""click"" : {{ ""output"" : ""/actions/legacy/in/left_grip_press"" }} }},
                   ""mode"" : ""button"",
                   ""path"" : ""{path}/input/grip""
                }}");

                // Pogo Pin 4 : Trigger (Simplifié en simple bouton pour éviter les erreurs d'axe)
                sourceBlocks.Add($@"{{
                   ""inputs"" : {{ ""click"" : {{ ""output"" : ""/actions/legacy/in/left_axis1_press"" }} }},
                   ""mode"" : ""button"",
                   ""path"" : ""{path}/input/trigger""
                }}");

                // Pogo Pin 2 : Application Menu
                sourceBlocks.Add($@"{{
                   ""inputs"" : {{ ""click"" : {{ ""output"" : ""/actions/legacy/in/left_applicationmenu_press"" }} }},
                   ""mode"" : ""button"",
                   ""path"" : ""{path}/input/application_menu""
                }}");
            }

            // On assemble tous les blocs d'inputs
            string sourcesJson = string.Join(",\n            ", sourceBlocks);

            // Génération du JSON hyper épuré (Plus de haptics, plus de poses, plus de trackpads !)
            string bindingContent = $@"{{
   ""action_manifest_version"" : 0,
   ""app_key"" : ""{currentAppKey}"",
   ""bindings"" : {{
      ""/actions/legacy"" : {{
         ""sources"" : [
            {sourcesJson}
         ]
      }}
   }},
   ""category"" : ""steamvr_input"",
   ""controller_type"" : ""{role.Key}"",
   ""name"" : ""Mooveo Hybrid {role.Key}""
}}";
            File.WriteAllText(Path.Combine(folderPath, bindingFileName), bindingContent);
        }

        // Binding minimal pour null_hmd (évite l'erreur "has no configured binding")
        string nullHmdBinding = $@"{{
   ""action_manifest_version"" : 0,
   ""alias_info"" : {{}},
   ""app_key"" : ""{currentAppKey}"",
   ""bindings"" : {{
      ""/actions/legacy"" : {{
         ""haptics"" : [],
         ""poses"" : [],
         ""skeleton"" : [],
         ""sources"" : []
      }}
   }},
   ""category"" : ""steamvr_input"",
   ""controller_type"" : ""null_hmd"",
   ""description"" : ""Mooveo null HMD placeholder"",
   ""interaction_profile"" : """",
   ""name"" : ""Mooveo null_hmd"",
   ""options"" : {{}},
   ""simulated_actions"" : []
}}";
        File.WriteAllText(Path.Combine(folderPath, "binding_null_hmd.json"), nullHmdBinding);
        defaultBindingsList.Add("{\"controller_type\": \"null_hmd\", \"binding_url\": \"binding_null_hmd.json\"}");

        string defaultBindingsArray = string.Join(",\n    ", defaultBindingsList);

        // Actions.json COMPLET avec TOUTES les actions legacy référencées par les bindings
        string actionsJson = $@"{{
  ""actions"": [
    {{ ""name"": ""/actions/legacy/in/Left_Pose"",                   ""type"": ""pose"" }},
    {{ ""name"": ""/actions/legacy/in/Right_Pose"",                  ""type"": ""pose"" }},
    {{ ""name"": ""/actions/legacy/out/left_haptic"",                ""type"": ""vibration"" }},
    {{ ""name"": ""/actions/legacy/out/right_haptic"",               ""type"": ""vibration"" }},
    {{ ""name"": ""/actions/legacy/in/left_system_press"",           ""type"": ""boolean"" }},
    {{ ""name"": ""/actions/legacy/in/right_system_press"",          ""type"": ""boolean"" }},
    {{ ""name"": ""/actions/legacy/in/left_axis0_press"",            ""type"": ""boolean"" }},
    {{ ""name"": ""/actions/legacy/in/right_axis0_press"",           ""type"": ""boolean"" }},
    {{ ""name"": ""/actions/legacy/in/left_axis0_touch"",            ""type"": ""boolean"" }},
    {{ ""name"": ""/actions/legacy/in/right_axis0_touch"",           ""type"": ""boolean"" }},
    {{ ""name"": ""/actions/legacy/in/left_axis0_value"",            ""type"": ""vector2"" }},
    {{ ""name"": ""/actions/legacy/in/right_axis0_value"",           ""type"": ""vector2"" }},
    {{ ""name"": ""/actions/legacy/in/left_axis1_press"",            ""type"": ""boolean"" }},
    {{ ""name"": ""/actions/legacy/in/right_axis1_press"",           ""type"": ""boolean"" }},
    {{ ""name"": ""/actions/legacy/in/left_axis1_touch"",            ""type"": ""boolean"" }},
    {{ ""name"": ""/actions/legacy/in/right_axis1_touch"",           ""type"": ""boolean"" }},
    {{ ""name"": ""/actions/legacy/in/left_axis1_value"",            ""type"": ""vector1"" }},
    {{ ""name"": ""/actions/legacy/in/right_axis1_value"",           ""type"": ""vector1"" }},
    {{ ""name"": ""/actions/legacy/in/left_grip_press"",             ""type"": ""boolean"" }},
    {{ ""name"": ""/actions/legacy/in/right_grip_press"",            ""type"": ""boolean"" }},
    {{ ""name"": ""/actions/legacy/in/left_applicationmenu_press"",  ""type"": ""boolean"" }},
    {{ ""name"": ""/actions/legacy/in/right_applicationmenu_press"", ""type"": ""boolean"" }}
  ],
  ""action_sets"": [
    {{ ""name"": ""/actions/legacy"", ""usage"": ""single"" }}
  ],
  ""default_bindings"": [
    {defaultBindingsArray}
  ],
  ""localization"": []
}}";
        File.WriteAllText(manifestPath, actionsJson);

        EVRInputError error = input.SetActionManifestPath(manifestPath);
        if (error == EVRInputError.None)
        {
            Debug.Log("<color=green>[AutoSetup]</color> Action manifest chargé avec tous les bindings tracker !");
            input.GetActionSetHandle("/actions/legacy", ref legacySetHandle);
        }
        else
        {
            Debug.LogError($"<color=red>[AutoSetup]</color> Erreur OpenVR SetActionManifestPath : {error}");
        }
    }

    void Update()
    {
        var input = OpenVR.Input;
        if (input == null || legacySetHandle == 0) return;

        VRActiveActionSet_t[] activeSets = new VRActiveActionSet_t[1];
        activeSets[0].ulActionSet = legacySetHandle;
        activeSets[0].ulRestrictedToDevice = OpenVR.k_ulInvalidInputValueHandle;

        uint size = (uint)Marshal.SizeOf(typeof(VRActiveActionSet_t));
        input.UpdateActionState(activeSets, size);
    }
}