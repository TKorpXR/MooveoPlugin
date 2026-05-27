using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Gère les raccourcis dynamiques liés aux événements de l'InputReader.
/// Les raccourcis sont assignés depuis l'UI via TryAssignShortcut().
/// </summary>
public class ControllerShortcutHandler : MonoBehaviour
{
    [Serializable]
    public class ShortcutEntry
    {
        public string Name;
        public ControllerButton Button1;
        public ControllerButton Button2;
        public UnityEngine.Events.UnityEvent OnTrigger;
    }

    [SerializeField]
    private List<ShortcutEntry> _shortcuts = new List<ShortcutEntry>();


    /// <summary>Raccourcis assignés dynamiquement : MethodInfo → [eventName1, eventName2]</summary>
    private Dictionary<MethodInfo, List<string>> _dynamicShortcuts = new Dictionary<MethodInfo, List<string>>();

    /// <summary>État des inputs par frame </summary>
    private readonly Dictionary<string, bool> _eventsTriggeredThisFrame = new Dictionary<string, bool>
    {
        { "TriggerPressed",    false },
        { "AButtonPressed",    false },
        { "BButtonPressed",    false },
        { "ThumbButtonPressed",false }
    };
    
    //private PainterController _painterController;
    //private UserUI _userUI;

    private InputReader _reader;
    private readonly Dictionary<ControllerButton, bool> _buttonStates = new Dictionary<ControllerButton, bool>();
    private readonly Dictionary<ControllerButton, bool> _buttonDown   = new Dictionary<ControllerButton, bool>();
    private bool _initialized = false;

    /// <summary>
    /// Initialise le handler. Doit être appelé après que PainterController et UserUI sont prêts.
    /// </summary>
    public void Init(InputReader reader)
    {
        UnbindAll();
        _reader = reader;

        foreach (ControllerButton btn in Enum.GetValues(typeof(ControllerButton)))
        {
            _buttonStates[btn] = false;
            _buttonDown[btn]   = false;
        }

        BindLegacy();

        BindDynamic();

        //On ne récupère PAS _userUI ici car Init() est appelé depuis OnEnable() pendant Instantiate(), AVANT que InitializePainter() ait ajouté le curseur dans GameManager._cursors. La résolution est faite en lazy dans InvokeShortcutMethod().
        //_painterController = GetComponent<PainterController>();
        //_userUI = null;

        _initialized = true;
    }

    private void BindLegacy()
    {
        if (_reader == null) return;
        _reader.TriggerPressed   += OnTriggerPressed;
        _reader.TriggerReleased  += OnTriggerReleased;
        _reader.AButtonPressed   += OnAPressed;
        _reader.AButtonReleased  += OnAReleased;
        _reader.BButtonPressed   += OnBPressed;
        _reader.BButtonReleased  += OnBReleased;
        _reader.ThumbButtonPressed += OnThumbPressed;
    }

    private void UnbindLegacy()
    {
        if (_reader == null) return;
        _reader.TriggerPressed   -= OnTriggerPressed;
        _reader.TriggerReleased  -= OnTriggerReleased;
        _reader.AButtonPressed   -= OnAPressed;
        _reader.AButtonReleased  -= OnAReleased;
        _reader.BButtonPressed   -= OnBPressed;
        _reader.BButtonReleased  -= OnBReleased;
        _reader.ThumbButtonPressed -= OnThumbPressed;
    }

    /// <summary>
    /// Abonnements explicites aux 4 événements "Pressed" de l'InputReader.
    /// </summary>
    private void BindDynamic()
    {
        if (_reader == null) return;
        _reader.TriggerPressed     += OnTriggerShortcut;
        _reader.AButtonPressed     += OnAButtonShortcut;
        _reader.BButtonPressed     += OnBButtonShortcut;
        _reader.ThumbButtonPressed += OnThumbShortcut;
    }

    private void UnbindDynamic()
    {
        if (_reader == null) return;
        _reader.TriggerPressed     -= OnTriggerShortcut;
        _reader.AButtonPressed     -= OnAButtonShortcut;
        _reader.BButtonPressed     -= OnBButtonShortcut;
        _reader.ThumbButtonPressed -= OnThumbShortcut;
    }

    private void OnTriggerShortcut()  => _eventsTriggeredThisFrame["TriggerPressed"]     = true;
    private void OnAButtonShortcut()  => _eventsTriggeredThisFrame["AButtonPressed"]     = true;
    private void OnBButtonShortcut()  => _eventsTriggeredThisFrame["BButtonPressed"]     = true;
    private void OnThumbShortcut()    => _eventsTriggeredThisFrame["ThumbButtonPressed"] = true;

    private void UnbindAll()
    {
        UnbindLegacy();
        UnbindDynamic();
    }

    /// <summary>
    /// Tente d'assigner un raccourci à une méthode.
    /// </summary>
    /// <param name="method">Méthode [ShortCut] à appeler</param>
    /// <param name="event1">premier événement InputReader (ou "None")</param>
    /// <param name="event2">deuxième événement InputReader (ou "None")</param>
    /// <returns>True si l'assignation a réussi, false sinon.</returns>
    public bool TryAssignShortcut(MethodInfo method, string event1, string event2)
    {
        const string none = "None";

        // Règle 1 : None + None → désactivation valide, on enregistre une liste vide
        if (event1 == none && event2 == none)
        {
            _dynamicShortcuts[method] = new List<string>();
            Debug.Log($"[ShortcutHandler] Raccourci désactivé : {method.Name}");
            return true;
        }

        // Règle 2 : Touche unique interdite (l'un des deux est "None" mais pas l'autre)
        if (event1 == none || event2 == none)
        {
            Debug.LogWarning($"[ShortcutHandler] Refus : raccourci à une seule touche interdit ({method.Name}).");
            return false;
        }

        // Règle 3 : Même touche deux fois interdite
        if (event1 == event2)
        {
            Debug.LogWarning($"[ShortcutHandler] Refus : les deux touches sont identiques ({event1}).");
            return false;
        }

        var newCombo = new List<string> { event1, event2 };

        // Règle 4 : Doublon — une autre méthode utilise déjà cette combinaison
        foreach (var kvp in _dynamicShortcuts)
        {
            if (kvp.Key == method) continue;
            if (CombosAreEqual(kvp.Value, newCombo))
            {
                Debug.LogWarning($"[ShortcutHandler] Refus doublon : {kvp.Key.Name} utilise déjà [{string.Join("+", newCombo)}].");
                return false;
            }
        }

        _dynamicShortcuts[method] = newCombo;
        Debug.Log($"[ShortcutHandler] Raccourci assigné : {method.Name} → [{string.Join("+", newCombo)}]");
        return true;
    }

    /// <summary>
    /// Retourne la combinaison actuellement assignée à une méthode, ou une liste vide si aucune.
    /// </summary>
    public List<string> GetAssignedCombo(MethodInfo method)
    {
        return _dynamicShortcuts.TryGetValue(method, out var combo) ? combo : new List<string>();
    }

    /// <summary>
    /// Retourne tous les raccourcis dynamiques assignés (méthode + combo).
    /// Utilisé par le debugger pour construire les boutons de test.
    /// </summary>
    public IReadOnlyDictionary<MethodInfo, List<string>> GetAllAssignedShortcuts()
        => _dynamicShortcuts;

    /// <summary>
    /// Simule le déclenchement simultané de deux événements et évalue immédiatement
    /// les raccourcis correspondants. Conçu uniquement pour les tests via le debugger.
    /// </summary>
    /// <param name="event1">premier événement</param>
    /// <param name="event2">deuxième événement</param>
    public void SimulateCombo(string event1, string event2)
    {
        if (!_initialized)
        {
            Debug.LogWarning("[ShortcutHandler] SimulateCombo appelé avant Init().");
            return;
        }

        if (!string.IsNullOrEmpty(event1) && _eventsTriggeredThisFrame.ContainsKey(event1))
            _eventsTriggeredThisFrame[event1] = true;

        if (!string.IsNullOrEmpty(event2) && _eventsTriggeredThisFrame.ContainsKey(event2))
            _eventsTriggeredThisFrame[event2] = true;

        foreach (var kvp in _dynamicShortcuts)
        {
            List<string> combo = kvp.Value;
            if (combo == null || combo.Count == 0) continue;

            bool allTriggered = combo.All(e =>
                _eventsTriggeredThisFrame.TryGetValue(e, out bool triggered) && triggered);

            if (allTriggered)
            {
                Debug.Log($"[ShortcutHandler] 🎮 SimulateCombo → déclenche {kvp.Key.Name}");
                InvokeShortcutMethod(kvp.Key);
            }
        }

        if (_eventsTriggeredThisFrame.ContainsKey(event1)) _eventsTriggeredThisFrame[event1] = false;
        if (_eventsTriggeredThisFrame.ContainsKey(event2)) _eventsTriggeredThisFrame[event2] = false;
    }

    private static bool CombosAreEqual(List<string> a, List<string> b)
    {
        if (a.Count != b.Count) return false;
        var setA = new HashSet<string>(a);
        var setB = new HashSet<string>(b);
        return setA.SetEquals(setB);
    }

    private void Update()
    {
        if (!_initialized) return;
        
        foreach (var shortcut in _shortcuts)
        {
            if (CheckShortcut(shortcut))
                shortcut.OnTrigger?.Invoke();
        }

        if (KeyBindingManager.Instance != null)
        {
            foreach (var binding in KeyBindingManager.Instance.Bindings)
            {
                if (binding.ControllerCombo != null && binding.ControllerCombo.Count > 0)
                {
                    if (CheckControllerCombo(binding.ControllerCombo))
                    {
                        KeyBindingManager.Instance.ExecuteBinding(binding);
                        break;
                    }
                }
            }
        }
        
        foreach (var kvp in _dynamicShortcuts)
        {
            List<string> combo = kvp.Value;
            if (combo == null || combo.Count == 0) continue;

            bool allTriggered = true;
            foreach (string evtName in combo)
            {
                if (!_eventsTriggeredThisFrame.TryGetValue(evtName, out bool triggered) || !triggered)
                {
                    allTriggered = false;
                    break;
                }
            }

            if (allTriggered)
            {
                InvokeShortcutMethod(kvp.Key);
            }
        }
        
        ClearFrameData();
    }

    /// <summary>
    /// Invoque la méthode shortcut sur la bonne cible (PainterController ou UserUI).
    /// Le UserUI est résolu en lazy car il n'est pas disponible au moment du Init().
    /// </summary>
    private void InvokeShortcutMethod(MethodInfo method)
    {
        if (method == null) return;

        // Résolution lazy du UserUI (non disponible au moment du Init())
        //if (_userUI == null) _userUI = ResolveUserUI();

        try
        {
            Type declaringType = method.DeclaringType;
            object target = null;

            /*if (_painterController != null && declaringType != null &&
                (declaringType == typeof(PainterController) || declaringType.IsAssignableFrom(typeof(PainterController))))
            {
                target = _painterController;
            }*/
            /*else if (_userUI != null && declaringType != null &&
                     (declaringType == typeof(UserUI) || declaringType.IsAssignableFrom(typeof(UserUI))))
            {
                target = _userUI;
            }*/

            if (target != null)
            {
                method.Invoke(target, null);
            }
            else
            {
                Debug.LogWarning($"[ShortcutHandler] Impossible de trouver la cible pour {method.Name} (declaring type: {method.DeclaringType?.Name})");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[ShortcutHandler] Erreur lors de l'invocation de {method.Name}: {e.Message}");
        }
    }

    /// <summary>
    /// Résolution lazy du UserUI associé à ce PainterController.
    /// </summary>
    /*private UserUI ResolveUserUI()
    {
        if (_painterController == null || GameManager.instance == null) return null;
        try
        {
            SprayCursorUI cursor = GameManager.instance.GetAssociatedCursor(_painterController);
            return cursor?.UserUI;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }*/
    

    private bool CheckShortcut(ShortcutEntry entry)
    {
        bool b1Active = entry.Button1 == ControllerButton.None || _buttonStates[entry.Button1];
        bool b2Active = entry.Button2 == ControllerButton.None || _buttonStates[entry.Button2];
        if (!b1Active || !b2Active) return false;

        bool b1JustPressed = entry.Button1 != ControllerButton.None && _buttonDown[entry.Button1];
        bool b2JustPressed = entry.Button2 != ControllerButton.None && _buttonDown[entry.Button2];
        return b1JustPressed || b2JustPressed;
    }

    private bool CheckControllerCombo(List<ControllerButton> combo)
    {
        if (combo == null || combo.Count == 0) return false;
        bool allHeld = true;
        bool anyJustPressed = false;
        foreach (var btn in combo)
        {
            if (!_buttonStates.ContainsKey(btn) || !_buttonStates[btn]) { allHeld = false; break; }
            if (_buttonDown.ContainsKey(btn) && _buttonDown[btn]) anyJustPressed = true;
        }
        return allHeld && anyJustPressed;
    }

    public void ClearFrameData()
    {
        // Legacy
        var legacyKeys = new List<ControllerButton>(_buttonDown.Keys);
        foreach (var k in legacyKeys) _buttonDown[k] = false;
        if (_buttonStates.ContainsKey(ControllerButton.Thumb))
            _buttonStates[ControllerButton.Thumb] = false;

        // Dynamic
        var dynKeys = new List<string>(_eventsTriggeredThisFrame.Keys);
        foreach (var k in dynKeys) _eventsTriggeredThisFrame[k] = false;
    }

    private void SetState(ControllerButton btn, bool state)
    {
        if (state && (!_buttonStates.ContainsKey(btn) || !_buttonStates[btn]))
            _buttonDown[btn] = true;
        _buttonStates[btn] = state;
    }

    private void OnDisable()
    {
        ClearFrameData();
        var keys = new List<ControllerButton>(_buttonStates.Keys);
        foreach (var k in keys) _buttonStates[k] = false;
    }

    private void OnDestroy() => UnbindAll();

    private void OnTriggerPressed()  => SetState(ControllerButton.Trigger, true);
    private void OnTriggerReleased() => SetState(ControllerButton.Trigger, false);
    private void OnAPressed()        => SetState(ControllerButton.A, true);
    private void OnAReleased()       => SetState(ControllerButton.A, false);
    private void OnBPressed()        => SetState(ControllerButton.B, true);
    private void OnBReleased()       => SetState(ControllerButton.B, false);
    private void OnThumbPressed()    => SetState(ControllerButton.Thumb, true);
}
