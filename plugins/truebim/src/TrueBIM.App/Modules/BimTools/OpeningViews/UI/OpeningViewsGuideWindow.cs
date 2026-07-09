using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TrueBIM.App.UI;

namespace TrueBIM.App.Modules.BimTools.OpeningViews.UI;

public sealed class OpeningViewsGuideWindow : Window
{
    private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(18, 38, 58));
    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(74, 90, 106));
    private static readonly Brush GuideBorderBrush = new SolidColorBrush(Color.FromRgb(215, 222, 232));
    private static readonly Brush PanelBrush = new SolidColorBrush(Color.FromRgb(248, 250, 252));

    public OpeningViewsGuideWindow()
    {
        Title = "Методичка: фасады дверей/окон";
        Icon = IconFactory.CreateImage(TrueBimIcon.OpeningViews, 32);
        Width = 880;
        Height = 720;
        MinWidth = 760;
        MinHeight = 620;
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
            MaxWidth = 820
        };

        body.Children.Add(CreateHeader());
        body.Children.Add(CreateSection(
            "Что делает плагин",
            CreateParagraph("Инструмент собирает двери и окна, которые видны на активном плане Revit, показывает таблицу предпросмотра и затем создаёт для выбранных проёмов отдельные фасадные elevation-виды."),
            CreateParagraph("Для каждого будущего вида проверяются имя, категория, семейство, тип, уровень, ориентация, наличие типа фасада, bounding box элемента и дубли уже существующих видов."),
            CreateDiagramCard("Путь данных внутри текущего инструмента.", CreatePipelineDiagram())));
        body.Children.Add(CreateSection(
            "Рабочий сценарий",
            CreateNumberedList(
                "Откройте обычный план, на котором видны нужные двери или окна.",
                "Выберите тип фасада, шаблон вида, категории, ориентацию, масштаб и запасы crop box.",
                "Нажмите «Предпросмотр»: модель не меняется, но таблица покажет будущие имена видов и причины пропуска.",
                "Оставьте отмеченными только нужные строки и нажмите «Создать виды».",
                "После выполнения проверьте отчёт в окне или сохраните CSV для передачи в задачу/QA.")));
        body.Children.Add(CreateSection(
            "Что меняет модель",
            CreateSafetyGrid()));
        body.Children.Add(CreateSection(
            "Поля и настройки",
            CreateBulletedList(
                "Тип фасада: Revit ViewFamilyType для elevation-видов. Без него строки не готовы к созданию.",
                "Ориентация: «по элементу» берёт FacingOrientation, «по стене» пробует стену-основу и выбирает сторону, ближайшую к FacingOrientation.",
                "Шаблон вида: необязательный elevation view template, который применяется к созданному виду.",
                "Crop, мм: запас вокруг двери или окна по ширине и высоте.",
                "Глубина, мм: запас crop box по направлению взгляда.",
                "Имя вида: шаблон с токенами {ElementId}, {CategoryKey}, {Category}, {Family}, {Type}, {Level}. Дубли имён пропускаются.")));
        body.Children.Add(CreateSection(
            "Ограничения и диагностика",
            CreateBulletedList(
                "Плагин запускается только на обычном активном плане, не на листе, шаблоне, 3D-виде, спецификации или browser-виде.",
                "Предпросмотр пустой, если двери/окна скрыты на активном плане, не входят в выбранные категории или не проходят видовой фильтр Revit.",
                "Строка не создаётся, если не выбран тип фасада, нет bounding box или уже существует вид с таким именем.",
                "Если стена-основа не найдена, инструмент использует ориентацию самого элемента и пишет это в сообщении строки.",
                "Для ошибки приложите активный план, ElementId строки, CSV-отчёт и лог `%APPDATA%\\TrueBIM\\Logs\\truebim.log`.")));

        return body;
    }

    private static UIElement CreateHeader()
    {
        StackPanel header = new()
        {
            Margin = new Thickness(0, 0, 0, 14)
        };

        StackPanel titleRow = new()
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleRow.Children.Add(IconFactory.Create(TrueBimIcon.OpeningViews, 30));
        titleRow.Children.Add(new TextBlock
        {
            Text = "Методичка по фасадам дверей/окон",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(titleRow);

        header.Children.Add(CreateParagraph("Эта справка относится только к модулю «Фасады дверей/окон» и описывает текущий сценарий: сбор проёмов с активного плана, предпросмотр, проверка дублей, создание elevation-видов и CSV-отчёт."));
        return header;
    }

    private static Border CreateSection(string title, params UIElement[] children)
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

    private static TextBlock CreateParagraph(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = MutedBrush,
            Margin = new Thickness(0, 4, 0, 8),
            LineHeight = 19
        };
    }

    private static UIElement CreateNumberedList(params string[] items)
    {
        StackPanel stack = new()
        {
            Margin = new Thickness(0, 6, 0, 0)
        };

        for (int index = 0; index < items.Length; index++)
        {
            stack.Children.Add(CreateParagraph($"{index + 1}. {items[index]}"));
        }

        return stack;
    }

    private static UIElement CreateBulletedList(params string[] items)
    {
        StackPanel stack = new()
        {
            Margin = new Thickness(0, 4, 0, 0)
        };

        foreach (string item in items)
        {
            stack.Children.Add(CreateParagraph($"- {item}"));
        }

        return stack;
    }

    private static UIElement CreateDiagramCard(string caption, Canvas diagram)
    {
        StackPanel stack = new();
        stack.Children.Add(new Border
        {
            BorderBrush = GuideBorderBrush,
            BorderThickness = new Thickness(1),
            Background = PanelBrush,
            Padding = new Thickness(8),
            Child = diagram
        });

        TextBlock captionBlock = CreateParagraph(caption);
        captionBlock.Margin = new Thickness(0, 6, 0, 10);
        stack.Children.Add(captionBlock);
        return stack;
    }

    private static Canvas CreatePipelineDiagram()
    {
        Canvas canvas = new()
        {
            Width = 760,
            Height = 178,
            ClipToBounds = true
        };

        Brush arrowBrush = new SolidColorBrush(Color.FromRgb(74, 90, 106));
        AddNode(canvas, 10, 26, 128, 72, "1. План", "видимые двери и окна", Color.FromRgb(232, 243, 240), Color.FromRgb(31, 138, 112));
        AddArrow(canvas, 143, 62, 174, 62, arrowBrush);
        AddNode(canvas, 180, 26, 128, 72, "2. Preview", "имя, статус, дубли", Color.FromRgb(234, 240, 250), Color.FromRgb(53, 100, 168));
        AddArrow(canvas, 313, 62, 344, 62, arrowBrush);
        AddNode(canvas, 350, 26, 128, 72, "3. Выбор", "только отмеченные строки", Color.FromRgb(255, 243, 219), Color.FromRgb(176, 111, 0));
        AddArrow(canvas, 483, 62, 514, 62, arrowBrush);
        AddNode(canvas, 520, 26, 128, 72, "4. Создание", "ElevationMarker + crop", Color.FromRgb(246, 238, 250), Color.FromRgb(128, 77, 156));
        AddArrow(canvas, 653, 62, 684, 62, arrowBrush);
        AddNode(canvas, 690, 26, 60, 72, "5.", "CSV", Color.FromRgb(252, 235, 235), Color.FromRgb(178, 58, 72));

        AddCanvasText(canvas, "Предпросмотр, фильтр и снятие выбора не меняют модель Revit.", 20, 122, 560, 15, FontWeights.SemiBold, TextBrush);
        AddCanvasText(canvas, "Запись начинается только после «Создать виды» и отдельного подтверждения.", 20, 148, 680, 14, FontWeights.Normal, MutedBrush);
        return canvas;
    }

    private static UIElement CreateSafetyGrid()
    {
        Grid grid = new()
        {
            Margin = new Thickness(0, 4, 0, 0)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        UIElement safeColumn = CreateChecklistColumn(
            "Без записи в модель",
            new SolidColorBrush(Color.FromRgb(31, 138, 112)),
            "открытие окна и методички;",
            "выбор категорий и настроек;",
            "предпросмотр таблицы;",
            "фильтр, выбор и снятие строк;",
            "экспорт уже собранного CSV-отчёта.");
        Grid.SetColumn(safeColumn, 0);
        grid.Children.Add(safeColumn);

        UIElement writeColumn = CreateChecklistColumn(
            "Меняет модель",
            new SolidColorBrush(Color.FromRgb(178, 58, 72)),
            "команда «Создать виды» после подтверждения;",
            "создание ElevationMarker и ViewSection;",
            "присвоение имени вида;",
            "применение шаблона вида;",
            "настройка crop box и масштаба.");
        Grid.SetColumn(writeColumn, 1);
        grid.Children.Add(writeColumn);

        return grid;
    }

    private static UIElement CreateChecklistColumn(string title, Brush accent, params string[] items)
    {
        StackPanel stack = new()
        {
            Margin = new Thickness(0, 0, 14, 0)
        };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Foreground = accent,
            Margin = new Thickness(0, 0, 0, 4)
        });

        foreach (string item in items)
        {
            stack.Children.Add(CreateParagraph($"- {item}"));
        }

        return stack;
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
        footer.Children.Add(CreateParagraph("Подсказка описывает текущую реализацию модуля и не относится к другим инструментам TrueBIM."));
        return footer;
    }

    private static void AddNode(Canvas canvas, double x, double y, double width, double height, string title, string subtitle, Color fill, Color stroke)
    {
        StackPanel content = new()
        {
            Margin = new Thickness(8)
        };
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextBrush,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 11,
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        });

        Border node = new()
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(fill),
            BorderBrush = new SolidColorBrush(stroke),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = content
        };
        Canvas.SetLeft(node, x);
        Canvas.SetTop(node, y);
        canvas.Children.Add(node);
    }

    private static void AddArrow(Canvas canvas, double x1, double y1, double x2, double y2, Brush brush)
    {
        canvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = brush,
            StrokeThickness = 2
        });
        canvas.Children.Add(new Polygon
        {
            Points = new PointCollection
            {
                new(x2, y2),
                new(x2 - 8, y2 - 5),
                new(x2 - 8, y2 + 5)
            },
            Fill = brush
        });
    }

    private static void AddCanvasText(Canvas canvas, string text, double x, double y, double width, double fontSize, FontWeight fontWeight, Brush brush)
    {
        TextBlock textBlock = new()
        {
            Text = text,
            Width = width,
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
            FontWeight = fontWeight,
            Foreground = brush
        };
        Canvas.SetLeft(textBlock, x);
        Canvas.SetTop(textBlock, y);
        canvas.Children.Add(textBlock);
    }
}
