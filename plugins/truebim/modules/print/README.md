# Печать

Модуль пакетной печати и экспорта листов Revit в PDF, DWG и DXF.

Статус: первый рабочий релиз готов как release candidate для Revit 2022 и Revit 2025. Модуль установлен через manifest `truebim.print`, доступен из TrueBIM launcher и панели `БИМ`, проходит Release build/test и локальный preflight deploy для целевых версий.

## Что сделано

- Кнопка `Печать`, Revit-команда, manifest и подключение к launcher.
- Окно выбора листов из открытых документов Revit.
- Фильтр источника и сохранение выбора листов при переключении источников.
- Конструктор имени файла с предпросмотром, нормализацией и русскими токенами `{Номер листа}`, `{Имя листа}`, `{Номер проекта}`, `{Имя проекта}`, `{Имя документа}`, `{Дата:yyyy-MM-dd}`, `{Счетчик}` и `{Счетчик:000}`. Старые английские токены также поддерживаются для сохраненных настроек.
- Прямой экспорт отдельных PDF через `PDFExportOptions`.
- Объединенный PDF через один вызов `Document.Export` на документ-источник.
- PDF-настройки цвета, качества растровых элементов и raster/vector режима.
- Прямой экспорт DWG и DXF.
- Выбор DWG/DXF export setup из сохраненных настроек Revit.
- Расширенный DWG-профиль поверх `DWGExportOptions`: версия DWG, цвета, переопределения, единицы, координаты, merged views, solids, линии, штриховки, текст и базовые layer/file mappings.
- Fallback на стандартные `DWGExportOptions` / `DXFExportOptions`, если setup не выбран или в документе нет сохраненных CAD setups.
- Сохранение базовых настроек окна в `%APPDATA%\TrueBIM\<RevitVersion>\print-settings.json`.
- Сохранение DWG-профилей в `%APPDATA%\TrueBIM\<RevitVersion>\dwg-export-profiles.json`.
- Unit-тесты чистой логики печати.

Если в Revit открыто несколько документов, модуль собирает листы из каждого документа с печатаемыми листами. В окне есть фильтр источника; выбор листов сохраняется при переключении фильтра и восстанавливается при возврате к источнику. При экспорте каждый лист отправляется через свой `Document`. Для режима `Один PDF` при выборе листов из нескольких документов создается отдельный объединенный PDF на каждый документ.

Для PDF доступен режим отдельных файлов и режим `Один PDF`. В объединенном режиме модуль экспортирует выбранные листы одним вызовом `Document.Export`, нормализует имя общего файла и не блокирует PDF-only экспорт из-за дублей индивидуальных имен листов. Также доступны настройки цвета, качества растровых элементов и raster/vector режима.

Для DWG и DXF окно показывает отдельные настройки экспорта. Если в документе нет сохраненных CAD export setups или пользователь оставляет значение по умолчанию, модуль экспортирует через стандартные `DWGExportOptions` / `DXFExportOptions` и отображает fallback в статусе. Для DWG доступно окно `Настройки DWG...`: профиль может использовать Revit Export Setup как базу, поверх нее применяет выбранные `DWGExportOptions`, сохраняется в JSON и может создать новый Revit DWG Export Setup без изменения глобальных настроек без явного действия пользователя.

Настройки окна сохраняются в `%APPDATA%\TrueBIM\<RevitVersion>\print-settings.json`: папка экспорта, маска имени, отображение листов-заглушек, выбранные форматы, режим объединенного PDF, имя общего PDF, PDF-настройки и выбранные CAD setups. DWG-профили и последняя DWG-сводка сохраняются отдельно в `%APPDATA%\TrueBIM\<RevitVersion>\dwg-export-profiles.json`.

## Завершенные задачи первого релиза

1. Каркас кнопки и модуля.
2. MVP окна печати.
3. Конструктор имени файла.
4. PDF export: отдельные файлы, объединенный PDF и PDF-настройки.
5. DWG/DXF export: прямой экспорт, выбор Revit export setup, расширенный DWG-профиль и default fallback.
6. Несколько источников листов: открытые документы, фильтр источника и сохранение выбора.
7. Сохранение базовой конфигурации окна.
8. Unit-тесты, Release build/test, local deploy и installer preflight.

## Проверка

Последний локальный preflight выполнен 2026-07-03:

- `plugins\truebim\scripts\qa-preflight-2025.ps1` - PASS;
- `plugins\truebim\scripts\qa-preflight-2022.ps1` - PASS;
- `C:\Program Files\dotnet\dotnet.exe format TrueBIM.sln --verify-no-changes` - PASS;
- `C:\Program Files\dotnet\dotnet.exe build TrueBIM.sln --configuration Release` - PASS;
- `C:\Program Files\dotnet\dotnet.exe test TrueBIM.sln --configuration Release` - PASS.

## Backlog

Вкладки по источникам, чтение связанных моделей, print sets, фильтры/группировка по параметрам листов и редактирование таблиц `ExportLayerTable` / `ExportLinetypeTable` / `ExportPatternTable` / `ExportFontTable` добавляются следующими шагами по плану в `docs/print-module-plan.md`.
