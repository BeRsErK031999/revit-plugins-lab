using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrueBIM.App.UI;

namespace TrueBIM.App.Modules.BimTools.ClashReport.UI;

public sealed class ClashReportGuideWindow : TrueBimWindow
{
    private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(18, 38, 58));
    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(74, 90, 106));
    private static readonly Brush GuideBorderBrush = new SolidColorBrush(Color.FromRgb(215, 222, 232));
    private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(53, 100, 168));
    private static readonly Brush AccentBackgroundBrush = new SolidColorBrush(Color.FromRgb(234, 240, 250));
    private static readonly Brush SuccessBrush = new SolidColorBrush(Color.FromRgb(31, 138, 112));
    private static readonly Brush SuccessBackgroundBrush = new SolidColorBrush(Color.FromRgb(232, 243, 240));
    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(176, 111, 0));
    private static readonly Brush WarningBackgroundBrush = new SolidColorBrush(Color.FromRgb(255, 247, 230));

    public ClashReportGuideWindow()
    {
        Title = "Методичка: отчёт коллизий";
        Icon = IconFactory.CreateImage(TrueBimIcon.Help, 32);
        Width = 860;
        Height = 720;
        MinWidth = 760;
        MinHeight = 600;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();
    }

    private UIElement CreateContent()
    {
        DockPanel root = new()
        {
            Margin = new Thickness(18)
        };

        UIElement footer = CreateFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        ScrollViewer viewer = new()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = CreateGuideBody()
        };
        root.Children.Add(viewer);
        return root;
    }

    private static UIElement CreateGuideBody()
    {
        StackPanel body = new()
        {
            MaxWidth = 790
        };

        body.Children.Add(CreateHeader());
        body.Children.Add(CreateCallout(
            "Принцип работы",
            "Плагин не ищет коллизии сам. Он читает готовый список из CSV/XML, находит указанные элементы в открытой модели Revit и помогает быстро перейти к каждой проблеме в отдельном 3D-виде.",
            AccentBrush,
            AccentBackgroundBrush));

        body.Children.Add(CreateSection(
            "Начало работы — 5 простых шагов",
            CreateStep(1, "Откройте инструмент", "Откройте нужную модель, затем нажмите TrueBIM → Координация → Отчёт коллизий."),
            CreateStep(2, "Добавьте файл", "Нажмите «Добавить файл» и выберите CSV или XML."),
            CreateStep(3, "Выберите коллизию", "Щёлкните нужную строку в таблице. Фильтр поможет найти её по имени, ID, статусу или ElementId."),
            CreateStep(4, "Покажите проблему", "Нажмите «Показать в 3D» или дважды щёлкните строку. Плагин откроет вид BIM_Clash_Report_3D и ограничит его вокруг коллизии."),
            CreateStep(5, "Запишите результат", "Измените статус, добавьте комментарий и нажмите «Сохранить».")));

        body.Children.Add(CreateSection(
            "Куда нажимать",
            CreateAction("Добавить файл", "загрузить новый CSV/XML и заполнить таблицу."),
            CreateAction("Проверить", "ещё раз найти элементы по ElementId после обновления модели или RVT-связей."),
            CreateAction("Выбрать", "выделить найденные элементы в текущей модели Revit. Для элемента из связи выбирается экземпляр связи."),
            CreateAction("Показать в 3D", "перейти к выбранной коллизии и включить section box вокруг неё."),
            CreateAction("Подсветка в 3D", "показать найденные элементы красным и синим на служебном 3D-виде."),
            CreateAction("Сохранить", "сохранить статусы и комментарии локально на компьютере."),
            CreateAction("Журнал", "посмотреть, что было импортировано, найдено или не найдено.")));

        body.Children.Add(CreateSection(
            "Как подготовить CSV",
            CreateParagraph("Одна строка CSV — одна коллизия. Проще всего использовать заголовки:"),
            CreateCodeLine("ClashId;Name;ElementId1;ElementId2;X;Y;Z;Status;Comment"),
            CreateParagraph("Для навигации достаточно корректных ElementId или координат X, Y, Z. Если заголовков нет, плагин ожидает поля именно в указанном выше порядке."),
            CreateCallout(
                "Если используются RVT-связи",
                "ElementId1/2 должен указывать на экземпляр связи в основной модели, а LinkedElementId1/2 — на элемент внутри связанного файла. Связь должна быть загружена.",
                WarningBrush,
                WarningBackgroundBrush)));

        body.Children.Add(CreateSection(
            "Что важно помнить",
            CreateBullet("Исходный CSV/XML не изменяется."),
            CreateBullet("Статусы и комментарии сохраняются локально, а не записываются в RVT-модель и не отправляются обратно в Navisworks."),
            CreateBullet("Если строка показывает «Нет ElementId», переход всё ещё возможен при наличии координат."),
            CreateBullet("Если элемент не найден, проверьте открытую модель, актуальность ElementId и загрузку RVT-связей."),
            CreateBullet("Служебный вид BIM_Clash_Report_3D создаётся автоматически при первом переходе.")));

        body.Children.Add(CreateCallout(
            "Самый короткий сценарий",
            "Добавить файл → выбрать строку → Показать в 3D → исправить модель → поставить статус Resolved → Сохранить.",
            SuccessBrush,
            SuccessBackgroundBrush));
        return body;
    }

    private static UIElement CreateHeader()
    {
        StackPanel header = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 14)
        };
        header.Children.Add(IconFactory.Create(TrueBimIcon.Help, 30));

        StackPanel text = new()
        {
            Margin = new Thickness(12, 0, 0, 0)
        };
        text.Children.Add(new TextBlock
        {
            Text = "Как работать с отчётом коллизий",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextBrush
        });
        text.Children.Add(new TextBlock
        {
            Text = "Пошаговая инструкция без лишних терминов",
            Foreground = MutedBrush,
            Margin = new Thickness(0, 3, 0, 0)
        });
        header.Children.Add(text);
        return header;
    }

    private static UIElement CreateSection(string title, params UIElement[] children)
    {
        StackPanel content = new()
        {
            Margin = new Thickness(14)
        };
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextBrush,
            Margin = new Thickness(0, 0, 0, 8)
        });

        foreach (UIElement child in children)
        {
            content.Children.Add(child);
        }

        return new Border
        {
            BorderBrush = GuideBorderBrush,
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Margin = new Thickness(0, 0, 8, 14),
            Child = content
        };
    }

    private static UIElement CreateStep(int number, string title, string description)
    {
        Grid row = new()
        {
            Margin = new Thickness(0, 3, 0, 8)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Border numberBadge = new()
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = AccentBrush,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = number.ToString(),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(numberBadge, 0);
        row.Children.Add(numberBadge);

        TextBlock text = new()
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = MutedBrush,
            LineHeight = 19
        };
        text.Inlines.Add(new System.Windows.Documents.Run(title + ". ")
        {
            FontWeight = FontWeights.SemiBold,
            Foreground = TextBrush
        });
        text.Inlines.Add(description);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private static UIElement CreateAction(string button, string description)
    {
        TextBlock text = CreateParagraph(string.Empty);
        text.Inlines.Add(new System.Windows.Documents.Run($"«{button}» — ")
        {
            FontWeight = FontWeights.SemiBold,
            Foreground = TextBrush
        });
        text.Inlines.Add(description);
        return text;
    }

    private static TextBlock CreateParagraph(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = MutedBrush,
            Margin = new Thickness(0, 3, 0, 7),
            LineHeight = 19
        };
    }

    private static UIElement CreateBullet(string text)
    {
        return CreateParagraph($"• {text}");
    }

    private static UIElement CreateCodeLine(string text)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            BorderBrush = GuideBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 2, 0, 9),
            Child = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Consolas"),
                Foreground = TextBrush,
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    private static UIElement CreateCallout(string title, string text, Brush accent, Brush background)
    {
        StackPanel content = new()
        {
            Margin = new Thickness(12, 10, 12, 10)
        };
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Foreground = accent,
            Margin = new Thickness(0, 0, 0, 4)
        });
        content.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TextBrush,
            LineHeight = 19
        });

        return new Border
        {
            Background = background,
            BorderBrush = accent,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Margin = new Thickness(0, 0, 8, 14),
            Child = content
        };
    }

    private UIElement CreateFooter()
    {
        DockPanel footer = new()
        {
            LastChildFill = true,
            Margin = new Thickness(0, 14, 8, 0)
        };

        Button closeButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Close, "Закрыть"),
            MinWidth = 120,
            Height = 32,
            IsCancel = true,
            ToolTip = "Закрыть методичку."
        };
        closeButton.Click += (_, _) => Close();
        DockPanel.SetDock(closeButton, Dock.Right);
        footer.Children.Add(closeButton);
        footer.Children.Add(CreateParagraph("Методичка всегда доступна по кнопке «?» в окне отчёта коллизий."));
        return footer;
    }
}
