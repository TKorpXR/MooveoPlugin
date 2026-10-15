using UnityEngine;

/// <summary>
/// Icônes dessinées au code, pour les cas où le projet n'a pas de fichier correspondant et où l'on
/// ne veut pas en dépendre. Elles sortent en BLANC : la couleur se pose au style
/// (<c>-unity-background-image-tint-color</c>), une seule texture sert donc partout.
/// </summary>
public static class UIIcons
{
    private const int Size = 128;

    private static Texture2D _warningTriangle;

    /// <summary>Triangle d'avertissement avec son point d'exclamation, évidé dans la forme.</summary>
    public static Texture2D WarningTriangle
    {
        get
        {
            if (_warningTriangle == null) _warningTriangle = BuildWarningTriangle();
            return _warningTriangle;
        }
    }

    private static Texture2D BuildWarningTriangle()
    {
        Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            name = "UIIcons.WarningTriangle",
            hideFlags = HideFlags.HideAndDontSave,   // survit aux changements de scène, invisible au projet
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color32[] pixels = new Color32[Size * Size];

        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            // Sur-échantillonnage 3x3 : sans lui les bords obliques du triangle crénellent.
            float coverage = 0f;
            for (int sy = 0; sy < 3; sy++)
            for (int sx = 0; sx < 3; sx++)
            {
                float u = (x + (sx + 0.5f) / 3f) / Size;
                float v = (y + (sy + 0.5f) / 3f) / Size;
                if (Inside(u, v)) coverage += 1f / 9f;
            }

            pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(coverage * 255f));
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }

    /// <summary>Triangle plein, moins le point d'exclamation (évidé, donc transparent).</summary>
    private static bool Inside(float u, float v)
    {
        // Sommet en haut, base en bas (v croît vers le haut en coordonnées de texture).
        if (!InTriangle(u, v, 0.5f, 0.97f, 0.03f, 0.13f, 0.97f, 0.13f)) return false;

        // Évidement : barre verticale + point.
        bool bar = u > 0.445f && u < 0.555f && v > 0.34f && v < 0.70f;
        bool dot = (u - 0.5f) * (u - 0.5f) + (v - 0.265f) * (v - 0.265f) < 0.058f * 0.058f;
        return !bar && !dot;
    }

    private static bool InTriangle(float px, float py,
                                   float ax, float ay, float bx, float by, float cx, float cy)
    {
        float d1 = Edge(px, py, ax, ay, bx, by);
        float d2 = Edge(px, py, bx, by, cx, cy);
        float d3 = Edge(px, py, cx, cy, ax, ay);

        bool negative = d1 < 0f || d2 < 0f || d3 < 0f;
        bool positive = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(negative && positive);
    }

    private static float Edge(float px, float py, float ax, float ay, float bx, float by)
        => (px - bx) * (ay - by) - (ax - bx) * (py - by);
}
