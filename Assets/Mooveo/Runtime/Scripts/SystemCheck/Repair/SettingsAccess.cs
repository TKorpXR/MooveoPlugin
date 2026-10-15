using System.IO;
using System.Reflection;

/// <summary>
/// Lecture et écriture d'un réglage texte de <see cref="GlobalSettings"/> PAR SON NOM. Une réparation
/// porte le nom du réglage qu'elle corrige (« StickersPath ») : c'est ce nom que cite le diagnostic,
/// et ce qui évite une réparation par réglage.
/// </summary>
public static class SettingsAccess
{
    public static string Get(string settingName)
    {
        ObservableSetting<string> setting = Find(settingName);
        return setting != null ? setting.Value : null;
    }

    /// <summary>
    /// Écrit et ENREGISTRE. ⚠ <c>SaveToJson</c> avale ses erreurs (simple log) : on relit le fichier
    /// pour s'assurer que la valeur y est bien, sinon la réparation paraîtrait réussie et serait
    /// perdue au prochain lancement.
    /// </summary>
    public static bool TrySet(string settingName, string value, out string error)
    {
        error = null;
        GlobalSettings.Core.GlobalSettings settings = GlobalSettings.Core.GlobalSettings.Instance;
        ObservableSetting<string> setting = Find(settingName);
        if (settings == null || setting == null)
        {
            error = $"Réglage {settingName} introuvable : la modification n'a pas pu être faite.";
            return false;
        }

        setting.Value = value;
        settings.SaveSettings();

        try
        {
            // JsonUtility double les barres obliques inverses.
            string json = File.ReadAllText(PathChecks.SettingsFilePath);
            if (json.Contains((value ?? "").Replace("\\", "\\\\"))) return true;
        }
        catch { /* illisible : traité comme non enregistré */ }

        error = $"{settingName} a été modifié pour cette session, mais n'a pas pu être enregistré dans " +
                $"{PathChecks.SettingsFilePath}. Vérifiez les droits d'écriture sur ce fichier.";
        return false;
    }

    private static ObservableSetting<string> Find(string settingName)
    {
        if (string.IsNullOrEmpty(settingName) || GlobalSettings.Core.GlobalSettings.Instance == null) return null;
        FieldInfo field = typeof(GlobalSettings.Core.GlobalSettings).GetField(settingName, BindingFlags.Public | BindingFlags.Instance);
        return field?.GetValue(GlobalSettings.Core.GlobalSettings.Instance) as ObservableSetting<string>;
    }
}
