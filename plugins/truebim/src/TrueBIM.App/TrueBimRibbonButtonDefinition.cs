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
    string LongDescription = "",
    string AvailabilityClassName = "",
    IReadOnlyList<TrueBimRibbonPulldownItemDefinition>? PulldownItems = null)
{
    public IReadOnlyList<TrueBimRibbonPulldownItemDefinition> Items => PulldownItems ?? [];

    public bool IsPulldown => Items.Count > 0;
}

public sealed record TrueBimRibbonPulldownItemDefinition(
    string Name,
    string Text,
    string CommandClassName,
    TrueBimIcon Icon,
    string Tooltip,
    bool BeginsGroup = false);

public static class TrueBimRibbon
{
    public const string BimPanelName = "БИМ";
    public const string BimDocumentationPanelName = "Оформление";
    public const string BimViewsPanelName = "Виды";
    public const string BimCoordinationPanelName = "Координация";
    public const string BimLibraryPanelName = "Библиотека";
    public const string ModelReviewPanelName = "Проверка модели";
    public const string GeometryPanelName = "Геометрия";
    public const string ParametersPanelName = "Параметры";
    public const string AdministrationPanelName = "Администрирование";
    public const string HelpPanelName = "Помощь";
    public const string KrPanelName = "КР";
    public const string EomPanelName = "ЭОМ";
    public const string SsPanelName = "СС";

    private const string CommandNamespace = "TrueBIM.App.Commands";

    public static IReadOnlyList<TrueBimRibbonPulldownItemDefinition> ViewVisibilityPulldownItems { get; } =
    [
        new(
            "TrueBIM_ViewVisibility_All",
            "Все",
            $"{CommandNamespace}.{nameof(OpenViewVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Показывает все доступные категории на активном виде."),
        new(
            "TrueBIM_ViewVisibility_Windows",
            "Окна",
            $"{CommandNamespace}.{nameof(ToggleWindowsVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость окон на активном виде."),
        new(
            "TrueBIM_ViewVisibility_Doors",
            "Двери",
            $"{CommandNamespace}.{nameof(ToggleDoorsVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость дверей на активном виде."),
        new(
            "TrueBIM_ViewVisibility_Walls",
            "Стены",
            $"{CommandNamespace}.{nameof(ToggleWallsVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость стен на активном виде."),
        new(
            "TrueBIM_ViewVisibility_Columns",
            "Колонны",
            $"{CommandNamespace}.{nameof(ToggleColumnsVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость колонн на активном виде."),
        new(
            "TrueBIM_ViewVisibility_StructuralFraming",
            "Каркас несущий",
            $"{CommandNamespace}.{nameof(ToggleStructuralFramingVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость несущего каркаса на активном виде."),
        new(
            "TrueBIM_ViewVisibility_StructuralFoundation",
            "Фундамент несущей конструкции",
            $"{CommandNamespace}.{nameof(ToggleStructuralFoundationVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость несущих фундаментов на активном виде."),
        new(
            "TrueBIM_ViewVisibility_Rebar",
            "Армирование",
            $"{CommandNamespace}.{nameof(ToggleRebarVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость арматуры на активном виде."),
        new(
            "TrueBIM_ViewVisibility_Components",
            "Компоненты",
            $"{CommandNamespace}.{nameof(ToggleComponentsVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость компонентов на активном виде."),
        new(
            "TrueBIM_ViewVisibility_GenericModels",
            "Обобщенные модели",
            $"{CommandNamespace}.{nameof(ToggleGenericModelsVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость обобщенных моделей на активном виде."),
        new(
            "TrueBIM_ViewVisibility_Roofs",
            "Крыши",
            $"{CommandNamespace}.{nameof(ToggleRoofsVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость крыш на активном виде.",
            BeginsGroup: true),
        new(
            "TrueBIM_ViewVisibility_Floors",
            "Перекрытия",
            $"{CommandNamespace}.{nameof(ToggleFloorsVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость перекрытий на активном виде."),
        new(
            "TrueBIM_ViewVisibility_Ceilings",
            "Потолки",
            $"{CommandNamespace}.{nameof(ToggleCeilingsVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость потолков на активном виде."),
        new(
            "TrueBIM_ViewVisibility_Stairs",
            "Лестницы",
            $"{CommandNamespace}.{nameof(ToggleStairsVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость лестниц на активном виде.",
            BeginsGroup: true),
        new(
            "TrueBIM_ViewVisibility_Railings",
            "Ограждения",
            $"{CommandNamespace}.{nameof(ToggleRailingsVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость ограждений на активном виде."),
        new(
            "TrueBIM_ViewVisibility_Ramps",
            "Пандусы",
            $"{CommandNamespace}.{nameof(ToggleRampsVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость пандусов на активном виде."),
        new(
            "TrueBIM_ViewVisibility_Grids",
            "Оси",
            $"{CommandNamespace}.{nameof(ToggleGridsVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость осей на активном виде.",
            BeginsGroup: true),
        new(
            "TrueBIM_ViewVisibility_Levels",
            "Уровни",
            $"{CommandNamespace}.{nameof(ToggleLevelsVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость уровней на активном виде."),
        new(
            "TrueBIM_ViewVisibility_Sections",
            "Разрезы",
            $"{CommandNamespace}.{nameof(ToggleSectionsVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость разрезов на активном виде."),
        new(
            "TrueBIM_ViewVisibility_Elevations",
            "Фасады",
            $"{CommandNamespace}.{nameof(ToggleElevationsVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость фасадов на активном виде."),
        new(
            "TrueBIM_ViewVisibility_Tags",
            "Марки",
            $"{CommandNamespace}.{nameof(ToggleTagsVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость марок на активном виде."),
        new(
            "TrueBIM_ViewVisibility_ReferencePlanes",
            "Опорные плоскости",
            $"{CommandNamespace}.{nameof(ToggleReferencePlanesVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость опорных плоскостей на активном виде."),
        new(
            "TrueBIM_ViewVisibility_Lines",
            "Линии",
            $"{CommandNamespace}.{nameof(ToggleLinesVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость линий на активном виде."),
        new(
            "TrueBIM_ViewVisibility_Mass",
            "Формообразующие",
            $"{CommandNamespace}.{nameof(ToggleMassVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость формообразующих на активном виде."),
        new(
            "TrueBIM_ViewVisibility_Ducts",
            "Воздуховоды",
            $"{CommandNamespace}.{nameof(ToggleDuctsVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость воздуховодов на активном виде.",
            BeginsGroup: true),
        new(
            "TrueBIM_ViewVisibility_FlexDucts",
            "Гибкие воздуховоды",
            $"{CommandNamespace}.{nameof(ToggleFlexDuctsVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость гибких воздуховодов на активном виде."),
        new(
            "TrueBIM_ViewVisibility_Pipes",
            "Трубы",
            $"{CommandNamespace}.{nameof(TogglePipesVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость труб на активном виде."),
        new(
            "TrueBIM_ViewVisibility_Wires",
            "Провода",
            $"{CommandNamespace}.{nameof(ToggleWiresVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость проводов на активном виде."),
        new(
            "TrueBIM_ViewVisibility_CableTrays",
            "Кабельные лотки",
            $"{CommandNamespace}.{nameof(ToggleCableTraysVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость кабельных лотков на активном виде."),
        new(
            "TrueBIM_ViewVisibility_Conduits",
            "Короба",
            $"{CommandNamespace}.{nameof(ToggleConduitsVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость коробов на активном виде."),
        new(
            "TrueBIM_ViewVisibility_MechanicalEquipment",
            "Оборудование",
            $"{CommandNamespace}.{nameof(ToggleMechanicalEquipmentVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость оборудования на активном виде."),
        new(
            "TrueBIM_ViewVisibility_ElectricalEquipment",
            "Электрооборудование",
            $"{CommandNamespace}.{nameof(ToggleElectricalEquipmentVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость электрооборудования на активном виде."),
        new(
            "TrueBIM_ViewVisibility_AnalyticalModel",
            "Все категории аналитической модели",
            $"{CommandNamespace}.{nameof(ToggleAnalyticalModelVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость категорий аналитической модели на активном виде.",
            BeginsGroup: true),
        new(
            "TrueBIM_ViewVisibility_PointClouds",
            "Облака точек",
            $"{CommandNamespace}.{nameof(TogglePointCloudsVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость облаков точек на активном виде."),
        new(
            "TrueBIM_ViewVisibility_Links",
            "Связи",
            $"{CommandNamespace}.{nameof(ToggleLinksVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость связей Revit на активном виде.",
            BeginsGroup: true),
        new(
            "TrueBIM_ViewVisibility_ImportSymbols",
            "Обозначения импорта",
            $"{CommandNamespace}.{nameof(ToggleImportSymbolsVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость импортированных обозначений на активном виде."),
        new(
            "TrueBIM_ViewVisibility_RasterImages",
            "Растровые изображения",
            $"{CommandNamespace}.{nameof(ToggleRasterImagesVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость растровых изображений на активном виде."),
        new(
            "TrueBIM_ViewVisibility_GenericAnnotations",
            "Аннотации",
            $"{CommandNamespace}.{nameof(ToggleGenericAnnotationsVisibilityCommand)}",
            TrueBimIcon.Visibility,
            "Переключает видимость аннотаций на активном виде.")
    ];

    public static IReadOnlyList<string> PanelNames { get; } =
    [
        BimPanelName,
        BimDocumentationPanelName,
        BimViewsPanelName,
        BimCoordinationPanelName,
        BimLibraryPanelName,
        ModelReviewPanelName,
        GeometryPanelName,
        ParametersPanelName,
        AdministrationPanelName,
        HelpPanelName,
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
            "Включает и выключает видимость категорий на активном виде.",
            LongDescription: "Открывает выпадающий список категорий без отдельного модального окна. Выбор пункта сразу переключает видимость категории на активном виде.",
            PulldownItems: ViewVisibilityPulldownItems),
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
            "Переключает видимые оси активного 2D-вида между 2D и 3D режимами.",
            "Доступно только при открытом проекте Revit и обычном активном 2D-виде модели: план, разрез, фасад или потолочный план. На листах, 3D-видах, спецификациях и шаблонах кнопка недоступна.",
            $"{CommandNamespace}.{nameof(DatumExtentCommandAvailability)}"),
        new(
            BimViewsPanelName,
            "TrueBIM_OpeningViews",
            "Фасады\nдверей/окон",
            $"{CommandNamespace}.{nameof(OpeningViewsCommand)}",
            TrueBimIcon.OpeningViews,
            "Собирает двери и окна на активном плане, показывает предпросмотр и создаёт для них фасадные elevation-виды.",
            "Инструмент для проёмов: сбор дверей и окон активного плана, имена видов BIM_Opening_*, проверка дублей, выбор типа фасада и шаблона вида, crop box вокруг элемента и CSV-отчёт."),
        new(
            BimCoordinationPanelName,
            "TrueBIM_ClashReport",
            "Отчёт\nколлизий",
            $"{CommandNamespace}.{nameof(ClashReportCommand)}",
            TrueBimIcon.ClashReport,
            "Импортирует CSV/XML со списком коллизий и открывает выбранную коллизию в 3D.",
            "Инструмент координации: импорт внешнего списка коллизий, статусы и комментарии, локальное JSON-состояние, section box и подсветка найденных элементов."),
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
            HelpPanelName,
            "TrueBIM_OpenLogs",
            "Логи",
            $"{CommandNamespace}.{nameof(OpenTrueBimLogsCommand)}",
            TrueBimIcon.Logs,
            "Открывает локальный файл логов TrueBIM.",
            "Быстрый доступ к truebim.log для диагностики ошибок и передачи логов в поддержку."),
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

    public static bool IsButtonAvailableForRevitVersion(TrueBimRibbonButtonDefinition button, string revitVersion)
    {
        Guard.NotNull(button, nameof(button));

        return IsButtonAvailableForRevitVersion(button.CommandClassName, revitVersion);
    }

    public static bool IsButtonAvailableForRevitVersion(string commandClassName, string revitVersion)
    {
        if (!string.Equals(
                commandClassName,
                $"{CommandNamespace}.{nameof(DatumExtentCommand)}",
                StringComparison.Ordinal)
            && !string.Equals(commandClassName, nameof(DatumExtentCommand), StringComparison.Ordinal))
        {
            return true;
        }

        return int.TryParse(revitVersion, out int version) && version > 2022;
    }
}
