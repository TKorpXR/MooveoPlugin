using UnityEngine;
using UnityEngine.UIElements;

public static class UIToast
{
    public enum Kind { Success, Error, Warning }

    public static void Show(VisualElement root, string title, string message, bool isSuccess, float durationMs = 3000f)
        => Show(root, title, message, isSuccess ? Kind.Success : Kind.Error, durationMs);

    private static NotificationLevel ToLevel(Kind kind)
    {
        switch (kind)
        {
            case Kind.Error: return NotificationLevel.Error;
            case Kind.Warning: return NotificationLevel.Warning;
            default: return NotificationLevel.Info;
        }
    }

    /// <summary>
    /// ⚠ Compatibilité. Les 32 appels existants passent par ici sans changer d'un caractère, mais
    /// sont REDIRIGÉS vers <see cref="NotificationCenter"/> dès qu'un hôte global existe : un toast
    /// lié à la racine d'un panneau disparaît avec lui et ne peut rien afficher avant son ouverture.
    /// Le rendu local ne sert plus que de repli — notamment l'overlay de calibration, qui vit dans
    /// une scène où l'hôte global n'existe pas.
    /// </summary>
    public static void Show(VisualElement root, string title, string message, Kind kind, float durationMs = 3000f)
    {
        if (NotificationHost.Instance != null)
        {
            NotificationCenter.Notify(ToLevel(kind), title, message, durationMs: durationMs);
            return;
        }

        if (root == null) return;

        // Container
        var toast = new VisualElement();
        toast.AddToClassList("toast-container");
        toast.AddToClassList("toast--hidden"); // Start hidden

        switch (kind)
        {
            case Kind.Success: toast.AddToClassList("toast--success"); break;
            case Kind.Warning: toast.AddToClassList("toast--warning"); break;
            default:           toast.AddToClassList("toast--error");   break;
        }

        // Icon
        var icon = new VisualElement();
        icon.AddToClassList("toast-icon");
        switch (kind)
        {
            case Kind.Success: icon.AddToClassList("icon-status-ok"); break;
            case Kind.Warning: icon.AddToClassList("icon-status-warning"); break;
            default:           icon.AddToClassList("icon-close"); break;
        }
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
