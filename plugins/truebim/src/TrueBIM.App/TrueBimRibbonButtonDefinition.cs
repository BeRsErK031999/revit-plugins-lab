using TrueBIM.App.Commands;
using TrueBIM.App.UI;

namespace TrueBIM.App;

public sealed record TrueBimRibbonButtonDefinition(
    string PanelName,
    string Name,
    string Text,
    string CommandClassName,
    TrueBimIcon Icon,
    string Tooltip,
    string LongDescription = "");

public static class TrueBimRibbon
{
    public const string BimPanelName = "БИМ";
    public const string BimDocumentationPanelName = "Оформление";
    public const string BimViewsPanelName = "Виды";
    public const string BimReleasePanelName = "Выпуск";
    public const string BimCoordinationPanelName = "Координация";
    public const string BimLibraryPanelName = "Библиотека";
    public const string ModelReviewPanelName = "Проверка модели";
    public const string GeometryPanelName = "Геометрия";
    public const string ParametersPanelName = "Параметры";
    public const string AdministrationPanelName = "Администрирование";
    public const string KrPanelName = "КР";
    public const string EomPanelName = "ЭОМ";
    public const string SsPanelName = "СС";

    private const string CommandNamespace = "TrueBIM.App.Commands";

    public static IReadOnlyList<string> PanelNames { get; } =
    [
        BimPanelName,
        BimDocumentationPanelName,
        BimViewsPanelName,
        BimReleasePanelName,
        BimCoordinationPanelName,
        BimLibraryPanelName,
        ModelReviewPanelName,
        GeometryPanelName,
        ParametersPanelName,
        AdministrationPanelName,
        KrPanelName,
        EomPanelName,
        SsPanelName
    ];

    public static IReadOnlyList<TrueBimRibbonButtonDefinition> Buttons { get; } =
    [
        new(
            BimPanelName,
            "TrueBIM_SheetNumbering",
            "Нумератор\nлистов",
            $"{CommandNamespace}.{nameof(OpenSheetNumberingCommand)}",
            TrueBimIcon.SheetNumbering,
            "Открывает нумератор листов TrueBIM."),
        new(
            BimPanelName,
            "TrueBIM_Print",
            "Печать",
            $"{CommandNamespace}.{nameof(OpenPrintCommand)}",
            TrueBimIcon.Print,
            "Открывает модуль печати и экспорта листов TrueBIM."),
        new(
            BimPanelName,
            "TrueBIM_ViewVisibility",
            "Видимость",
            $"{CommandNamespace}.{nameof(OpenViewVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Включает и выключает видимость категорий на активном виде."),
        new(
            BimPanelName,
            "TrueBIM_IsoFieldRebar",
            "Армирование\nпо изополям",
            $"{CommandNamespace}.{nameof(IsoFieldRebarCommand)}",
            TrueBimIcon.IsoFieldRebar,
            "Открывает экспериментальный модуль армирования по изополям.",
            "Безопасная заглушка: показывает статус подключения без распознавания, OpenCV/Python и создания арматуры."),
        new(
            BimDocumentationPanelName,
            "TrueBIM_AutoMepDimensions",
            "Авторазмеры\nMEP",
            $"{CommandNamespace}.{nameof(AutoMepDimensionsCommand)}",
            TrueBimIcon.AutoDimensions,
            "Открывает каркас инструмента авторазмеров MEP.",
            "Каркас будущего инструмента для размеров MEP: профиль, фильтры, предпросмотр, выполнение и отчет без изменений модели в первом срезе."),
        new(
            BimDocumentationPanelName,
            "TrueBIM_AutoTags",
            "Автомарки",
            $"{CommandNamespace}.{nameof(AutoTagCommand)}",
            TrueBimIcon.AutoTags,
            "Открывает каркас инструмента автоматической расстановки марок.",
            "Каркас будущего инструмента автомарок: категории, тип марки, фильтры, предпросмотр и отчет без создания марок в первом срезе."),
        new(
            BimDocumentationPanelName,
            "TrueBIM_TitleBlockFill",
            "Оформить\nштамп",
            $"{CommandNamespace}.{nameof(TitleBlockFillCommand)}",
            TrueBimIcon.TitleBlock,
            "Открывает каркас инструмента заполнения штампа.",
            "Каркас будущего инструмента оформления штампа: выбор листов, профиль параметров, предпросмотр и отчет без записи параметров в первом срезе."),
        new(
            BimViewsPanelName,
            "TrueBIM_DatumExtents",
            "Оси\n2D/3D",
            $"{CommandNamespace}.{nameof(DatumExtentCommand)}",
            TrueBimIcon.DatumExtents,
            "Открывает каркас инструмента управления 2D/3D экстентами осей.",
            "Каркас будущего инструмента для DatumPlane: выбор осей/уровней, режим экстентов, предпросмотр и отчет без изменения видов в первом срезе."),
        new(
            BimViewsPanelName,
            "TrueBIM_OpeningViews",
            "Виды дверей\n/ окон",
            $"{CommandNamespace}.{nameof(OpeningViewsCommand)}",
            TrueBimIcon.OpeningViews,
            "Создает elevation-виды дверей и окон с предпросмотром.",
            "MVP инструмента для проемов: сбор дверей и окон активного плана, имена видов BIM_Opening_*, проверка дублей, шаблон вида, crop box и CSV-отчет."),
        new(
            BimReleasePanelName,
            "TrueBIM_BatchExport",
            "Экспорт\nPDF/DWG",
            $"{CommandNamespace}.{nameof(BatchExportCommand)}",
            TrueBimIcon.Export,
            "Открывает каркас инструмента пакетного экспорта PDF/DWG.",
            "Каркас будущего инструмента выпуска: профиль экспорта, выбор листов, правило имени файла, предпросмотр и отчет."),
        new(
            BimCoordinationPanelName,
            "TrueBIM_ClashReport",
            "Отчёт\nколлизий",
            $"{CommandNamespace}.{nameof(ClashReportCommand)}",
            TrueBimIcon.ClashReport,
            "Открывает каркас инструмента отчета коллизий.",
            "Каркас будущего инструмента координации: импорт CSV, статусы, комментарии, переход в 3D и отчет без подсветки элементов в первом срезе."),
        new(
            BimLibraryPanelName,
            "TrueBIM_FamilyManager",
            "Диспетчер\nсемейств",
            $"{CommandNamespace}.{nameof(FamilyManagerCommand)}",
            TrueBimIcon.FamilyManager,
            "Открывает каркас диспетчера семейств.",
            "Каркас будущего инструмента библиотеки: папки семейств, поиск, типы, избранное, загрузка и отчет без изменения проекта в первом срезе."),
        new(
            ModelReviewPanelName,
            "TrueBIM_ColorByParameter",
            "Цвета\nпо параметрам",
            $"{CommandNamespace}.{nameof(ColorByParameterCommand)}",
            TrueBimIcon.ColorByParameter,
            "Открывает подготовку раскраски активного вида по значениям выбранного параметра.",
            "Инструмент BIM-проверки для будущего выбора категорий, параметра, уникальных значений и применения фильтров с префиксом BIM_F_."),
        new(
            GeometryPanelName,
            "TrueBIM_JoinCut",
            "Соединить /\nВырезать",
            $"{CommandNamespace}.{nameof(JoinCutCommand)}",
            TrueBimIcon.JoinCut,
            "Открывает настройку правил соединения и вырезания геометрии.",
            "MVP-инструмент для конфигураций, правил соединения/вырезания, предпросмотра, выполнения и отчета по обработке геометрии."),
        new(
            ParametersPanelName,
            "TrueBIM_CopyParameters",
            "Копирование\nпараметров",
            $"{CommandNamespace}.{nameof(CopyParametersCommand)}",
            TrueBimIcon.CopyParameters,
            "Открывает подготовку копирования выбранных параметров между элементами.",
            "Инструмент для будущего выбора исходного элемента, копируемых параметров, элементов-получателей и отчета по пропущенным значениям."),
        new(
            ParametersPanelName,
            "TrueBIM_ParaManager",
            "ParaManager",
            $"{CommandNamespace}.{nameof(ParaManagerCommand)}",
            TrueBimIcon.Parameters,
            "Открывает подготовку управления shared parameters и параметрами проекта.",
            "MVP-инструмент для будущего импорта shared parameters из CSV или Excel, предпросмотра привязок и отчета по созданным параметрам."),
        new(
            AdministrationPanelName,
            "TrueBIM_CreateWorksets",
            "Рабочие\nнаборы",
            $"{CommandNamespace}.{nameof(CreateWorksetsCommand)}",
            TrueBimIcon.Worksets,
            "Открывает подготовку создания рабочих наборов из CSV или Excel.",
            "Инструмент администрирования для будущей проверки шаблона, включения worksharing только с подтверждением и создания worksets с отчетом."),
        new(
            KrPanelName,
            "TrueBIM_CollapseScheduleColumns",
            "Свернуть\nВРС",
            $"{CommandNamespace}.{nameof(CollapseScheduleColumnsCommand)}",
            TrueBimIcon.ScheduleCollapse,
            "Скрывает нулевые столбцы в выбранной спецификации."),
        new(
            EomPanelName,
            "TrueBIM_VoltageDropCalculation",
            "Расчет\nпотери\nнапряжения",
            $"{CommandNamespace}.{nameof(OpenVoltageDropCalculationCommand)}",
            TrueBimIcon.VoltageDrop,
            "Открывает расчет потери напряжения и нагрузок по данным первого листа Excel.")
    ];
}
