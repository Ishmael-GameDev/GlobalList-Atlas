# GlobalList Atlas

Мод для Hollow Knight: каталог карт сообщества прямо в игре — поиск, скачивание, установка и управление редакторами карт.

A Hollow Knight mod: the community map catalog inside the game — search, download, install, and manage map editors.

---

# Русский

## Что это

GlobalList Atlas подключается к таблицам карт сообщества и позволяет скачивать и запускать карты, не выходя из игры. Мод сам раскладывает файлы по папкам редактора, следит за обязательными модами и делает бекапы перед каждой заменой.

## Каталоги

- **Platforming** — общая таблица GlobalList.
- **Events** — ивенты, коллаборация с русским комьюнити Events HK и Ивентового Сезона.
- **Architect Total Parser** — карты публичного сервера Architect. Открывается дев-режимом (кнопка с гаечным ключом справа в шапке) и заменяет Platforming и Events.

## Как открыть

Кнопка с глобусом в верхней части главного меню. Слева — список карт, справа — подробности выбранной карты.

## Список карт

- Поиск по названию и автору.
- Фильтры: звёзды, редактор, теги, верификация, статус (не скачано / установлено / запущено).
- Карты сгруппированы по лигам, цвет кнопки совпадает с цветом карты в таблице.
- Выбор перемещается мышью или клавишами вверх/вниз из настроек управления игры.
- Сверху видно состояние сети и то, какая карта сейчас запущена в каком редакторе. Цвета запущенных карт и пресетов запоминаются между сессиями.

## Architect Total Parser

- Сортировка по дате загрузки (новее выше) или по скачиваниям с группами 1000 / 500 / 250 / 100 / 50 / 25 / 10.
- Фильтры: редактор (New Architect, Legacy, Silksong Architect), сложность, длина, теги, статус. Цвета фильтров и значений в карточке — от красного к голубому.
- Дубликаты (одинаковые название, статы, описание и дата) скрываются, остаётся самая новая карта.
- Аватар карты справа от названия. Загрузка аватара видна полоской прогресса.
- Отметки: **●** — просмотрено (клик переключает игнор, только отмеченные, только неотмеченные), **▲** и **▼** — лайк и дизлайк. Отметки хранятся только у вас.
- Скачивания и лайки в карточке и в панели карты.
- Каталог кэшируется на диске на 6 часов. Кнопка **Перезагрузить** (рядом с гаечным ключом, держать) загружает его заново.
- Если сервер недоступен, появляется кнопка переключения на зеркало. Зеркало не включается само и сбрасывается при каждом открытии парсера.
- Если загрузка не удалась, мод повторяет её каждые 10 секунд.

## Панель карты

Показывает автора, редакторы, теги, оценку и данные о верификации. Ниже — состояние файлов и модов.

- **Скачать** — качает архив с Google Drive в папку мода. Загрузка не прерывается при переходе к другой карте, её можно отменить.
- **Запустить** — раскладывает файлы карты по папкам редактора. До этого шага карта не появится в редакторе.
- **Выключить карту** — переносит все активные файлы редактора в бекап, оставляя его пустым.
- **Переустановить** — скачивает архив заново с нуля.
- **Удалить** — удаляет файлы карты, а если карта запущена, убирает её и из папки редактора. Срабатывает по удержанию.
- **В избранное** и **Открыть в таблице** — в шапке панели.

## Редакторы и моды

Для каждого редактора карты видно, установлен ли он, включён ли и какая у него версия. Его можно установить, включить или выключить прямо отсюда. Включение и выключение — это перенос папки мода между `Mods` и `Mods/Disabled`, поэтому изменения вступают в силу только после перезапуска игры.

Так же отображаются обязательные публичные моды карты (из ссылки с шестерёнкой в таблице) и дополнительные моды, лежащие внутри архива карты. Публичные моды скачиваются из ModLinks с проверкой SHA256.

Если набор включённых модов вернулся к тому, каким был при запуске игры, мод сообщит, что перезапуск не нужен. Иначе внизу появится кнопка перезапуска, срабатывающая по удержанию.

Одновременно включённые Legacy Architect и New Architect конфликтуют — мод предупредит об этом.

## Бекапы

Перед каждым запуском карты содержимое папки редактора уезжает в папку вида `GlobalistInstaller_backup_дата`. Кнопка «Бекапы» открывает список: можно восстановить, удалить по одному или удалить все, кроме трёх свежих на редактор.

## Язык

Переключается кнопкой с флагом. Строки интерфейса лежат в JSON-файлах внутри мода; можно положить свой файл в папку `Localization` рядом с dll, и он перекроет встроенный.

---

# English

## What it is

GlobalList Atlas connects to the community map tables and lets you download and launch maps without leaving the game. It places files into the right editor folders, tracks required mods, and makes a backup before every replacement.

## Catalogs

- **Platforming** — the main GlobalList table.
- **Events** — event maps, a collaboration with the Russian community Events HK and Ивентовый Сезон.
- **Architect Total Parser** — maps from the public Architect server. Opened by dev mode (the wrench button on the right of the header), replaces Platforming and Events.

## Opening it

Use the globe button at the top of the main menu. The map list is on the left, details of the selected map on the right.

## Map list

- Search by name and author.
- Filters: stars, editor, tags, verification, status (not downloaded / installed / launched).
- Maps are grouped by league, and a button's color matches the map's color in the spreadsheet.
- Move the selection with the mouse or with the up/down keys from the game's control settings.
- The top shows network state and which map is currently launched in which editor. Colors of launched maps and presets are remembered between sessions.

## Architect Total Parser

- Sort by upload date (newer first) or by downloads, grouped 1000 / 500 / 250 / 100 / 50 / 25 / 10.
- Filters: editor (New Architect, Legacy, Silksong Architect), difficulty, length, tags, status. Filter colors run from red to blue.
- Duplicates (same name, stats, description and upload date) are hidden; the newest one stays.
- Avatar on the right of the title. Avatar loading is shown as a progress bar.
- Marks: **●** — seen (a click cycles through ignore, only marked, only unmarked), **▲** and **▼** — like and dislike. Marks are stored only for you.
- Downloads and likes in the card and in the panel.
- The catalog is cached on disk for 6 hours. The **Reload** button (next to the wrench, press and hold) loads it again.
- If the server is unreachable, a button offers to switch to the mirror. The mirror is never enabled automatically and resets each time the parser is opened.
- If a load fails, the mod retries every 10 seconds.

## Map panel

Shows the author, editors, tags, rating, and verification details, followed by file and mod status.

- **Download** — fetches the archive from Google Drive into the mod's folder. The download survives switching to another map and can be cancelled.
- **Launch** — copies the map files into the editor folders. The map does not appear in the editor before this step.
- **Unload map** — moves everything currently in the editor folders into a backup, leaving them empty.
- **Reinstall** — downloads the archive again from scratch.
- **Delete** — removes the map files, and also clears them from the editor folder if the map is launched. Press and hold to confirm.
- **Add to favorites** and **Open in the spreadsheet** — in the panel header.

## Editors and mods

For each editor the panel shows whether it is installed, whether it is enabled, and its version. You can install, enable, or disable it here. Enabling and disabling moves the mod folder between `Mods` and `Mods/Disabled`, so changes only take effect after a game restart.

Required public mods (from the gear link in the spreadsheet) and extra mods bundled inside the map archive are listed the same way. Public mods are downloaded from ModLinks with SHA256 verification.

If the set of enabled mods returns to what it was at startup, the mod tells you that no restart is needed. Otherwise a restart button appears at the bottom; press and hold it to confirm.

Legacy Architect and New Architect conflict when both are enabled, and the mod warns about it.

## Backups

Before every launch, the contents of the editor folder are moved into a folder named `GlobalistInstaller_backup_<date>`. The Backups button opens the list, where you can restore or delete them one by one, or delete all but the three newest per editor.

## Language

Switch it with the flag button. Interface strings live in JSON files inside the mod; you can put your own file into a `Localization` folder next to the dll and it will override the built-in one.
