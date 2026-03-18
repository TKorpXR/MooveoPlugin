using GlobalSettings.Core;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Component attaché au curseur permettant de naviguer dans l'interface utilisateur de Mooveo
/// Se cmponent permet de gérer l'aspect visuel du curseur (position, taille, sprite)
/// /// </summary>
public class CalibrationCursorUI : MonoBehaviour
{
    [SerializeField] private Image _defaultCursorImage, _uiCursor, _uiCursorBorder;
    [SerializeField] private float _scaleMultiplier = 1.3f;
    [SerializeField] private CursorInteractor _cursorInteractor;
    
    private Image _cursor;
    public RectTransform RectTransform => gameObject.GetComponent<RectTransform>();
    public CursorInteractor CursorInteractor => _cursorInteractor;
    
    /// <summary>
    /// Initialise le curseur.
    /// Le curseur récupère son taux de scale par defaut set dans les <see cref="Instance"/> qu'on peut aussi retrouver dans un json en build
    /// Et notifie le <see cref="CalibrationManager"/> qu'il est pret a etre ajouter en tant que curseur
    /// Enfin il initialise le <see cref="CursorInteractor"/>
     /// </summary>
    public void Init()
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
        
        _cursorInteractor.Init();
        _cursor = _defaultCursorImage;
    }
    
    
    /// <summary>
    /// Met a jour la position du curseur et son scale
    /// </summary>
    /// <param name="controller">Transform de la manette associée dans le world</param>
    /// <param name="cam">La camera qui affiche actuellement l'activité</param>
    /// <param name="simulatedRadius">Radius temporaire pour les tests</param>
    public void UpdateCursor(Transform controller, Camera cam, float simulatedRadius)
    {
        if (_cursor == null || cam == null) return;

        RectTransform canvasRect = _cursor.canvas.GetComponent<RectTransform>();
        Vector2 localPoint;
        
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                cam.WorldToScreenPoint(controller.position),
                cam,
                out localPoint))
        {
            RectTransform.localPosition = localPoint;
        }
        
        float scale = simulatedRadius * _scaleMultiplier;
        transform.localScale = new Vector3(scale, scale, scale);
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
