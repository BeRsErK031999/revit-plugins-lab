# Shared Parameter Inspector test assets

`scenario-manifest.json` — машиночитаемая спецификация тестового RVT-проекта и набора RFA
для текущей доступной версии Revit 2025.

На момент добавления manifest бинарные RVT/RFA не создавались и не загружались в Revit:
Revit не был запущен, а валидные файлы этих форматов нельзя создавать текстовыми средствами.
Поле `assetStatus` намеренно равно `prepared_definition`.

## Подготовка

1. В Revit 2025 создать проект `SharedParameterInspector-QA-2025.rvt`.
2. Создать общие параметры с GUID из `sharedParameters`.
3. Реализовать все записи `projectScenarios` и сверить `expected`.
4. Создать 15 RFA из `familyScenarios` в подпапке `Families`.
5. Загрузить в проект семейства, где `loadIntoProject=true`.
6. Сохранить исходный проект только до тестов удаления; для destructive-сценариев
   использовать отдельную копию.
7. После подготовки изменить `assetStatus` только вместе с добавлением реальных файлов и
   протокола проверки.

## Протокол запуска

- установить собранный TrueBIM;
- открыть проект в Revit;
- запустить `TrueBIM → Координация → Общие параметры`;
- выполнить catalog, quick analysis, family presence, deep family analysis и export;
- destructive-тесты выполнять последними на копии;
- для каждого сценария записать `Pass`, `Fail` или `Blocked` и приложить лог TrueBIM.
