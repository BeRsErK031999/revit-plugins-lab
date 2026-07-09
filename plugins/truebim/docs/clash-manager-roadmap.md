# RoadMap: Clash Manager для TrueBIM

Дата среза: 2026-07-08.

Исходный ориентир: документ `Создание аналога Clash Manager как надстройки для Autodesk Revit.docx`.

## Цель

Перевести текущий инструмент `Отчёт коллизий` из формата простого Revit-отчета в рабочий Clash Manager внутри TrueBIM: локальный Revit-first модуль координации, где BIM-координатор запускает проверки, группирует результаты, назначает ответственных, ведет статусы, возвращается к проблеме в 3D и экспортирует управляемый отчет.

## Что уже есть

В TrueBIM уже реализован MVP+:

- команда `Отчёт коллизий` на панели `Координация`;
- сканирование видимых элементов текущей модели;
- сканирование текущей модели против загруженных `RevitLinkInstance`;
- опциональное сканирование RVT-связей между собой;
- локальное JSON-состояние;
- статусы, комментарии и CSV-экспорт;
- переход к найденной коллизии в служебном 3D-виде `BIM_Clash_Report_3D`;
- selection и подсветка элементов при навигации.

Ограничение текущей версии: это еще broad-phase инструмент по bounding box. Он полезен как ранний coordination report, но не закрывает весь сценарий Clash Manager: нет профилей правил, stable baseline, назначений, групп, истории, точной solid-проверки, пакетных снимков, BCF/PDF/Excel и интеграций.

## Рабочий MVP

Минимальная рабочая версия Clash Manager для TrueBIM должна включать:

- управление локальным профилем проверки;
- режимы проверки: current model, current model vs RVT links, RVT links vs RVT links;
- тип коллизии: `Hard`, далее `Clearance` и `Semantic`;
- статусы `New`, `Active`, `Approved`, `Resolved`, `Ignored`;
- назначенного ответственного и комментарий;
- приоритет и severity score;
- группировку по источнику и паре категорий, затем spatial/system-zone grouping;
- стабильный fingerprint для повторного сопоставления результата;
- сохранение triage-состояния между перезапусками;
- 3D-навигацию с section box;
- CSV на первом этапе, затем Excel/PDF;
- unit-тесты для доменной логики без зависимости от Revit.

## RoadMap работ

### 1. Triage foundation

Статус: первый рабочий срез готов.

Цель: сделать каждую найденную коллизию управляемой задачей координации.

- Добавить доменные поля: `ClashType`, `Priority`, `SeverityScore`, `GroupKey`, `Fingerprint`, `AssignedTo`.
- Считать priority/severity из приблизительного объема пересечения bounding box.
- Сохранять `AssignedTo` в JSON вместе со статусом и комментарием.
- Привязать новое состояние к stable fingerprint с fallback на старый `ClashId`.
- Расширить CSV-отчет и grid в окне.

### 2. Rule profile

Статус: первый рабочий срез готов.

Цель: уйти от одного общего набора чекбоксов к профилю проверки.

- Ввести rule profile: имя теста, тип проверки, tolerance, minimum overlap, linked model scope.
- Добавить импорт/экспорт профиля в JSON.
- Подготовить UI к нескольким сохраненным тестам.
- Далее: добавить категории A/B и отделить domain profile от WPF-окна.

### 3. Baseline and deduplication

Статус: частично готово: stable fingerprint и дедупликация добавлены, snapshot/baseline еще запланированы.

Цель: после повторного сканирования показывать не шум, а изменение картины.

- Сделано: stable fingerprint для результата.
- Сделано: дедупликация и сортировка найденных результатов по priority/severity.
- Далее: сохранять snapshot последнего запуска.
- Далее: отмечать `New`, `Existing`, `Resolved candidate`.
- Далее: добавить summary по группам и статусам.
- Далее: подготовить baseline compare для будущих batch-сценариев.

### 4. Better grouping

Статус: первый рабочий срез готов.

Цель: сократить тысячи строк до читаемых coordination buckets.

- Группировка по паре элементов.
- Группировка по source/category pair.
- Spatial grouping по центрам коллизий.
- Позже: system-zone grouping по level/zone/system/workset.

### 5. Exact detection contract

Статус: запланировано.

Цель: подготовить переход от broad-phase bounding box к точным проверкам.

- Оставить текущий collector как coarse stage.
- Ввести engine contract для exact stage.
- Добавить безопасную обработку `Solid`, `ElementIntersectsSolidFilter` и `BooleanOperationsUtils`.
- Сохранять причину пропуска элементов без solid-геометрии.
- Не блокировать Revit на больших моделях: лимиты, прогресс, отмена.

### 6. Reporting package

Статус: запланировано.

Цель: сделать отчет пригодным для передачи участникам проекта.

- Улучшенный CSV с fingerprint, группой, ответственным, статусом и severity.
- Excel-экспорт.
- PDF-сводка.
- Снимок 3D-вида или placeholder-контракт для будущих snapshots.
- Manual QA сценарий для экспорта и повторного открытия состояния.

### 7. Revit pane and external events

Статус: запланировано после стабилизации core.

Цель: перейти от отдельного окна к полноценному coordination workspace.

- DockablePane для постоянной панели Clash Manager.
- ExternalEvent gateway для действий из UI.
- Отдельные команды: open pane, run scan, zoom/select, save state.
- Безопасная подготовка к будущим `Create Issue` и `Apply Suggestion`.

### 8. Production extensions

Статус: вне первого MVP.

- DataStorage markers внутри RVT.
- SQLite вместо одного JSON для больших проектов.
- BCF import/export.
- APS/BIM 360/ACC issue sync.
- Dynamo package.
- Navisworks companion.
- Suggestion engine с preview/confirm.

## Порядок ручной проверки

1. Закрыть Revit перед local deploy или installer smoke.
2. Открыть проект с несколькими model categories и загруженными RVT-связями.
3. `TrueBIM` -> `Координация` -> `Отчёт коллизий`.
4. Запустить `Сканировать` для текущей модели и RVT-связей.
5. Проверить колонки type, priority, group, responsible, severity.
6. Назначить ответственного, поменять статус и комментарий.
7. Нажать `Сохранить`, закрыть и снова открыть окно.
8. Убедиться, что состояние подтянулось по fingerprint.
9. Экспортировать и импортировать JSON-профиль.
10. Проверить `Выбрать` и `Показать в 3D`.
11. Экспортировать CSV и проверить новые поля отчета.
