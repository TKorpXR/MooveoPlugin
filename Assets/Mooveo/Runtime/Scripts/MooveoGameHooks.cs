using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Points d'extension du SDK Mooveo vers le JEU qui l'utilise.
///
/// Le système (vérifications, réparations, notifications, popups, pointage UI) ne connaît aucune
/// classe de jeu : tout ce qu'il demandait autrefois directement à GraffWall passe par ici. Équivalent
/// GraffWall entre parenthèses — c'est ce que GraffWall branche s'il consomme le SDK.
///
/// ⚠ Un jeu n'a RIEN à brancher pour que calibration et tracking fonctionnent. Sans branchement,
/// chaque point se comporte comme GraffWall en scène de calibration, où aucun objet de jeu n'existe :
/// les demandes de guide, de réparation hors écran de calibration et de toast sont ignorées, aucun
/// curseur n'est redessiné sur les notifications, aucun panneau joueur n'est fermé.
///
/// Brancher au plus tôt (Awake d'un manager de jeu), et débrancher à sa destruction : ces champs
/// sont statiques, ils survivent aux changements de scène.
///
/// Vérifications de logiciels tiers (SteamVR, HotFolderPrint, EOS Utility) : opt-in, cf.
/// <see cref="MooveoSoftwareChecks"/>.
/// </summary>
public static class MooveoGameHooks
{
    // =========================================================================
    // Hôte des popups (guides, réparations, toasts)
    // =========================================================================

    /// <summary>
    /// Racine où poser les popups demandées depuis le système hors d'un écran qui a son propre hôte :
    /// « Voir la procédure » d'une notification ou de l'« État du système », « Résoudre » d'une
    /// carte de notification (GraffWall : <c>GlobalWorldUI.PopupRoot</c>). Null = demandes ignorées.
    /// </summary>
    public static Func<VisualElement> PopupHost;

    /// <summary>
    /// Appelé juste AVANT d'afficher une popup sur <see cref="PopupHost"/> (GraffWall :
    /// <c>GlobalWorldUI.ClearWayForPopup</c>). ⚠ Une modale posée dans un UIDocument world-space ne
    /// passe pas devant un autre document plus proche de la caméra : c'est ici que le jeu ferme les
    /// menus de ses joueurs (<see cref="WorldPanelUtils.CloseAllUserMenus"/>).
    /// </summary>
    public static Action BeforePopup;

    /// <summary>
    /// Appelé juste APRÈS avoir affiché une popup sur l'hôte (GraffWall : recalcul différé du
    /// collider de la GlobalWorldUI, <c>UpdateColliderFromUI</c>).
    /// </summary>
    public static Action<VisualElement> AfterPopup;

    /// <summary>Fermeture de toute popup ouverte par ces points (GraffWall : <c>OnPopupClosed</c>).</summary>
    public static Action PopupClosed;

    /// <summary>
    /// Racine des toasts locaux (GraffWall : <c>GlobalWorldUI.ToastRoot</c>). Null = aucun toast.
    /// <see cref="UIToast"/> redirige de toute façon vers le centre de notifications dès qu'un hôte
    /// global existe.
    /// </summary>
    public static Func<VisualElement> ToastHost;

    /// <summary>Ouvre le guide illustré <paramref name="guideId"/> sur l'hôte (GraffWall : <c>UIManager.ShowGuidePopup</c>).</summary>
    public static bool ShowGuide(string guideId, string message = null, Func<string> onConfirm = null)
    {
        VisualElement host = PopupHost?.Invoke();
        if (host == null) return false;

        BeforePopup?.Invoke();
        GuidePopup.Show(host, GuidePopupData.Find(guideId), message, PopupClosed, onConfirm);
        AfterPopup?.Invoke(host);
        return true;
    }

    /// <summary>Lance les réparations d'une vérification sur l'hôte (GraffWall : <c>UIManager.ResolveCheck</c>).</summary>
    public static bool ResolveCheck(CheckReport report)
    {
        VisualElement host = PopupHost?.Invoke();
        if (host == null || report == null) return false;

        BeforePopup?.Invoke();
        if (!RepairRunner.Resolve(new[] { report }, host)) return false;

        AfterPopup?.Invoke(host);
        return true;
    }

    /// <summary>Toast sur l'hôte des toasts (GraffWall : <c>UIManager.ShowToast</c>).</summary>
    public static void ShowToast(string title, string message, UIToast.Kind kind, float durationMs = 4000f)
    {
        VisualElement host = ToastHost?.Invoke();
        if (host == null) return;
        UIToast.Show(host, title, message, kind, durationMs);
    }

    // =========================================================================
    // Curseurs redessinés sur les notifications
    // =========================================================================

    /// <summary>Une image d'un curseur, relevée à l'écran (origine en bas à gauche).</summary>
    public struct ScreenCursorLayer
    {
        public Sprite Sprite;
        public Color Color;
        public Rect ScreenRect;
    }

    /// <summary>
    /// Curseur d'un joueur vivant sur un canvas WORLD-SPACE (GraffWall : <c>SprayCursorUI</c>).
    /// ⚠ Un panneau d'écran en overlay est dessiné après toute la scène 3D : aucun ordre de tri ne
    /// fait passer un tel curseur devant une notification. <see cref="NotificationHost"/> le redessine
    /// donc dans son propre panneau, image par image, à partir de ce relevé.
    /// </summary>
    public interface IScreenCursor
    {
        bool isActiveAndEnabled { get; }

        /// <summary>Vide <paramref name="into"/>, y ajoute les images visibles dans l'ordre de dessin, rend leur nombre.</summary>
        int CollectScreenLayers(Camera cam, List<ScreenCursorLayer> into);
    }

    /// <summary>Curseurs actifs (GraffWall : <c>UIManager.Cursors</c>). Le jeu ajoute et retire les siens.</summary>
    public static readonly List<IScreenCursor> ScreenCursors = new List<IScreenCursor>();

    // =========================================================================
    // Panneaux de joueur (menus world-space appartenant à un joueur)
    // =========================================================================

    /// <summary>
    /// Panneau world-space appartenant à un joueur (GraffWall : <c>UserUI</c>, le MainLayout). Le
    /// pont souris lui indique QUI interagit, et les popups globales le referment.
    /// </summary>
    public interface IPlayerOwnedPanel
    {
        int OwnerID { get; }
        bool IsOpen { get; }
        void PushInteractingPlayer(int playerID);
        void ForceClose();
    }

    /// <summary>
    /// Joueur que pilote la souris en mode Admin, -1 = aucun (GraffWall : <c>AdminMouseSpray.MousePlayerID</c>).
    /// Sans valeur, un clic souris sur un panneau de joueur est attribué à son propriétaire.
    /// </summary>
    public static Func<int> MousePlayerID;

    // =========================================================================
    // Modales externes
    // =========================================================================

    /// <summary>
    /// Modale propre au jeu qui doit recevoir poignée (fermer) et pouce (confirmer) quand elle est au
    /// premier plan (GraffWall : appairage Remote, <c>PairingManager</c>). Consultée par
    /// <see cref="ModalPopups"/> après <see cref="ChoicePopup"/> et avant <see cref="GuidePopup"/>.
    /// </summary>
    public sealed class ExternalModal
    {
        public Func<bool> IsOpen;
        public Action Dismiss;
        public Action Confirm;
    }

    public static readonly List<ExternalModal> ExternalModals = new List<ExternalModal>();

    // =========================================================================
    // Scène de jeu
    // =========================================================================

    /// <summary>
    /// Surface de jeu générée après calibration (GraffWall : <c>GameManager.WallTransform</c>), lue par
    /// la vérification « Mur et caméra ». Null = le jeu ne déclare pas de surface, la vérification ne
    /// contrôle alors que la caméra.
    /// </summary>
    public static Func<Transform> WallTransform;

    // =========================================================================
    // Identité de l'application
    // =========================================================================

    private static string _appName;

    /// <summary>
    /// Nom de l'application dans les messages à l'opérateur (« relancez … ») : le
    /// <c>productName</c> du jeu. ⚠ Mis en cache au démarrage : <c>Application.productName</c> ne
    /// s'appelle que depuis le thread principal, or la détection HotFolder et le journal d'erreurs
    /// tournent en partie hors de lui.
    /// </summary>
    public static string AppName
    {
        get
        {
            if (_appName == null)
            {
                try { _appName = Application.productName; }
                catch (UnityException) { return "l'application"; }
                if (string.IsNullOrEmpty(_appName)) _appName = "l'application";
            }
            return _appName;
        }
    }

    /// <summary>Nom de l'application utilisable dans un nom de fichier (journaux, gabarits de bordure).</summary>
    public static string AppFileName
    {
        get
        {
            string name = AppName == "l'application" ? "Mooveo" : AppName;
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Replace(' ', '_');
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void CacheAppName()
    {
        _appName = null;
        _ = AppName;
    }

    // =========================================================================
    // Utilitaire
    // =========================================================================

    /// <summary>
    /// ⚠ Un objet Unity détruit n'est pas <c>null</c> vu à travers une interface (l'opérateur == de
    /// UnityEngine.Object n'est pas appelé) : on teste donc la vie de l'objet sous-jacent.
    /// </summary>
    public static bool IsAlive(object target)
    {
        if (target == null) return false;
        if (target is UnityEngine.Object unityObject) return unityObject != null;
        return true;
    }
}
