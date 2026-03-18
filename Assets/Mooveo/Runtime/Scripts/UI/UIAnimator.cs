#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections;
using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Events;


[RequireComponent(typeof(RectTransform))]
public class UIAnimator : MonoBehaviour
{
    public enum AnimationType
    {
        Scale,
        Fade,
        Move
    }
    
    [Header("Settings")]
    [SerializeField] AnimationType _animationType = AnimationType.Scale;
    [SerializeField] float _duration = 0.5f;
    [SerializeField] AnimationCurve _curve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [ShowIf("_move"), Header("Move Settings")]
    [SerializeField] Vector3 _moveFrom;
    [ShowIf("_move"), SerializeField] Vector3 _moveTo;
    [ShowIf("_move"), SerializeField] float _debugRadiusPoint;

    [ShowIf("_scale"), Header("Scale Settings")]    
    [SerializeField] Vector3 _scaleFrom = Vector3.zero;
    [ShowIf("_scale"), SerializeField] Vector3 _scaleTo = Vector3.one;

    [ShowIf("_fade"), Header("Fade Settings")]
    [Range(0f,1f)] [SerializeField] float _alphaFrom = 0f;
    [Range(0f,1f)] [ShowIf("_fade"), SerializeField] float _alphaTo = 1f;

    private RectTransform _rect;
    private CanvasGroup _canvasGroup;

    private bool _fade, _scale, _move;

    [SerializeField] private UnityEvent OnOpenAnimationEnd;
    [SerializeField] private UnityEvent OnCloseAnimationEnd;

    private void OnValidate()
    {
        switch (_animationType)
        {
            case AnimationType.Scale:
                _scale = true;
                _move = false;
                _fade = false;
                break;
            case AnimationType.Fade:
                _fade = true;
                _move = false;
                _scale = false;
                break;
            case AnimationType.Move:
                _move = true;
                _fade = false;
                _scale = false;
                break;
        }
    }

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
        {
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        switch (_animationType)
        {
            case AnimationType.Scale:
                _rect.localScale = _scaleFrom;
                break;
            case AnimationType.Fade:
                _canvasGroup.alpha = _alphaFrom;
                break;
            case AnimationType.Move:
                _rect.position = _moveFrom;
                break;
        }
    }

    public void Init(AnimationType type, Vector3 moveFrom, Vector3 moveTo, float debugRadiusPoint = 0.1f)
    {
        _animationType = type;
        _moveFrom = moveFrom;
        _moveTo = moveTo;
        _debugRadiusPoint = debugRadiusPoint;
    }
    public void Init(AnimationType type, Vector3 scaleFrom, Vector3 scaleTo)
    {
        _animationType = type;
        _scaleFrom = scaleFrom;
        _scaleTo = scaleTo;
    }
    public void Init(AnimationType type, float alphaTo, float alphaFrom)
    {
        _animationType = type;
        _alphaTo = alphaTo;
        _alphaFrom = alphaFrom;
    }
    
    public void PlayOpen()
    {
        StopAllCoroutines();
        StartCoroutine(AnimateOpen());
    }

    public void PlayClose()
    {
        StopAllCoroutines();
        StartCoroutine(AnimateClose());
    }

    private IEnumerator AnimateOpen()
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / _duration;
            float eval = _curve.Evaluate(t);

            ApplyAnimation(eval, true);

            yield return null;
        }
        ApplyAnimation(1f, true);
        _canvasGroup.interactable = true;
        _canvasGroup.blocksRaycasts = true;
        OnOpenAnimationEnd?.Invoke();
    }

    private IEnumerator AnimateClose()
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / _duration;
            float eval = _curve.Evaluate(1f - t);

            ApplyAnimation(eval, false);

            yield return null;
        }
        ApplyAnimation(0f, false);
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = false;
        OnCloseAnimationEnd?.Invoke();
    }

    private void ApplyAnimation(float t, bool opening)
    {
        switch (_animationType)
        {
            case AnimationType.Scale:
                _rect.localScale = Vector3.Lerp(_scaleFrom, _scaleTo, t);
                break;

            case AnimationType.Move:
                _rect.anchoredPosition = Vector3.Lerp(_moveFrom, _moveTo, t);
                break;

            case AnimationType.Fade:
                if (_canvasGroup != null)
                    _canvasGroup.alpha = Mathf.Lerp(_alphaFrom, _alphaTo, t);
                break;
        }
    }

    public void UpdateScaleTo(Vector3 scaleTo) => _scaleTo = scaleTo;
#if UNITY_EDITOR 
    private void OnDrawGizmosSelected()
    {
        if (_rect == null)
            _rect = GetComponent<RectTransform>();
        
        if (_move)
        {
            Gizmos.color = Color.green;
            Vector3 worldFrom = _rect.TransformPoint(_moveFrom);
            Vector3 worldTo = _rect.TransformPoint(_moveTo);
            Gizmos.DrawSphere(worldFrom, _debugRadiusPoint);
            Gizmos.DrawSphere(worldTo, _debugRadiusPoint);
            Handles.Label(worldFrom, "Move From");
            Handles.Label(worldTo, "Move To");
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(worldFrom, worldTo);
        }
        
        if (_scale)
        {
            Gizmos.color = Color.cyan;
            Vector3 pos = _rect.position;
            Gizmos.DrawWireCube(pos, _scaleFrom);
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireCube(pos, _scaleTo);
        }
    }
#endif
}
