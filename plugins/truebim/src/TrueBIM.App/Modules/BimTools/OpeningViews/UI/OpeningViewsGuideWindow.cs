using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TrueBIM.App.UI;

namespace TrueBIM.App.Modules.BimTools.OpeningViews.UI;

public sealed class OpeningViewsGuideWindow : TrueBimWindow
{
    private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(18, 38, 58));
    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(74, 90, 106));
    private static readonly Brush GuideBorderBrush = new SolidColorBrush(Color.FromRgb(215, 222, 232));
    private static readonly Brush PanelBrush = new SolidColorBrush(Color.FromRgb(248, 250, 252));

    public OpeningViewsGuideWindow()
    {
        Title = "Методичка: фасады проёмов";
        Icon = IconFactory.CreateImage(TrueBimIcon.Help, 32);
        Width = 920;
        Height = 720;
        MinWidth = 880;
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
            CreateParagraph("Меню «Фасады проёмов» создаёт отдельные elevation-виды для дверей, окон и прямолинейных витражей активного плана, а затем оформляет открытый фасад маркой и ассоциативными габаритами."),
            CreateParagraph("Для каждого будущего вида проверяются имя, категория, семейство, тип, уровень, ориентация, наличие типа фасада, bounding box полной модельной геометрии элемента и дубли уже существующих видов."),
            CreateDiagramCard("Путь данных внутри текущего инструмента.", CreatePipelineDiagram())));
        body.Children.Add(CreateSection(
            "Рабочий сценарий",
            CreateNumberedList(
                "Откройте обычный план, на котором видны нужные двери, окна или стены-витражи.",
                "Выберите тип фасада, шаблон вида, категории, ориентацию, масштаб и запасы crop box.",
                "Нажмите «1. Предпросмотр»: модель не меняется, но таблица покажет будущие имена видов и причины пропуска.",
                "Оставьте отмеченными только нужные строки и нажмите «2. Создать виды».",
                "После выполнения проверьте отчёт в окне или сохраните CSV для передачи в задачу/QA.",
                "Откройте созданный фасад, снова раскройте «Фасады проёмов» и выберите «Шаг 3: оформить активный фасад».",
                "Проверьте preview марки и размеров, затем подтвердите создание аннотаций.")));
        body.Children.Add(CreateSection(
            "Как проходит образмеривание",
            CreateParagraph("Команда оформления сначала находит исходную дверь, окно или витраж по метаданным созданного вида. До подтверждения она показывает, какие аннотации сможет создать, и не меняет модель."),
            CreateDiagramCard("Схема расположения аннотаций относительно полного габарита элемента.", CreateDimensioningDiagram()),
            CreateBulletedList(
                "Марка над фасадом берётся в порядке: Type Mark → Mark экземпляра → имя типа → категория и ElementId.",
                "Двери и окна: ширина строится между reference planes Left/Right, высота — между Bottom/Top. В семействе эти planes должны совпадать именно с границами проёма; иначе размер будет ассоциативным, но геометрически неверным.",
                "Витражи: плагин просматривает геометрию стены, панелей и импостов, оставляет грани, параллельные осям фасада, и выбирает две крайние грани по ширине и две по высоте.",
                "Размер ширины ставится снизу, размер высоты — справа. Смещение равно 4 мм на листе с учётом масштаба вида; марка располагается на 6 мм выше габарита.",
                "Линейные размеры создаются стандартным default DimensionType проекта, марка — доступным TextNoteType. Выбор отдельных типов оформления будет добавлен на этапе профилей.",
                "Для дверей и окон размер получает суффикс «(проём)». Для витража используется «(габарит витража)», потому что цепочка строится по крайним граням конструкции.",
                "Перед созданием аннотаций crop box расширяется так, чтобы в него вошли обе размерные линии и марка; если шаблон вида блокирует crop, плагин сообщает об этом и продолжает доступные операции.",
                "Если одна из пар references не найдена, соответствующий размер не создаётся, а причина остаётся в preview и итоговом сообщении.",
                "Повторный запуск удаляет только ранее созданные этим модулем TextNote и Dimension, затем строит их заново. Ручные аннотации не затрагиваются.")));
        body.Children.Add(CreateSection(
            "Что меняет модель",
            CreateSafetyGrid()));
        body.Children.Add(CreateSection(
            "Поля и настройки",
            CreateBulletedList(
                "Тип фасада: Revit ViewFamilyType для elevation-видов. Без него строки не готовы к созданию.",
                "Ориентация: для двери/окна «по элементу» берёт FacingOrientation, «по стене» — стену-основу; витраж всегда смотрит по наружной ориентации своей стены.",
                "Шаблон вида: необязательный elevation view template, который применяется к созданному виду.",
                "Crop, мм: запас вокруг двери/окна или полного габарита витража по ширине и высоте.",
                "Глубина, мм: запас crop box по направлению взгляда.",
                "Имя вида: шаблон с токенами {ElementId}, {CategoryKey}, {Category}, {Family}, {Type}, {Level}. Дубли имён пропускаются.")));
        body.Children.Add(CreateSection(
            "Ограничения и диагностика",
            CreateBulletedList(
                "Плагин запускается только на обычном активном плане, не на листе, шаблоне, 3D-виде, спецификации или browser-виде.",
                "Предпросмотр пустой, если элементы скрыты на активном плане, не входят в выбранные категории или не проходят видовой фильтр Revit.",
                "Строка не создаётся, если не выбран тип фасада, нет model/view bounding box или уже существует вид с таким именем.",
                "Для crop сначала используется полный model bounding box. Bounding box активного плана применяется только как резерв и отмечается в сообщении предпросмотра.",
                "Если стена-основа не найдена, инструмент использует ориентацию самого элемента и пишет это в сообщении строки.",
                "Для двери/окна размеры создаются по стабильным reference planes Left/Right и Bottom/Top. Если planes обозначают раму, а не проём, сначала исправьте семейство.",
                "Для витража выбираются крайние вертикальные и горизонтальные грани стены, панелей и импостов; это габарит конструкции, а не отдельная цепочка монтажного проёма.",
                "Поддерживаются прямолинейные стены-витражи. Дуговые витражи предпросмотр помечает как требующие отдельной развёртки.",
                "Повторное оформление удаляет и создаёт заново только марку и размеры TrueBIM; ручные аннотации не затрагиваются.",
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
        titleRow.Children.Add(IconFactory.Create(TrueBimIcon.Help, 30));
        titleRow.Children.Add(new TextBlock
        {
            Text = "Методичка по фасадам проёмов",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(titleRow);

        header.Children.Add(CreateParagraph("Эта справка относится только к модулю «Фасады проёмов» и описывает текущий сценарий: сбор дверей, окон и витражей с активного плана, предпросмотр, проверка дублей, создание elevation-видов, оформление и CSV-отчёт."));
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
        AddNode(canvas, 10, 26, 128, 72, "План", "двери, окна, витражи", Color.FromRgb(232, 243, 240), Color.FromRgb(31, 138, 112));
        AddArrow(canvas, 143, 62, 158, 62, arrowBrush);
        AddNode(canvas, 164, 26, 128, 72, "1. Preview", "имя, статус, дубли", Color.FromRgb(234, 240, 250), Color.FromRgb(53, 100, 168));
        AddArrow(canvas, 297, 62, 312, 62, arrowBrush);
        AddNode(canvas, 318, 26, 128, 72, "2. Создание", "ElevationMarker + crop", Color.FromRgb(255, 243, 219), Color.FromRgb(176, 111, 0));
        AddArrow(canvas, 451, 62, 466, 62, arrowBrush);
        AddNode(canvas, 472, 26, 128, 72, "Открыть вид", "фасад в Project Browser", Color.FromRgb(246, 238, 250), Color.FromRgb(128, 77, 156));
        AddArrow(canvas, 605, 62, 620, 62, arrowBrush);
        AddNode(canvas, 626, 26, 124, 72, "3. Оформить", "марка + 2 габарита", Color.FromRgb(252, 235, 235), Color.FromRgb(178, 58, 72));

        AddCanvasText(canvas, "Предпросмотр, фильтр и снятие выбора не меняют модель Revit.", 20, 122, 560, 15, FontWeights.SemiBold, TextBrush);
        AddCanvasText(canvas, "Запись выполняется дважды и только после подтверждений: сначала создаются виды, затем — аннотации активного фасада.", 20, 148, 715, 14, FontWeights.Normal, MutedBrush);
        return canvas;
    }

    private static Canvas CreateDimensioningDiagram()
    {
        Canvas canvas = new()
        {
            Width = 760,
            Height = 238,
            ClipToBounds = true
        };
        Brush geometryBrush = new SolidColorBrush(Color.FromRgb(53, 100, 168));
        Brush referenceBrush = new SolidColorBrush(Color.FromRgb(176, 111, 0));
        Brush dimensionBrush = new SolidColorBrush(Color.FromRgb(178, 58, 72));

        Border element = new()
        {
            Width = 300,
            Height = 120,
            Background = new SolidColorBrush(Color.FromRgb(234, 240, 250)),
            BorderBrush = geometryBrush,
            BorderThickness = new Thickness(2),
            Child = new TextBlock
            {
                Text = "Полный габарит\nдвери / окна / витража",
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = TextBrush,
                FontWeight = FontWeights.SemiBold
            }
        };
        Canvas.SetLeft(element, 220);
        Canvas.SetTop(element, 42);
        canvas.Children.Add(element);

        AddReferenceLine(canvas, 220, 24, 220, 188, referenceBrush, "Left / крайняя грань", 70, 8);
        AddReferenceLine(canvas, 520, 24, 520, 188, referenceBrush, "Right / крайняя грань", 525, 8);
        AddReferenceLine(canvas, 198, 42, 548, 42, referenceBrush, "Top", 556, 34);
        AddReferenceLine(canvas, 198, 162, 548, 162, referenceBrush, "Bottom", 556, 154);

        AddDimensionLine(canvas, 220, 198, 520, 198, dimensionBrush);
        AddCanvasText(canvas, "ширина — снизу", 310, 204, 150, 13, FontWeights.SemiBold, dimensionBrush);
        AddDimensionLine(canvas, 574, 42, 574, 162, dimensionBrush);
        AddCanvasText(canvas, "высота — справа", 590, 92, 145, 13, FontWeights.SemiBold, dimensionBrush);
        AddCanvasText(canvas, "Марка типа", 320, 18, 120, 14, FontWeights.SemiBold, TextBrush);
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
            "настройка crop box и масштаба;",
            "создание марки и ассоциативных размеров;",
            "при повторе — удаление и перестроение только TrueBIM-аннотаций.");
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

    private static void AddReferenceLine(
        Canvas canvas,
        double x1,
        double y1,
        double x2,
        double y2,
        Brush brush,
        string label,
        double labelX,
        double labelY)
    {
        canvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = brush,
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 }
        });
        AddCanvasText(canvas, label, labelX, labelY, 165, 12, FontWeights.Normal, brush);
    }

    private static void AddDimensionLine(Canvas canvas, double x1, double y1, double x2, double y2, Brush brush)
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

        bool isHorizontal = Math.Abs(y2 - y1) < Math.Abs(x2 - x1);
        canvas.Children.Add(new Line
        {
            X1 = isHorizontal ? x1 : x1 - 6,
            Y1 = isHorizontal ? y1 - 6 : y1,
            X2 = isHorizontal ? x1 : x1 + 6,
            Y2 = isHorizontal ? y1 + 6 : y1,
            Stroke = brush,
            StrokeThickness = 2
        });
        canvas.Children.Add(new Line
        {
            X1 = isHorizontal ? x2 : x2 - 6,
            Y1 = isHorizontal ? y2 - 6 : y2,
            X2 = isHorizontal ? x2 : x2 + 6,
            Y2 = isHorizontal ? y2 + 6 : y2,
            Stroke = brush,
            StrokeThickness = 2
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
