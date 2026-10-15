using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// La SOURIS sur toutes les interfaces UI Toolkit, par le même chemin que le curseur spray
/// (<see cref="CursorInteractor"/> : pointage puis événements envoyés à l'élément).
///
/// ⚠ Pourquoi pas l'EventSystem natif : les panneaux WORLD-SPACE (barre de menu, MainLayout,
/// notifications) ne reçoivent la souris que par une configuration d'entrée qui vise
/// <c>Camera.main</c> — or en build, OpenVR initialise l'affichage XR et <c>Camera.main</c> est la
/// caméra du CASQUE, pas celle qui s'affiche à l'écran. Le rayon partait donc d'ailleurs que du
/// pointeur. Ici le rayon part de la caméra RÉELLEMENT affichée (cible « aucun œil »).
///
/// ⚠ La souris native est COUPÉE sur les modules XRI : sur un panneau d'écran (calibration) les deux
/// chemins livreraient chacun leur clic, et un bouton bascule se rouvrirait aussitôt. Aucun bouton
/// UGUI n'existe dans les scènes du build, rien d'autre ne dépend de cette souris-là.
///
/// En mode Admin, le clic gauche reste réservé à l'UI ; le clic droit pilote le spray simulé.
/// </summary>
[Preserve]
public class MouseUIBridge : MonoBehaviour
{
    private const int UILayerMask = 1 << 5;

    private static MouseUIBridge _instance;

    private VisualElement _hovered;
    private VisualElement _pressed;
    // Repli du PointerUp si l'élément pressé a été détruit.
    private VisualElement _pressedRoot;
    private UserUI _hoveredUserUI;
    private UserUI _pressedUserUI;
    private Vector2 _lastScreen;
    private Vector2 _panelPosition;
    private bool _wasPressed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (_instance != null) return;
        GameObject go = new GameObject("MouseUIBridge") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<MouseUIBridge>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        MuteNativeMouse();
    }

    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _hovered = null;
        _pressed = null;
        _pressedRoot = null;
        _hoveredUserUI = null;
        _pressedUserUI = null;
        _wasPressed = false;
        MuteNativeMouse();
    }

    private static void MuteNativeMouse()
    {
        foreach (XRUIInputModule module in FindObjectsByType<XRUIInputModule>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            module.enableMouseInput = false;
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 screen = mouse.position.ReadValue();
        bool pressed = mouse.leftButton.isPressed;
        bool moved = (screen - _lastScreen).sqrMagnitude > 0.01f;
        Vector2 delta = screen - _lastScreen;
        _lastScreen = screen;

        // Souris immobile et bouton relâché : rien à dire, ne pas disputer le survol au curseur spray.
        if (!moved && !pressed && !_wasPressed) return;

        VisualElement picked = Pick(screen, out Vector2 panelPosition);
        if (picked != null) _panelPosition = panelPosition;

        if (picked != _hovered)
        {
            if (_hovered != null) Send(PointerLeaveEvent.GetPooled(), _hovered, delta);
            _hovered = picked;
            if (_hovered != null) Send(PointerEnterEvent.GetPooled(), _hovered, delta);
        }

        if (moved)
        {
            // Les sliders synthétiques sont pilotés directement, comme ceux du spray. Ne pas
            // propager le glissement au ScrollView qui contient le réglage.
            if (_pressed is Slider || _pressed is SliderInt)
                VirtualMouseDriver.DragSliderIfAny(_pressed, _panelPosition);
            else
            {
                if (_hovered != null) Send(PointerMoveEvent.GetPooled(), _hovered, delta);
                // Glisser (défilement tactile) : l'élément pressé suit, même quand on sort de lui.
                if (_pressed != null && _pressed != _hovered) Send(PointerMoveEvent.GetPooled(), _pressed, delta);
            }
        }

        if (pressed && !_wasPressed && _hovered != null)
        {
            _pressed = _hovered;
            _pressedRoot = _pressed.panel?.visualTree;
            _pressedUserUI = _hoveredUserUI;
            PrepareUserInteraction();
            Send(PointerDownEvent.GetPooled(), _pressed, delta);
            VirtualMouseDriver.DragSliderIfAny(_pressed, _panelPosition);
        }
        else if (!pressed && _wasPressed && _pressed != null)
        {
            // ⚠ Élément détruit pendant l'appui : le PointerUp part à la racine, la capture le
            // redirige vers son détenteur (cf. VirtualMouseDriver.EndPress).
            Send(PointerUpEvent.GetPooled(), _pressed.panel != null ? _pressed : _pressedRoot, delta);
            if (_pressed.panel != null && _pressed == _hovered && !(_pressed is Slider || _pressed is SliderInt))
            {
                PrepareUserInteraction();
                Send(ClickEvent.GetPooled(), _pressed, delta);
                // Même traitement manuel que le curseur spray : un clic synthétique ne bascule pas seul.
                if (_pressed is RadioButton radio) { if (!radio.value) radio.value = true; }
                else if (_pressed is Toggle toggle) toggle.value = !toggle.value;
            }
            _pressed = null;
            _pressedRoot = null;
            _pressedUserUI = null;
        }

        _wasPressed = pressed;
    }

    private void PrepareUserInteraction()
    {
        if (_pressedUserUI == null) return;
        int playerID = AdminMouseSpray.MousePlayerID;
        _pressedUserUI.PushInteractingPlayer(playerID >= 0 ? playerID : _pressedUserUI.OwnerID);
    }

    // =========================================================================
    // Pointage
    // =========================================================================

    /// <summary>
    /// Panneaux d'ÉCRAN d'abord (ils sont dessinés par-dessus tout), puis panneaux du monde par rayon
    /// sur leur collider. Un document est tenu pour world-space s'il porte un collider : tous ceux du
    /// projet en ont un, c'est ce que vise le curseur spray.
    /// </summary>
    private VisualElement Pick(Vector2 screen, out Vector2 panelPosition)
    {
        panelPosition = default;
        _hoveredUserUI = null;

        UIDocument[] documents = FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
        var screenDocs = new List<UIDocument>();
        foreach (UIDocument doc in documents)
            if (doc.isActiveAndEnabled && doc.rootVisualElement?.panel != null && doc.GetComponent<Collider>() == null)
                screenDocs.Add(doc);
        screenDocs.Sort((a, b) => b.sortingOrder.CompareTo(a.sortingOrder));

        foreach (UIDocument doc in screenDocs)
        {
            IPanel panel = doc.rootVisualElement.panel;
            Vector2 position = ScreenSpaceUI.ToPanel(panel, screen);
            VisualElement hit = WorldPanelUtils.PickAt(panel, position);

            // ⚠ Un panneau d'écran a presque toujours un conteneur plein écran, pickable par défaut :
            // le retenir tel quel avalerait TOUS les clics, et les panneaux du monde derrière lui ne
            // recevraient jamais rien. On ne s'arrête que sur une cible réelle — la même règle que le
            // curseur spray — ou sur le voile d'une modale, qui doit justement bloquer.
            if (hit == null || !(WorldPanelUtils.IsInteractiveUIElement(hit) || InModalOverlay(hit))) continue;
            panelPosition = position;
            _hoveredUserUI = doc.GetComponentInParent<UserUI>();
            return Interactive(hit);
        }

        Camera camera = DisplayCamera();
        if (camera == null) return null;

        // ⚠ TOUS les impacts, du plus proche au plus loin, et pas seulement le premier. La barre de
        // menu globale est posée DERRIÈRE le MainLayout (UIDistanceFromWall - 0.1) : quand le
        // collider du MainLayout couvre la même zone, s'arrêter au premier impact tombait sur une
        // zone vide de celui-ci et le clic se perdait — seuls les boutons de la GlobalWorldUI ne
        // répondaient pas. On retient le premier document qui a une VRAIE cible sous le pointeur.
        Ray ray = camera.ScreenPointToRay(screen);
        RaycastHit[] hits = Physics.RaycastAll(ray, 1000f, UILayerMask, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit rayHit in hits)
        {
            UIDocument worldDoc = rayHit.collider.GetComponentInParent<UIDocument>();
            if (worldDoc == null || worldDoc.rootVisualElement?.panel == null) continue;

            // Même conversion que le curseur spray : le point d'impact dans le repère du document.
            Vector3 local = worldDoc.transform.InverseTransformPoint(rayHit.point);
            Vector2 position = new Vector2(local.x, local.y);
            VisualElement picked = WorldPanelUtils.PickAt(worldDoc.rootVisualElement.panel, position);
            if (picked == null || !(WorldPanelUtils.IsInteractiveUIElement(picked) || InModalOverlay(picked))) continue;

            panelPosition = position;
            _hoveredUserUI = worldDoc.GetComponentInParent<UserUI>();
            return Interactive(picked);
        }

        return null;
    }

    /// <summary>
    /// La caméra dessinée À L'ÉCRAN : sans cible d'œil, sans texture, sur l'écran 0. ⚠ Surtout pas
    /// <c>Camera.main</c>, qui est la caméra du casque dès que l'affichage XR tourne. La règle vit
    /// dans <see cref="ScreenSpaceUI"/> : elle sert aussi au pointage du spray.
    /// </summary>
    private static Camera DisplayCamera() => ScreenSpaceUI.DisplayCamera();

    /// <summary>Voiles des modales (GuidePopup, ChoicePopup, État du système), nommés « …-overlay ».</summary>
    private static bool InModalOverlay(VisualElement element)
    {
        for (VisualElement current = element; current != null; current = current.parent)
            if (current.name != null && current.name.EndsWith("-overlay") && current.pickingMode == PickingMode.Position)
                return true;
        return false;
    }

    /// <summary>Le libellé ou l'icône d'un bouton renvoie au bouton lui-même, comme au spray.</summary>
    private static VisualElement Interactive(VisualElement element)
    {
        for (VisualElement current = element; current != null; current = current.parent)
            if (current is Button || current is RadioButton || current is Toggle || current is Slider || current is SliderInt) return current;
        return element;
    }

    // =========================================================================
    // Envoi
    // =========================================================================

    private static class Props<T> where T : PointerEventBase<T>, new()
    {
        public static readonly PropertyInfo Position = typeof(T).GetProperty("position", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        public static readonly PropertyInfo Delta = typeof(T).GetProperty("deltaPosition", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        public static readonly PropertyInfo PointerId = typeof(T).GetProperty("pointerId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    }

    /// <summary>
    /// Même envoi que <see cref="CursorInteractor"/>, avec l'identifiant de la SOURIS : le curseur
    /// spray utilise le 1, les captures de pointeur (défilement tactile) ne se mélangent donc pas.
    /// </summary>
    private void Send<T>(T evt, VisualElement target, Vector2 delta) where T : PointerEventBase<T>, new()
    {
        if (target == null || target.panel == null) { evt.Dispose(); return; }

        evt.target = target;
        Props<T>.Position?.SetValue(evt, (Vector3)_panelPosition);
        Props<T>.Delta?.SetValue(evt, (Vector3)delta);
        Props<T>.PointerId?.SetValue(evt, PointerId.mousePointerId);
        try { target.SendEvent(evt); }
        finally { evt.Dispose(); }
    }
}
