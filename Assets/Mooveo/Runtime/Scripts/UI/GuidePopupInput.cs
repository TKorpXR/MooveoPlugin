using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Écoute les manettes POUR LE COMPTE de <see cref="GuidePopup"/>, tant qu'une popup est ouverte.
///
/// ⚠ Pourquoi en plus de l'interception dans <c>DefaultController</c> : celle-ci ne fonctionne que
/// si un contrôleur est bien présent, actif et lié au moment de l'affichage. Une popup peut
/// apparaître AVANT que les testeurs de calibration soient apparus, ou pendant qu'un peintre est en
/// attente — et l'opérateur se retrouve alors devant une fenêtre qui ne répond à rien. On s'abonne
/// donc DIRECTEMENT à tous les <see cref="InputReader"/> de la scène, et on rebalaie régulièrement
/// pour attraper ceux qui arrivent après.
///
/// Les deux chemins peuvent se déclencher ensemble : fermer une popup déjà fermée ne fait rien.
/// </summary>
[DefaultExecutionOrder(-50)]
public class GuidePopupInput : MonoBehaviour
{
    private const float RescanSeconds = 1f;

    /// <summary>
    /// Trace des entrées reçues pendant qu'une popup est ouverte. À rallumer si le câblage manette
    /// ne répond pas sur un poste : c'est le seul moyen de distinguer « aucun InputReader présent »
    /// de « événement jamais émis ».
    /// </summary>
    public static bool Verbose = false;

    private static GuidePopupInput _instance;

    private readonly System.Collections.Generic.List<InputReader> _bound =
        new System.Collections.Generic.List<InputReader>();

    private float _nextScan;

    /// <summary>Crée l'écouteur au premier affichage. Sans coût tant qu'aucune popup ne s'ouvre.</summary>
    public static void Ensure()
    {
        if (_instance != null) return;

        GameObject go = new GameObject("GuidePopupInput") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<GuidePopupInput>();
    }

    private void Update()
    {
        if (!ModalPopups.IsOpen)
        {
            if (_bound.Count > 0) Unbind();
            return;
        }

        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + RescanSeconds;
            Rebind();
        }

        HandleKeyboard();
    }

    private void OnDisable() => Unbind();

    // =========================================================================
    // Manettes
    // =========================================================================

    private void Rebind()
    {
        InputReader[] readers = FindObjectsByType<InputReader>(FindObjectsSortMode.None);
        if (readers.Length == _bound.Count) return;

        Unbind();
        foreach (InputReader reader in readers)
        {
            if (reader == null) continue;
            reader.AButtonPressed += OnDismissKey;
            reader.BButtonPressed += OnDismissKey;
            reader.ThumbButtonPressed += OnConfirmKey;
            reader.TriggerPressed += OnTriggerTrace;
            _bound.Add(reader);
        }

        if (Verbose)
        {
            Debug.Log($"[GuidePopupInput] {_bound.Count} InputReader écouté(s) pendant la popup.");
            foreach (InputReader reader in _bound)
                Debug.Log($"[GuidePopupInput]   • {reader.name} — mode {reader.ConfigMode}, " +
                          $"SimulateVR {reader.SimulateVR}, rôle {reader.TargetRole}");
        }
    }

    private void Unbind()
    {
        foreach (InputReader reader in _bound)
        {
            if (reader == null) continue;
            reader.AButtonPressed -= OnDismissKey;
            reader.BButtonPressed -= OnDismissKey;
            reader.ThumbButtonPressed -= OnConfirmKey;
            reader.TriggerPressed -= OnTriggerTrace;
        }
        _bound.Clear();
    }

    private static void OnDismissKey()
    {
        if (Verbose) Debug.Log("[GuidePopupInput] poignée reçue.");
        ModalPopups.Dismiss();
    }

    private static void OnConfirmKey()
    {
        if (Verbose) Debug.Log("[GuidePopupInput] pouce reçu.");
        ModalPopups.Confirm();
    }

    /// <summary>Témoin seul : la gâchette n'agit pas sur la popup, elle sert à cliquer dedans.</summary>
    private static void OnTriggerTrace()
    {
        if (Verbose) Debug.Log("[GuidePopupInput] gâchette reçue (aucune action sur la popup).");
    }

    // =========================================================================
    // Clavier — repli d'éditeur et secours si aucune manette ne répond
    // =========================================================================

    private void HandleKeyboard()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.escapeKey.wasPressedThisFrame) ModalPopups.Dismiss();
        else if (keyboard.enterKey.wasPressedThisFrame &&
                 !keyboard.leftAltKey.isPressed && !keyboard.rightAltKey.isPressed) ModalPopups.Confirm();
        else if (keyboard.leftArrowKey.wasPressedThisFrame) GuidePopup.PreviousPage();
        else if (keyboard.rightArrowKey.wasPressedThisFrame) GuidePopup.NextPage();
    }
}
