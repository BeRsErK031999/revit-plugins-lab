using System.Windows;
using System.Windows.Controls;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.UI;

public class TrueBimWindow : Window
{
    public TrueBimWindow()
    {
        TrueBimWindowChrome.Apply(this);
    }

    protected Grid BuildShell(
        UIElement? header,
        UIElement? commandBar,
        UIElement body,
        UIElement? status = null,
        UIElement? footer = null)
    {
        return TrueBimUi.CreateWindowShell(header, commandBar, body, status, footer);
    }

    protected void ApplyTrueBimShell(
        UIElement? header,
        UIElement? commandBar,
        UIElement body,
        UIElement? status = null,
        UIElement? footer = null)
    {
        Content = BuildShell(header, commandBar, body, status, footer);
    }
}
