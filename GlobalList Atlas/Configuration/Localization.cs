using System.Collections.Generic;
using UnityEngine;

namespace GlobalListAtlas.Configuration;

public enum Language
{
    English,
    Russian
}

public static class Localization
{
    public static Language CurrentLanguage { get; private set; } = Language.English;
    private static readonly Dictionary<string, Dictionary<Language, string>> Translations = new();

    public static void Initialize()
    {
        if (Translations.Count > 0) return;

        // Статус-бар
        Add("status.online", "Online", "Онлайн");
        Add("status.offline", "Offline", "Оффлайн");
        Add("status.no_network", "No network", "Нет сети");
        Add("status.ping", "Ping", "Пинг");
        Add("status.free", "Free", "Свободно");
        Add("status.dash", "—", "—");

        // Кнопки бара
        Add("button.auto_save", "Auto-save", "Автосохранение");

        // Панель списка
        Add("list.select_map", "Select a map from the list on the left", "Выберите карту в списке слева");
        Add("list.loading_catalog", "Loading catalog...", "Загрузка каталога...");
        Add("list.no_connection_retry", "No connection, retry... ({0})", "Нет соединения, повтор... ({0})");
        Add("list.filter_stars", "★ ▾", "★ ▾");
        Add("list.filter_editor", "Editor ▾", "Редактор ▾");
        Add("list.filter_tags", "Tags ▾", "Теги ");
        Add("list.filter_verified", "Verified ▾", "Вериф. ▾");
        Add("list.filter_status", "Status ▾", "Статус ▾");

        // Фильтра
        Add("filter.stars.title", "Filter by stars", "Фильтр по звёздам");
        Add("filter.stars.no_stars", "0 ★ (no stars)", "0 ★ (без звёзд)");
        Add("filter.verified.title", "Filter by verification", "Фильтр по верификации");
        Add("filter.verified.verified", "Verified", "Верифицировано");
        Add("filter.verified.not_verified", "Not verified", "Не верифицировано");
        Add("filter.editor.title", "Filter by editor", "Фильтр по редактору");
        Add("filter.tags.title", "Filter by tags", "Фильтр по тегам");
        Add("filter.status.title", "Filter by status", "Фильтр по статусу");
        Add("filter.status.not_downloaded", "Not downloaded", "Не скачано");
        Add("filter.status.installed", "Installed", "Установлено");
        Add("filter.status.running", "Running", "Запущено");

        // Статусы карт
        Add("map.status.downloaded", "Downloaded", "Скачано");
        Add("map.status.launched", "Map launched", "Карта запущена");

        // Кнопки действий
        Add("button.download", "Download map files", "Скачать файлы карты");
        Add("button.launch", "Launch", "Запустить");
        Add("button.launching", "Launching...", "Запуск...");
        Add("button.open_editor_folder", "📁 Open editor folder", "📁 Открыть папку редактора");
        Add("button.open_mods_folder", " Open mods folder", "📁 Открыть папку модов");
        Add("button.install_public_mods", "Install required mods", "Установить обязательные моды");
        Add("button.install_additional_mods", "Install additional mods", "Установить доп. моды");
        Add("button.reinstall", "🔄 Reinstall", "🔄 Переустановить");
        Add("button.reinstalling", "Reinstalling...", "Переустановка...");
        Add("button.cancel_download", "Cancel download", "Отменить скачивание");
        Add("button.installing", "Installing...", "Установка...");

        // Бейджи
        Add("badge.files", "Map files", "Файлы карты");
        Add("badge.files_available", "Available for download", "Доступны для скачивания");
        Add("badge.files_missing", "Link missing", "Ссылка отсутствует");
        Add("badge.public_mods", "Required public mods", "Публичные обязательные моды");
        Add("badge.additional_mods", "Additional mods", "Доп. моды");
        Add("badge.additional_mods_after_download", "Available after downloading the map", "Доступно после скачивания карты");
        Add("badge.yes", "Yes", "Есть");
        Add("badge.no", "No", "Нет");

        // Текстовые файлы
        Add("txt.readme_found", "Readme file found — definitely worth reading: {0}", "Найден файл Readme — точно стоит прочитать: {0}");
        Add("txt.file_found", "Text file found — possibly worth reading: {0}", "Найден текстовый файл — возможно, стоит прочитать: {0}");

        // Скачиванин и переустановка
        Add("txt.readme_downloaded", "Readme file found ({0}) — definitely worth reading. It will be moved to the editor's folder when the map is launched.", "Найден файл Readme ({0}) — точно стоит прочитать. Он будет перемещён в папку редактора при запуске карты.");
        Add("txt.file_downloaded", "Text file found ({0}) — possibly worth reading. It will be moved to the editor's folder when the map is launched.", "Найден текстовый файл ({0}) — возможно, стоит прочитать. Он будет перемещён в папку редактора при запуске карты.");

        Add("txt.readme_reinstalled", "Map reinstalled. Readme file found ({0}) — definitely worth reading. It will be moved to the editor's folder when the map is launched.", "Карта переустановлена. Найден файл Readme ({0}) — точно стоит прочитать. Он будет перемещён в папку редактора при запуске карты.");
        Add("txt.file_reinstalled", "Map reinstalled. Text file found ({0}) — possibly worth reading. It will be moved to the editor's folder when the map is launched.", "Карта переустановлена. Найден текстовый файл ({0}) — возможно, стоит прочитать. Он будет перемещён в папку редактора при запуске карты.");

        Add("txt.and_more", "{0} and {1} more", "{0} и ещё {1}");

        // Моды
        Add("mods.needed", "Needed:", "Нужно:");
        Add("mods.installed", "Installed ({0}/{1}):", "Установлено ({0}/{1}):");
        Add("mods.none", "  — none", "  — ничего");
        Add("mods.all_installed", "All installed!", "Всё установлено!");
        Add("mods.installed_success", "Successfully installed", "Успешно установлено");
        Add("mods.already_installed", "Already installed", "Уже было установлено");
        Add("mods.nothing_to_install", "Nothing to install", "Нечего устанавливать");
        Add("mods.installed_restart", "Additional mods installed. To make them work, you need to fully restart the game.", "Доп. моды установлены. Для их работы нужно полностью перезапустить игру.");
        Add("mods.public_installed", "Mods installed: {0}. To make them work, you need to fully restart the game.", "Установлены моды: {0}. Для их работы нужно полностью перезапустить игру.");
        Add("mods.public_failed", "Failed to install some mods: {0}", "Не удалось установить некоторые моды: {0}");
        Add("mods.public_all_installed", "All required mods are already installed.", "Все обязательные моды уже были установлены.");

        // Ошибка
        Add("error.no_editor", "Map has no editor specified.", "У карты не указан редактор.");
        Add("error.no_editor_folder", "There is no standard working folder for the specified map editors.", "Для указанных редакторов карт нет стандартной рабочей папки.");
        Add("error.open_editor_folder", "Error opening editor folder: {0}", "Ошибка при открытии папки редактора: {0}");
        Add("error.open_mods_folder", "Error opening mods folder: {0}", "Ошибка при открытии папки модов: {0}");
        Add("error.offline_download", "Cannot download map in offline mode. Disable offline mode to download files.", "Невозможно скачать карту в оффлайн-режиме. Отключите оффлайн-режим для загрузки файлов.");
        Add("error.offline_reinstall", "Cannot reinstall map in offline mode. Disable offline mode to download files.", "Невозможно переустановить карту в оффлайн-режиме. Отключите оффлайн-режим для загрузки файлов.");
        Add("error.generic", "Error: {0}", "Ошибка: {0}");
        Add("error.editor_not_installed", "Required map editor is not installed: {0}", "Требуемый редактор карт не установлен: {0}");
        Add("error.reinstall_failed", "Reinstallation error: {0}", "Ошибка переустановки: {0}");
        Add("error.large_file", "Map file is quite large ({0:F1} MB). Downloading may take time, please wait...", "Файл карты достаточно большой ({0:F1} МБ). Скачивание может занять время, подождите...");

        // Панель деталей
        Add("panel.editor", "Editor", "Редактор");
        Add("panel.tags", "Tags", "Теги");
        Add("panel.rating", "Rating", "Оценка");
        Add("panel.verified", "Verified", "Верифицировано");
        Add("panel.not_verified", "Not verified", "Не верифицировано");
        Add("panel.verified_by_unknown", "by unknown", "неизвестно кем");
        Add("panel.not_specified", "not specified", "не указан");
        Add("panel.none", "none", "нет");

        // Форматирование
        Add("format.mb", "MB", "МБ");
        Add("format.kb", "KB", "КБ");
        Add("format.ms", "ms", "мс");

        // Загрузка каталога
        Add("error.manager_unavailable", "Download manager unavailable", "Менеджер загрузки недоступен");
        Add("catalog.loading_percent", "Loading table... {0}% ({1}/{2})", "Загрузка таблицы... {0}% ({1}/{2})");
        Add("catalog.loading_bytes", "Loading table... {0}", "Загрузка таблицы... {0}");

        // Лиги
        Add("league.void", "VOID LEAGUE", "ПУСТОТНАЯ ЛИГА");
        Add("league.diamond", "DIAMOND LEAGUE", "АЛМАЗНАЯ ЛИГА");
        Add("league.gold", "GOLD LEAGUE", "ЗОЛОТАЯ ЛИГА");
        Add("league.silver", "SILVER LEAGUE", "СЕРЕБРЯНАЯ ЛИГА");
        Add("league.bronze", "BRONZE LEAGUE", "БРОНЗОВАЯ ЛИГА");
        Add("league.white", "WHITE LEAGUE", "ЛОКС ЛИГА");
        Add("league.unknown", "OTHER", "ДРУГОЕ");

        // Кнопки статус-бара
        Add("button.auto_save", "Auto-save", "Автосохранение");
        Add("button.auto_save_on", "Auto-save ON", "Автосохранение ВКЛ");
        Add("button.auto_save_off", "Auto-save OFF", "Автосохранение ВЫКЛ");

        // Заголовки фильтров
        Add("filter.stars.title", "Filter by stars", "Фильтр по звёздам");
        Add("filter.stars.no_stars", "0 ★ (no stars)", "0 ★ (без звёзд)");
        Add("filter.verified.title", "Filter by verification", "Фильтр по верификации");
        Add("filter.verified.verified", "Verified", "Верифицировано");
        Add("filter.verified.not_verified", "Not verified", "Не верифицировано");
        Add("filter.editor.title", "Filter by editor", "Фильтр по редактору");
        Add("filter.tags.title", "Filter by tags", "Фильтр по тегам");
        Add("filter.status.title", "Filter by status", "Фильтр по статусу");
        Add("filter.status.not_downloaded", "Not downloaded", "Не скачано");
        Add("filter.status.installed", "Installed", "Установлено");
        Add("filter.status.running", "Running", "Запущено");
        Add("filter.show_all", "Show all", "Показать все");

        // Подсказки (не используются)
        Add("hint.auto_save_missing", "Table not saved. Enable to use offline mode.", "Таблица не сохранена для работы в оффлайн-режиме.");
        Add("hint.auto_save_outdated", "Table might be outdated. Click to update.", "Таблица может быть устаревшей. Обновить.");

        // Уведомления автосохранения
        Add("autosave.enabled", "Auto-save enabled. The catalog will now update on startup.", "Автосохранение включено. Каталог будет обновляться при запуске.");
        Add("autosave.disabled", "Auto-save disabled. The catalog will no longer update on startup. Offline mode will use the last saved version.", "Автосохранение выключено. Каталог больше не будет обновляться при запуске. Оффлайн-режим будет использовать последнюю сохранённую версию.");

        Logging.Log.Info("[Localization] Инициализировано переводов: " + Translations.Count);
    }

    private static void Add(string key, string en, string ru)
    {
        if (!Translations.ContainsKey(key))
            Translations[key] = new Dictionary<Language, string>();
        Translations[key][Language.English] = en;
        Translations[key][Language.Russian] = ru;
    }

    public static string Get(string key)
    {
        if (Translations.TryGetValue(key, out var dict))
        {
            if (dict.TryGetValue(CurrentLanguage, out var text))
                return text;
            if (dict.TryGetValue(Language.English, out var en))
                return en;
        }
        // Fallback: возвращаем ключ — перевод отсутствует
        return key;
    }

    public static string Get(string key, params object[] args)
    {
        string template = Get(key);
        try { return string.Format(template, args); }
        catch { return template; }
    }

    public static void SetLanguage(Language lang)
    {
        CurrentLanguage = lang;
    }
}