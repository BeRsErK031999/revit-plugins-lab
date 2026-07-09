using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TrueBIM.App.Modules.BimTools.FamilyManager.Models;
using TrueBIM.App.Modules.BimTools.FamilyManager.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.UI;

public sealed class FamilyManagerCompactPaneControl : UserControl
{
    private readonly string folderPath;
    private readonly FamilyManagerProfileStorage profileStorage;
    private readonly ITrueBimLogger logger;
    private readonly Action openManager;
    private readonly Action hidePane;
    private readonly TextBlock folderTitle = new();
    private readonly TextBlock folderPathText = new();
    private readonly TextBlock cacheText = new();
    private readonly UniformGrid metricsGrid = new()
    {
        Columns = 2,
        Margin = new Thickness(0, 10, 0, 10)
    };
    private readonly StackPanel categoryList = new();
    private readonly StackPanel recentList = new();

    public FamilyManagerCompactPaneControl(
        string folderPath,
        FamilyManagerProfileStorage profileStorage,
        ITrueBimLogger logger,
        Action openManager,
        Action hidePane)
    {
        this.folderPath = FamilyPathNormalizer.Normalize(folderPath);
        this.profileStorage = profileStorage ?? throw new ArgumentNullException(nameof(profileStorage));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.openManager = openManager ?? throw new ArgumentNullException(nameof(openManager));
        this.hidePane = hidePane ?? throw new ArgumentNullException(nameof(hidePane));

        Background = Brushes.WhiteSmoke;
        Content = CreateContent();
        RefreshSummary();
    }

    private UIElement CreateContent()
    {
        DockPanel root = new()
        {
            LastChildFill = true,
            Margin = new Thickness(12)
        };

        StackPanel actions = new()
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 12)
        };

        Button openButton = CreateButton("Открыть окно", TrueBimIcon.FamilyManager);
        openButton.Click += (_, _) => openManager();
        actions.Children.Add(openButton);

        Button hideButton = CreateButton("Скрыть", TrueBimIcon.Close);
        hideButton.Click += (_, _) => hidePane();
        actions.Children.Add(hideButton);

        DockPanel.SetDock(actions, Dock.Top);
        root.Children.Add(actions);

        StackPanel content = new();
        content.Children.Add(CreateHeaderBlock());
        content.Children.Add(metricsGrid);
        content.Children.Add(CreateListBlock("Категории", categoryList));
        content.Children.Add(CreateListBlock("Недавние семейства", recentList));
        root.Children.Add(new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        return root;
    }

    private UIElement CreateHeaderBlock()
    {
        Border border = CreateSectionBorder();
        StackPanel panel = new();

        TextBlock title = new()
        {
            Text = "Диспетчер семейств",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(title);

        folderTitle.FontWeight = FontWeights.SemiBold;
        folderTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        folderTitle.Margin = new Thickness(0, 0, 0, 4);
        panel.Children.Add(folderTitle);

        folderPathText.Foreground = Brushes.DimGray;
        folderPathText.FontSize = 11;
        folderPathText.TextWrapping = TextWrapping.Wrap;
        folderPathText.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(folderPathText);

        cacheText.Foreground = Brushes.DimGray;
        cacheText.FontSize = 11;
        panel.Children.Add(cacheText);

        border.Child = panel;
        return border;
    }

    private UIElement CreateListBlock(string title, Panel host)
    {
        Border border = CreateSectionBorder();
        border.Margin = new Thickness(0, 0, 0, 10);

        StackPanel panel = new();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        panel.Children.Add(host);

        border.Child = panel;
        return border;
    }

    private void RefreshSummary()
    {
        try
        {
            FamilyManagerProfile profile = profileStorage.Load();
            FamilyManagerPaneSummary summary = new FamilyManagerPaneSummaryBuilder().Build(profile, folderPath);
            ApplySummary(summary);
        }
        catch (Exception exception)
        {
            logger.Warning($"Failed to refresh Family Manager compact pane: {exception.Message}");
            folderTitle.Text = "Не удалось прочитать кэш";
            folderPathText.Text = folderPath;
            cacheText.Text = "Откройте окно диспетчера и пересканируйте библиотеку.";
        }
    }

    private void ApplySummary(FamilyManagerPaneSummary summary)
    {
        folderTitle.Text = summary.FolderName;
        folderTitle.ToolTip = summary.FolderPath;
        folderPathText.Text = summary.FolderPath;
        folderPathText.ToolTip = summary.FolderPath;
        cacheText.Text = summary.CacheUpdatedDisplay;

        metricsGrid.Children.Clear();
        metricsGrid.Children.Add(CreateMetric("Семейств", summary.FamiliesDisplay));
        metricsGrid.Children.Add(CreateMetric("Категорий", summary.CategoriesDisplay));
        metricsGrid.Children.Add(CreateMetric("Метаданные", summary.MetadataDisplay));
        metricsGrid.Children.Add(CreateMetric("Типов", summary.TypesDisplay));

        ApplyList(categoryList, summary.Categories, "Нет категорий в кэше");
        ApplyList(recentList, summary.RecentFamilies, "Нет семейств в выбранной папке");
    }

    private static void ApplyList(Panel host, IReadOnlyList<string> items, string emptyText)
    {
        host.Children.Clear();
        if (items.Count == 0)
        {
            host.Children.Add(CreateMutedText(emptyText));
            return;
        }

        foreach (string item in items)
        {
            TextBlock text = new()
            {
                Text = item,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 3),
                ToolTip = item
            };
            host.Children.Add(text);
        }
    }

    private static Border CreateMetric(string label, string value)
    {
        Border border = new()
        {
            Background = Brushes.White,
            BorderBrush = Brushes.Gainsboro,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(8)
        };

        StackPanel panel = new();
        panel.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brushes.DimGray,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        border.Child = panel;
        return border;
    }

    private static TextBlock CreateMutedText(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private static Border CreateSectionBorder()
    {
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brushes.Gainsboro,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10)
        };
    }

    private static Button CreateButton(string text, TrueBimIcon icon)
    {
        return new Button
        {
            Content = IconFactory.CreateButtonContent(icon, text),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Height = 30,
            Margin = new Thickness(0, 0, 0, 6)
        };
    }
}
