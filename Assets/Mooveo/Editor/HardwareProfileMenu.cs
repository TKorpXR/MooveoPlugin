using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Choix du matériel de suivi dans l'éditeur : menu <c>Mooveo/Matériel…</c>.
/// Le choix vit dans les EditorPrefs (propres au poste, jamais commités), cf. <see cref="HardwareProfiles"/>.
///
/// ⚠ Une FENÊTRE et non une entrée de menu par profil : un <c>[MenuItem]</c> est figé à la
/// compilation, il faudrait en écrire un à la main pour chaque matériel. La fenêtre parcourt
/// <see cref="HardwareProfiles.All"/> : un profil déclaré y apparaît sans rien toucher ici.
/// </summary>
public class HardwareProfileMenu : EditorWindow
{
    [MenuItem("Mooveo/Matériel…", priority = 1)]
    private static void Open()
    {
        var window = GetWindow<HardwareProfileMenu>(utility: true, title: "Matériel de suivi");
        window.minSize = new Vector2(420, 120 + 70 * HardwareProfiles.All.Count);
    }

    /// <summary>Profil choisi dans l'éditeur, relu à chaque appel (le build peut suivre un changement récent).</summary>
    public static HardwareProfile Selected
    {
        get
        {
            var stored = (HardwareProfile)EditorPrefs.GetInt(HardwareProfiles.EditorPrefsKey, (int)HardwareProfiles.Default.Id);
            return HardwareProfiles.IsDeclared(stored) ? stored : HardwareProfiles.Default.Id;
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Matériel utilisé sur ce poste de développement", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Mémorisé dans les EditorPrefs de ce poste, jamais dans le projet. En jeu, le choix se fait " +
            "au premier lancement puis depuis les paramètres.", MessageType.None);

        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
        {
            HardwareProfile selected = Selected;
            foreach (HardwareProfileData profile in HardwareProfiles.All)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    bool isSelected = profile.Id == selected;
                    bool pick = EditorGUILayout.ToggleLeft(profile.DisplayName, isSelected, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(profile.Description, EditorStyles.wordWrappedMiniLabel);
                    if (pick && !isSelected) Select(profile);
                }
            }
        }

        if (EditorApplication.isPlaying)
            EditorGUILayout.HelpBox("Hors Play seulement : en jeu, changer de matériel demande de relancer " +
                                    "scan et calibration.", MessageType.Info);
    }

    private static void Select(HardwareProfileData profile)
    {
        HardwareProfiles.Select(profile.Id);
        Debug.Log($"[HardwareProfile] Éditeur réglé sur : {profile.DisplayName}. " +
                  $"Pensez aussi à SteamVR : tools/steamvr/Set-SteamVRProfile.ps1 -Mode {profile.SteamVRMode}");
    }
}

/// <summary>
/// Cuit le profil choisi dans l'éditeur dans le build, quel qu'il soit — les deux profils
/// (Vive Trackers, PICO 4 Enterprise) sont des configurations de production à part égale, aucune
/// confirmation ni blocage CI ne distingue plus l'une de l'autre.
///
/// ⚠ Le fichier n'existe QUE pendant le build : absent, le jeu retombe sur Vive Trackers. Un build
/// interrompu pourrait le laisser derrière lui — d'où son entrée dans le .gitignore, et sa
/// suppression systématique en fin de build.
/// </summary>
public class HardwareProfileBuildHook : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    // Dossier Resources du PROJET hôte, jamais celui du package : installé par UPM, le package est en
    // lecture seule. Le fichier est supprimé après chaque build (OnPostprocessBuild).
    private const string ResourcesDir = "Assets/Resources";
    private static string BakedPath => $"{ResourcesDir}/{HardwareProfiles.BakedResourceName}.txt";

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        HardwareProfile profile = HardwareProfileMenu.Selected;
        HardwareProfileData data = HardwareProfiles.Get(profile);

        Directory.CreateDirectory(ResourcesDir);
        File.WriteAllText(BakedPath, profile.ToString());
        AssetDatabase.ImportAsset(BakedPath, ImportAssetOptions.ForceSynchronousImport);
        Debug.Log($"[HardwareProfile] Profil cuit dans le build : {data.DisplayName}");
    }

    public void OnPostprocessBuild(BuildReport report) => DeleteBaked();

    private static void DeleteBaked()
    {
        if (AssetDatabase.LoadAssetAtPath<TextAsset>(BakedPath) != null) AssetDatabase.DeleteAsset(BakedPath);
        else if (File.Exists(BakedPath)) File.Delete(BakedPath);
    }
}
