# GlobalList Atlas

Мод для Hollow Knight: каталог карт сообщества прямо в игре — поиск, скачивание, установка и управление редакторами карт.

A Hollow Knight mod: the community map catalog inside the game — search, download, install, and manage map editors.

---

# Русский

## Что это

GlobalList Atlas подключается к общей таблице карт сообщества (GlobalList) и позволяет скачивать и запускать карты, не выходя из игры. Мод сам раскладывает файлы по папкам нужного редактора, следит за обязательными модами и делает бекапы перед каждой заменой.

## Как открыть

Кнопка с глобусом в верхней части главного меню. Слева — список карт, справа — подробности выбранной карты.

## Список карт

- Поиск по названию и автору.
- Фильтры: звёзды, редактор, теги, верификация, статус (не скачано / установлено / запущено).
- Кнопка с сердцем под фильтрами — показать только избранное.
- Карты сгруппированы по лигам, цвет кнопки совпадает с цветом карты в таблице.
- Выбор перемещается мышью или клавишами вверх/вниз из настроек управления игры.
- Сверху видно состояние сети (пинг и скорость) и то, какая карта сейчас запущена в каком редакторе.

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

GlobalList Atlas connects to the community map spreadsheet (GlobalList) and lets you download and launch maps without leaving the game. It places files into the right editor folders, tracks required mods, and makes a backup before every replacement.

## Opening it

Use the globe button at the top of the main menu. The map list is on the left, details of the selected map on the right.

## Map list

- Search by name and author.
- Filters: stars, editor, tags, verification, status (not downloaded / installed / launched).
- The heart button under the filters shows favorites only.
- Maps are grouped by league, and a button's color matches the map's color in the spreadsheet.
- Move the selection with the mouse or with the up/down keys from the game's control settings.
- The top shows network state (ping and speed) and which map is currently launched in which editor.

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
