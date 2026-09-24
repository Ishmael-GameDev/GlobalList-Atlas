using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GlobalListAtlas.UI;

public static class PopupStack
{
    private static readonly List<GameObject> Stack = new();

    public static bool Any
    {
        get
        {
            Prune();
            return Stack.Count > 0;
        }
    }

    public static void Register(GameObject popupCanvas)
    {
        if (popupCanvas == null) return;

        Prune();
        Stack.Add(popupCanvas);
    }

    public static void Close(GameObject popupCanvas)
    {
        if (popupCanvas == null) return;

        Stack.Remove(popupCanvas);
        Object.Destroy(popupCanvas);
    }

    public static bool CloseTop()
    {
        Prune();
        if (Stack.Count == 0) return false;

        var top = Stack[Stack.Count - 1];
        Stack.RemoveAt(Stack.Count - 1);
        Object.Destroy(top);
        return true;
    }

    public static void CloseAll()
    {
        Prune();
        foreach (var popup in Stack.ToList())
            Object.Destroy(popup);

        Stack.Clear();
    }

    // Окно могли уничтожить мимо стека — чистим "мёртвые" ссылки
    private static void Prune() => Stack.RemoveAll(p => p == null);
}
