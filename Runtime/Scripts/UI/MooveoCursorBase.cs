using System;
using UnityEngine;

public class MooveoCursorBase : MonoBehaviour
{
    [SerializeField] protected RectTransform _cursorRect;
    protected RectTransform _canvasRect;
    
    public RectTransform CursorRect => _cursorRect;

    protected virtual void Awake()
    {
        _cursorRect = GetComponent<RectTransform>();
    }
    
    public virtual void Init(RectTransform canvasRect)
    {
        _canvasRect = canvasRect;
    }
    
    public virtual void UpdatePosition(Vector3 worldHitPoint)
    {
        if (_canvasRect != null && _cursorRect != null)
        {
            Vector3 localPoint3D = _canvasRect.InverseTransformPoint(worldHitPoint);
            _cursorRect.localPosition = new Vector3(localPoint3D.x, localPoint3D.y, 0f);
        }
    }
    
    public virtual void Enable(bool isVisible)
    {
        gameObject.SetActive(isVisible);
    }
}
