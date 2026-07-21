using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.FinishSchedule.UI;

public sealed class FinishScheduleGuideWindow : TrueBimWindow
{
    private static readonly Brush TextBrush = TrueBimBrushes.TextPrimary;
    private static readonly Brush MutedBrush = TrueBimBrushes.TextSecondary;
    private static readonly Brush GuideBorderBrush = TrueBimBrushes.Border;

    public FinishScheduleGuideWindow()
    {
        Title = "Методичка: ведомость отделки";
        Icon = IconFactory.CreateImage(TrueBimIcon.FinishSchedule, TrueBimTheme.IconSizeRibbon);
        Width = 940;
        Height = 800;
        MinWidth = 760;
        MinHeight = 620;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();
    }

    private UIElement CreateContent()
    {
        ScrollViewer viewer = new()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = CreateGuideBody()
        };

        return BuildShell(
            header: TrueBimUi.CreateHeader(
                Title,
                "Полный порядок настройки, проверки и формирования ведомости отделки помещений",
                TrueBimIcon.FinishSchedule),
            commandBar: null,
            body: viewer,
            status: null,
            footer: CreateFooter());
    }

    private static UIElement CreateGuideBody()
    {
        StackPanel body = new()
        {
            MaxWidth = 860
        };

        Border startBanner = TrueBimUi.CreateInfoBanner(
            "Короткий маршрут: координатор один раз настраивает проект → пользователь выбирает область расчёта → выполняет предпросмотр → проверяет понятную сводку → формирует и открывает спецификацию. Предпросмотр модель не меняет.",
            TrueBimUiSeverity.Info);
        startBanner.Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, TrueBimTheme.Spacing16);
        body.Children.Add(startBanner);

        body.Children.Add(CreateSection(
            "Кому и когда нужны настройки",
            CreateRoleFlow(),
            CreateParagraph("Окно «Конфигурация проекта» предназначено прежде всего для BIM-координатора. Обычному пользователю после первичной настройки достаточно выбрать категории, область и имя спецификации в главном окне.")));

        body.Children.Add(CreateSection(
            "Что должно быть подготовлено в модели",
            CreateParagraph("Плагин рассчитывает помещения и отделочные элементы активного документа Revit. Чтобы результат был полным, помещения должны быть размещены и иметь корректные границы, а отделка — действительно соприкасаться с ними."),
            CreateBulletedList(
                "Есть хотя бы одно размещённое помещение в выбранной области расчёта.",
                $"Типы отделочных стен, полов и потолков имеют параметр «{FinishScheduleSettings.ClassificationParameterName}» и нужное значение классификации.",
                $"У включённых категорий заполнен общий текстовый параметр типа с описанием отделки; рекомендуемое имя — «{FinishSchedulePreferredParameterNames.Description}».",
                "Для записи результата существуют записываемые текстовые параметры экземпляра помещения.",
                "Если включена запись принадлежности, у физических элементов существуют записываемые текстовые параметры экземпляра.",
                "Расчёт выполняется по активному документу. Элементы связанной модели не следует считать поддерживаемым источником отделки.")));

        body.Children.Add(CreateSection(
            "Обязательные, условные и необязательные поля",
            CreateParagraph("Требования ниже относятся к команде «Сформировать». Для «Предпросмотра» достаточно категорий, значений классификации и корректной области расчёта — выходные параметры ещё можно не настраивать."),
            CreateRequirementLegend(),
            CreateRequirementsTable()));

        body.Children.Add(CreateSection(
            "Первичная настройка проекта — работа координатора",
            CreateNumberedList(
                "Откройте рабочий документ Revit и запустите «Ведомость отделки».",
                "Если общих параметров ещё нет, нажмите «Добавить параметры по умолчанию». Плагин создаст недостающие параметры TrueBIM и привяжет их к помещениям, стенам, перекрытиям и потолкам. Существующие совместимые параметры останутся без потери данных.",
                $"Заполните у типов отделки параметр «{FinishScheduleSettings.ClassificationParameterName}». Рекомендуемые значения по умолчанию: стены — «Внутренняя отделка», полы — «Пол», потолки — «Потолки». Значения сравниваются с конфигурацией.",
                "Заполните у типов отделки параметр описания. Он должен быть текстовым параметром типа и присутствовать у всех включённых физических категорий.",
                "Нажмите «Конфигурация проекта» и проверьте четыре блока: классификацию, источник описания, идентификатор помещения и выходные параметры помещений.",
                "В блоке классификации задайте точное значение для каждой категории. Отключённая в текущем расчёте категория не участвует в проверке и не требует выходных параметров.",
                "В блоке описания выберите единый текстовый параметр типа. Его значение попадёт в ведомость как наименование отделки.",
                "Выберите идентификатор помещения: номер, имя или пользовательский текстовый параметр экземпляра помещения.",
                "Назначьте отдельные записываемые параметры помещения для списка помещений, описания и площади каждой включённой категории. Один параметр нельзя использовать для двух разных выходных значений.",
                "Нажмите «Применить». Кнопка «Отмена» закрывает конфигурацию без переноса изменений в главное окно.",
                "Нажмите «Экспорт конфигурации», чтобы сохранить проектные правила в JSON. На другом компьютере или в другой модели откройте конфигурацию и выполните «Импорт конфигурации».",
                "После импорта проверьте сопоставление параметров: JSON переносит проектную классификацию, источник описания, идентификатор и выходные параметры, но не меняет категории текущего расчёта, его область и имя спецификации.")));

        body.Children.Add(CreateSection(
            "Текущий расчёт — пошаговая работа пользователя",
            CreateNumberedList(
                "В блоке «Категории отделки» оставьте только нужные категории. Должна быть включена хотя бы одна: стены, полы или потолки.",
                "Решите, нужна ли запись фактической принадлежности в элементы отделки. Для обычного выпуска ведомости флажок можно оставить выключенным.",
                "Выберите область: «По этажу», «По секции или корпусу» либо «По всему объекту». Для этажа обязательно выберите уровень; для секции — параметр помещения и точное значение.",
                "Задайте название спецификации. TrueBIM обновляет только спецификацию со своим служебным маркером; чужой вид с таким же именем не перезаписывается.",
                "При необходимости нажмите «Сохранить настройки». Локальный профиль сохраняется даже при незавершённой конфигурации и автоматически сохраняется при закрытии окна.",
                "Нажмите «Предпросмотр». Плагин прочитает модель, проверит классификацию, соберёт помещения и рассчитает площади, но ничего не запишет.",
                "Проверьте строку состояния и значок предупреждений рядом с методичкой. По нажатию откроется короткая понятная сводка. Полный технический отчёт можно скопировать отдельной кнопкой.",
                "Если отчёт ожидаемый и полная проверка настроек пройдена, нажмите «Сформировать». Перед записью просмотрите план изменений и подтвердите действие.",
                "Плагин запишет выходные параметры помещений, при включённом флажке — принадлежность элементов, затем создаст или обновит управляемую спецификацию.",
                "После успешного результата нажмите «Открыть спецификацию». Главное окно закроется, а Revit откроет созданный или обновлённый вид.")));

        body.Children.Add(CreateSection(
            "Какие данные читаются и куда записывается результат",
            CreateDataFlowTable(),
            CreateParagraph("Площади записываются в текстовые параметры в согласованном формате ведомости. Описания и площади разных вариантов отделки идут синхронными блоками; между разными отделками добавляется пустая строка для визуального разделения.")));

        body.Children.Add(CreateSection(
            "Как плагин определяет отделку и площадь",
            CreateBulletedList(
                $"Категория элемента и значение параметра типа «{FinishScheduleSettings.ClassificationParameterName}» определяют, относится ли элемент к стенам, полам или потолкам.",
                "Помещения с одинаковым набором отделок объединяются в одну строку спецификации; в первом столбце перечисляются их идентификаторы.",
                "Для стен используется фактический контакт с границей помещения; площадь проёмов и геометрия учитываются средствами Revit и расчётным контуром плагина.",
                "Для полов и потолков плагин старается получить площадь фактической границы помещения, а не просто взять площадь всего элемента.",
                "Наклонный потолок поддерживается: горизонтальность не является обязательным условием. Для надёжного расчёта это должен быть элемент категории Revit «Потолки», участвующий в границе помещения и реально соприкасающийся с верхней границей помещения.",
                "Окраска лестничного марша, смоделированная как лестница, не считается потолком. Отдельного класса «Отделка лестницы» в текущей ведомости нет.",
                "Знак «—» означает, что отделка выбранной категории у группы помещений отсутствует.",
                "Значение «[Не удалось определить]» означает неполный расчёт геометрии. Его нельзя считать нулём или отсутствием отделки — требуется проверить отчёт.")));

        body.Children.Add(CreateSection(
            "Что меняет модель, а что безопасно",
            CreateSafetyGrid()));

        body.Children.Add(CreateSection(
            "Статусы и сообщения",
            CreateStatusExplanation(
                "Готово",
                TrueBimUiSeverity.Success,
                "Настройки корректны, расчёт завершён без критичных неопределённостей."),
            CreateStatusExplanation(
                "Выполнено частично",
                TrueBimUiSeverity.Warning,
                "Часть значений рассчитана, но есть замечания, способные повлиять на ведомость. Откройте значок предупреждений и проверьте указанные помещения и элементы."),
            CreateStatusExplanation(
                "Без изменений",
                TrueBimUiSeverity.Info,
                "Параметры и управляемая спецификация уже соответствуют расчёту. Это нормальный результат повторного запуска."),
            CreateStatusExplanation(
                "Не удалось определить",
                TrueBimUiSeverity.Danger,
                "Для конкретной категории и помещения не получена надёжная площадь. Выпускать ведомость без проверки этого места не следует.",
                isLast: true)));

        body.Children.Add(CreateSection(
            "Если потолок не попал в ведомость",
            CreateNumberedList(
                "Убедитесь, что категория «Потолки» включена в текущем расчёте.",
                $"Проверьте, что объект действительно имеет категорию Revit «Потолки», а его тип содержит параметр «{FinishScheduleSettings.ClassificationParameterName}» с тем же значением, которое задано в конфигурации.",
                "Проверьте наличие и заполнение выбранного параметра описания у типа потолка.",
                "В свойствах потолка включите «Граница помещения» (Room Bounding), если потолок должен формировать верхнюю границу помещения.",
                "Проверьте верхний предел и смещение помещения: геометрия помещения должна доходить до потолка и реально с ним соприкасаться.",
                "Проверьте область расчёта — уровень, секцию или корпус. Помещение должно входить в выбранную область.",
                "Запустите «Предпросмотр», скопируйте отчёт и найдите идентификаторы помещения и потолка. Наклон сам по себе не является причиной исключения.",
                "Если объект является лестницей или моделью другой категории, он не будет автоматически принят за потолок, даже если визуально находится над помещением.")));

        body.Children.Add(CreateSection(
            "Другие частые проблемы",
            CreateTroubleshootingTable()));

        body.Children.Add(CreateSection(
            "Что приложить к обращению",
            CreateBulletedList(
                "версию Revit и название активного документа;",
                "точный шаг: настройка, предпросмотр, формирование или открытие спецификации;",
                "скриншот окна, проблемного элемента и его свойств;",
                "скопированный полный отчёт из окна плагина;",
                "идентификаторы помещений и элементов из отчёта;",
                "файл `%APPDATA%\\TrueBIM\\Logs\\truebim.log` или актуальную копию `truebim.log`;",
                "JSON конфигурации, если проблема проявляется после импорта.")));

        return body;
    }

    private static UIElement CreateRoleFlow()
    {
        Grid grid = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, TrueBimTheme.Spacing8)
        };
        for (int index = 0; index < 3; index++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        AddFlowCard(grid, 0, "1. Координатор", "Создаёт параметры, задаёт классификацию, источник описания и выходные поля.", TrueBimUiSeverity.Info);
        AddFlowCard(grid, 1, "2. Пользователь", "Выбирает категории и область, выполняет предпросмотр и проверяет отчёт.", TrueBimUiSeverity.Warning);
        AddFlowCard(grid, 2, "3. Выпуск", "Подтверждает формирование, затем открывает и проверяет спецификацию Revit.", TrueBimUiSeverity.Success);
        return grid;
    }

    private static void AddFlowCard(Grid grid, int column, string title, string text, TrueBimUiSeverity severity)
    {
        StackPanel content = new()
        {
            Margin = new Thickness(TrueBimTheme.Spacing12)
        };
        content.Children.Add(TrueBimUi.CreateStatusBadge(title, severity));
        TextBlock description = CreateParagraph(text);
        description.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0);
        content.Children.Add(description);

        Border card = new()
        {
            Background = TrueBimBrushes.SurfaceAlt,
            BorderBrush = GuideBorderBrush,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
            Margin = new Thickness(0, 0, column < 2 ? TrueBimTheme.Spacing8 : 0, 0),
            Child = content
        };
        Grid.SetColumn(card, column);
        grid.Children.Add(card);
    }

    private static UIElement CreateRequirementLegend()
    {
        StackPanel legend = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        };
        legend.Children.Add(TrueBimUi.CreateStatusBadge("Обязательно", TrueBimUiSeverity.Danger));
        Border conditional = TrueBimUi.CreateStatusBadge("Условно", TrueBimUiSeverity.Warning);
        conditional.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        legend.Children.Add(conditional);
        Border optional = TrueBimUi.CreateStatusBadge("Необязательно", TrueBimUiSeverity.Info);
        optional.Margin = new Thickness(TrueBimTheme.Spacing8, 0, 0, 0);
        legend.Children.Add(optional);
        return legend;
    }

    private static UIElement CreateRequirementsTable()
    {
        StackPanel rows = new();
        rows.Children.Add(CreateRequirementRow(
            "Категории отделки",
            "Обязательно",
            TrueBimUiSeverity.Danger,
            "Включите минимум одну категорию."));
        rows.Children.Add(CreateRequirementRow(
            "Значение классификации",
            "Обязательно",
            TrueBimUiSeverity.Danger,
            $"Непустое значение «{FinishScheduleSettings.ClassificationParameterName}» для каждой включённой категории."));
        rows.Children.Add(CreateRequirementRow(
            "Источник описания",
            "Обязательно",
            TrueBimUiSeverity.Danger,
            "Текстовый параметр типа, доступный у всех включённых физических категорий."));
        rows.Children.Add(CreateRequirementRow(
            "Выходные параметры",
            "Обязательно",
            TrueBimUiSeverity.Danger,
            "Отдельные записываемые текстовые параметры экземпляра помещения: список помещений, описание и площадь каждой включённой категории."));
        rows.Children.Add(CreateRequirementRow(
            "Название спецификации",
            "Обязательно",
            TrueBimUiSeverity.Danger,
            "Непустое имя управляемой спецификации помещений."));
        rows.Children.Add(CreateRequirementRow(
            "Уровень",
            "Условно",
            TrueBimUiSeverity.Warning,
            "Обязателен только для области «По этажу»."));
        rows.Children.Add(CreateRequirementRow(
            "Параметр и значение секции",
            "Условно",
            TrueBimUiSeverity.Warning,
            "Обязательны только для области «По секции или корпусу»."));
        rows.Children.Add(CreateRequirementRow(
            "Пользовательский идентификатор",
            "Условно",
            TrueBimUiSeverity.Warning,
            "Текстовый параметр экземпляра помещения нужен только при выборе одноимённого режима."));
        rows.Children.Add(CreateRequirementRow(
            "Параметры принадлежности",
            "Условно",
            TrueBimUiSeverity.Warning,
            "Нужны для каждой включённой категории только при включённой записи принадлежности."));
        rows.Children.Add(CreateRequirementRow(
            "Сохранение профиля и JSON",
            "Необязательно",
            TrueBimUiSeverity.Info,
            "Упрощает повторные расчёты и передачу проектной конфигурации, но не влияет на геометрию расчёта.",
            isLast: true));

        return CreateTableFrame(rows);
    }

    private static UIElement CreateRequirementRow(
        string field,
        string status,
        TrueBimUiSeverity severity,
        string description,
        bool isLast = false)
    {
        Grid row = CreateTableGrid(190, 125);
        row.Children.Add(CreateCellText(field, FontWeights.SemiBold));

        Border badge = TrueBimUi.CreateStatusBadge(status, severity);
        badge.HorizontalAlignment = HorizontalAlignment.Left;
        badge.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(badge, 1);
        row.Children.Add(badge);

        TextBlock details = CreateCellText(description, FontWeights.Normal);
        details.Foreground = MutedBrush;
        Grid.SetColumn(details, 2);
        row.Children.Add(details);
        return CreateTableRowFrame(row, isLast);
    }

    private static UIElement CreateDataFlowTable()
    {
        StackPanel rows = new();
        rows.Children.Add(CreateDataFlowRow(
            "Читается из типа отделки",
            $"«{FinishScheduleSettings.ClassificationParameterName}» и выбранный источник описания, например «{FinishSchedulePreferredParameterNames.Description}»."));
        rows.Children.Add(CreateDataFlowRow(
            "Читается из помещения",
            "Номер, имя или пользовательский идентификатор; уровень; параметр секции при соответствующей области."));
        rows.Children.Add(CreateDataFlowRow(
            "Записывается в помещение",
            "Список помещений группы, описания стен, полов и потолков, а также соответствующие площади."));
        rows.Children.Add(CreateDataFlowRow(
            "Записывается в отделку",
            "Идентификаторы фактически связанных помещений — только если включён флажок записи принадлежности."));
        rows.Children.Add(CreateDataFlowRow(
            "Создаётся или обновляется",
            "Спецификация помещений, которой TrueBIM назначил служебный маркер.",
            isLast: true));
        return CreateTableFrame(rows);
    }

    private static UIElement CreateDataFlowRow(string source, string destination, bool isLast = false)
    {
        Grid row = CreateTableGrid(215, 0);
        row.ColumnDefinitions.RemoveAt(1);
        row.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        row.Children.Add(CreateCellText(source, FontWeights.SemiBold));
        TextBlock details = CreateCellText(destination, FontWeights.Normal);
        details.Foreground = MutedBrush;
        Grid.SetColumn(details, 1);
        row.Children.Add(details);
        return CreateTableRowFrame(row, isLast);
    }

    private static UIElement CreateSafetyGrid()
    {
        Grid grid = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, 0)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        UIElement safe = CreateChecklistColumn(
            "Не меняет модель",
            TrueBimUiSeverity.Success,
            "открытие методички и конфигурации;",
            "редактирование полей до применения;",
            "сохранение локального профиля;",
            "импорт и экспорт JSON конфигурации;",
            "предпросмотр и копирование отчёта.");
        Grid.SetColumn(safe, 0);
        grid.Children.Add(safe);

        UIElement write = CreateChecklistColumn(
            "Меняет модель",
            TrueBimUiSeverity.Danger,
            "добавление параметров по умолчанию;",
            "подтверждённое формирование;",
            "запись результатов в помещения;",
            "запись принадлежности при включённом флажке;",
            "создание или обновление управляемой спецификации.");
        Grid.SetColumn(write, 1);
        grid.Children.Add(write);
        return grid;
    }

    private static UIElement CreateChecklistColumn(
        string title,
        TrueBimUiSeverity severity,
        params string[] items)
    {
        StackPanel content = new()
        {
            Margin = new Thickness(TrueBimTheme.Spacing12)
        };
        content.Children.Add(TrueBimUi.CreateStatusBadge(title, severity));
        UIElement list = CreateBulletedList(items);
        content.Children.Add(list);

        return new Border
        {
            Background = TrueBimBrushes.SurfaceAlt,
            BorderBrush = GuideBorderBrush,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
            Margin = severity == TrueBimUiSeverity.Success
                ? new Thickness(0, 0, TrueBimTheme.Spacing8, 0)
                : new Thickness(0),
            Child = content
        };
    }

    private static UIElement CreateStatusExplanation(
        string title,
        TrueBimUiSeverity severity,
        string text,
        bool isLast = false)
    {
        Grid row = CreateTableGrid(165, 0);
        row.ColumnDefinitions.RemoveAt(1);
        row.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        Border badge = TrueBimUi.CreateStatusBadge(title, severity);
        badge.HorizontalAlignment = HorizontalAlignment.Left;
        badge.VerticalAlignment = VerticalAlignment.Top;
        row.Children.Add(badge);
        TextBlock details = CreateCellText(text, FontWeights.Normal);
        details.Foreground = MutedBrush;
        Grid.SetColumn(details, 1);
        row.Children.Add(details);
        return CreateTableRowFrame(row, isLast);
    }

    private static UIElement CreateTroubleshootingTable()
    {
        StackPanel rows = new();
        rows.Children.Add(CreateTroubleshootingRow(
            "Параметра нет в списке",
            "Проверьте категорию привязки, тип/экземпляр, тип данных и доступность записи. Для стандартного комплекта используйте «Добавить параметры по умолчанию», затем заново откройте или обновите окно."));
        rows.Children.Add(CreateTroubleshootingRow(
            "Кнопка «Сформировать» неактивна",
            "Прочитайте предупреждение проверки. Обычно не назначено обязательное поле, повторно использован выходной параметр или не заполнена область расчёта."));
        rows.Children.Add(CreateTroubleshootingRow(
            "Помещения не найдены",
            "Проверьте размещение и границы помещений, выбранный уровень, параметр секции и точное значение фильтра."));
        rows.Children.Add(CreateTroubleshootingRow(
            "Появилось «[Не удалось определить]»",
            "Скопируйте отчёт, найдите ID помещения и элемента, затем проверьте контакт, границы помещения, проёмы и геометрию указанного элемента."));
        rows.Children.Add(CreateTroubleshootingRow(
            "Спецификация не обновилась",
            "Проверьте полную валидацию, подтверждение записи и итоговый отчёт. Чужой вид без маркера TrueBIM не перезаписывается; при необходимости задайте другое имя."));
        rows.Children.Add(CreateTroubleshootingRow(
            "Получено «Без изменений»",
            "Это не ошибка: рассчитанные значения и управляемая спецификация уже совпадают с моделью.",
            isLast: true));
        return CreateTableFrame(rows);
    }

    private static UIElement CreateTroubleshootingRow(string problem, string action, bool isLast = false)
    {
        Grid row = CreateTableGrid(215, 0);
        row.ColumnDefinitions.RemoveAt(1);
        row.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        row.Children.Add(CreateCellText(problem, FontWeights.SemiBold));
        TextBlock details = CreateCellText(action, FontWeights.Normal);
        details.Foreground = MutedBrush;
        Grid.SetColumn(details, 1);
        row.Children.Add(details);
        return CreateTableRowFrame(row, isLast);
    }

    private static Grid CreateTableGrid(double firstColumnWidth, double secondColumnWidth)
    {
        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(firstColumnWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(secondColumnWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return row;
    }

    private static Border CreateTableFrame(UIElement content)
    {
        return new Border
        {
            Background = TrueBimBrushes.SurfaceAlt,
            BorderBrush = GuideBorderBrush,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
            Padding = new Thickness(TrueBimTheme.Spacing12, 0, TrueBimTheme.Spacing12, 0),
            Child = content
        };
    }

    private static Border CreateTableRowFrame(UIElement content, bool isLast)
    {
        return new Border
        {
            BorderBrush = GuideBorderBrush,
            BorderThickness = isLast
                ? new Thickness(0)
                : new Thickness(0, 0, 0, TrueBimTheme.BorderWidth),
            Padding = new Thickness(0, TrueBimTheme.Spacing8, 0, TrueBimTheme.Spacing8),
            Child = content
        };
    }

    private static TextBlock CreateCellText(string text, FontWeight fontWeight)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = fontWeight,
            Foreground = TextBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing12, 0),
            LineHeight = 19
        };
    }

    private static Border CreateSection(string title, params UIElement[] children)
    {
        StackPanel content = new()
        {
            Margin = TrueBimTheme.SectionPadding
        };
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = TrueBimTheme.SectionTitleFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextBrush,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        });

        foreach (UIElement child in children)
        {
            content.Children.Add(child);
        }

        return new Border
        {
            BorderBrush = GuideBorderBrush,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            Background = TrueBimBrushes.Surface,
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, TrueBimTheme.Spacing16),
            Child = content
        };
    }

    private static TextBlock CreateParagraph(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = MutedBrush,
            Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, TrueBimTheme.Spacing8),
            LineHeight = 19
        };
    }

    private static UIElement CreateNumberedList(params string[] items)
    {
        StackPanel stack = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, 0)
        };
        for (int index = 0; index < items.Length; index++)
        {
            stack.Children.Add(CreateListItem($"{index + 1}.", items[index]));
        }

        return stack;
    }

    private static UIElement CreateBulletedList(params string[] items)
    {
        StackPanel stack = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, 0)
        };
        foreach (string item in items)
        {
            stack.Children.Add(CreateListItem("•", item));
        }

        return stack;
    }

    private static UIElement CreateListItem(string marker, string text)
    {
        Grid row = new()
        {
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock
        {
            Text = marker,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.Accent
        });
        TextBlock item = CreateParagraph(text);
        item.Margin = new Thickness(0);
        Grid.SetColumn(item, 1);
        row.Children.Add(item);
        return row;
    }

    private UIElement CreateFooter()
    {
        Button closeButton = TrueBimUi.CreateSecondaryButton(
            "Закрыть методичку",
            TrueBimIcon.Close,
            (_, _) => Close(),
            minWidth: 165);
        closeButton.IsCancel = true;
        closeButton.ToolTip = "Закрыть методичку и вернуться к настройкам ведомости.";

        TextBlock hint = CreateParagraph("Методичка описывает текущую версию модуля. Закрытие окна не меняет настройки и модель Revit.");
        hint.Margin = new Thickness(0);
        hint.VerticalAlignment = VerticalAlignment.Center;
        return TrueBimUi.CreateFooter(hint, closeButton);
    }
}
