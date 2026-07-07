# Армирование по изополям: дорожная карта

## Контекст текущей архитектуры

TrueBIM подключается к Revit через `TrueBIM.App.App`, который создает вкладку `TrueBIM`, панели из `TrueBimRibbon.PanelNames` и кнопки из `TrueBimRibbon.Buttons`. Классы `IExternalCommand` сейчас живут в `TrueBIM.App.Commands`, а предметные модули размещаются в `TrueBIM.App.Modules/*`. Иконки кнопок не хранятся отдельными bitmap-файлами: они генерируются кодом через `IconFactory`.

Для модуля изополей выбран путь, который вписывается в эту структуру:

- command-адаптер: `TrueBIM.App.Commands.IsoFieldRebarCommand`;
- изолированный код модуля: `TrueBIM.App.Modules.IsoFieldRebar`;
- документация модуля: `plugins/truebim/src/TrueBIM.App/Modules/IsoFieldRebar/README.md`.

## Принципы

- На ранних этапах не подключать OpenCV, Python, Tesseract, ONNX и другие тяжелые зависимости.
- До отдельной задачи не создавать реальную арматуру и не менять модель Revit.
- Любая ошибка нового модуля должна завершаться понятным `TaskDialog` и записью в лог, а не падением Revit.
- Держать модуль изолированным и подключать его к существующей ribbon-инфраструктуре только через минимальные адаптеры.
- Для поддержки Revit 2022/2025 сохранять текущий подход проекта: `net48` для старых версий и `net8.0-windows` для Revit 2025.

## Текущий статус

- Этап 1 выполнен: создан изолированный namespace, DTO и stub-runner.
- Этап 2 выполнен: добавлена кнопка `Армирование по изополям` на панель `БИМ`.
- Этап 3 выполнен: добавлено WPF-окно модуля.
- Этап 4 выполнен в безопасном режиме: добавлен выбор изображения или JSON-файла, путь хранится только в состоянии окна.
- Этап 5 выполнен: доступен stub-runner без OpenCV/Python.
- Этап 6 выполнен: зафиксирован JSON-контракт `schemaVersion: "1.0"` и добавлен reader с базовой валидацией.
- Этап 7 выполнен: добавлен экранный WPF-preview JSON-контуров и управляемые `DetailCurve`-линии предпросмотра на активном 2D-виде Revit.
- Этап 8 выполнен: добавлен выбор стены или плиты как будущего host-элемента без изменения модели Revit.
- Этап 9 выполнен: добавлена калибровка координат изображения через image anchor, масштаб `мм/пикс` и инверсию оси Y для Revit preview.
- Этап 10 выполнен: добавлен read-only preview правил армирования для распознанных зон с валидацией host, типа арматуры и шага.
- Этап 11 выполнен: добавлен controlled write-flow, который создает одну тестовую арматуру на выбранном host-элементе только после явного подтверждения пользователя.
- Этап 12 выполнен: добавлен slab-specific placement для простых плит, который строит параллельные тестовые Rebar-линии по валидным зонам внутри bounding box плиты.
- Этап 13 выполнен: добавлен wall-specific placement для простых прямых стен с локальной осью/нормалью, несколькими зонами и направлениями `AlongHost`/`Vertical`.
- Этап 14 выполнен: добавлен CLI runner для внешнего worker-а с timeout, temp request/output files и строгой валидацией output JSON.
- Этап 15 выполнен: расширено логирование диагностики вокруг file selection, recognition, preview, правил и controlled write-flow.
- Этап 16 выполнен: добавлены manual QA checklist, sample JSON inputs и тест, который валидирует documented examples.
- Этап 17 выполнен: методичка по текущему модулю `Армирование по изополям` с визуальной SVG-карточкой попадает в installer/artifact `Docs` payload, а установочный validator проверяет наличие IsoField guide assets.
- Следующий рекомендуемый этап: провести ручной runtime smoke в Revit 2022/2025 после установки свежего `TrueBIM-Setup.exe`.

## Этапы

### 1. Архитектурный каркас модуля

- Цель: создать безопасную основу без изменений модели Revit.
- Код: добавить DTO для точки, полилинии, результата распознавания и правила армирования; добавить интерфейс runner и stub-реализацию.
- Файлы: `Modules/IsoFieldRebar/Models/*`, `Modules/IsoFieldRebar/Services/*`, `Modules/IsoFieldRebar/README.md`.
- Проверка: сборка решения и тест на пустой результат stub-runner.
- Риски: преждевременное попадание Revit API в чистые модели или появление тяжелых зависимостей.
- Готово: модуль компилируется, isolated namespace создан, stub возвращает безопасный пустой результат.

### 2. Кнопка в существующей плашке BIM

- Цель: сделать модуль доступным из Revit без влияния на существующие кнопки.
- Код: добавить `IsoFieldRebarCommand`, ribbon-definition и иконку в `IconFactory`.
- Файлы: `Commands/IsoFieldRebarCommand.cs`, `TrueBimRibbonButtonDefinition.cs`, `UI/IconFactory.cs`.
- Проверка: тесты ribbon-definition, запуск Revit и визуальная проверка кнопки `Армирование по изополям` на панели `БИМ`.
- Риски: ошибка в имени command-класса сломает запуск кнопки.
- Готово: кнопка видна, команда запускается и показывает информационный диалог.

### 3. Окно модуля

- Цель: заменить простой `TaskDialog` на небольшое WPF-окно с состоянием будущего workflow.
- Код: добавить `IsoFieldRebarWindow` с заголовком, блоком выбранного файла, статусом распознавания и disabled-кнопками будущих шагов.
- Файлы: `Modules/IsoFieldRebar/UI/IsoFieldRebarWindow.xaml`, `IsoFieldRebarWindow.xaml.cs`.
- Проверка: ручной запуск из Revit, проверка владельца окна и закрытия без side effects.
- Риски: WPF-окно может потерять owner или фокус внутри Revit.
- Готово: окно открывается модально, не меняет документ и не требует активной модели.

### 4. Выбор изображения/файла изополей

- Цель: разрешить пользователю выбрать исходный файл без распознавания.
- Код: добавить file picker с фильтрами для изображений и JSON, сохранить выбранный путь только в состоянии окна.
- Файлы: `Services/IIsoFieldFilePicker.cs`, `Services/IsoFieldFilePicker.cs`, `UI/IsoFieldRebarWindow*`.
- Проверка: выбрать файл, отменить выбор, проверить отображение пути.
- Риски: длинные пути, Unicode-пути и отсутствующие файлы.
- Готово: выбранный файл отображается, отмена не вызывает ошибок.

### 5. Заглушка распознавания

- Цель: стабилизировать контракт результата до подключения реального распознавания.
- Код: расширить `StubIsoFieldRecognitionRunner`, возвращать пустой или контролируемый demo-result.
- Файлы: `Services/StubIsoFieldRecognitionRunner.cs`, `Models/*`, тесты.
- Проверка: unit-тесты на пустой результат и диагностические сообщения.
- Риски: demo-данные могут быть приняты за реальные.
- Готово: UI умеет показать, что распознавание пока является заглушкой.

### 6. Формат JSON для результата распознавания

- Цель: зафиксировать переносимый контракт между будущим worker и модулем Revit.
- Код: добавить JSON reader/writer и schema examples.
- Файлы: `Services/IIsoFieldJsonReader.cs`, `Services/IsoFieldJsonReader.cs`, `docs/IsoFieldRebar/examples/*.json`.
- Проверка: unit-тесты чтения валидного, пустого и ошибочного JSON.
- Риски: потеря единиц измерения и неоднозначность координат.
- Готово: JSON-формат описан, reader валидирует обязательные поля.

### 7. Предпросмотр распознанных контуров в Revit

- Цель: показать пользователю контуры без создания арматуры.
- Код: добавить preview service для временных линий или управляемых DetailLines/ModelLines с явной очисткой.
- Файлы: `Revit/IsoFieldPreviewService.cs`, `Models/*`, `UI/*`.
- Проверка: ручной запуск на sample-модели, создание и очистка preview-геометрии.
- Риски: temporary graphics API отличается между версиями Revit; постоянные линии требуют транзакции.
- Готово: контуры видны и очищаются без остаточных элементов.

### 8. Выбор стены или плиты как host-элемента

- Цель: связать распознанные зоны с конкретным host-элементом.
- Код: добавить selection service с фильтром категорий стен и плит.
- Файлы: `Revit/RevitElementSelectionService.cs`, `Models/*`, `UI/*`.
- Проверка: выбрать стену, выбрать плиту, отменить выбор.
- Риски: linked model, group, design option и read-only documents.
- Готово: выбранный host отображается в UI и не меняет модель.

### 9. Калибровка координат изображения относительно Revit

- Цель: задать преобразование координат из изображения в координаты Revit.
- Код: добавить модель calibration transform и UI для двух или трех контрольных точек.
- Файлы: `Models/IsoFieldCalibration.cs`, `Services/IsoFieldCoordinateMapper.cs`, `UI/*`.
- Проверка: unit-тесты преобразования точек и ручная проверка на простом прямоугольнике.
- Риски: масштаб, поворот, зеркальность, единицы измерения и погрешность кликов.
- Готово: точки из JSON стабильно попадают в ожидаемые Revit-координаты.

### 10. Правила преобразования зон изополей в параметры армирования

- Цель: описать, как зона изополей превращается в будущие параметры армирования.
- Код: расширить `RebarRule`, добавить validation service и readonly preview.
- Файлы: `Models/RebarRule.cs`, `Services/RebarRuleValidationService.cs`, `UI/*`.
- Проверка: тесты валидации шага, диаметра, направления и host-type.
- Риски: разные нормативные правила для стен и плит.
- Готово: правила можно просмотреть и проверить без создания арматуры.

### 11. MVP создания тестовой арматуры

- Цель: разрешить controlled write-flow для одного тестового элемента без автоматической раскладки по всем зонам.
- Код: добавить Revit service с транзакцией, подбором доступного `RebarBarType` и явным подтверждением пользователя.
- Файлы: `Revit/IsoFieldRebarCreationService.cs`, `Models/IsoFieldRebarCreationResult.cs`, `UI/*`.
- Проверка: sample-модель, Undo в Revit, проверка одного созданного элемента.
- Риски: неверный host, неправильные cover/bar type, несовместимость Revit API версий.
- Готово: создается минимальная тестовая арматура только после подтверждения.

### 12. Поддержка плит

- Цель: адаптировать правила и ориентацию армирования для плит.
- Код: добавить slab-specific mapper, правила направлений `Auto/X/Y` и offsets для нескольких валидных зон.
- Файлы: `Models/IsoFieldRebarPlacement*.cs`, `Revit/SlabRebarPlacementService.cs`.
- Проверка: unit-тесты направления, offsets для нескольких зон и отказа от non-slab правил; ручная sample-плита остается для Revit QA.
- Риски: наклонные плиты, отверстия, compound structure.
- Готово: MVP рассчитывает параллельные тестовые линии для простой плиты.

### 13. Поддержка стен

- Цель: адаптировать правила для вертикальных host-элементов.
- Код: добавить wall-specific mapper, обработку локальной системы координат стены и подключение к controlled write-flow.
- Файлы: `Models/*`, `Revit/WallRebarPlacementService.cs`.
- Проверка: unit-тесты направления, offsets и отказа от non-wall правил; sample-стены разной ориентации остаются для Revit QA.
- Риски: curved walls, stacked walls, openings, mirrored geometry.
- Готово: MVP корректно работает на простой прямой стене, создает по одному тестовому элементу на валидную зону и поддерживает `AlongHost`/`Vertical`.

### 14. Поддержка внешнего Python/CLI worker для OpenCV

- Цель: вынести тяжелое распознавание из Revit-процесса.
- Код: добавить CLI runner с timeout, temp files и строгим JSON-контрактом; включать его через env-настройки без добавления OpenCV/Python зависимостей в Revit.
- Файлы: `Services/IsoFieldCliRecognitionRunner.cs`, `docs/IsoFieldRebar/worker-contract.md`.
- Проверка: fake CLI в тестах для успешного output JSON, non-zero exit code и timeout; ручной запуск worker на sample-файле остается для интеграционного QA.
- Риски: зависания, несовместимые Python environments, большие изображения.
- Готово: Revit вызывает настроенный внешний worker безопасно, получает валидированный JSON и без настройки остается на `StubIsoFieldRecognitionRunner`.

### 15. Логирование и диагностика

- Цель: сделать ошибки понятными для пользователя и разработчика.
- Код: расширить logging around file selection, recognition, preview and write-flow; добавить diagnostic metadata для активного recognition runner-а.
- Файлы: `Commands/IsoFieldRebarCommand.cs`, `Services/*`, `Revit/*`.
- Проверка: сценарии ошибки файла, ошибки JSON, ошибки worker timeout; unit-тесты diagnostic metadata для stub/CLI runner-а.
- Риски: лог может раскрывать лишние локальные пути при передаче отчетов.
- Готово: key failures и early-return сценарии имеют user-friendly dialog/status и запись в `%APPDATA%\TrueBIM\Logs\truebim.log`.

### 16. Тестирование на sample-моделях

- Цель: закрепить ожидаемое поведение на воспроизводимых моделях.
- Код: добавить checklist и lightweight fixtures.
- Файлы: `docs/IsoFieldRebar/manual-qa.md`, sample inputs в `docs/IsoFieldRebar/examples/*.json`.
- Проверка: Revit 2022/2025 smoke, preview, selection, cancel flows; unit-тест чтения documented example JSON.
- Риски: sample-модели могут быть несовместимы с частью версий Revit.
- Готово: есть повторяемый QA сценарий, sample inputs и known limitations.

### 17. Упаковка и установка

- Цель: включить модуль в существующий build/deploy/installer flow.
- Код: при необходимости обновить installer payload или manifests, если появятся новые assets; IsoField doc assets копируются в `Docs/assets`.
- Файлы: `plugins/truebim/scripts/*`, `plugins/truebim/installer/*`, manifests.
- Проверка: `build-installer.ps1`, `test-installation.ps1`, local deploy for supported Revit versions.
- Риски: installer matrix строже обычной сборки и может выявить legacy API issues.
- Готово: модуль устанавливается вместе с TrueBIM, кнопка доступна после перезапуска Revit.
