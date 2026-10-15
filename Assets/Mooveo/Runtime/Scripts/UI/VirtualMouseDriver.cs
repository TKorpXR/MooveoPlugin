using UnityEngine;
using UnityEngine.UIElements;
using System.Reflection;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public class VirtualMouseDriver : MonoBehaviour
{
    public float dragThreshold = 30f; 

    private bool _isClicking;
    private bool _isDragging;
    private Vector2 _pressStartPanelPos;
    
    private VisualElement _currentHoveredElement;
    private VisualElement _pressedElement;
    // Document de l'appui en cours (cf. UpdateMousePosition, EndPress).
    private VisualElement _pressedDocRoot;
    private UIDocument _currentUIDoc;
    private Vector2 _lastPanelPos;
    
    // --- LE BOOTSTRAP ---
    private Mouse _bootstrapMouse;

    public int PointerId { get; set; } = 1;

    private void Start()
    {
        var controller = GetComponentInParent<DefaultController>();
        if (controller != null) PointerId = UnityEngine.UIElements.PointerId.touchPointerIdBase + controller.PlayerID;
    }
    
    public void UpdateMousePosition(Vector2 panelPos, Vector2 screenPos, UIDocument targetUI) //screenpos
    {
        // --- BOOTSTRAP : LE RÉVEIL DU PANNEAU ---
        // En admin, garder la souris physique courante : la souris fantôme volerait ses clics.
        // Pour les trackers, réveiller un nouveau panneau avec un événement natif.
        if (targetUI != null && targetUI != _currentUIDoc && (GlobalSettings.Core.GlobalSettings.Instance == null || !GlobalSettings.Core.GlobalSettings.Instance.Admin.Value))
        {
            if (_bootstrapMouse == null)
            {
                _bootstrapMouse = InputSystem.AddDevice<Mouse>(); // Création d'une souris fantôme
            }
            
            InputSystem.QueueStateEvent(_bootstrapMouse, new MouseState { position = screenPos });
        }

        bool hasTarget = targetUI != null && targetUI.rootVisualElement != null && targetUI.rootVisualElement.panel != null;

        // ⚠ Un appui appartient au document où il a commencé : il se termine dès qu'on n'en vise
        // plus aucun (le relâchement, fait hors mode UI, ne serait jamais relayé) ou qu'on en vise
        // un autre (à la frame de sortie, sa position vient d'un autre repère et projetait la liste
        // tout en bas). Avant d'écraser _lastPanelPos : on s'arrête à la dernière position valide.
        bool leftPressedDocument = hasTarget && _pressedDocRoot != null && targetUI.rootVisualElement != _pressedDocRoot;
        if (!hasTarget || leftPressedDocument) EndPress(allowClick: false);

        _lastPanelPos = panelPos;
        _currentUIDoc = targetUI;

        if (!hasTarget)
        {
            ClearHover();
            return;
        }
        
        SendPointerEvent(PointerMoveEvent.GetPooled(), targetUI.rootVisualElement, panelPos);

        VisualElement picked = WorldPanelUtils.PickAt(targetUI.rootVisualElement.panel, panelPos);
        picked = GetInteractiveParent(picked);

        if (_isClicking)
        {
            if (_pressedElement != null)
            {
                // Slider : on pilote directement la valeur depuis la position du curseur (le drag interne
                // du slider ne réagit pas aux PointerEvents synthétiques -> sinon jouable qu'à la souris).
                // On NE propage PAS de PointerMove (sinon le ScrollView parent scrollerait en même temps).
                if (_pressedElement is Slider || _pressedElement is SliderInt)
                {
                    DragSliderIfAny(_pressedElement, panelPos);
                    _isDragging = true; // évite un ClickEvent parasite au relâchement
                    return;
                }

                if (!_isDragging && Vector2.Distance(_pressStartPanelPos, panelPos) > dragThreshold)
                {
                    _isDragging = true;
                    SendPointerEvent(PointerCancelEvent.GetPooled(), _pressedElement, panelPos);
                }
                SendPointerEvent(PointerMoveEvent.GetPooled(), _pressedElement, panelPos);
            }
            return;
        }

        if (picked != _currentHoveredElement)
        {
            if (_currentHoveredElement != null)
                SendPointerEvent(PointerLeaveEvent.GetPooled(), _currentHoveredElement, panelPos);

            _currentHoveredElement = picked;

            if (_currentHoveredElement != null)
                SendPointerEvent(PointerEnterEvent.GetPooled(), _currentHoveredElement, panelPos);
        }

        if (_currentHoveredElement != null)
        {
            SendPointerEvent(PointerMoveEvent.GetPooled(), _currentHoveredElement, panelPos);
        }
    }

    // Relevé de debug (PaintDebug.ShowUiSpray) : ce que le pilote croit survoler et presser.
    public VisualElement DebugHovered => _currentHoveredElement;
    public VisualElement DebugPressed => _pressedElement;
    public bool DebugIsClicking => _isClicking;
    public UIDocument DebugDocument => _currentUIDoc;

    public void SimulateLeftClick(bool isDown)
    {
        // Le relâchement ne dépend pas du document visé : l'élément pressé vit dans le sien.
        if (!isDown)
        {
            EndPress(allowClick: true);
            return;
        }

        _isClicking = true;

        if (_currentUIDoc == null) return;

        _isDragging = false;
        _pressStartPanelPos = _lastPanelPos;
        _pressedElement = _currentHoveredElement;
        _pressedDocRoot = _pressedElement != null ? _currentUIDoc.rootVisualElement : null;

        if (_pressedElement != null)
        {
            SendPointerEvent(PointerDownEvent.GetPooled(), _pressedElement, _lastPanelPos);
            DragSliderIfAny(_pressedElement, _lastPanelPos); // saut immédiat à la position du curseur
        }
    }

    /// <summary>Termine l'appui en cours (sans effet s'il n'y en a pas).</summary>
    /// <param name="allowClick">Faux pour une interruption (le spray a quitté le document) : ni clic,
    /// ni inertie.</param>
    private void EndPress(bool allowClick)
    {
        VisualElement pressed = _pressedElement;
        VisualElement pressedRoot = _pressedDocRoot;
        bool wasDragging = _isDragging;

        _isClicking = false;
        _isDragging = false;
        _pressedElement = null;
        _pressedDocRoot = null;

        if (pressed == null) return;

        // Interruption : la capture est rendue AVANT le PointerUp. `HasPointerCapture` lit la capture
        // en attente et devient faux aussitôt : le TouchScrollManipulator s'arrête net, sans inertie.
        IPanel pressedPanel = pressed.panel ?? pressedRoot?.panel;
        if (!allowClick && pressedPanel != null) PointerCaptureHelper.ReleasePointer(pressedPanel, PointerId);

        // ⚠ Élément détruit pendant l'appui (liste reconstruite pendant un scan) : la capture est
        // tenue par un parent resté en place. Le PointerUp part à la racine, la capture le redirige.
        if (pressed.panel == null)
        {
            if (pressedRoot != null && pressedRoot.panel != null)
                SendPointerEvent(PointerUpEvent.GetPooled(), pressedRoot, _lastPanelPos);
            return;
        }

        SendPointerEvent(PointerUpEvent.GetPooled(), pressed, _lastPanelPos);

        if (!allowClick || wasDragging || pressed != _currentHoveredElement) return;

        SendPointerEvent(ClickEvent.GetPooled(), pressed, _lastPanelPos);

        if (pressed is RadioButton rb)
        {
            if (!rb.value) rb.value = true;
        }
        else if (pressed is Toggle t)
        {
            t.value = !t.value;
        }
    }

    private void ClearHover()
    {
        if (_currentHoveredElement != null)
        {
            SendPointerEvent(PointerLeaveEvent.GetPooled(), _currentHoveredElement, _lastPanelPos);
            _currentHoveredElement = null;
        }
    }

    /// <summary>
    /// Pilote la valeur d'un Slider/SliderInt directement depuis la position du curseur (panneau),
    /// car le drag interne du slider ne réagit pas aux PointerEvents synthétiques de la manette.
    /// </summary>
    public static void DragSliderIfAny(VisualElement el, Vector2 panelPos)
    {
        if (el is Slider sl)
            sl.value = Mathf.Lerp(sl.lowValue, sl.highValue, SliderRatio(sl, sl.direction, panelPos));
        else if (el is SliderInt si)
            si.value = Mathf.RoundToInt(Mathf.Lerp(si.lowValue, si.highValue, SliderRatio(si, si.direction, panelPos)));
    }

    /// <summary>Position normalisée [0,1] du curseur le long de la piste du slider (utilise le tracker si présent).</summary>
    private static float SliderRatio(VisualElement slider, SliderDirection dir, Vector2 panelPos)
    {
        VisualElement track = slider.Q(className: "unity-base-slider__tracker") ?? slider;
        Rect wb = track.worldBound;
        if (dir == SliderDirection.Vertical)
            return Mathf.Clamp01(Mathf.InverseLerp(wb.yMax, wb.yMin, panelPos.y)); // bas = low, haut = high
        return Mathf.Clamp01(Mathf.InverseLerp(wb.xMin, wb.xMax, panelPos.x));
    }

    private VisualElement GetInteractiveParent(VisualElement element)
    {
        VisualElement current = element;
        while (current != null)
        {
            if (current is Button || current is RadioButton || current is Toggle || current is Slider || current is Scroller)
                return current;
            current = current.parent;
        }
        return element;
    }

    private void SendPointerEvent<T>(T evt, VisualElement target, Vector2 position) where T : EventBase, new()
    {
        evt.target = target;

        var posProp = typeof(T).GetProperty("position", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (posProp != null) posProp.SetValue(evt, (Vector3)position);
        
        var idProp = typeof(T).GetProperty("pointerId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (idProp != null) idProp.SetValue(evt, PointerId);

        try { target?.SendEvent(evt); }
        finally { evt.Dispose(); }
    }
}
