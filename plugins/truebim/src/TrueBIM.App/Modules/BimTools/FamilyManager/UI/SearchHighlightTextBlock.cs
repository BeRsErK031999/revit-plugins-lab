using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.UI;

public sealed class SearchHighlightTextBlock : TextBlock
{
    public static readonly DependencyProperty HighlightTextProperty = DependencyProperty.Register(
        nameof(HighlightText),
        typeof(string),
        typeof(SearchHighlightTextBlock),
        new PropertyMetadata(string.Empty, OnHighlightInputChanged));

    public static readonly DependencyProperty SearchTextProperty = DependencyProperty.Register(
        nameof(SearchText),
        typeof(string),
        typeof(SearchHighlightTextBlock),
        new PropertyMetadata(string.Empty, OnHighlightInputChanged));

    public string HighlightText
    {
        get => (string)GetValue(HighlightTextProperty);
        set => SetValue(HighlightTextProperty, value);
    }

    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    private static void OnHighlightInputChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((SearchHighlightTextBlock)dependencyObject).RefreshInlines();
    }

    private void RefreshInlines()
    {
        Inlines.Clear();
        string text = HighlightText ?? string.Empty;
        if (text.Length == 0)
        {
            return;
        }

        string search = SearchText?.Trim() ?? string.Empty;
        if (search.Length == 0)
        {
            Inlines.Add(new Run(text));
            return;
        }

        int currentIndex = 0;
        while (currentIndex < text.Length)
        {
            int matchIndex = text.IndexOf(search, currentIndex, StringComparison.CurrentCultureIgnoreCase);
            if (matchIndex < 0)
            {
                Inlines.Add(new Run(text.Substring(currentIndex)));
                break;
            }

            if (matchIndex > currentIndex)
            {
                Inlines.Add(new Run(text.Substring(currentIndex, matchIndex - currentIndex)));
            }

            Inlines.Add(new Run(text.Substring(matchIndex, search.Length))
            {
                Background = Brushes.Gold,
                Foreground = Brushes.Black,
                FontWeight = FontWeights.SemiBold
            });
            currentIndex = matchIndex + search.Length;
        }
    }
}
