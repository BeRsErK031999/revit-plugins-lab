using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.IsoFieldRebar.UI;

public sealed class IsoFieldRebarGuideWindow : TrueBimWindow
{
    private static readonly Brush TextBrush = TrueBimBrushes.TextPrimary;
    private static readonly Brush MutedBrush = TrueBimBrushes.TextSecondary;
    private static readonly Brush GuideBorderBrush = TrueBimBrushes.Border;
    private static readonly Brush PanelBrush = TrueBimBrushes.SurfaceAlt;

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
        ScrollViewer viewer = new()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = CreateGuideBody()
        };

        return BuildShell(
            header: TrueBimUi.CreateHeader(
                Title,
                "Справка по безопасному сценарию IsoField Rebar: комплект, назначение слоёв, preview, host, правила и пробная запись.",
                TrueBimIcon.IsoFieldRebar),
            commandBar: null,
            body: viewer,
            status: null,
            footer: CreateFooter());
    }

    private static UIElement CreateGuideBody()
    {
        StackPanel body = new()
        {
            MaxWidth = 820
        };

        body.Children.Add(CreateSection(
            "Как работает текущий режим",
            CreateParagraph("Модуль ведёт пользователя от комплекта изополей к явному назначению верх/низ, безопасной проверке контуров, выбору host-элемента и только затем к созданию пробного армирования. До кнопки «Создать пробное армирование» модель Revit не должна меняться."),
            CreateDiagramCard("Путь данных в текущем режиме модуля.", CreatePipelineDiagram())));
        body.Children.Add(CreateSection(
            "Комплект из четырёх карт",
            CreateParagraph("Для расчётных изображений выберите вместе As1X, As2X, As3Y и As4Y. Плагин сверит роль в имени файла с маркером в растровом заголовке ПК ЛИРА, проверит pixel size и покажет миниатюры. Направление X/Y определяется ролью, а верхнюю/нижнюю грань пользователь подтверждает явно."),
            CreateNumberedList(
                "Выберите четыре изображения одновременно или откройте ранее сохранённый `.isofield-set.json`.",
                "Проверьте подпись происхождения роли: «имя + заголовок», «только по имени» или «по заголовку».",
                "Если имя и заголовок конфликтуют, строка подсветится красным — выберите правильный слой вручную.",
                "Для направлений X и Y назначьте ровно один слой «Низ» и один слой «Верх».",
                "Сохраните manifest: он зафиксирует относительные пути, размеры, SHA-256 и назначение слоёв.",
                "Распознайте четыре изображения и продолжайте к host и правилам.")));
        body.Children.Add(CreateSection(
            "Пример с JSON",
            CreateParagraph("Для проверки удобно взять fixture `docs/IsoFieldRebar/examples/sample-wall-zones.json`: он уже содержит контуры зон и не требует внешнего worker-а распознавания."),
            CreateDiagramCard("Пример: JSON-контур становится preview, затем правилом для выбранной стены или плиты.", CreateExampleDiagram()),
            CreateNumberedList(
                "Выберите JSON-файл изополей.",
                "Проверьте контуры в окне и, при необходимости, покажите временные линии в Revit.",
                "Выберите простую стену или плиту как host-элемент.",
                "Рассчитайте правила армирования и прочитайте диагностику.",
                "Создавайте пробное армирование только после подтверждения и сразу проверяйте Undo.")));
        body.Children.Add(CreateSection(
            "Границы безопасности",
            CreateSafetyGrid()));
        body.Children.Add(CreateSection(
            "Что прикладывать к ошибке",
            CreateBulletedList(
                "версию Revit и название активного документа;",
                "входной JSON, manifest или комплект изображений изополей;",
                "скриншот этого окна после шага, где возникла проблема;",
                "лог `%APPDATA%\\TrueBIM\\Logs\\truebim.log`;",
                "описание шага: файл, preview, host, правила или пробная запись.")));

        return body;
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
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0)
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
            Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, 0)
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
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            Background = PanelBrush,
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
            Padding = new Thickness(TrueBimTheme.Spacing8),
            Child = diagram
        });

        TextBlock captionBlock = CreateParagraph(caption);
        captionBlock.Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, TrueBimTheme.Spacing12);
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

        Brush arrowBrush = MutedBrush;
        AddNode(canvas, 10, 28, 130, 70, "1. Источник", "роль из имени + заголовка", TrueBimBrushes.SuccessBackground, TrueBimBrushes.Success);
        AddArrow(canvas, 145, 63, 175, 63, arrowBrush);
        AddNode(canvas, 180, 28, 130, 70, "2. Контуры", "JSON reader или CLI-worker", TrueBimBrushes.InfoBackground, TrueBimBrushes.Info);
        AddArrow(canvas, 315, 63, 345, 63, arrowBrush);
        AddNode(canvas, 350, 28, 130, 70, "3. Preview", "картинка в окне и линии Revit", TrueBimBrushes.WarningBackground, TrueBimBrushes.Warning);
        AddArrow(canvas, 485, 63, 515, 63, arrowBrush);
        AddNode(canvas, 520, 28, 130, 70, "4. Host", "стена или плита", TrueBimBrushes.NeutralBackground, TrueBimBrushes.Accent);
        AddArrow(canvas, 655, 63, 685, 63, arrowBrush);
        AddNode(canvas, 690, 28, 60, 70, "5.", "правила", TrueBimBrushes.DangerBackground, TrueBimBrushes.Danger);

        AddCanvasText(canvas, "Безопасная часть: чтение, preview, выбор host и расчет правил не создают арматуру.", 20, 128, 510, 15, FontWeights.SemiBold, TextBrush);
        AddCanvasText(canvas, "Запись в модель начинается только после кнопки «Создать пробное армирование» и отдельного подтверждения.", 20, 154, 690, 14, FontWeights.Normal, MutedBrush);
        AddCanvasText(canvas, "Undo должен вернуть модель в исходное состояние после пробной записи.", 20, 178, 690, 14, FontWeights.Normal, MutedBrush);
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
        AddCanvasText(canvas, "Host + пробное армирование", 542, 10, 200, 15, FontWeights.SemiBold, TextBrush);

        Border jsonBlock = new()
        {
            Width = 205,
            Height = 174,
            Background = TrueBimBrushes.Surface,
            BorderBrush = GuideBorderBrush,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(TrueBimTheme.Radius6),
            Padding = new Thickness(TrueBimTheme.Spacing12),
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

        Brush arrowBrush = MutedBrush;
        AddArrow(canvas, 232, 122, 278, 122, arrowBrush);
        AddArrow(canvas, 482, 122, 528, 122, arrowBrush);

        Border previewBorder = new()
        {
            Width = 190,
            Height = 174,
            Background = TrueBimBrushes.Surface,
            BorderBrush = GuideBorderBrush,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(TrueBimTheme.Radius6),
            Child = CreatePreviewPicture()
        };
        Canvas.SetLeft(previewBorder, 286);
        Canvas.SetTop(previewBorder, 36);
        canvas.Children.Add(previewBorder);

        Border hostBorder = new()
        {
            Width = 205,
            Height = 174,
            Background = TrueBimBrushes.Surface,
            BorderBrush = GuideBorderBrush,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(TrueBimTheme.Radius6),
            Child = CreateHostPicture()
        };
        Canvas.SetLeft(hostBorder, 536);
        Canvas.SetTop(hostBorder, 36);
        canvas.Children.Add(hostBorder);

        AddCanvasText(canvas, "В примере контур `wall-zone-a` сначала отображается как полилиния, затем превращается в read-only правило. Пробный Rebar создаётся только после подтверждения пользователя.", 20, 226, 710, 14, FontWeights.Normal, MutedBrush);
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
            Fill = TrueBimBrushes.SurfaceAlt
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
            Stroke = TrueBimBrushes.Success,
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
            Stroke = TrueBimBrushes.Info,
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
            Fill = TrueBimBrushes.SurfaceAlt
        });
        Rectangle hostShape = new()
        {
            Width = 142,
            Height = 74,
            RadiusX = 4,
            RadiusY = 4,
            Fill = TrueBimBrushes.InfoBackground,
            Stroke = TrueBimBrushes.Info,
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
                Stroke = TrueBimBrushes.Danger,
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
            Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, 0)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        UIElement safeColumn = CreateChecklistColumn(
            "Без записи в модель",
            TrueBimBrushes.Success,
            "выбор файла и чтение JSON;",
            "preview контуров в окне;",
            "временные линии предпросмотра;",
            "выбор host-элемента;",
            "расчет правил и диагностика.");
        Grid.SetColumn(safeColumn, 0);
        grid.Children.Add(safeColumn);

        UIElement writeColumn = CreateChecklistColumn(
            "Меняет модель",
            TrueBimBrushes.Danger,
            "только команда «Создать пробное армирование»;",
            "только после подтверждения;",
            "создаётся пробный Rebar;",
            "проверяется через Revit Undo.");
        Grid.SetColumn(writeColumn, 1);
        grid.Children.Add(writeColumn);

        return grid;
    }

    private static UIElement CreateChecklistColumn(string title, Brush accent, params string[] items)
    {
        StackPanel stack = new()
        {
            Margin = new Thickness(0, 0, TrueBimTheme.Spacing16, 0)
        };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Foreground = accent,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing4)
        });

        foreach (string item in items)
        {
            stack.Children.Add(CreateParagraph($"- {item}"));
        }

        return stack;
    }

    private UIElement CreateFooter()
    {
        Button closeButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Close, "Закрыть"),
            MinWidth = 120,
            MinHeight = TrueBimTheme.ControlHeight32,
            Style = TrueBimStyles.CreateButtonStyle(),
            IsCancel = true,
            ToolTip = "Закрыть методичку."
        };
        closeButton.Click += (_, _) => Close();

        return TrueBimUi.CreateFooter(
            CreateParagraph("Подсказка отражает текущий безопасный режим модуля и не описывает другие инструменты TrueBIM."),
            closeButton);
    }

    private static void AddNode(Canvas canvas, double x, double y, double width, double height, string title, string subtitle, Brush fill, Brush stroke)
    {
        StackPanel content = new()
        {
            Margin = new Thickness(TrueBimTheme.Spacing8)
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
            Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, 0)
        });

        Border node = new()
        {
            Width = width,
            Height = height,
            Background = fill,
            BorderBrush = stroke,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
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
