using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TrueBIM.App.UI;

namespace TrueBIM.App.Modules.IsoFieldRebar.UI;

public sealed class IsoFieldRebarGuideWindow : Window
{
    private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(18, 38, 58));
    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(74, 90, 106));
    private static readonly Brush GuideBorderBrush = new SolidColorBrush(Color.FromRgb(215, 222, 232));
    private static readonly Brush PanelBrush = new SolidColorBrush(Color.FromRgb(248, 250, 252));

    public IsoFieldRebarGuideWindow()
    {
        Title = "Методичка: армирование по изополям";
        Icon = IconFactory.CreateImage(TrueBimIcon.IsoFieldRebar, 32);
        Width = 900;
        Height = 760;
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
            "Как работает каркас",
            CreateParagraph("Каркас модуля ведет пользователя от входного файла изополей к безопасной проверке контуров, выбору host-элемента и только затем к созданию тестовой арматуры. До кнопки «Создать тестовую» модель Revit не должна меняться."),
            CreateDiagramCard("Путь данных в текущем каркасе модуля.", CreatePipelineDiagram())));
        body.Children.Add(CreateSection(
            "Пример с JSON",
            CreateParagraph("Для проверки удобно взять fixture `docs/IsoFieldRebar/examples/sample-wall-zones.json`: он уже содержит контуры зон и не требует внешнего worker-а распознавания."),
            CreateDiagramCard("Пример: JSON-контур становится preview, затем правилом для выбранной стены или плиты.", CreateExampleDiagram()),
            CreateNumberedList(
                "Выберите JSON-файл изополей.",
                "Проверьте контуры в окне и, при необходимости, покажите временные линии в Revit.",
                "Выберите простую стену или плиту как host-элемент.",
                "Рассчитайте правила армирования и прочитайте диагностику.",
                "Создавайте тестовую арматуру только после подтверждения и сразу проверяйте Undo.")));
        body.Children.Add(CreateSection(
            "Границы безопасности",
            CreateSafetyGrid()));
        body.Children.Add(CreateSection(
            "Что прикладывать к ошибке",
            CreateBulletedList(
                "версию Revit и название активного документа;",
                "входной JSON или изображение изополей;",
                "скриншот этого окна после шага, где возникла проблема;",
                "лог `%APPDATA%\\TrueBIM\\Logs\\truebim.log`;",
                "описание шага: файл, preview, host, правила или тестовая запись.")));

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
        titleRow.Children.Add(IconFactory.Create(TrueBimIcon.IsoFieldRebar, 30));
        titleRow.Children.Add(new TextBlock
        {
            Text = "Методичка каркаса изополей",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(titleRow);

        header.Children.Add(CreateParagraph("Эта справка относится только к модулю `Армирование по изополям` и описывает текущий безопасный сценарий: входной файл, preview, host, правила и тестовую арматуру."));
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
            Height = 220,
            ClipToBounds = true
        };

        Brush arrowBrush = new SolidColorBrush(Color.FromRgb(74, 90, 106));
        AddNode(canvas, 10, 28, 130, 70, "1. Файл", "JSON или изображение", Color.FromRgb(232, 243, 240), Color.FromRgb(31, 138, 112));
        AddArrow(canvas, 145, 63, 175, 63, arrowBrush);
        AddNode(canvas, 180, 28, 130, 70, "2. Контуры", "JSON reader или CLI-worker", Color.FromRgb(234, 240, 250), Color.FromRgb(53, 100, 168));
        AddArrow(canvas, 315, 63, 345, 63, arrowBrush);
        AddNode(canvas, 350, 28, 130, 70, "3. Preview", "картинка в окне и линии Revit", Color.FromRgb(255, 243, 219), Color.FromRgb(176, 111, 0));
        AddArrow(canvas, 485, 63, 515, 63, arrowBrush);
        AddNode(canvas, 520, 28, 130, 70, "4. Host", "стена или плита", Color.FromRgb(246, 238, 250), Color.FromRgb(128, 77, 156));
        AddArrow(canvas, 655, 63, 685, 63, arrowBrush);
        AddNode(canvas, 690, 28, 60, 70, "5.", "правила", Color.FromRgb(252, 235, 235), Color.FromRgb(178, 58, 72));

        AddCanvasText(canvas, "Безопасная часть: чтение, preview, выбор host и расчет правил не создают арматуру.", 20, 128, 510, 15, FontWeights.SemiBold, TextBrush);
        AddCanvasText(canvas, "Запись в модель начинается только после кнопки «Создать тестовую» и отдельного подтверждения.", 20, 154, 690, 14, FontWeights.Normal, MutedBrush);
        AddCanvasText(canvas, "Undo должен вернуть модель в исходное состояние после тестовой записи.", 20, 178, 690, 14, FontWeights.Normal, MutedBrush);
        return canvas;
    }

    private static Canvas CreateExampleDiagram()
    {
        Canvas canvas = new()
        {
            Width = 760,
            Height = 285,
            ClipToBounds = true
        };

        AddCanvasText(canvas, "Входной JSON", 20, 10, 180, 15, FontWeights.SemiBold, TextBrush);
        AddCanvasText(canvas, "Preview в окне", 292, 10, 180, 15, FontWeights.SemiBold, TextBrush);
        AddCanvasText(canvas, "Host + тестовая арматура", 542, 10, 200, 15, FontWeights.SemiBold, TextBrush);

        Border jsonBlock = new()
        {
            Width = 205,
            Height = 174,
            Background = Brushes.White,
            BorderBrush = GuideBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Child = new TextBlock
            {
                Text = "{\n  \"schemaVersion\": \"1.0\",\n  \"polylines\": [\n    {\n      \"id\": \"wall-zone-a\",\n      \"points\": [ ... ]\n    }\n  ]\n}",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = TextBrush
            }
        };
        Canvas.SetLeft(jsonBlock, 20);
        Canvas.SetTop(jsonBlock, 36);
        canvas.Children.Add(jsonBlock);

        Brush arrowBrush = new SolidColorBrush(Color.FromRgb(74, 90, 106));
        AddArrow(canvas, 232, 122, 278, 122, arrowBrush);
        AddArrow(canvas, 482, 122, 528, 122, arrowBrush);

        Border previewBorder = new()
        {
            Width = 190,
            Height = 174,
            Background = Brushes.White,
            BorderBrush = GuideBorderBrush,
            BorderThickness = new Thickness(1),
            Child = CreatePreviewPicture()
        };
        Canvas.SetLeft(previewBorder, 286);
        Canvas.SetTop(previewBorder, 36);
        canvas.Children.Add(previewBorder);

        Border hostBorder = new()
        {
            Width = 205,
            Height = 174,
            Background = Brushes.White,
            BorderBrush = GuideBorderBrush,
            BorderThickness = new Thickness(1),
            Child = CreateHostPicture()
        };
        Canvas.SetLeft(hostBorder, 536);
        Canvas.SetTop(hostBorder, 36);
        canvas.Children.Add(hostBorder);

        AddCanvasText(canvas, "В примере контур `wall-zone-a` сначала отображается как полилиния, затем превращается в read-only правило. Тестовый Rebar создается только после подтверждения пользователя.", 20, 226, 710, 14, FontWeights.Normal, MutedBrush);
        return canvas;
    }

    private static Canvas CreatePreviewPicture()
    {
        Canvas canvas = new()
        {
            Width = 188,
            Height = 172,
            ClipToBounds = true
        };

        canvas.Children.Add(new Rectangle
        {
            Width = 188,
            Height = 172,
            Fill = new SolidColorBrush(Color.FromRgb(248, 250, 252))
        });
        canvas.Children.Add(new Polyline
        {
            Points = new PointCollection
            {
                new(32, 118),
                new(68, 52),
                new(122, 58),
                new(152, 124)
            },
            Stroke = new SolidColorBrush(Color.FromRgb(31, 138, 112)),
            StrokeThickness = 4,
            StrokeLineJoin = PenLineJoin.Round
        });
        canvas.Children.Add(new Polyline
        {
            Points = new PointCollection
            {
                new(42, 136),
                new(95, 92),
                new(145, 140)
            },
            Stroke = new SolidColorBrush(Color.FromRgb(53, 100, 168)),
            StrokeThickness = 3,
            StrokeLineJoin = PenLineJoin.Round
        });
        AddCanvasText(canvas, "2 контура", 58, 18, 90, 14, FontWeights.SemiBold, TextBrush);
        return canvas;
    }

    private static Canvas CreateHostPicture()
    {
        Canvas canvas = new()
        {
            Width = 203,
            Height = 172,
            ClipToBounds = true
        };

        canvas.Children.Add(new Rectangle
        {
            Width = 203,
            Height = 172,
            Fill = new SolidColorBrush(Color.FromRgb(248, 250, 252))
        });
        Rectangle hostShape = new()
        {
            Width = 142,
            Height = 74,
            RadiusX = 4,
            RadiusY = 4,
            Fill = new SolidColorBrush(Color.FromRgb(234, 240, 250)),
            Stroke = new SolidColorBrush(Color.FromRgb(53, 100, 168)),
            StrokeThickness = 2
        };
        Canvas.SetLeft(hostShape, 30);
        Canvas.SetTop(hostShape, 48);
        canvas.Children.Add(hostShape);

        for (int index = 0; index < 4; index++)
        {
            Line rebar = new()
            {
                X1 = 46 + (index * 30),
                Y1 = 62,
                X2 = 46 + (index * 30),
                Y2 = 108,
                Stroke = new SolidColorBrush(Color.FromRgb(178, 58, 72)),
                StrokeThickness = 4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            canvas.Children.Add(rebar);
        }

        AddCanvasText(canvas, "host", 84, 24, 70, 14, FontWeights.SemiBold, TextBrush);
        AddCanvasText(canvas, "test rebar", 64, 132, 90, 13, FontWeights.Normal, MutedBrush);
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
            "выбор файла и чтение JSON;",
            "preview контуров в окне;",
            "временные линии предпросмотра;",
            "выбор host-элемента;",
            "расчет правил и диагностика.");
        Grid.SetColumn(safeColumn, 0);
        grid.Children.Add(safeColumn);

        UIElement writeColumn = CreateChecklistColumn(
            "Меняет модель",
            new SolidColorBrush(Color.FromRgb(178, 58, 72)),
            "только команда «Создать тестовую»;",
            "только после подтверждения;",
            "создается тестовый Rebar;",
            "проверяется через Revit Undo.");
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
        footer.Children.Add(CreateParagraph("Подсказка отражает текущий каркас модуля и не описывает другие инструменты TrueBIM."));
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
