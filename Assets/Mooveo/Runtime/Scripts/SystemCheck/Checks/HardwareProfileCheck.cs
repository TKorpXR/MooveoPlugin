using UnityEngine.Scripting;

/// <summary>
/// Le profil matériel de TEST doit se VOIR : un build PICO confondu avec un build de production ne
/// marcherait pas avec les trackers, et la panne se découvrirait chez le client. Avertissement tant
/// que le profil n'est pas celui de production ; muet sinon.
/// </summary>
[Preserve]
public class HardwareProfileCheck : SyncSystemCheck
{
    public override string Category => CheckCategory.Configuration;
    public override string Name => "Profil matériel";
    public override CheckScope Scope => CheckScope.Both;
    public override int Order => -100;

    protected override CheckResult Evaluate()
    {
        HardwareProfileData profile = HardwareProfiles.Current;
        if (!profile.IsTestProfile) return CheckResult.Ok(profile.DisplayName);

        return CheckResult.Warning(
            $"Profil de TEST actif : {profile.DisplayName}. Ce build ne fonctionne pas avec les Vive Trackers.",
            "Ne pas livrer. Pour la production : éditeur → GraffWall/Matériel/Vive Trackers, puis rebuilder.");
    }
}
