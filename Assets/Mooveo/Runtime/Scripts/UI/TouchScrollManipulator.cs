using System;
using UnityEngine;
using UnityEngine.UIElements;

public class TouchScrollManipulator : PointerManipulator
{
    private Vector2 _startPos;
    private Vector2 _startScroll;
    private bool _isDragging;
    private bool _isPressed;
    private float _lastDragStopTime = -1f;
    private int _pointerId = -1;
    private ScrollView _scrollView;
    private float _dragThresholdSqr = 64f; // 8 panel pixels: tolerate finger jitter.

    public TouchScrollManipulator(ScrollView scrollView)
    {
        _scrollView = scrollView;
        activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
    }

    protected override void RegisterCallbacksOnTarget()
    {
        target.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
        target.RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
        target.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
        target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        target.RegisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
    }

    protected override void UnregisterCallbacksFromTarget()
    {
        target.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
        target.UnregisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
        target.UnregisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
        target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        target.UnregisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
    }

    // Inertia State
    private Vector2 _velocity;
    private long _lastTime;
    private IVisualElementScheduledItem _inertiaScheduler;
    private const float DecelerationRate = 0.92f; // Friction
    private const float StopVelocity = 0.1f;

    /// <summary>Masque d'axe selon le mode du ScrollView : Vertical => (0,1), Horizontal => (1,0), sinon (1,1).</summary>
    private Vector2 AxisMask()
    {
        switch (_scrollView.mode)
        {
            case ScrollViewMode.Vertical:   return new Vector2(0f, 1f);
            case ScrollViewMode.Horizontal: return new Vector2(1f, 0f);
            default:                          return Vector2.one;
        }
    }

    /// <summary>
    /// Sens par axe : le VERTICAL garde le ressenti historique "souris" (main vers le haut ->
    /// contenu vers le bas, scroll = start + delta) utilisé par tous les menus existants ;
    /// l'HORIZONTAL (tiroir stickers/pochoirs du Mode Enfant) est en ressenti "tactile" naturel
    /// (le contenu SUIT le doigt), d'où le signe inversé sur X uniquement.
    /// </summary>
    private static readonly Vector2 AxisSign = new Vector2(-1f, 1f);

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (_isPressed || evt.button > 0) return; // Synthetic primary events may carry button=-1.
        // Sliders and text fields own their gesture. Never steal their pointer capture.
        for (var el = evt.target as VisualElement; el != null && el != target; el = el.parent)
            if (el is Slider || el is SliderInt || el is MinMaxSlider || el is TextField || el is Scroller) return;
        _pointerId = evt.pointerId;
        
        _isPressed = true;
        _startPos = evt.position;
        _startScroll = _scrollView.scrollOffset;
        
        // Stop any existing inertia
        _inertiaScheduler?.Pause();
        _velocity = Vector2.zero;
        _lastTime = DateTime.Now.Ticks;
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        if (!_isPressed || evt.pointerId != _pointerId) return;
        
        // Calculate Scale Factor (Layout Pixels vs Panel/World Units)
        float scaleFactor = 1.0f;
        if (target.worldBound.width > 0.001f)
        {
            scaleFactor = target.layout.width / target.worldBound.width;
        }

        // Calculate Velocity for Inertia
        long now = DateTime.Now.Ticks;
        float dt = (now - _lastTime) / 10000000.0f; // Seconds
        if (dt > 0.001f)
        {
            Vector2 currentPos = evt.position;
            Vector2 deltaPos = currentPos - _startPos; // Since last "captured" start
            
            // For velocity, we need per-frame delta. 
            // _startPos is reset every time we process a drag frame below.
            // If dragging, _startPos is "last frame pos".
            
            if (_isDragging)
            {
                 // Invert Axis and Scale for Velocity
                 // User wants to invert: Hand UP -> Content DOWN (Mouse style not Phone style)
                 // Delta UP is neg Y. neg * scale * Invert = pos scroll.
                 _velocity = Vector2.Scale(Vector2.Scale((deltaPos * scaleFactor) / dt, AxisMask()), AxisSign); // contraint à l'axe + sens par axe
            }
        }
        _lastTime = now;

        // If already dragging
        if (_isDragging)
        {
            Vector2 currentPos = evt.position;
            Vector2 delta = Vector2.Scale(Vector2.Scale(currentPos - _startPos, AxisMask()), AxisSign); // axe du ScrollView + sens par axe (cf. AxisSign)

            // INVERT AXIS REQUEST: Change '-' to '+'
            // Original (Phone): scroll = start - delta
            // New (Mouse): scroll = start + delta
            _scrollView.scrollOffset = _startScroll + (delta * scaleFactor);
            
            // Reset reference for next frame relative calculation
            _startScroll = _scrollView.scrollOffset;
            _startPos = currentPos;
            
            evt.StopPropagation();
            return;
        }

        // Check Threshold
        float distSqr = Vector2.Scale((Vector2)evt.position - _startPos, AxisMask()).sqrMagnitude;
        
        if (distSqr > _dragThresholdSqr)
        {
            _isDragging = true;
            target.CapturePointer(evt.pointerId); 
            
            _startScroll = _scrollView.scrollOffset; 
            _startPos = evt.position; 
            
            // Clear velocity on start drag
            _velocity = Vector2.zero;
            
            evt.StopPropagation(); 
        }
    }

    private void OnPointerUp(PointerUpEvent evt)
    {
        if (!_isPressed || evt.pointerId != _pointerId) return;
        _isPressed = false;
        if (_isDragging)
        {
            _isDragging = false;
            _lastDragStopTime = Time.realtimeSinceStartup;
            if (target.HasPointerCapture(evt.pointerId)) target.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
            ApplyInertia();
        }
    }

    private void OnClick(ClickEvent evt)
    {
        if (evt.pointerId == _pointerId && Time.realtimeSinceStartup - _lastDragStopTime < 0.1f)
            evt.StopPropagation();
    }

    private void ApplyInertia()
    {
        if (_velocity.sqrMagnitude < StopVelocity) return;
        
        if (_inertiaScheduler == null)
        {
            _inertiaScheduler = target.schedule.Execute(UpdateInertia).Every(16); // ~60fps
        }
        else
        {
            _inertiaScheduler.Resume();
        }
    }

    private void UpdateInertia()
    {
        // Decelerate
        _velocity *= DecelerationRate;
        
        // Apply
        // Note: Velocity is already scaled and inverted from OnPointerMove
        // But we are adding it per frame (dt ~ 0.016)
        _scrollView.scrollOffset += _velocity * 0.016f; 

        // Stop if slow
        if (_velocity.sqrMagnitude < StopVelocity)
        {
            _velocity = Vector2.zero;
            _inertiaScheduler.Pause();
        }
    }
    
    private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
    {
        if (evt.pointerId != _pointerId) return;
        _isDragging = false;
        _isPressed = false;
        // Keep inertia running or stop? Maybe stop on interrupt.
        // _inertiaScheduler?.Pause();
    }
}
