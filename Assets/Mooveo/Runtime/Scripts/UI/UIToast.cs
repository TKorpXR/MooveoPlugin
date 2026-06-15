using UnityEngine;
using UnityEngine.UIElements;

public static class UIToast
{
    public static void Show(VisualElement root, string title, string message, bool isSuccess, float durationMs = 3000f)
    {
        if (root == null) return;

        // Container
        var toast = new VisualElement();
        toast.AddToClassList("toast-container");
        toast.AddToClassList("toast--hidden"); // Start hidden
        
        if (isSuccess) toast.AddToClassList("toast--success");
        else toast.AddToClassList("toast--error");

        // Icon
        var icon = new VisualElement();
        icon.AddToClassList("toast-icon");
        // Using existing generic icon classes
        icon.AddToClassList(isSuccess ? "icon-status-ok" : "icon-close"); 
        toast.Add(icon);

        // Text container
        var textContainer = new VisualElement();
        textContainer.AddToClassList("toast-text-container");

        var titleLabel = new Label(title);
        titleLabel.AddToClassList("toast-title");
        textContainer.Add(titleLabel);

        var messageLabel = new Label(message);
        messageLabel.AddToClassList("toast-message");
        textContainer.Add(messageLabel);

        toast.Add(textContainer);
        root.Add(toast);

        // Fade In
        toast.schedule.Execute(() =>
        {
            toast.RemoveFromClassList("toast--hidden");
        }).StartingIn(10); // small delay to ensure DOM update

        // Fade Out
        toast.schedule.Execute(() =>
        {
            toast.AddToClassList("toast--hidden");
            
            // Remove from hierarchy after animation
            toast.schedule.Execute(() =>
            {
                if (root.Contains(toast))
                {
                    root.Remove(toast);
                }
            }).StartingIn(300); // 300ms for exit animation
            
        }).StartingIn((long)durationMs);
    }
}
