using System;
using System.Collections.Generic;
using UnityEngine;

public enum NotificationLevel { Info, Warning, Error }

/// <summary>Une notification affichée à l'opérateur. Immuable une fois émise, sauf son état lu/fermé.</summary>
public sealed class Notification
{
    public NotificationLevel Level;
    public string Title;
    public string Message;

    /// <summary>Bouton d'action optionnel (« Corriger », « Voir la procédure »…).</summary>
    public string ActionLabel;
    public Action Action;

    /// <summary>Durée d'affichage. 0 ou moins = reste jusqu'à fermeture explicite.</summary>
    public float DurationMs;

    public DateTime Time;
    public bool Dismissed;

    /// <summary>Clé de dédoublonnage : une même panne ne doit pas empiler dix cartes identiques.</summary>
    public string Key;

    /// <summary>
    /// Bulle discrète : une ligne, titre seul. Pour un simple ÉTAT (joueur déconnecté, suivi perdu), pas
    /// pour un diagnostic : une carte compacte n'a ni message ni bouton, et ne compte pas dans la limite
    /// des cartes visibles — sinon quatre joueurs chasseraient une carte « Résoudre » de l'écran.
    /// </summary>
    public bool Compact;

    public bool Persistent => DurationMs <= 0f;
}

/// <summary>
/// Point d'entrée UNIQUE des notifications. Volontairement statique et sans dépendance à la scène :
/// on doit pouvoir signaler une panne depuis n'importe où, y compris avant qu'une interface existe.
///
/// ⚠ C'est tout l'intérêt par rapport à <see cref="UIToast"/>, qui exige qu'on lui passe la racine
/// d'un panneau : rien ne pouvait s'afficher tant qu'aucun menu n'était ouvert — précisément le
/// moment où les pannes de démarrage se produisent. Ici, ce qui est émis trop tôt est MIS EN
/// ATTENTE et déversé dès que l'hôte apparaît.
/// </summary>
public static class NotificationCenter
{
    public const float InfoDurationMs = 4500f;
    public const float WarningDurationMs = 9000f;
    // Retour à la normale d'un joueur : une bulle brève suffit.
    public const float CompactDurationMs = 2500f;
    // Une erreur ne disparaît jamais seule : l'opérateur doit l'avoir vue.
    public const float ErrorDurationMs = 0f;

    private const int HistoryMax = 60;

    private static readonly List<Notification> _pending = new List<Notification>();
    private static readonly List<Notification> _history = new List<Notification>();
    private static bool _hostReady;
    private static bool _gameStarted;

    /// <summary>
    /// Rien ne s'affiche tant qu'aucun mode de jeu n'est lancé. L'écran de sélection est le premier
    /// contact avec l'application : l'y couvrir de diagnostics techniques donne l'impression que
    /// tout est cassé avant même d'avoir commencé. Les notifications sont MISES EN ATTENTE, pas
    /// perdues, et arrivent au démarrage du mode.
    /// </summary>
    public static void SetGameStarted(bool started)
    {
        if (_gameStarted == started) return;
        _gameStarted = started;
        if (started) Flush();
    }

    /// <summary>Émise quand une notification doit être AFFICHÉE (jamais avant que l'hôte soit prêt).</summary>
    public static event Action<Notification> Posted;

    /// <summary>Émise quand une notification est fermée, par l'opérateur ou par expiration.</summary>
    public static event Action<Notification> Closed;

    /// <summary>Historique complet de la session, du plus ancien au plus récent.</summary>
    public static IReadOnlyList<Notification> History => _history;

    // =========================================================================
    // Émission
    // =========================================================================

    public static Notification Info(string title, string message, string actionLabel = null, Action action = null)
        => Notify(NotificationLevel.Info, title, message, actionLabel, action);

    public static Notification Warning(string title, string message, string actionLabel = null, Action action = null)
        => Notify(NotificationLevel.Warning, title, message, actionLabel, action);

    public static Notification Error(string title, string message, string actionLabel = null, Action action = null)
        => Notify(NotificationLevel.Error, title, message, actionLabel, action);

    /// <param name="immediate">
    /// Événement EN DIRECT (bombe déconnectée, suivi perdu) : il passe la retenue du lancement de
    /// partie (<see cref="SetGameStarted"/>), pensée pour les diagnostics de démarrage. Retenu, il
    /// arrivait en bloc bien plus tard, décrivant un état qui n'existait plus.
    /// </param>
    public static Notification Notify(NotificationLevel level, string title, string message,
                                      string actionLabel = null, Action action = null,
                                      float durationMs = float.NaN, string key = null,
                                      bool immediate = false, bool compact = false)
    {
        Notification notification = new Notification
        {
            Level = level,
            Title = title ?? "",
            Message = message ?? "",
            ActionLabel = actionLabel,
            Action = action,
            DurationMs = float.IsNaN(durationMs) ? DefaultDuration(level) : durationMs,
            Time = DateTime.Now,
            Key = key,
            Compact = compact
        };

        // Dédoublonnage : une vérification qui se relance ne doit pas empiler la même carte.
        if (!string.IsNullOrEmpty(key)) DismissByKey(key);

        _history.Add(notification);
        if (_history.Count > HistoryMax) _history.RemoveAt(0);

        // Toujours dans le journal : l'historique à l'écran est volatil, le log survit à la session.
        LogToConsole(notification);

        if (_hostReady && (_gameStarted || immediate)) Posted?.Invoke(notification);
        else _pending.Add(notification);

        return notification;
    }

    private static float DefaultDuration(NotificationLevel level)
    {
        switch (level)
        {
            case NotificationLevel.Error: return ErrorDurationMs;
            case NotificationLevel.Warning: return WarningDurationMs;
            default: return InfoDurationMs;
        }
    }

    private static void LogToConsole(Notification n)
    {
        string line = $"[Notification] {n.Title} — {n.Message}";
        switch (n.Level)
        {
            case NotificationLevel.Error: Debug.LogError(line); break;
            case NotificationLevel.Warning: Debug.LogWarning(line); break;
            default: Debug.Log(line); break;
        }
    }

    // =========================================================================
    // Fermeture
    // =========================================================================

    public static void Dismiss(Notification notification)
    {
        if (notification == null || notification.Dismissed) return;
        notification.Dismissed = true;
        _pending.Remove(notification);
        Closed?.Invoke(notification);
    }

    /// <summary>Ferme toutes les notifications portant cette clé (relance d'une vérification).</summary>
    public static void DismissByKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return;

        // Copie : Dismiss modifie les listes par l'intermédiaire de ses abonnés.
        foreach (Notification n in _history.ToArray())
            if (!n.Dismissed && n.Key == key) Dismiss(n);
    }

    // =========================================================================
    // Hôte
    // =========================================================================

    /// <summary>
    /// L'hôte d'affichage s'annonce. En devenant prêt, il reçoit tout ce qui a été émis avant lui —
    /// sinon les vérifications de démarrage parleraient dans le vide.
    /// </summary>
    public static void SetHostReady(bool ready)
    {
        _hostReady = ready;
        if (ready) Flush();
    }

    private static void Flush()
    {
        if (!_hostReady || !_gameStarted) return;

        Notification[] queued = _pending.ToArray();
        _pending.Clear();
        foreach (Notification n in queued)
            if (!n.Dismissed) Posted?.Invoke(n);
    }
}
