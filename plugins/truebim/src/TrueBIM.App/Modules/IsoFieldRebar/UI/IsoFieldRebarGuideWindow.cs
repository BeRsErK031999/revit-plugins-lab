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
                "Пошаговая инструкция: от выбора карт до проверки и применения арматуры.",
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
            "Перед началом",
            CreateParagraph("Для расчёта арматуры нужны четыре карты из одного расчёта: две для направления X и две для направления Y. Модуль работает с горизонтальными плитами и прямыми обычными стенами. Наклонные плиты, криволинейные, составные и витражные стены пока не поддерживаются."),
            CreateParagraph("Готовый файл с уже найденными зонами можно открыть для просмотра и проверки интерфейса, но создать по нему арматуру нельзя."),
            CreateDiagramCard("Общий порядок работы.", CreatePipelineDiagram())));
        body.Children.Add(CreateSection(
            "Шаг 1. Загрузите карты",
            CreateNumberedList(
                "Нажмите «Выбрать карты или готовые зоны» и выберите сразу четыре карты изополей.",
                "Убедитесь, что в таблице появились четыре строки. Если строка красная, укажите правильную карту вручную.",
                "Для плиты назначьте каждой карте низ или верх. Для стены назначьте внутреннюю или наружную сторону.",
                "Нажмите «Найти зоны на 4 картах». После обработки проверьте количество контуров и цветовых шкал.",
                "Если этот комплект понадобится снова, нажмите «Сохранить комплект».")));
        body.Children.Add(CreateSection(
            "Шаг 2. Проверьте зоны",
            CreateParagraph("Контуры в окне — это только изображение результата. Они не добавляются в модель."),
            CreateNumberedList(
                "Нажмите «Исправить зоны», если нужно исключить лишнюю область, изменить её диапазон или объединить соседние области.",
                "Кнопка «Показать линии на виде» добавляет на текущий вид Revit вспомогательные линии. Это не арматура, но линии являются элементами модели.",
                "После проверки нажмите «Удалить линии с вида», если они больше не нужны.",
                "Если зоны обрезаны, пропали или явно не совпадают с картой, вернитесь к первому шагу и проверьте выбранные файлы.")));
        body.Children.Add(CreateSection(
            "Шаг 3. Совместите карты со стеной или плитой",
            CreateNumberedList(
                "Нажмите «Выбрать стену/плиту», затем укажите конструкцию в Revit.",
                "На карте выберите три хорошо различимые точки. Третья точка должна находиться в стороне от линии между первыми двумя.",
                "Введите координаты этих точек на карте и по очереди укажите те же места на выбранной конструкции.",
                "Нажмите «Проверить привязку». В окне появится схема совмещения с границами конструкции и отверстиями.",
                "Если зоны отмечены красным или третья точка не прошла проверку, исправьте точки и повторите проверку.",
                "Успешную привязку можно сохранить и позже восстановить для той же конструкции и вида.")));
        body.Children.Add(CreateSection(
            "Шаг 4. Рассчитайте и примените арматуру",
            CreateNumberedList(
                "Выберите режим расчёта, отступ арматуры от поверхности, отступ от границ и минимальную длину стержня.",
                "Нажмите «Рассчитать раскладку». Модуль покажет зоны, принятые сочетания, стороны конструкции и количество стержней. Модель на этом шаге не меняется.",
                "Просмотрите блок проверки. Ошибки нужно исправить. Предупреждения можно подтвердить только после проверки отмеченных мест.",
                "При необходимости выделите строку и нажмите «Настроить выбранную». Можно заменить сочетание или исключить зону.",
                "Нажмите «Сравнить с моделью». В таблице появится, какие стержни будут добавлены, изменены или удалены. Сравнение ничего не записывает в модель.",
                "Нажмите «Сохранить отчёт», если нужен файл для проверки или передачи коллегам.",
                "Только после просмотра таблицы нажмите «Применить изменения» и подтвердите действие. Модуль изменит лишь ту арматуру, которую ранее создал сам; ручную арматуру он не затрагивает.",
                "После применения проверьте итоговые количества, отступ арматуры от поверхности, отверстия и положение стержней. Все изменения арматуры можно отменить одной стандартной командой отмены Revit.")));
        body.Children.Add(CreateSection(
            "Если кнопка недоступна",
            CreateBulletedList(
                "посмотрите блок «Готовность» справа: там написан следующий обязательный шаг;",
                "проверьте, что выбраны четыре карты и для каждой назначена сторона конструкции;",
                "убедитесь, что привязка по трём точкам прошла без красных зон;",
                "раскройте блок проверки и исправьте ошибки;",
                "перед применением обязательно выполните «Сравнить с моделью»;",
                "если модель изменилась после сравнения, проверьте обновлённую таблицу ещё раз.")));
        body.Children.Add(CreateSection(
            "Границы безопасности",
            CreateSafetyGrid()));
        body.Children.Add(CreateSection(
            "Что прикладывать к ошибке",
            CreateBulletedList(
                "версию Revit и название активного документа;",
                "готовый файл с зонами, сохранённый комплект или четыре исходные карты;",
                "скриншот этого окна после шага, где возникла проблема;",
                "журнал работы, который открывается одноимённой кнопкой;",
                "короткое описание: что было выбрано, какая кнопка нажата и что произошло.")));

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
        AddNode(canvas, 10, 28, 130, 70, "1. Карты", "выбор четырёх файлов", TrueBimBrushes.SuccessBackground, TrueBimBrushes.Success);
        AddArrow(canvas, 145, 63, 175, 63, arrowBrush);
        AddNode(canvas, 180, 28, 130, 70, "2. Зоны", "поиск и проверка", TrueBimBrushes.InfoBackground, TrueBimBrushes.Info);
        AddArrow(canvas, 315, 63, 345, 63, arrowBrush);
        AddNode(canvas, 350, 28, 130, 70, "3. Просмотр", "контуры и вспомогательные линии", TrueBimBrushes.WarningBackground, TrueBimBrushes.Warning);
        AddArrow(canvas, 485, 63, 515, 63, arrowBrush);
        AddNode(canvas, 520, 28, 130, 70, "4. Конструкция", "стена или плита", TrueBimBrushes.NeutralBackground, TrueBimBrushes.Accent);
        AddArrow(canvas, 655, 63, 675, 63, arrowBrush);
        AddNode(canvas, 680, 28, 70, 70, "5.", "раскладка", TrueBimBrushes.DangerBackground, TrueBimBrushes.Danger);

        AddCanvasText(canvas, "Выбор карт, проверка зон, привязка и расчёт раскладки не создают арматуру.", 20, 128, 680, 15, FontWeights.SemiBold, TextBrush);
        AddCanvasText(canvas, "«Сравнить с моделью» заполняет таблицу без записи; применение требует отдельного подтверждения.", 20, 154, 720, 14, FontWeights.Normal, MutedBrush);
        AddCanvasText(canvas, "Вспомогательные линии на виде создаются отдельной кнопкой и так же отдельно удаляются.", 20, 178, 720, 14, FontWeights.Normal, MutedBrush);
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

        AddCanvasText(canvas, "Готовые зоны", 20, 10, 180, 15, FontWeights.SemiBold, TextBrush);
        AddCanvasText(canvas, "Контуры в окне", 292, 10, 180, 15, FontWeights.SemiBold, TextBrush);
        AddCanvasText(canvas, "Конструкция и арматура", 542, 10, 200, 15, FontWeights.SemiBold, TextBrush);

        Border zonesBlock = new()
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
                Text = "Готовые зоны\n\nЗон: 12\nГраницы: найдены\nОшибок: нет\n\nМожно переходить\nк просмотру.",
                FontSize = 12,
                Foreground = TextBrush
            }
        };
        Canvas.SetLeft(zonesBlock, 20);
        Canvas.SetTop(zonesBlock, 36);
        canvas.Children.Add(zonesBlock);

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

        AddCanvasText(canvas, "В примере готовый контур можно только посмотреть. Для расчёта арматуры нужен полный комплект из четырёх карт и проверенная привязка к конструкции.", 20, 226, 710, 14, FontWeights.Normal, MutedBrush);
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

        AddCanvasText(canvas, "конструкция", 64, 24, 110, 14, FontWeights.SemiBold, TextBrush);
        AddCanvasText(canvas, "арматура", 64, 132, 90, 13, FontWeights.Normal, MutedBrush);
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
            "Не меняет арматуру",
            TrueBimBrushes.Success,
            "выбор карт и загрузка готовых зон;",
            "просмотр контуров в окне;",
            "исправление и объединение зон;",
            "выбор стены или плиты;",
            "совмещение по трём точкам;",
            "расчёт количества и положения стержней;",
            "ручная настройка зон;",
            "сравнение с моделью;",
            "сохранение отчёта.",
            "Важно: «Показать линии на виде» добавляет вспомогательные линии в модель, но не создаёт арматуру.");
        Grid.SetColumn(safeColumn, 0);
        grid.Children.Add(safeColumn);

        UIElement writeColumn = CreateChecklistColumn(
            "Меняет модель",
            TrueBimBrushes.Danger,
            "только команда «Применить изменения»;",
            "только после отдельного сравнения, просмотра таблицы и подтверждения;",
            "стержни создаются только внутри проверенных зон;",
            "повтор без изменений не создаёт дубли;",
            "арматура, созданная вручную, не затрагивается;",
            "все изменения отменяются одной стандартной командой отмены Revit.");
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
