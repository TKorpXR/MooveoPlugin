using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Component attaché au curseur permettant de naviguer dans l'interface utilisateur de Mooveo
/// Se cmponent permet de gérer l'aspect visuel du curseur (position, taille, sprite)
/// /// </summary>
public class CalibrationCursorUI : MonoBehaviour
{
    [SerializeField] private Image _defaultCursorImage, _uiCursor, _uiCursorBorder, _toolIcon, _toolIcon2;
    [SerializeField] private float _scaleMultiplier = 1.3f;
    [SerializeField] private CursorInteractor _cursorInteractor;
    
    private Image _cursor;
    public RectTransform RectTransform => gameObject.GetComponent<RectTransform>();
    public CursorInteractor CursorInteractor => _cursorInteractor;
    
    /// <summary>
    /// Initialise le curseur.
    /// Le curseur récupère son taux de scale par defaut set dans les <see cref="GlobalSettings"/> qu'on peut aussi retrouver dans un json en build
    /// Et notifie le <see cref="CalibrationManager"/> qu'il est pret a etre ajouter en tant que curseur
    /// Enfin il initialise le <see cref="CursorInteractor"/>
     /// </summary>
    public void Init(CalibrationController controller)
    {
        GlobalSettings.Core.GlobalSettings.Instance.CursorFactorScale.Bind(() => _scaleMultiplier, value => _scaleMultiplier = value);
        
        if (UICalibrationToolkit.instance != null)
        {
            UICalibrationToolkit.instance.AddCursor(this);
        }
        else if (UICalibrationManager.instance != null)
        {
            UICalibrationManager.instance.AddCursor(this);
        }
        
        _cursorInteractor.Init(true);
        _cursor = _defaultCursorImage;
        controller.OnLockRotationChanged += SetToolIcon;
        controller.OnLockSizeChanged += SetToolIconN2;
    }
    
    
    /// <summary>
    /// Met a jour la position du curseur et son scale
    /// </summary>
    /// <param name="hitPoint">Position 3D de l'impact du rayon sur le mur</param>
    /// <param name="simulatedRadius">Radius temporaire pour les tests</param>
    public void UpdateCursor(Vector3 hitPoint, float simulatedRadius)
    {
        if (_cursor == null) return;

        RectTransform canvasRect = _cursor.canvas.GetComponent<RectTransform>();
        
        // Utilise InverseTransformPoint pour placer le curseur exactement sur le point d'impact
        Vector3 localPoint3D = canvasRect.InverseTransformPoint(hitPoint);
        RectTransform.localPosition = new Vector3(localPoint3D.x, localPoint3D.y, 0f);
        
        float scale = simulatedRadius * _scaleMultiplier;
        transform.localScale = new Vector3(scale, scale, scale);
    }

    public void SetVisibility(bool isVisible)
    {
        if (_cursor != null)
        {
            _cursor.enabled = isVisible;
        }
    }
    
    private void SetToolIcon(bool isVisible)
    {
        _toolIcon.enabled = isVisible;
    }

    private void SetToolIconN2(bool isVisible)
    {
        _toolIcon2.enabled = isVisible;
    }
    
    public void Destroy()
    {
        if (UICalibrationToolkit.instance != null)
        {
            UICalibrationToolkit.instance.RemoveCursor(this);
        }
        else if (UICalibrationManager.instance != null)
        {
            UICalibrationManager.instance.RemoveCursor(this);
        }
        
        Destroy(this.gameObject);
    }
}
