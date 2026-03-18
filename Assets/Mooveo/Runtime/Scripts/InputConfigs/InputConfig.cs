using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "InputConfig", menuName = "Configs/InputConfig", order = 0)]
public class InputConfig : ScriptableObject
{
    [Header("Triggers")]
    public InputActionReference TriggerAction;

    public InputActionReference TriggerButton;
    
    [Header("Thumb")]
    public InputActionReference ThumbAction;
    public InputActionReference ThumbButton;

    [Header("Buttons")]
    public InputActionReference AButton;
    public InputActionReference BButton;
    
    [Header("Position")]
    public InputActionReference PositionAction;

    public InputActionReference ScrollAction;
    
    [Header("Rotation")]
    public InputActionReference RotationAction;
    
    [Header("IsTracked")]
    public InputActionReference IsTrackedAction;

    [Header("Optional Settings")]
    [Tooltip("Valeur minimale du trigger pour commencer à peindre")]
    public float minTriggerValue = 0.2f;
}
