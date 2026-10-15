using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Écran de choix du matériel de suivi, montré au tout PREMIER lancement, avant que le système de
/// vérifications ne s'exécute (cf. issue #71 : le choix ne doit plus être réservé à l'éditeur).
///
/// ⚠ Pas de « Annuler » qui laisserait le poste sans profil : la sortie neutre (poignée) équivaut à
/// choisir le profil de PRODUCTION — un choix par défaut sûr, jamais un profil de test.
/// ⚠ Généralisé : parcourt <see cref="HardwareProfiles.All"/>, aucun matériel n'est nommé ici.
/// </summary>
public static class HardwareProfilePicker
{
    private const float HostReadyTimeoutSeconds = 5f;

    /// <summary>
    /// Montre l'écran si aucun choix n'est encore mémorisé, puis appelle <paramref name="onDone"/> —
    /// immédiatement s'il y a déjà un choix, sinon une fois l'opérateur décidé (et la configuration
    /// SteamVR mise à jour si besoin). Ne bloque JAMAIS indéfiniment : hôte introuvable ou choix
    /// impossible à afficher retombent sur le profil de production, sans arrêter le lancement.
    /// </summary>
    public static IEnumerator ShowIfNeeded(System.Func<VisualElement> resolveOverlay, System.Action onDone)
    {
        if (HardwareProfiles.HasPersistedChoice)
        {
            onDone?.Invoke();
            yield break;
        }

        VisualElement overlay = null;
        float deadline = Time.realtimeSinceStartup + HostReadyTimeoutSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            overlay = resolveOverlay?.Invoke();
            if (overlay != null) break;
            yield return null;
        }

        if (overlay == null)
        {
            Debug.LogError("[HardwareProfilePicker] Aucun panneau disponible pour proposer le choix du " +
                           "matériel : repli sur le profil de production, sans le mémoriser.");
            onDone?.Invoke();
            yield break;
        }

        HardwareProfileData chosen = null;
        var options = new List<string>();
        foreach (HardwareProfileData profile in HardwareProfiles.All)
            options.Add($"{profile.DisplayName}\n{profile.Description}");

        ChoicePopup.Show(overlay, "Choix du matériel",
            "Quel matériel de suivi est utilisé sur ce poste ? Ce choix est mémorisé et pourra être " +
            "changé plus tard depuis les paramètres.",
            options, "Utiliser Vive Trackers (par défaut)",
            i => chosen = (i >= 0 && i < HardwareProfiles.All.Count) ? HardwareProfiles.All[i] : HardwareProfiles.Default);

        while (chosen == null) yield return null;

        HardwareProfiles.Select(chosen.Id);
        yield return ApplySteamVRConfigIfNeeded(overlay);

        onDone?.Invoke();
    }

    /// <summary>
    /// Change de matériel SUR DEMANDE, à n'importe quel moment — contrairement à
    /// <see cref="ShowIfNeeded"/>, qui ne s'affiche que tant qu'aucun choix n'est mémorisé. Sert au
    /// bouton toujours accessible de la calibration (<c>CalibrationManager</c>) : si le profil
    /// mémorisé ne correspond pas au matériel réellement branché, AUCUN contrôleur n'est détecté
    /// (`AnyControllerChecker` cherche un rôle que SteamVR, configuré pour l'autre matériel, ne
    /// fournit pas) — sans un accès qui ne dépende pas d'un contrôleur déjà détecté, l'opérateur
    /// serait bloqué sans aucun moyen d'agir. `ChoicePopup` répond aussi à la SOURIS sur un panneau
    /// SCREEN SPACE — OVERLAY, donc ce bouton reste joignable même à zéro périphérique VR.
    ///
    /// Annuler, ou choisir le profil déjà actif, ne fait rien (<paramref name="onChanged"/> pas
    /// appelé). Sinon : mémorise, corrige la config SteamVR si besoin, puis appelle
    /// <paramref name="onChanged"/> avec le nouveau profil — c'est l'appelant qui relance le scan et
    /// la vérification, `HardwareProfilePicker` ne connaît pas ces détails de calibration.
    /// </summary>
    public static IEnumerator ShowChangeMenu(VisualElement overlay, System.Action<HardwareProfileData> onChanged,
                                             string message = null)
    {
        if (overlay == null) yield break;

        HardwareProfileData current = HardwareProfiles.Current;
        var options = new List<string>();
        foreach (HardwareProfileData profile in HardwareProfiles.All)
        {
            string suffix = profile.Id == current.Id ? " (actuel)" : "";
            options.Add($"{profile.DisplayName}{suffix}\n{profile.Description}");
        }

        HardwareProfileData picked = null;
        bool closed = false;
        ChoicePopup.Show(overlay, "Changer de matériel",
            message ?? "Choisir un autre matériel de suivi que celui actif sur ce poste.",
            options, "Annuler",
            index =>
            {
                if (index >= 0 && index < HardwareProfiles.All.Count) picked = HardwareProfiles.All[index];
                closed = true;
            });

        while (!closed) yield return null;
        if (picked == null || picked.Id == current.Id) yield break; // Annulé, ou déjà actif.

        HardwareProfiles.Select(picked.Id);
        yield return ApplySteamVRConfigIfNeeded(overlay);
        onChanged?.Invoke(picked);
    }

    /// <summary>
    /// Corrige la configuration SteamVR pour le profil ACTIF (<see cref="HardwareProfiles.Current"/>)
    /// — SEULEMENT si elle ne lui correspond déjà pas. Réutilise <see cref="SteamVRConfigRepair"/>
    /// pour la fermeture/correction/relance (qui arrête et redémarre elle-même la session XR de
    /// GraffWall s'il y en a une) sans dupliquer cette logique, et sans montrer sa confirmation
    /// quand rien n'a besoin de changer.
    ///
    /// Appelé ici (premier lancement, aucun profil connu quand SteamVR a démarré) et par le
    /// changement de profil EN COURS DE PARTIE (<c>GlobalWorldUI.SwitchHardwareProfileRoutine</c>).
    /// </summary>
    public static IEnumerator ApplySteamVRConfigIfNeeded(VisualElement overlay)
    {
        HardwareProfileData profile = HardwareProfiles.Current;
        if (!SteamVRConfig.TryLocate(SteamVRBootstrap.VrMonitorHint, out SteamVRConfig.Paths paths, out string error))
        {
            Debug.LogWarning($"[HardwareProfilePicker] Configuration SteamVR non vérifiée : {error}");
            yield break;
        }

        List<SteamVRConfig.Difference> differences;
        try { differences = SteamVRConfig.Inspect(paths, profile.SteamVRMode); }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[HardwareProfilePicker] Configuration SteamVR illisible : {e.Message}");
            yield break;
        }
        if (differences.Count == 0) yield break; // Déjà bonne : pas de popup pour rien.

        var ctx = new RepairContext(overlay);
        yield return new SteamVRConfigRepair().Attempt(ctx);
        // Un échec reste visible ensuite via SteamVrConfigCheck (« État du système »), avec son
        // propre bouton « Résoudre » : rien à répéter ici.
    }
}
