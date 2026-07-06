# План работ по BIM-инструментам

## Исходный контекст

ТЗ описывает единый набор команд для BIM-зоны ribbon:

- `Цвета по параметрам`
- `Копирование параметров`
- `Рабочие наборы`
- `ParaManager`

В текущем проекте уже есть одна Revit-вкладка `TrueBIM` и базовая панель `БИМ`. Чтобы не переносить существующие рабочие команды, план сохраняет вкладку `TrueBIM` и добавляет новые панели из ТЗ внутри неё.

## Задача 1: Ribbon И UI-Каркас

Статус: реализовано первым срезом.

Цель: сделать новые команды видимыми и открываемыми до добавления логики, которая меняет модель Revit.

Состав работ:

- Добавить панели `Проверка модели`, `Параметры` и `Администрирование`.
- Добавить четыре push button с иконками 32x32, tooltip, long description и отдельными классами `IExternalCommand`.
- Открывать общее WPF-окно-каркас примерно 900x650 для каждой команды.
- Не менять существующие панели `БИМ`, `КР`, `ЭОМ`, `СС` и их команды.
- Добавить smoke-тесты на имена кнопок, панели, command class, иконки и описания.

Приёмка:

- Вкладка `TrueBIM` по-прежнему загружается.
- Новые кнопки находятся на ожидаемых панелях.
- Каждая новая кнопка ведёт на отдельную Revit external command.
- Каждая кнопка открывает окно-каркас без изменений активной модели.
- Первый срез не создаёт, не удаляет и не меняет Revit-фильтры, параметры или рабочие наборы.

## Задача 2: Копирование Параметров

Цель: копировать выбранные изменяемые значения параметров с исходного элемента на выбранные элементы-получатели.

Планируемые файлы:

- `CopyParametersCommand.cs`
- `CopyParametersWindow`
- `ParameterCopyService`
- `ParameterCompatibilityService`
- `ElementSelectionService`
- `CopyParameterRow`
- `ParameterCopyResult`
- `ElementCopyReportRow`

Основное поведение:

- Использовать единственный предвыбранный элемент как исходный, если он есть; иначе попросить пользователя выбрать исходный элемент.
- Показывать копируемые параметры с именем, значением, storage type, источником instance/type, возможностью записи и предупреждениями.
- Давать выбрать элементы-получатели через Revit selection.
- Копировать значения внутри `Transaction`, учитывая `StorageType`, GUID shared parameter, built-in parameters, read-only флаги и совместимость типов.
- Показывать отчёт по скопированным значениям, пропускам и причинам пропуска.

## Задача 3: Рабочие Наборы

Цель: создавать пользовательские worksets из проверенного CSV или Excel-шаблона.

Планируемые файлы:

- `CreateWorksetsCommand.cs`
- `CreateWorksetsWindow`
- `WorksetExcelReader`
- `WorksetCreationService`
- `WorksetValidationService`
- `WorksharingService`
- `WorksetImportRow`
- `WorksetCreateResult`
- `WorksetValidationIssue`

Основное поведение:

- Сначала поддержать CSV; `.xlsx` добавлять только если зависимость уже надёжно встроена в проект.
- Показывать предпросмотр нормализованных имён worksets до применения.
- Помечать строки как `Будет создан`, `Уже существует`, `Пустая строка`, `Недопустимое имя` или `Дубликат в файле`.
- Никогда не включать worksharing молча; предупреждать про undo history и требовать явное подтверждение.
- Создавать worksets внутри `Transaction` и показывать отчёт по созданным, пропущенным и ошибочным строкам.

## Задача 4: Цвета По Параметрам

Цель: окрашивать элементы активного вида по значениям выбранного параметра через Revit view filters.

Планируемые файлы:

- `ColorByParameterCommand.cs`
- `ColorByParameterWindow`
- `ColorByParameterService`
- `ParameterValueReader`
- `ViewFilterService`
- `ColorPaletteService`
- `FilterNameBuilder`
- `BimCategoryItem`
- `BimParameterItem`
- `ColorRuleRow`
- `ColorApplyResult`

Основное поведение:

- Собирать категории из элементов, видимых на активном виде.
- Показывать параметры выбранных категорий с разделением по источнику и storage type.
- Показывать уникальные значения, включая `<Пусто / Не заполнено>`, с цветами и чекбоксом использования.
- Применять `ParameterFilterElement` и `OverrideGraphicSettings` только к активному виду.
- Использовать безопасные имена фильтров с префиксом `BIM_F_`.
- Ограничивать чрезмерное количество уникальных значений и предупреждать пользователя перед созданием большого числа фильтров.
- Очищать только фильтры активного вида, созданные этим инструментом, и не удалять пользовательские фильтры молча.

## Задача 5: ParaManager MVP

Цель: импортировать shared parameters в проект из CSV или Excel с выбранным shared parameter file.

Планируемые файлы:

- `ParaManagerCommand.cs`
- `ParaManagerWindow`
- `SharedParameterFileService`
- `ProjectParameterBindingService`
- `ParameterExcelImportService`
- `CategoryResolveService`
- `ParameterExportService`
- `ParaManagerValidationService`
- `ParameterImportRow`
- `ParameterBindingPreviewRow`
- `ParameterImportResult`
- `SharedParameterDefinitionInfo`

Основное поведение:

- Начать с импорта project parameters из CSV/Excel.
- Требовать выбранный shared parameter `.txt`.
- До применения валидировать категории, типы данных, binding type, существующие definitions и конфликты привязки.
- Создавать или переиспользовать группы и definitions в shared parameter file.
- Привязывать instance/type параметры к найденным категориям внутри `Transaction`.
- Показывать отчёт по созданным definitions, привязанным параметрам, пропущенным строкам и ошибкам.

Отложенный объём:

- Массовое редактирование параметров семейств.
- Nested family parameters.
- Генерация новых GUID для существующих параметров.
- Merge нескольких shared parameter files.
- Удаление project parameters.
- Автоматическое изменение `.rfa` файлов.

## Общие Правила

- Все изменения модели выполнять внутри Revit `Transaction`.
- При отмене пользовательского выбора возвращать `Result.Cancelled`, где используется Revit selection.
- Ошибки показывать через `TaskDialog` и писать в лог TrueBIM.
- Для автоматически созданных фильтров, настроек или definitions использовать префикс `BIM_`.
- Не удалять пользовательские фильтры, параметры или рабочие наборы без явного подтверждения.
