using System;
using System.Collections.Generic;
using UnityEngine;

public abstract class CalibrationManagerBase: MonoBehaviour
{
    [Header("Mooveo Base Settings")] 
    [SerializeField] protected bool _autoInit = false;
    [Tooltip("La référence (souvent le joueur ou la caméra) pour définir la gauche/droite")]
    [SerializeField] protected Transform _referenceView;
    
    protected List<Vector3> _points = new List<Vector3>();
    protected List<Vector3> _normals = new List<Vector3>();
    
    public Vector3 PlayAreaCenter { get; protected set; }
    public Quaternion PlayAreaRotation { get; protected set; }
    public float PlayAreaWidth { get; protected set; }
    public float PlayAreaHeight { get; protected set; }
    public Vector3 PlayAreaForward { get; protected set; }

    private void Start()
    {
        if(_autoInit) Init(MooveoConfigManager.Load());
    }

    protected abstract void ApplyCalibration();
    public virtual void Init(MooveoConfig config)
    {
        if (config == null)
        {
           Debug.LogError($"Mooveo Config is null : {config}");
           return;
        }
        
        _points = config.Points;
        _normals = config.Normals;

        if (CalculatePlayAreaMaths())
        {
            ApplyCalibration();
        }
    }
    

    protected bool CalculatePlayAreaMaths()
    {
        if (_points == null || _points.Count < 3)
        {
            if (_points != null)
                Debug.LogWarning(
                    $"Pas assez de points pour calibrer la zone Mooveo. Nombre de points : {_points.Count}");
            else                 Debug.LogError($"_points est vide");
            return false;
        }

        PlayAreaCenter = CalculateCenter(_points, out int centerIndex);
        (Vector3 left, Vector3 right) = CalculateLeftAndRight(_points, centerIndex);
        
        PlayAreaWidth = Vector3.Distance(left, right) * 2f;
        
        float screenRatio = (float)Screen.width / Screen.height;
        PlayAreaHeight = PlayAreaWidth / screenRatio;

        if (_normals != null && _normals.Count > 0)
        {
            Vector3 avgNormal = Vector3.zero;
            foreach (Vector3 n in _normals) avgNormal += n;
            avgNormal /= _normals.Count;
            
            avgNormal.y = 0; 
            avgNormal.Normalize(); 
            PlayAreaForward = -avgNormal; 
        }
        else
        {
            Vector3 horizontalDir = (right - left).normalized;
            PlayAreaForward = Vector3.Cross(Vector3.up, horizontalDir);
        }

        PlayAreaRotation = Quaternion.LookRotation(-PlayAreaForward, Vector3.up);
        
        return true;
    }
    protected Vector3 CalculateCenter(List<Vector3> points, out int centerIndex)
    {
        Vector3 avg = (points[0] + points[1] + points[2]) / 3f;
        
        centerIndex = 0;
        float minDist = Vector3.Distance(points[0], avg);
        for (int i = 1; i < 3; i++)
        {
            float d = Vector3.Distance(points[i], avg);
            if (d < minDist)
            {
                minDist = d;
                centerIndex = i;
            }
        }
        return points[centerIndex];
    }

    protected (Vector3 left, Vector3 right) CalculateLeftAndRight(List<Vector3> points, int centerIndex)
    {
        List<Vector3> extremites = new List<Vector3>();
        for (int i = 0; i < points.Count; i++)
        {
            if (i != centerIndex)
                extremites.Add(points[i]);
        }

        Vector3 e1 = extremites[0];
        Vector3 e2 = extremites[1];

        Vector3 dir1 = (e1 - points[centerIndex]).normalized;
        Vector3 dir2 = (e2 - points[centerIndex]).normalized;
        
        Vector3 referenceRight = _referenceView != null ? _referenceView.right : Vector3.right;
        
        float dot1 = Vector3.Dot(dir1, referenceRight);
        float dot2 = Vector3.Dot(dir2, referenceRight);
        
        if (dot1 < 0f) return (e1, e2);
        else return (e2, e1);
    }
}
