using GlobalListAtlas.Configuration;

namespace GlobalListAtlas.UI;

public static class VpnGuide
{
    private static readonly string[] Russian =
    {
        "# Доступ к Architect",
        "",
        "Если сервер Architect не отвечает, обычно мешает блокировка. Сначала попробуйте способ ниже, он почти всегда помогает.",
        "",
        "## 1. TUN-режим Xray (рекомендуется)",
        "",
        "Включите TUN в вашем клиенте (Xray, Happ или Clash). Он направляет весь трафик системы через туннель, поэтому мод ничего не требует. Если TUN работает, остальные способы не нужны.",
        "",
        "## 2. Clash",
        "",
        "- Откройте вкладку **Rules**.",
        "- Нажмите карандаш справа сверху.",
        "- Добавьте правило **DOMAIN-SUFFIX** со значением `pythonanywhere.com`.",
        "- Направьте правило через основной сервер (лучше общий).",
        "- Правило должно стоять выше правил для России (GEOIP RU и подобных), иначе сработают они.",
        "",
        "## 3. Happ",
        "",
        "- Нажмите стрелку справа на плашке подключаемой локации.",
        "- В конфигурации в правилах маршрутизации добавьте в самое начало списка правило: домен `domain:pythonanywhere.com` с тегом основного сервера, например `proxy`. Правило должно стоять выше правил для России, иначе они сработают первыми.",
        "",
        "---",
        "",
        "Если ничего из этого не помогает, автор мода сделал зеркало. Его можно включить кнопкой, когда сервер недоступен, но оно **не рекомендуется**: работает медленнее и может не отвечать. Сначала попробуйте настроить доступ сами.",
    };

    private static readonly string[] English =
    {
        "# Access to Architect",
        "",
        "If the Architect server does not respond, a block is usually the cause. Try the method below first, it almost always helps.",
        "",
        "## 1. Xray TUN mode (recommended)",
        "",
        "Turn on TUN in your client (Xray, Happ or Clash). It routes all system traffic through the tunnel, so the mod needs no setup. If TUN works, the other methods are not needed.",
        "",
        "## 2. Clash",
        "",
        "- Open the **Rules** tab.",
        "- Click the pencil at the top right.",
        "- Add a **DOMAIN-SUFFIX** rule with the value `pythonanywhere.com`.",
        "- Route it through your main server (preferably the shared one).",
        "- The rule must be above the Russian rules (GEOIP RU and similar), otherwise those win.",
        "",
        "## 3. Happ",
        "",
        "- Click the arrow on the right of the location plate.",
        "- In the config, add to the very top of the routing rules: domain `domain:pythonanywhere.com` with the tag of your main server, for example `proxy`. It must come before the Russian rules, otherwise they match first.",
        "",
        "---",
        "",
        "If none of this helps, the mod author made a mirror. It can be enabled with a button when the server is unreachable, but it is **not recommended**: it is slower and may not respond. Try to configure access yourself first.",
    };

    public static string Title() =>
        Localization.CurrentLanguage == Configuration.Language.Russian ? "Доступ к Architect" : "Access to Architect";

    public static string Markdown() =>
        string.Join("\n", Localization.CurrentLanguage == Configuration.Language.Russian ? Russian : English);
}
