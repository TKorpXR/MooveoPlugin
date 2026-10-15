using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR.Management;

/// <summary>
/// Corrige la configuration de SteamVR pour le profil matériel courant : fermer SteamVR, réécrire
/// ses fichiers (<see cref="SteamVRConfig"/>), le relancer.
///
/// ⚠ GraffWall est lui-même CLIENT de SteamVR : son chargeur XR OpenVR est connecté. Fermer SteamVR
/// sous ses pieds le laisserait sur une session morte. On arrête donc le chargeur XR AVANT, et c'est
/// en le réinitialisant qu'on relance SteamVR (<c>OpenVR.Init</c> le démarre), avec la nouvelle
/// configuration. Sans chargeur actif (mode Admin, XR désactivé), on relance SteamVR nous-mêmes.
///
/// ⚠ Le travail sur les fichiers et l'attente de fermeture tournent HORS du fil principal : fermer
/// SteamVR peut prendre jusqu'à ~10 s, qui figeraient l'image au projecteur.
///
/// ⚠ CRASH VÉCU (natif, `XRInputSubsystem::Update`, EN PLAY DANS L'ÉDITEUR — 29/09/2026) :
/// `StopSubsystems`/`DeinitializeLoader` sont synchrones côté C#, mais rien ne garantit que le
/// plugin natif OpenVR ait fini de démonter sa session avant que le thread d'arrière-plan tue
/// `vrserver.exe`. Pendant toute l'attente qui suit, la Game View de l'éditeur continue d'appeler
/// `XRInputSubsystem::Update` à chaque frame (`RenderPlayModeViewCamerasInternal`, un chemin de
/// rendu propre à l'éditeur, absent d'un build) : le processus tué sous ses pieds y laisse un
/// handle natif invalide. D'où <see cref="XrTeardownSettleSeconds"/>, une marge après le
/// démontage, avant de lancer la fermeture. ⚠ À revalider en BUILD, pas seulement en Play : ce
/// chemin de rendu de la Game View n'existe pas dans un exécutable, la marge y est donc une
/// précaution, pas la preuve que le problème s'y reproduirait à l'identique.
/// </summary>
public sealed class SteamVRConfigRepair : ICheckRepair
{
    public string Label => "Corriger la configuration SteamVR";

    // Demande confirmation quand SteamVR tourne : le fermer coupe le suivi des bombes.
    public bool Unattended => false;

    // Marge après DeinitializeLoader/avant InitializeLoader : cf. crash documenté en en-tête.
    private const float XrTeardownSettleSeconds = 1f;

    public IEnumerator Attempt(RepairContext ctx)
    {
        HardwareProfileData profile = HardwareProfiles.Current;
        bool running = SteamVRConfig.IsRunning();

        if (running)
        {
            int picked = RepairContext.Cancelled;
            yield return ctx.Choose("Configuration SteamVR",
                $"SteamVR n'est pas configuré pour « {profile.DisplayName} ». Il va être fermé, corrigé puis " +
                "relancé : le suivi sera coupé quelques secondes.",
                new[] { "Fermer et corriger SteamVR" }, i => picked = i);
            if (picked != 0) yield break;
        }

        // Lu ICI, sur le fil principal : Ensure tourne ensuite hors de lui.
        string mode = profile.SteamVRMode;
        string hint = SteamVRBootstrap.VrMonitorHint;
        string backup = SteamVRBootstrap.BackupRoot;

        XRManagerSettings xr = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
        bool xrWasActive = xr != null && xr.isInitializationComplete;
        if (xrWasActive)
        {
            ctx.Progress("Configuration SteamVR", "Arrêt de la session XR…");
            xr.StopSubsystems();
            xr.DeinitializeLoader();
            // ⚠ Marge avant de tuer SteamVR : cf. crash documenté en en-tête de la classe. Le
            // démontage natif du plugin OpenVR n'est pas garanti fini au retour de l'appel ci-dessus.
            yield return new WaitForSecondsRealtime(XrTeardownSettleSeconds);
            ctx.EndProgress();
        }

        ctx.Progress("Configuration SteamVR", running ? "Fermeture de SteamVR et correction…" : "Correction…");
        Task<SteamVRConfig.Result> work = Task.Run(() => SteamVRConfig.Ensure(mode, hint, backup, allowClose: true));
        while (!work.IsCompleted) yield return null;
        ctx.EndProgress();

        SteamVRConfig.Result result = work.IsFaulted
            ? new SteamVRConfig.Result { Outcome = SteamVRConfig.Outcome.Failed, Message = work.Exception?.GetBaseException().Message }
            : work.Result;
        if (!result.Ok) ctx.Fail(result.Message);

        // Relance, qu'elle ait réussi ou non : laisser le poste sans SteamVR serait pire.
        if (xrWasActive)
        {
            // Même précaution dans l'autre sens : laisser le fichier fraîchement écrit et le
            // processus fraîchement tué se stabiliser avant de reconnecter un client OpenVR.
            yield return new WaitForSecondsRealtime(XrTeardownSettleSeconds);
            ctx.Progress("SteamVR", "Relancement…");
            yield return xr.InitializeLoader();
            ctx.EndProgress();
            if (xr.activeLoader != null) xr.StartSubsystems();
            else ctx.Fail("SteamVR n'a pas pu être relancé par le chargeur XR. Relancez " + MooveoGameHooks.AppName + ".");
        }
        else
        {
            yield return LaunchSoftwareRepair.Launch(ctx, KnownSoftware.SteamVR);
        }
    }
}
