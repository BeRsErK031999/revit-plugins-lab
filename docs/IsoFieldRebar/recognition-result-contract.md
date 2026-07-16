# IsoField recognition result JSON contract

Версия контракта: `1.0`.

Этот формат описывает результат распознавания изополей до привязки к геометрии Revit. Координаты остаются в системе исходного файла, чаще всего в пикселях изображения. Преобразование в координаты Revit будет отдельным этапом через calibration model.

Встроенный PNG-runner дополнительно передаёт внутри процесса `IsoFieldLegend` с упорядоченными RGB-уровнями, числовыми границами `MinimumValue`/`MaximumValue` в `см²/м` и ролью слоя. Числа принимаются только при полном распознавании всех границ и строгой монотонности шкалы. В JSON-схему `1.0` это поле пока не сериализуется, поэтому внешний CLI остаётся обратно совместимым.

## Root object

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `schemaVersion` | string | yes | Сейчас поддерживается только `1.0`. |
| `source` | object | no | Информация об исходном файле. Reader сохраняет поле как внешний metadata block и не использует его для построения Revit geometry. |
| `polylines` | array | yes | Распознанные линии или замкнутые зоны изополей. Может быть пустым массивом. |
| `diagnostics` | array of string | no | Предупреждения или заметки worker-а. |

## Polyline object

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `id` | string | yes | Stable id внутри результата распознавания. |
| `layerRole` | string | no | Расчётный слой: `As1X`, `As2X`, `As3Y` или `As4Y`. При обработке комплекта TrueBIM заполняет роль по исходному файлу. |
| `zoneName` | string | no | Имя зоны или слоя изополей. |
| `confidence` | number | no | Уверенность распознавания в диапазоне `0..1`. |
| `points` | array | yes | Точки полилинии в координатах исходного файла. Минимум две точки. |

## Point object

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `x` | number | yes | X coordinate in source units. |
| `y` | number | yes | Y coordinate in source units. |

## Validation rules

- `schemaVersion` must be `1.0`.
- `polylines` must be present.
- Every polyline must have a non-empty `id`.
- Every polyline must have at least two points.
- Every point must have finite `x` and `y` values.
- If `layerRole` is present, it must be one of `As1X`, `As2X`, `As3Y`, `As4Y` (case-insensitive on read).
- Unknown fields are ignored for forward compatibility.

## Safety boundaries

- Reading this JSON does not create Revit elements.
- Reading this JSON does not start OpenCV, Python, Tesseract, ONNX or any external worker.
- Preview and model writes are separate future stages.
