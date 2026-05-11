using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;

public enum UIInteractionMode
{
    PhysicsRaycast3D,
    EventSystem
}
public class CursorInteractor : MonoBehaviour
{
    [SerializeField] private UIInteractionMode _interactionMode = UIInteractionMode.PhysicsRaycast3D;
    [SerializeField] private bool _debug = false;

    private Camera _camera;
    private PointerEventData _pointer;
    private readonly List<RaycastResult> _results = new List<RaycastResult>();
    private float _dragThreshold;

    private GameObject _currentTarget;
    private GameObject _pressedTarget;

    bool _isClicking = false;
    bool _clicked = false;

    private Vector2 _pressStartScreenPos;
    private Vector2 _lastScreenPos;
    public event Action OnClickedOutside;
    public event Action<bool> OnClick;

    // UI Toolkit Cache
    private VisualElement _currentVisualElement;
    private VisualElement _pressedVisualElement;

    public void Init()
    {
        _camera = GlobalSettings.Core.GlobalSettings.MainCamera;
        GlobalSettings.Core.GlobalSettings.Instance.DragThreshold.Bind(() => _dragThreshold, value => _dragThreshold = value);
        _pointer = new PointerEventData(EventSystem.current);
        this.enabled = false;
    }

    private Vector2 _alignedPointerPos;

    void Update()
    {
        if (_camera == null || EventSystem.current == null) return;
        
        Vector2 screenPos = _camera.WorldToScreenPoint(transform.position);

        _lastScreenPos = screenPos;
        
        _results.Clear();
        RaycastResult firstResult = new RaycastResult();
        GameObject newTarget = null;
        
        bool hitUIToolkit = false;
        
        if (_interactionMode == UIInteractionMode.PhysicsRaycast3D)
        {
            Ray ray = _camera.ScreenPointToRay(screenPos);
            // EXPLICITLY target Layer 5 (UI)
            int mask = 1 << 5;
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, mask, QueryTriggerInteraction.Collide)) 
            {
                var physicsUIDoc = hit.collider.GetComponentInParent<UIDocument>();
                if (physicsUIDoc != null)
                {
                    //Debug.Log($"<color=magenta>[INTERACTION]</color> Physics Raycast FOUND UIDocument: {hit.collider.name} | Distance: {hit.distance}");
                    hitUIToolkit = true;
                    newTarget = hit.collider.gameObject; 
                    
                    firstResult.gameObject = newTarget;
                    firstResult.distance = hit.distance;
                    firstResult.worldPosition = hit.point;
                    HandleUIToolkitInteraction(physicsUIDoc, screenPos, hit);
                }
            } 
        }
        else
        {
            _pointer.position = screenPos;
            _pointer.delta = screenPos - _lastScreenPos;
            _pointer.scrollDelta = Vector2.zero;
            _pointer.useDragThreshold = true;
            EventSystem.current.RaycastAll(_pointer, _results);
            firstResult = _results.Count > 0 ? _results[0] : default;
            newTarget = firstResult.gameObject;
            // 1. Try Event System Result
            if (newTarget != null && newTarget.TryGetComponent<UIDocument>(out var uiDoc))
            {
                //Debug.Log($"<color=cyan>[INTERACTION]</color> EventSystem Hit UIDocument: {newTarget.name}");
                hitUIToolkit = true;
                HandleUIToolkitInteraction(uiDoc, screenPos);
            }
            else if (newTarget != null)
            {
                // Debug.Log("EventSystem Hit Other: " + newTarget.name);
            }
        }
        

        if (!hitUIToolkit)
        {
            // Clear hover if we left UI Toolkit
            if (_currentVisualElement != null)
            {
                 UsingUIToolkitPointerEvent(PointerLeaveEvent.GetPooled(), _currentVisualElement, _alignedPointerPos, _pointer.delta);
                 _currentVisualElement = null;
            }
            
            // Handle Dragging OUTSIDE bounds
            if (_pressedVisualElement != null)
            {
                // We are holding/dragging but raycast hits nothing/world.
                // We must continue supplying coordinates relative to the PRESSED element's panel.
                _alignedPointerPos = RuntimePanelUtils.ScreenToPanel(_pressedVisualElement.panel, screenPos);
            }
        }
        
        // --- STANDARD UGUI LOGIC ---
        if (newTarget != _currentTarget)
        {
            if (_currentTarget != null)
                ExecuteEvents.Execute(_currentTarget, _pointer, ExecuteEvents.pointerExitHandler);

            if (newTarget != null)
                ExecuteEvents.Execute(newTarget, _pointer, ExecuteEvents.pointerEnterHandler);

            _currentTarget = newTarget;
        }
        
        if (_isClicking && !_clicked) // DOWN
        {
            if (newTarget != null)
            {
                if (hitUIToolkit)
                {
                    if (_currentVisualElement != null)
                    {
                        _pressedVisualElement = _currentVisualElement;
                        _pressStartScreenPos = screenPos;
                        UsingUIToolkitPointerEvent(PointerDownEvent.GetPooled(), _pressedVisualElement, _alignedPointerPos, _pointer.delta);
                    }
                    else
                    {
                        // On a frappé le BoxCollider global avec le Raycast3D, mais UI Toolkit n'a intercepté aucun élément cliquable/visible (renvoie null).
                        // Cela équivaut systématiquement à un clic dans le vide (hors UI).
                        OnClickedOutside?.Invoke();
                    }
                }
                else
                {
                    // UGUI CLICK
                    _pressedTarget = newTarget;
                    _pointer.pressPosition = _pointer.position;
                    _pointer.pointerPressRaycast = firstResult;
                    _pointer.rawPointerPress = newTarget;
                    _pointer.eligibleForClick = true;
                    
                    GameObject dragHandler = ExecuteEvents.GetEventHandler<IDragHandler>(newTarget);
                    if (dragHandler != null)
                    {
                        ExecuteEvents.Execute(dragHandler, _pointer, ExecuteEvents.initializePotentialDrag);
                        _pointer.pointerDrag = dragHandler;
                    }

                    GameObject pressed = ExecuteEvents.ExecuteHierarchy(newTarget, _pointer, ExecuteEvents.pointerDownHandler)
                                         ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(newTarget);
                    _pointer.pointerPress = pressed;
                }
            }
            else
            {
                OnClickedOutside?.Invoke();
            }
        }
        else if (_isClicking && _clicked) // DRAG/HOLD
        {
            // UI TOOLKIT MOVE/DRAG
            if (_pressedVisualElement != null)
            {
                UsingUIToolkitPointerEvent(PointerMoveEvent.GetPooled(), _pressedVisualElement, _alignedPointerPos, _pointer.delta);
            }

            if (_pointer.pointerDrag != null)
            {
                if (!_pointer.dragging && _pointer.delta.sqrMagnitude > _dragThreshold)
                {
                    _pointer.dragging = true;
                    ExecuteEvents.Execute(_pointer.pointerDrag, _pointer, ExecuteEvents.beginDragHandler);
                }
                
                ExecuteEvents.Execute(_pointer.pointerDrag, _pointer, ExecuteEvents.dragHandler);
            }
        }
        else if (!_isClicking && _clicked) // UP
        {
            // UI TOOLKIT UP & CLICK
            if (_pressedVisualElement != null)
            {
                UsingUIToolkitPointerEvent(PointerUpEvent.GetPooled(), _pressedVisualElement, _alignedPointerPos, _pointer.delta);
                
                float travelDistance = Vector2.Distance(_pressStartScreenPos, screenPos);
                bool isDragIntent = travelDistance > _dragThreshold;
                
                if (_currentVisualElement == _pressedVisualElement && !isDragIntent)
                {
                    
                    UsingUIToolkitPointerEvent(ClickEvent.GetPooled(), _pressedVisualElement, _alignedPointerPos, _pointer.delta);
                    
                    if (_pressedVisualElement is RadioButton rb) //Gestion manuelle des radiobutonns
                    {
                        if (!rb.value) rb.value = true; 
                    }
                    else if (_pressedVisualElement is Toggle t)
                    {
                        // Pour les cases à cocher standard
                        t.value = !t.value;
                    }
                }
                else if (isDragIntent)
                {
                    if (_debug) Debug.Log($"[XR_DEBUG] Click ignored: User intended to scroll (Distance: {travelDistance})");
                }
                else
                {
                    if (_debug) Debug.LogWarning($"[XR_DEBUG] Click cancelled: Pressed on '{_pressedVisualElement?.name}' but released on '{_currentVisualElement?.name}'");
                }
                
                _pressedVisualElement = null;
            }

            // UGUI UP
            if (_pointer.pointerPress != null)
            {
                ExecuteEvents.Execute(_pointer.pointerPress, _pointer, ExecuteEvents.pointerUpHandler);
                
                GameObject clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(_currentTarget);
                if (_pointer.eligibleForClick && _pointer.pointerPress == clickHandler && !_pointer.dragging)
                {
                    ExecuteEvents.Execute(_pointer.pointerPress, _pointer, ExecuteEvents.pointerClickHandler);
                }
                
                if (_pointer.pointerDrag != null)
                    ExecuteEvents.Execute(_pointer.pointerDrag, _pointer, ExecuteEvents.endDragHandler);
            }
            
            _pointer.eligibleForClick = false;
            _pointer.pointerPress = null;
            _pointer.rawPointerPress = null;
            _pointer.pointerDrag = null;
            _pressedTarget = null;
            _pointer.pointerDrag = null;
            _pointer.dragging = false;
        }
        
        _clicked = _isClicking;
    }

    private void HandleUIToolkitInteraction(UIDocument uiDoc, Vector2 screenPos, RaycastHit? physicsHit = null)
    {
        try
        {
            // Debug.Log($"HandleUI: Root={(uiDoc.rootVisualElement != null)} Hit={physicsHit.HasValue}");
            if (uiDoc.rootVisualElement == null) return;
            
            EnforceButtonPicking(uiDoc.rootVisualElement);
            
            Vector2 panelPos = Vector2.zero;
            VisualElement picked = null;

            if (physicsHit.HasValue)
            {
                Vector3 localHit = uiDoc.transform.InverseTransformPoint(physicsHit.Value.point);
                panelPos = new Vector2(localHit.x, localHit.y);
                picked = uiDoc.rootVisualElement.panel.Pick(panelPos);
            }
            else
            {
                //panelPos = RuntimePanelUtils.ScreenToPanel(uiDoc.rootVisualElement.panel, screenPos);
                //Debug.Log($"Panel pos in else: {panelPos}");
                //picked = uiDoc.rootVisualElement.panel.Pick(panelPos);
            }
            
            if (picked != null)
            {
                if (_isClicking || picked != _currentVisualElement)
                {
                    // Debug.Log($"[XR_DEBUG] Raw Hit: '{picked.name}' (Type: {picked.GetType().Name})");
                }
                
                var originalPicked = picked;
                picked = GetInteractiveParent(picked); // recupere le premier obj interactif en parent
                
                if (originalPicked != picked && _isClicking)
                {
                    //Debug.Log($"[XR_DEBUG] Redirected '{originalPicked.name}' ({originalPicked.GetType().Name}) -> Parent '{picked.name}' ({picked.GetType().Name})");
                }
            }
            
            // Store Aligned Position for Events
            _alignedPointerPos = panelPos;

            if (picked != _currentVisualElement)
            {
                if(picked != null)
                {
                    string elementName = string.IsNullOrEmpty(picked.name) ? "Élément_Sans_Nom" : picked.name;
                    //Debug.Log($"<color=cyan>[HOVER]</color> Curseur entre sur : <b>{elementName}</b> <i>({picked.GetType().Name})</i>");
                }
                else if (_currentVisualElement != null)
                {
                    if (_debug) Debug.Log($"<color=grey>[HOVER]</color> Curseur sort dans le vide.");
                }
                if (_currentVisualElement != null)
                    UsingUIToolkitPointerEvent(PointerLeaveEvent.GetPooled(), _currentVisualElement, _alignedPointerPos, _pointer.delta);
                
                _currentVisualElement = picked;
                
                if (_currentVisualElement != null)
                    UsingUIToolkitPointerEvent(PointerEnterEvent.GetPooled(), _currentVisualElement, _alignedPointerPos, _pointer.delta);
            }
            
            if (_currentVisualElement != null)
            {
                 UsingUIToolkitPointerEvent(PointerMoveEvent.GetPooled(), _currentVisualElement, _alignedPointerPos, _pointer.delta);
            }
        }
        catch (System.Exception e)
        {
            if (_debug) Debug.LogError($"CRASH in HandleUI: {e.Message}\n{e.StackTrace}");
        }
    }

    // Reflection Cache to avoid lookup cost every frame
    private static class PointerEventReflection<T> where T : PointerEventBase<T>, new()
    {
        public static readonly PropertyInfo PositionProp = typeof(T).GetProperty("position", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        public static readonly PropertyInfo DeltaProp = typeof(T).GetProperty("deltaPosition", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        public static readonly PropertyInfo PointerIdProp = typeof(T).GetProperty("pointerId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    }

    private void UsingUIToolkitPointerEvent<T>(T evt, VisualElement target, Vector2 position, Vector2 delta = default) where T : PointerEventBase<T>, new()
    {
        evt.target = target;

        string targetName = string.IsNullOrEmpty(target?.name) ? "Élément_Sans_Nom" : target.name;
        if (_debug && typeof(T).Name != "PointerMoveEvent")
        {
            Debug.Log($"<color=yellow>[UI_EVENT]</color> Envoi de <b>{typeof(T).Name}</b> vers <b>{targetName}</b> (<i>{target?.GetType().Name}</i>) | Pos: {position} | Delta: {delta}");
        }

        // Use Reflection to set protected/private setters
        var posProp = PointerEventReflection<T>.PositionProp;
        if (posProp != null) posProp.SetValue(evt, (Vector3)position);
        
        var deltaProp = PointerEventReflection<T>.DeltaProp;
        if (deltaProp != null) deltaProp.SetValue(evt, (Vector3)delta);
        
        // CRITICAL: Set Pointer ID so CapturePointer works!
        var idProp = PointerEventReflection<T>.PointerIdProp;
        if (idProp != null) idProp.SetValue(evt, 1); // Use ID 1 for main cursor
        
        target.SendEvent(evt);
    }
    
    public void SetClicking(bool isClicking)
    {
        _isClicking = isClicking;
        OnClick?.Invoke(_isClicking);
    }


    public bool IsClicking() => _isClicking;

    private void EnforceButtonPicking(VisualElement root)
    {
        UQueryBuilder<Button> buttons = root.Query<Button>();
        buttons.ForEach(btn => 
        {
            btn.pickingMode = PickingMode.Position;
            foreach(var child in btn.Children())
            {
                child.pickingMode = PickingMode.Ignore;
            }
        });
        
        UQueryBuilder<RadioButton> radios = root.Query<RadioButton>();
        radios.ForEach(radio => 
        {
            radio.pickingMode = PickingMode.Position;
            foreach(var child in radio.Children())
            {
                child.pickingMode = PickingMode.Ignore;
            }
        });
    }
    
    private VisualElement GetInteractiveParent(VisualElement element)
    {
        VisualElement current = element;
        while (current != null)
        {
            // Si on trouve un Bouton, un RadioButton ou un Toggle, c'est lui qu'on veut cibler
            if (current is Button || current is RadioButton || current is Toggle)
            {
                return current;
            }
            // Sinon on continue de remonter
            current = current.parent;
        }
        // Si on a rien trouvé, on retourne l'élément d'origine (c'était peut-être juste un panneau)
        return element;
    }
}