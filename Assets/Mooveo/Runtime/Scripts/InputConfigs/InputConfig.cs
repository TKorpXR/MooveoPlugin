using System;
using UnityEngine;
using UnityEngine.InputSystem;
using HTC.UnityPlugin.Vive;
using HTC.UnityPlugin.VRModuleManagement;
using NaughtyAttributes;

public enum ConfigType
{
    InputSystem,
    ViveInputUtility
}
[CreateAssetMenu(fileName = "InputConfig", menuName = "Configs/InputConfig", order = 0)]
[Serializable]
public class InputConfig : ScriptableObject
{
    [Header("Configuration Type")]
    [SerializeField] private ConfigType _type = ConfigType.InputSystem;
    
    [ShowIf("_type", ConfigType.InputSystem), Header("Input System")]
    [Header("Triggers")]
    public InputActionReference TriggerAction;

    [ShowIf("_type", ConfigType.InputSystem)] public InputActionReference TriggerButton;
    
    [ShowIf("_type", ConfigType.InputSystem), Header("Thumb")]
    public InputActionReference ThumbAction;
    [ShowIf("_type", ConfigType.InputSystem)] public InputActionReference ThumbButton;

    [ShowIf("_type", ConfigType.InputSystem), Header("Buttons")]
    public InputActionReference AButton;
    [ShowIf("_type", ConfigType.InputSystem)] public InputActionReference BButton;
    
    [ShowIf("_type", ConfigType.InputSystem), Header("Position")]
    public InputActionReference PositionAction;

    [ShowIf("_type", ConfigType.InputSystem)] public InputActionReference ScrollAction;
    
    [ShowIf("_type", ConfigType.InputSystem), Header("Rotation")]
    public InputActionReference RotationAction;
    
    [ShowIf("_type", ConfigType.InputSystem), Header("IsTracked")]
    public InputActionReference IsTrackedAction;

    [Header("Optional Settings")]
    [Tooltip("Valeur minimale du trigger pour commencer à peindre")]
    public float minTriggerValue = 0.2f;
    
    [ShowIf("_type", ConfigType.ViveInputUtility), Header("Vive Input Utility (RAW Pogo Pins)")]
    [Tooltip("Si défini sur un TrackerRole (ex: Tracker1), le script lira les inputs bruts de ce tracker.")]
    public TrackerRole VIUTargetRole = TrackerRole.Tracker1;

    [ShowIf("_type", ConfigType.ViveInputUtility), Tooltip("Axe analogique pour la gâchette")]
    public VRModuleRawAxis VIUTriggerAxis = VRModuleRawAxis.Trigger;
    
    [ShowIf("_type", ConfigType.ViveInputUtility), Tooltip("Bouton numérique pour le clic de la gâchette")]
    public VRModuleRawButton VIUTriggerButton = VRModuleRawButton.Trigger;
    
    [ShowIf("_type", ConfigType.ViveInputUtility), Tooltip("Bouton mappé sur le bouton A (Undo/Redo)")]
    public VRModuleRawButton VIUAButton = VRModuleRawButton.Grip;
    
    [ShowIf("_type", ConfigType.ViveInputUtility), Tooltip("Bouton mappé sur le bouton B (Undo/Redo)")]
    public VRModuleRawButton VIUBButton = VRModuleRawButton.ApplicationMenu;
    
    [ShowIf("_type", ConfigType.ViveInputUtility), Tooltip("Bouton mappé sur le Thumb (Menu Mooveo)")]
    public VRModuleRawButton VIUThumbButton = VRModuleRawButton.Axis0;
}
