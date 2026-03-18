using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GlobalSettings.Core.Instance.Core
{
    [DefaultExecutionOrder(-100)]
public class ExampleProjectSettings : MonoBehaviour
{
    public enum MyEnum
    {
        OPTION_1,
        OPTION_2,
        OPTION_3,
        NULL
    };
    [System.Serializable]
    public class CustomSettingClass_A
    {
        public string Name;
        public int Life;
    }
    [System.Serializable]
    public class CustomSettingClass_B
    {
        public MyEnum Type;
        public bool IsActive;
    }
    
    public static ExampleProjectSettings Instance;
    
    [Header("Some Settings")]
    [Tooltip("integer setting")] 
    public ObservableSetting<int> MyIntegerSetting = new ObservableSetting<int>() { Value = 10 };
    [Tooltip("float setting")] 
    public ObservableSetting<float> MyFloatSetting = new ObservableSetting<float>() { Value = 0.25f };
    [Tooltip("boolean setting")] 
    public ObservableSetting<bool> MyBooleanSetting = new ObservableSetting<bool>() { Value = true };
    [Tooltip("string setting")] 
    public ObservableSetting<string> MyStringSetting = new ObservableSetting<string>() { Value = "Default value of my string setting" };
    
    [Header("Some Custom Settings")]
    public ObservableSetting<CustomSettingClass_A> MyCustomClassSetting = new ObservableSetting<CustomSettingClass_A>() 
        { Value = {Life = 20, Name = "DefaultName"} };

    [Header("Some List Settings")] 
    public ObservableList<CustomSettingClass_B> MyCustomClassListSetting =
    new ObservableList<CustomSettingClass_B>()
    {
        ListSetter = new List<CustomSettingClass_B>()
        {
            new CustomSettingClass_B() {Type = MyEnum.OPTION_1, IsActive = false},
            new CustomSettingClass_B() {Type = MyEnum.OPTION_2, IsActive = true}
        }
    };

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        
#if !UNITY_EDITOR
        LoadFromJson();
#endif
    }

    private void OnValidate()
    {
        MyIntegerSetting.ForceNotify();
        MyFloatSetting.ForceNotify();
        MyBooleanSetting.ForceNotify();
        MyStringSetting.ForceNotify();
        MyCustomClassSetting.ForceNotify();
        
        MyCustomClassListSetting.ForceNotify();
    }

    [ContextMenu("Save Settings Now")]
    private void SaveDebug() => SaveGlobalSettings();

    [ContextMenu("Load Settings Now")]
    private void LoadDebug() => LoadGlobalSettings();
    
    public void SaveGlobalSettings()
    {
        JsonHelpers.Save(this, "globalsettings");
        //sonHelpers.SaveToFullPath(this, "MyFullPath/MyFolder/FileName");
    }

    public void LoadGlobalSettings()
    {
        string path = System.IO.Path.Combine(Application.persistentDataPath, "globalsettings.json");
        
        string json = System.IO.File.ReadAllText(path);
        JsonUtility.FromJsonOverwrite(json, this);
    }
}
}

