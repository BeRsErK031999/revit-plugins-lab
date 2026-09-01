using System.Windows;
using System.Windows.Controls;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.Lintels.UI;

public sealed class LintelGuideWindow : TrueBimWindow
{
    public LintelGuideWindow()
    {
        Title = "Методичка: перемычки";
        Icon = IconFactory.CreateImage(TrueBimIcon.Help, TrueBimTheme.IconSizeRibbon);
        Width = 940;
        Height = 800;
        MinWidth = 760;
        MinHeight = 620;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        ApplyTrueBimShell(
            TrueBimUi.CreateHeader(
                "Как работать с перемычками",
                "От поиска исходных семейств до готовых видов 1:10 и изображений типоразмеров",
                TrueBimIcon.Help),
            commandBar: null,
            body: new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = CreateGuideBody()
            },
            status: null,
            footer: CreateFooter());
    }

    private static UIElement CreateGuideBody()
    {
        StackPanel body = new()
        {
            MaxWidth = 860
        };

        Border quickRoute = TrueBimUi.CreateInfoBanner(
            "Короткий маршрут: выберите область поиска → отметьте типоразмеры → проверьте и создайте сборки → выберите два семейства аннотаций .rfa → создайте оформленные виды 1:10. До подтверждения шага 3 модель не изменяется.",
            TrueBimUiSeverity.Info);
        quickRoute.Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, TrueBimTheme.Spacing16);
        body.Children.Add(quickRoute);

        body.Children.Add(CreateSection(
            "Что подготовить перед запуском",
            CreateBulletedList(
                "Откройте рабочий проект Revit. Плагин работает только с активным документом.",
                "Перемычки должны быть экземплярами загружаемых семейств. Для автоматического поиска в имени семейства, типоразмера или экземпляра должно быть слово «перемыч» либо «lintel».",
                "Для создания Assembly у родительской перемычки должны быть вложенные проектные компоненты с модельной геометрией. Если статус не «Готово», откройте диагностику и проверьте состав семейства.",
                "Подготовьте семейство рамки и семейство высотной отметки категории «Типовая аннотация» в формате .rfa. Если стандартная библиотека доступна, TrueBIM найдёт их автоматически.")));

        body.Children.Add(CreateSection(
            "Шаг 1. Откуда взять перемычки",
            CreateParagraph("Первое окно только читает проект. Выберите один из четырёх источников:"),
            CreateBulletedList(
                "Текущее выделение — используйте, когда нужные экземпляры уже выделены. Имена не проверяются, поэтому этот режим подходит для нестандартно названных семейств.",
                "Активный вид — ищет подходящие экземпляры, видимые на открытом виде, по словам «перемыч» или «lintel».",
                "Весь проект — ищет подходящие экземпляры во всём активном документе, включая элементы вне текущего вида. В больших моделях поиск может занять больше времени.",
                "Результаты, созданные TrueBIM — показывает исходные типоразмеры, для которых в проекте уже найдены сборки с именами TrueBIM. Режим станет доступен после создания первой такой сборки."),
            CreateParagraph("Нажмите «Далее: выбрать типоразмеры». Если ничего не найдено, вернитесь к выбору, используйте текущее выделение или проверьте имена семейств.")));

        body.Children.Add(CreateSection(
            "Шаг 2. Какие типоразмеры обрабатывать",
            CreateNumberedList(
                "Посмотрите сводку и таблицу найденных типоразмеров. Строки сгруппированы по Revit TypeId, поэтому одинаково названные типы не смешиваются.",
                "Отметьте одну или несколько строк со статусом «Готово» или «Сборка уже есть». Кнопка «Выбрать готовые» отмечает все доступные строки.",
                "Справа проверьте будущие имена сборки, бокового вида и PNG. На этом этапе модель по-прежнему не изменяется.",
                "При необходимости нажмите «Диагностика» или «Проверить выбранные». Проверка показывает состав и причины блокировки без записи в Revit.")));

        body.Children.Add(CreateSection(
            "Шаг 3. Создание сборок",
            CreateNumberedList(
                "Нажмите «Шаг 3: создать сборки». TrueBIM ещё раз выполнит безопасную проверку выбранных строк.",
                "Прочитайте итог: сколько сборок будет создано, уже существует или будет пропущено. Запись начинается только после подтверждения.",
                "Для каждого выбранного типоразмера создаётся одна Assembly из вложенных компонентов представительного экземпляра. Имена начинаются с «TB_Перемычка_» и включают TypeId.",
                "Повторный запуск не создаёт дубликат сборки с тем же TrueBIM-именем. Ошибка одной строки не отменяет успешно обработанные строки пакета."),
            CreateWarning("После шага 3 в проекте появляются или переиспользуются Assembly. Сама строка Assembly не открывается двойным щелчком. Раскрываемый дочерний вид и изображение появятся только после шага 4; если часть выбранных строк не получила отдельную сборку, она не блокирует создание видов для остальных.")));

        body.Children.Add(CreateSection(
            "Шаг 4. Что можно подгрузить и что будет создано",
            CreateParagraph("Нужны два файла семейств Revit (.rfa), оба категории «Типовая аннотация»:"),
            CreateBulletedList(
                "Рамка — размещается по центру каждого бокового вида. Стандартный файл ищется в корпоративной библиотеке автоматически; кнопкой «Выбрать рамку .rfa» можно указать другой файл.",
                "Высотная отметка — размещается по центру нижней границы перемычек. Кнопкой «Выбрать высотную отметку .rfa» можно указать другой файл. Для совместимого строкового параметра TrueBIM задаёт текст «отм.».",
                "Если семейство уже загружено в проект, Revit переиспользует его. Выбранный файл должен существовать и соответствовать категории «Типовая аннотация»."),
            CreateNumberedList(
                "Убедитесь, что обе строки файлов показывают выбранные .rfa, а сборки для отмеченных типов уже существуют.",
                "Нажмите «Шаг 4: создать виды 1:10».",
                "TrueBIM создаст или переиспользует левый боковой assembly view масштаба 1:10, настроит детализацию и скрытые линии, подгонит область обрезки, поставит габаритный размер, рамку и высотную аннотацию.",
                "Оформленный вид экспортируется в PNG шириной 1600 px. PNG импортируется или обновляется как ImageType и назначается исходному типоразмеру через параметр «Изображение типоразмера».")));

        body.Children.Add(CreateSection(
            "Обновление и повторный запуск",
            CreateBulletedList(
                "«Обновить из Revit» повторно читает тот источник, который был выбран на шаге 1. Для «Текущего выделения» сначала измените выделение в Revit.",
                "Выбор строк сохраняется после обновления, если соответствующие TypeId всё ещё присутствуют.",
                "Существующие TrueBIM-сборки и виды переиспользуются; собственные аннотации TrueBIM можно безопасно обновлять повторным запуском.",
                "PNG сохраняются локально в %LOCALAPPDATA%\\TrueBIM\\Lintels\\<проект>. Исходный RVT не обязан быть сохранён, но сохранённое имя проекта упрощает поиск файлов.")));

        body.Children.Add(CreateSection(
            "Если результат не получен",
            CreateBulletedList(
                "Ничего не найдено — проверьте активный документ и правило имени; для нестандартного имени выделите перемычки вручную.",
                "Статус не «Готово» — откройте «Диагностика» и проверьте вложенные компоненты, их геометрию и допустимость создания Assembly.",
                "Не выбирается шаг 4 — сначала создайте или найдите сборки шага 3, оставьте нужные строки отмеченными и укажите оба .rfa.",
                "Файл семейства не принимается — убедитесь, что это существующий .rfa категории «Типовая аннотация», а не модельное семейство другой категории.",
                "При ошибке откройте логи TrueBIM на панели «Помощь» и передайте запись вместе с именем проекта и ID проблемного типоразмера.")));

        return body;
    }

    private static Border CreateSection(string title, params UIElement[] elements)
    {
        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = TrueBimTheme.SectionTitleFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextPrimary,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        });
        foreach (UIElement element in elements)
        {
            content.Children.Add(element);
        }

        return new Border
        {
            Background = TrueBimBrushes.Surface,
            BorderBrush = TrueBimBrushes.Border,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
            Padding = TrueBimTheme.SectionPadding,
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
            Foreground = TrueBimBrushes.TextSecondary,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8),
            LineHeight = 19
        };
    }

    private static UIElement CreateNumberedList(params string[] items)
    {
        StackPanel stack = new();
        for (int index = 0; index < items.Length; index++)
        {
            stack.Children.Add(CreateListItem($"{index + 1}.", items[index]));
        }

        return stack;
    }

    private static UIElement CreateBulletedList(params string[] items)
    {
        StackPanel stack = new();
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

    private static Border CreateWarning(string text)
    {
        Border warning = TrueBimUi.CreateInfoBanner(text, TrueBimUiSeverity.Warning);
        warning.Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, 0);
        return warning;
    }

    private UIElement CreateFooter()
    {
        TextBlock hint = CreateParagraph("Методичка всегда доступна по кнопке с иконкой «?» в окнах модуля «Перемычки».");
        hint.Margin = new Thickness(0);
        hint.VerticalAlignment = VerticalAlignment.Center;

        Button closeButton = TrueBimUi.CreateSecondaryButton(
            "Закрыть методичку",
            TrueBimIcon.Close,
            (_, _) => Close(),
            minWidth: 165);
        closeButton.IsCancel = true;
        closeButton.ToolTip = "Закрыть методичку и вернуться к работе с перемычками.";
        return TrueBimUi.CreateFooter(hint, closeButton);
    }
}
