using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.UIElements;

/// <summary>
/// Seul lien entre une réparation et l'écran. Chaque méthode est une coroutine qui rend la main quand
/// l'opérateur a répondu : une réparation s'écrit donc comme un déroulé, sans callbacks imbriqués.
/// </summary>
public sealed class RepairContext
{
    /// <summary>Réponse de <see cref="Choose"/> quand l'opérateur renonce.</summary>
    public const int Cancelled = -1;

    private readonly VisualElement _host;

    public RepairContext(VisualElement host) => _host = host;

    /// <summary>« 2/5 » dans un « Tout résoudre », null pour une réparation seule.</summary>
    public string Step { get; internal set; }

    internal readonly List<string> Failures = new List<string>();

    /// <summary>Signale un échec EXPLIQUÉ. Tous sont regroupés dans un bilan à la fin.</summary>
    public void Fail(string message)
    {
        if (!string.IsNullOrEmpty(message)) Failures.Add(message);
    }

    /// <summary>
    /// Propose des choix. <paramref name="options"/> : une ligne par choix, la première ligne est le
    /// libellé, les suivantes un détail (chemin, nombre de fichiers). Le premier choix est celui du
    /// bouton POUCE : on y met le plus probable. Rend <see cref="Cancelled"/> si l'opérateur renonce.
    /// </summary>
    public IEnumerator Choose(string title, string message, IList<string> options, Action<int> picked)
    {
        int result = int.MinValue;
        // Dans un « Tout résoudre », renoncer PASSE à la suivante : le libellé doit le dire.
        string cancel = Step != null ? "Passer" : "Annuler";
        ChoicePopup.Show(_host, Titled(title), message, options, cancel, i => result = i);

        while (result == int.MinValue)
        {
            // Fermée d'ailleurs (changement de scène, autre modale) : on renonce proprement.
            if (!ChoicePopup.IsOpen) { result = Cancelled; break; }
            yield return null;
        }

        picked?.Invoke(result);
    }

    /// <summary>Message sans décision à prendre, un seul bouton.</summary>
    public IEnumerator Inform(string title, string message)
    {
        bool done = false;
        ChoicePopup.Show(_host, title, message, null, "Compris", _ => done = true);
        while (!done && ChoicePopup.IsOpen) yield return null;
    }

    /// <summary>Attente visible (recherche, lancement). <paramref name="onStop"/> null = pas d'arrêt.</summary>
    public void Progress(string title, string message, Action onStop = null)
        => ChoicePopup.ShowProgress(_host, Titled(title), message, onStop != null ? "Arrêter" : null, onStop);

    public void EndProgress()
    {
        if (ChoicePopup.InProgress) ChoicePopup.Close();
    }

    /// <summary>
    /// Recherche sur disque, en arrière-plan, avec progression et bouton « Arrêter » — qui rend ce qui
    /// est déjà trouvé plutôt que rien.
    /// </summary>
    public IEnumerator Search(string title, IEnumerable<string> roots, FolderSearch.Query query,
                              Action<List<FolderSearch.Candidate>> done)
    {
        var stop = new CancellationTokenSource();
        var progress = new FolderSearch.Progress();
        Progress(title, "Recherche en cours…", () => stop.Cancel());

        Task<List<FolderSearch.Candidate>> search = FolderSearch.RunAsync(roots, query, stop.Token, progress);
        while (!search.IsCompleted)
        {
            ChoicePopup.SetProgressMessage($"Recherche en cours… {progress.Visited} dossiers parcourus");
            yield return null;
        }

        EndProgress();
        done?.Invoke(search.Status == TaskStatus.RanToCompletion
            ? search.Result
            : new List<FolderSearch.Candidate>());
    }

    /// <summary>Sélecteur de dossier WINDOWS. Rend null si l'opérateur annule ou si rien n'est possible.</summary>
    public IEnumerator PickFolder(string title, string startFolder, Action<string> picked)
    {
        yield return Await(WindowsDialogs.PickFolderAsync(title, startFolder), title, picked);
    }

    /// <summary>Sélecteur d'exécutable WINDOWS (filtre .exe).</summary>
    public IEnumerator PickExecutable(string title, string startFolder, Action<string> picked)
    {
        yield return Await(WindowsDialogs.PickExecutableAsync(title, startFolder), title, picked);
    }

    private IEnumerator Await(Task<string> dialog, string title, Action<string> picked)
    {
        // Le jeu continue de tourner pendant que la fenêtre Windows est ouverte : on le DIT, sinon
        // une fenêtre passée derrière laisse croire que le bouton n'a rien fait.
        Progress(title, "Choisissez dans la fenêtre Windows qui vient de s'ouvrir.");
        while (!dialog.IsCompleted) yield return null;
        EndProgress();

        if (dialog.IsFaulted)
        {
            Fail("La fenêtre de sélection Windows n'a pas pu s'ouvrir : " +
                 dialog.Exception?.GetBaseException().Message);
            picked?.Invoke(null);
            yield break;
        }

        picked?.Invoke(dialog.Result);
    }

    private string Titled(string title) => Step == null ? title : $"{title}   ·   {Step}";
}
