# TrueBIM Shared Parameter Inspector

## Назначение и границы

Команда `Общие параметры` на панели `Координация` строит каталог `SharedParameterElement`,
анализирует использование выбранного GUID в активном RVT-проекте и в семействах, формирует
отчёт и выполняет только подтверждённый план удаления.

Источником истины служат:

- `SharedParameterElement.Id` и `GuidValue` для проектного каталога;
- `BindingMap` для instance/type binding и категорий;
- параметр элемента, полученный по GUID, для наличия, значения и read-only;
- `ScheduleDefinition`, поля, фильтры и сортировка для спецификаций;
- публичное дерево `ElementFilter` и `FilterRule` для фильтров видов;
- `FamilyManager`, размеры и ассоциации параметров для RFA;
- результат `Document.Delete` внутри откатываемой транзакции для dry run.

RVT-связи не анализируются. Инструмент не строит выводы по текстовым совпадениям имени там,
где API предоставляет GUID или ElementId. Совпадение формулы по похожему имени получает
`Probable` и блокирует автоматическое удаление.

## Архитектура

| Слой | Ответственность |
|---|---|
| `Models` | неизменяемые DTO анализа, confidence, blockers, план и результат удаления |
| `SharedParameterProjectCatalogService` | каталог `SharedParameterElement` и binding metadata |
| `SharedParameterProjectAnalysisService` | элементы, спецификации, фильтры, global associations, worksharing |
| `SharedParameterFamilyAnalysisService` | project presence и глубокий анализ открытых/внешних семейств |
| `SharedParameterViewFilterService` | дерево правил, применение на видах/шаблонах, возможность rebuild |
| `SharedParameterDeletionWorkflow` | dry run, свежий preflight, `TransactionGroup`, rollback и post-verification |
| `SharedParameterVersionAdapter` | различия ForgeTypeId/BuiltInParameterGroup и извлечение правил Revit API |
| `SharedParameterReportExportService` | UTF-8 JSON/CSV/HTML/TXT |
| `SharedParameterInspectorWindow` | modeless WPF, ExternalEvent-пакеты, прогресс, отмена и навигация |

Окно хранит только DTO, GUID и ElementId. Любое обращение к `Document` заново получает
активный документ внутри ExternalEvent и сверяет его с документом, для которого загружен
каталог. Долгий обход семейств выполняется по одному семейству на ExternalEvent; между
пакетами доступна отмена.

## Анализ проекта

Быстрый анализ показывает:

- instance/type binding, категории, data type, parameter group и `VariesAcrossGroups`;
- экземпляры и типы с параметром, заполненные/пустые/read-only значения, группировку по категориям;
- обычные, скрытые и embedded-поля спецификаций, filter/sort/group;
- простые и составные AND/OR правила фильтров, категории, виды/шаблоны, visibility,
  overrides и размещение вида на листе;
- ассоциации с глобальными параметрами;
- явные blockers, warnings и ошибки по фазам.

`Расчётные/объединённые поля` получают blocker, если публичный API не доказывает отсутствие
зависимости. Проверка наличия параметра в загруженных семействах использует
`Document.EditFamily`; каждое семейство закрывается без сохранения в `finally`.

## Глубокий анализ семейств

Источники: семейства активного проекта, папка с подкаталогами, выбранные RFA и текущее
семейство. Перед открытием папка сканируется, резервные `*.0001.rfa`, временные `~*.rfa` и
`*.tmp.rfa` исключаются. Внешние документы всегда закрываются без сохранения.

Отчёт включает тип/экземпляр, группу и тип данных, значения по типам, формулу самого
параметра, ссылки из других формул, dimensions/reporting, ассоциации параметров элементов,
вложенные семейства первого уровня и честный `ManualCheckRequired` для annotation/label
ограничений публичного API. Поиск по GUID не зависит от имени.

## Безопасное удаление

1. Повторный анализ актуального документа.
2. Проверка присутствия и глубокий анализ затронутых семейств.
3. Dry run в транзакции с обязательным `RollBack`.
4. Сравнение фактически удаляемых ElementId с уже обнаруженными зависимостями.
5. Формирование списка действий с риском, поддержкой API и возможностью отката.
6. Подтверждение чекбоксом и точным вводом имени параметра.
7. `TransactionGroup`: зависимости спецификаций, фильтры, global associations, семейства,
   binding и `SharedParameterElement`.
8. Post-verification; при остатках или неожиданной каскадной зависимости — откат всей группы.

`Advanced` разрешает только действия, которые отдельно помечены как поддержанные. Он не
обходит blockers, неизвестные каскадные зависимости, ошибки открытия семейства,
неподтверждённые formula/dimension/annotation dependencies или worksharing ownership.

## Совместимость

| Revit | Target | API |
|---|---|---|
| 2019–2024 | `net48` | matching `RevitAPI.dll` / `RevitAPIUI.dll` |
| 2025–2026 | `net8.0-windows` | matching API; 2026 собирается только при наличии SDK/API |

Различия data type/group и правил фильтров скрыты за `ISharedParameterVersionAdapter`.
Installer общий для TrueBIM и использует существующую матрицу `build-installer.ps1`.

## Автоматические проверки

Unit tests покрывают:

- поиск по имени, полному/частичному GUID, duplicate names и невалидный GUID;
- точные и похожие имена в формулах;
- агрегацию и дедупликацию элементов;
- blockers неизвестного dry-run cascade, opaque schedule dependencies и отсутствующего
  глубокого анализа семейства;
- UTF-8 JSON/CSV/HTML/TXT и экранирование;
- исключение резервных/временных RFA;
- ribbon contract и наличие собственного ExternalEvent dispatcher у modeless-окна.

Интеграционные Revit-сценарии описаны в
`test-assets/shared-parameter-inspector/scenario-manifest.json`. Это воспроизводимая
спецификация 27 проектных и 15 семейных фикстур, а не утверждение о выполненном live QA.

## Ограничения live QA

Полноценную проверку `EditFamily`, schedule/filter mutation, загрузки семейства,
worksharing ownership, post-verification и отображения окна можно считать выполненной
только после запуска соответствующей версии Revit с подготовленными RVT/RFA. Наличие
успешной сборки или unit tests не заменяет этот этап.
