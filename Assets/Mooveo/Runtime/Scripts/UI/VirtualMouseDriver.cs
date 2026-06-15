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
    private UIDocument _currentUIDoc;
    private Vector2 _lastPanelPos;
    
    // --- LE BOOTSTRAP ---
    private Mouse _bootstrapMouse;

    public int PointerId { get; set; } = 1;

    private void Start()
    {
        var controller = GetComponentInParent<DefaultController>();
        if (controller != null) PointerId = controller.PlayerID;
    }
    
    public void UpdateMousePosition(Vector2 panelPos, Vector2 screenPos, UIDocument targetUI) //screenpos
    {
        // --- BOOTSTRAP : LE RÉVEIL DU PANNEAU ---
        // Si on touche un nouveau menu, on le réveille avec un event natif au pixel près
        if (targetUI != null && targetUI != _currentUIDoc)
        {
            if (_bootstrapMouse == null)
            {
                _bootstrapMouse = InputSystem.AddDevice<Mouse>(); // Création d'une souris fantôme
            }
            
            InputSystem.QueueStateEvent(_bootstrapMouse, new MouseState { position = screenPos });
        }

        _lastPanelPos = panelPos;
        _currentUIDoc = targetUI;

        if (targetUI == null || targetUI.rootVisualElement == null || targetUI.rootVisualElement.panel == null)
        {
            ClearHover();
            return;
        }
        
        SendPointerEvent(PointerMoveEvent.GetPooled(), targetUI.rootVisualElement, panelPos);

        VisualElement picked = targetUI.rootVisualElement.panel.Pick(panelPos);
        picked = GetInteractiveParent(picked);

        if (_isClicking)
        {
            if (_pressedElement != null)
            {
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

    public void SimulateLeftClick(bool isDown)
    {
        _isClicking = isDown;

        if (_currentUIDoc == null) return;

        if (isDown)
        {
            _isDragging = false;
            _pressStartPanelPos = _lastPanelPos;
            _pressedElement = _currentHoveredElement;

            if (_pressedElement != null)
            {
                SendPointerEvent(PointerDownEvent.GetPooled(), _pressedElement, _lastPanelPos);
            }
        }
        else
        {
            if (_pressedElement != null)
            {
                SendPointerEvent(PointerUpEvent.GetPooled(), _pressedElement, _lastPanelPos);
                
                if (!_isDragging && _pressedElement == _currentHoveredElement)
                {
                    SendPointerEvent(ClickEvent.GetPooled(), _pressedElement, _lastPanelPos);
                    
                    if (_pressedElement is RadioButton rb)
                    {
                        if (!rb.value) rb.value = true;
                    }
                    else if (_pressedElement is Toggle t)
                    {
                        t.value = !t.value;
                    }
                }
                
                _pressedElement = null;
            }
            _isDragging = false;
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

        target.SendEvent(evt);
    }
}