using System.Linq;
using GlobalListAtlas.Configuration;
namespace GlobalListAtlas.UI;
public static class ConfigurationScreen
{
    // Бесполезен
    /*public static MenuScreen GetScreen(MenuScreen modListMenu, GlobalSettings settings)
    {
        var mapNumberLabels = Enumerable.Range(1, SheetConfig.MaxMapSlots)
            .Select(i => i.ToString())
            .ToArray();
        var elements = new Element[]
        {
            new KeyBind("Toggle Map List", settings.Keybinds.ToggleMapList),
        };
        _menuRef ??= new Menu("Globalist Map Downloader", elements);*
        return _menuRef.GetMenuScreen(modListMenu);
    }*/
}