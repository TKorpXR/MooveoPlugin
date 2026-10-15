using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Mise en sourdine d'un problème connu : « Ignorer » ne ferme plus seulement une carte, il retient
/// pour une durée — cette session, 12 h, 24 h ou une semaine. Clé = <see cref="CheckReport.Key"/>.
///
/// « Cette session » vit en mémoire ; les durées dans les PlayerPrefs (échéance UTC), pour survivre
/// au redémarrage — c'est tout leur intérêt sur un poste qu'on relance chaque matin.
///
/// ⚠ Un problème en sourdine NE DISPARAÎT PAS : il reste listé dans « État du système », grisé,
/// avec son échéance et un bouton « Réactiver ». Une semaine de silence sur une imprimante en panne
/// se découvrirait sinon sur le papier du client — même raison qui fait que « Continuer malgré tout »
/// de la popup HotFolder ne mémorise rien (CLAUDE.md §8).
/// </summary>
public static class CheckSnooze
{
    private const string PrefPrefix = "snooze:";

    /// <summary>Durées proposées, dans l'ordre d'affichage. Null = cette session.</summary>
    public static readonly IReadOnlyList<KeyValuePair<string, TimeSpan?>> Choices = new[]
    {
        new KeyValuePair<string, TimeSpan?>("Pour cette session", null),
        new KeyValuePair<string, TimeSpan?>("Pendant 12 heures", TimeSpan.FromHours(12)),
        new KeyValuePair<string, TimeSpan?>("Pendant 24 heures", TimeSpan.FromHours(24)),
        new KeyValuePair<string, TimeSpan?>("Pendant une semaine", TimeSpan.FromDays(7))
    };

    private static readonly HashSet<string> _session = new HashSet<string>();

    /// <summary>Un problème vient d'être mis en sourdine ou réactivé : icône et panneau se redessinent.</summary>
    public static event Action Changed;

    public static bool IsSnoozed(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        return _session.Contains(key) || ExpiryOf(key).HasValue;
    }

    /// <summary>Applique le choix n° <paramref name="choice"/> de <see cref="Choices"/> à ces clés.</summary>
    public static void Snooze(IEnumerable<string> keys, int choice)
    {
        if (keys == null || choice < 0 || choice >= Choices.Count) return;
        TimeSpan? span = Choices[choice].Value;

        foreach (string key in keys)
        {
            if (string.IsNullOrEmpty(key)) continue;
            if (span == null) _session.Add(key);
            else PlayerPrefs.SetString(PrefPrefix + key,
                                       DateTime.UtcNow.Add(span.Value).Ticks.ToString(CultureInfo.InvariantCulture));
        }

        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    public static void Clear(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        _session.Remove(key);
        PlayerPrefs.DeleteKey(PrefPrefix + key);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    /// <summary>« ignoré pour cette session » / « ignoré jusqu'au 18/09 à 14:30 ».</summary>
    public static string Describe(string key)
    {
        DateTime? until = ExpiryOf(key);
        if (until.HasValue)
            return $"ignoré jusqu'au {until.Value.ToLocalTime():dd/MM} à {until.Value.ToLocalTime():HH:mm}";
        return _session.Contains(key) ? "ignoré pour cette session" : null;
    }

    /// <summary>Échéance UTC encore à venir, sinon null. Une échéance passée est effacée au passage.</summary>
    private static DateTime? ExpiryOf(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        string raw = PlayerPrefs.GetString(PrefPrefix + key, null);
        if (string.IsNullOrEmpty(raw)) return null;

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks) &&
            ticks > DateTime.UtcNow.Ticks)
            return new DateTime(ticks, DateTimeKind.Utc);

        PlayerPrefs.DeleteKey(PrefPrefix + key);
        return null;
    }
}
