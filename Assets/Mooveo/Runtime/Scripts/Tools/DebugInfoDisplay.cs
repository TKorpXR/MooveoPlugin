using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.InputSystem;
using System.Linq;

public class DebugInfoDisplay : MonoBehaviour
{
    private static Vector3 _currentUIScale = Vector3.zero;
    
    [Header("Settings")]
    [SerializeField, Tooltip("Intervalle de rafraîchissement des textes en secondes")] 
    private float _updateInterval = 0.5f;

    private bool _isVisible = false;

    private int _frameCount = 0;
    private float _timeAccumulator = 0f;
    private float _averageFps = 0f;
    private float _currentFps = 0f;
    
    private string _perfText = "";
    private string _memoryText = "";
    private string _systemText = "";

    private void Start()
    {
        if (UIInputToolDebugger.Instance != null)
        {
            UIInputToolDebugger.Instance.ToggleDebugger(_isVisible);
        }
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
        {
            _isVisible = !_isVisible;
            
            if (UIInputToolDebugger.Instance != null)
            {
                UIInputToolDebugger.Instance.ToggleDebugger(_isVisible);
            }
        }

        if (!_isVisible) return;

        var controller = FindObjectsOfType<DefaultController>().FirstOrDefault(c => c.PlayerID == 0);
        if (controller != null && Keyboard.current != null)
        {
            // TOUCHE J (Trigger)
            /*if (Keyboard.current.jKey.wasPressedThisFrame)
            {
                controller.HandleTriggerPressed();
                controller.HandleTrigger(1f);
            }*/
            if (Keyboard.current.jKey.isPressed)
            {
                controller.SimulateTrigger = true;
            }
            if (Keyboard.current.jKey.wasReleasedThisFrame)
            {
                controller.SimulateTrigger = false;
            }

            // TOUCHE K (Bouton A)
            if (Keyboard.current.kKey.wasPressedThisFrame)
            {
                controller.HandleAPressed();
            }

            // TOUCHE L (Bouton Thumb)
            if (Keyboard.current.lKey.wasPressedThisFrame)
            {
                /*if (controller is PainterController painterController)
                {
                    painterController.ForceCenterCursor();
                }
                controller.HandleThumb();*/
            }
        }

        _currentFps = 1.0f / Mathf.Max(Time.unscaledDeltaTime, 0.00001f);
        
        _frameCount++;
        _timeAccumulator += Time.unscaledDeltaTime;

        if (_timeAccumulator >= _updateInterval)
        {
            _averageFps = _frameCount / _timeAccumulator;
            
            UpdateDebugStrings();

            _frameCount = 0;
            _timeAccumulator = 0f;
        }
    }

    private void UpdateDebugStrings()
    {
 
        float frameTimeMs = 1000.0f / Mathf.Max(_averageFps, 0.001f);
        _perfText = $"FPS: {_averageFps:F1} (Instantané: {_currentFps:F0}) | FrameTime: {frameTimeMs:F2} ms";
        
        long ramAllocated = Profiler.GetTotalAllocatedMemoryLong() / 1048576;
        long ramReserved = Profiler.GetTotalReservedMemoryLong() / 1048576;
        long vramGraphics = Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576;
        
        _memoryText = $"RAM (Allouée / Réservée): {ramAllocated} MB / {ramReserved} MB\n" +
                      $"VRAM GPU (Textures/Mesh): {vramGraphics} MB";
        
        _systemText = $"Screen Resolution: {Screen.width} x {Screen.height} @ {(int)Screen.currentResolution.refreshRateRatio.value}Hz\n" +
                      $"Screen Ratio: {(float)Screen.width / Screen.height:F2}";
    }

    private void OnGUI()
    {
        if (!_isVisible) return;

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            richText = true
        };
        style.normal.textColor = Color.yellow;
        
        GUI.backgroundColor = new Color(0, 0, 0, 0.8f);
        GUILayout.BeginArea(new Rect(10, 10, Screen.width - 20, Screen.height - 20), GUI.skin.box);
        GUILayout.BeginVertical();

        GUILayout.Label("<color=#00FFFF><b>--- GRAFFWALL PROFILER ---</b></color>", style);
        GUILayout.Space(10);
        
        if (_averageFps < 60f) style.normal.textColor = Color.red;
        else if (_averageFps < 90f) style.normal.textColor = new Color(1f, 0.5f, 0f); // Orange
        else style.normal.textColor = Color.green;
        
        GUILayout.Label($"<b>{_perfText}</b>", style);
        
        style.normal.textColor = Color.white;
        GUILayout.Label(_memoryText, style);
        
        GUILayout.Space(15);
        
        style.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
        GUILayout.Label(_systemText, style);
        
        GUILayout.Label($"GlobalSettings ScreenRatio: {GlobalSettings.Core.GlobalSettings.ScreenRatio:F2}", style);
        GUILayout.Label($"GlobalSettings ScreenSize: {GlobalSettings.Core.GlobalSettings.ScreenSize}", style);
        
#if ENABLE_INPUT_SYSTEM
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if(mouse != null)
             GUILayout.Label($"InputSystem Mouse Pos: {mouse.position.ReadValue()}", style);
#endif
        GUILayout.Label($"Current UI Scale: {_currentUIScale}", style);

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    public static void SetCurrentUIScale(Vector3 scale) => _currentUIScale = scale;
}