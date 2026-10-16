using UnityEngine;

/// <summary>
/// Projet de DÉMO du SDK (hors package) : la scène Mooveo imite un poste GraffWall, qui utilise les
/// trois logiciels tiers. Un jeu fait la même déclaration avec ce dont il a réellement besoin
/// (cf. MooveoSoftwareChecks).
/// </summary>
public static class MooveoDemoConfiguration
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Configure() => MooveoSoftwareChecks.Enable(MooveoSoftware.All);
}
