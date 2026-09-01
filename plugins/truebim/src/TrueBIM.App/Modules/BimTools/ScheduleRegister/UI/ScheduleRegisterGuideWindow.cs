using System.Windows;
using System.Windows.Controls;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.BimTools.ScheduleRegister.UI;

public sealed class ScheduleRegisterGuideWindow : TrueBimWindow
{
    private static readonly (string Title, string[] Steps)[] Sections =
    [
        (
            "Быстрый сценарий",
            [
                "До запуска команды выделите нужные листы в диспетчере проекта.",
                "В открытом окне проверьте список листов. Изменить его здесь нельзя: для другого набора закройте окно и повторите выбор в диспетчере.",
                "При необходимости нажмите значок готовности рядом с поиском, затем нажмите «Создать ведомость».",
                "TrueBIM откроет новую ведомость. Ранее созданные ведомости останутся без изменений."
            ]),
        (
            "Какие спецификации попадут в результат",
            [
                "Команда рассматривает только спецификации, размещённые на выбранных листах.",
                "Название берётся из первой строки шапки, а не из имени вида в диспетчере проекта.",
                "Сегменты одной спецификации объединяются в одну строку, номера листов перечисляются через запятую.",
                "Ревизии основной надписи и ранее созданные ведомости спецификаций пропускаются."
            ]),
        (
            "Фильтр «Не специфицировать»",
            [
                "По умолчанию фильтр выключен.",
                "Откройте «Настройки», включите фильтр и выберите параметр вида-спецификации.",
                "Укажите текст, например «Не специфицировать». Если значение параметра содержит этот текст, спецификация будет исключена.",
                "Если параметр отсутствует, спецификация останется в результате, а команда покажет предупреждение."
            ]),
        (
            "Что происходит с шаблоном",
            [
                "Исправный шаблон имеет имя «• Т • Общие данные • Ведомость спецификаций», заголовок и колонки «Лист», «Наименование», «Примечание».",
                "Если кто-то заполнил его рабочие строки или разместил на листе, TrueBIM автоматически создаст чистую копию с тем же оформлением.",
                "Если повреждены заголовок или колонки, в настройках нужно один раз выбрать исправный .rte или .rvt.",
                "Каждый запуск создаёт новую ведомость с первым свободным суффиксом: (2), (3) и далее."
            ]),
        (
            "Предупреждения и ошибки",
            [
                "Одинаковые заголовки разных спецификаций и повторные размещения не останавливают операцию — они перечисляются в итоговом предупреждении.",
                "Если выбранные листы не содержат подходящих спецификаций, проект не изменяется.",
                "При ошибке восстановления или заполнения транзакция откатывается, исходные ведомости не перезаписываются.",
                "Подробная техническая диагностика записывается в общий лог TrueBIM."
            ])
    ];

    public ScheduleRegisterGuideWindow()
    {
        Title = "Методичка: ведомость спецификаций";
        Width = 820;
        Height = 720;
        MinWidth = 660;
        MinHeight = 520;
        ResizeMode = ResizeMode.CanResize;
        Icon = IconFactory.CreateImage(TrueBimIcon.Help, 32);

        Button close = TrueBimUi.CreatePrimaryButton(
            "Понятно",
            TrueBimIcon.Check,
            (_, _) => Close(),
            minWidth: 120);
        close.IsDefault = true;
        close.IsCancel = true;
        ApplyTrueBimShell(
            TrueBimUi.CreateHeader(
                "Как создать ведомость спецификаций",
                "Короткая инструкция по выбору листов, фильтру, шаблону и результату.",
                TrueBimIcon.Help),
            null,
            CreateBody(),
            null,
            TrueBimUi.CreateFooter(null, close));
    }

    private static UIElement CreateBody()
    {
        StackPanel content = new();
        foreach ((string title, string[] steps) in Sections)
        {
            StackPanel section = new();
            for (int index = 0; index < steps.Length; index++)
            {
                Grid row = new()
                {
                    Margin = new Thickness(0, index == 0 ? 0 : TrueBimTheme.Spacing8, 0, 0)
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock
                {
                    Text = $"{index + 1}.",
                    FontWeight = FontWeights.SemiBold,
                    Foreground = TrueBimBrushes.Info,
                    Margin = new Thickness(0, 0, TrueBimTheme.Spacing8, 0)
                });
                TextBlock text = new()
                {
                    Text = steps[index],
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = TrueBimBrushes.TextPrimary
                };
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                section.Children.Add(row);
            }

            Border card = TrueBimUi.CreateSectionCard(title, section);
            card.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
            content.Children.Add(card);
        }

        return new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }
}
