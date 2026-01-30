using UnityEngine;

public static class ColorTable
{
    public static readonly Color[] Colors =
    {
        new Color(1f, 0.2f, 0.2f), // Red
        new Color(0.2f, 1f, 0.2f), // Green
        new Color(0.2f, 0.5f, 1f)  // Blue
    };

    public static Color Get(int index)
    {
        if (index < 0 || index >= Colors.Length)
            index = 0;

        return Colors[index];
    }

    public static int Count => Colors.Length;
}
