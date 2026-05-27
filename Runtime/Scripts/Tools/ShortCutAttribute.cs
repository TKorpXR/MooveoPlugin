using System;

[AttributeUsage(AttributeTargets.Method)]
public class ShortCutAttribute : Attribute
{
    public string DisplayName { get; }

    public ShortCutAttribute(string displayName)
    {
        DisplayName = displayName;
    }
}
