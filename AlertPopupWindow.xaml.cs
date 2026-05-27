using System.Windows;
using System.Windows.Input;

namespace SiteDownWindows;

public partial class AlertPopupWindow : Window
{
    private readonly Action? _seeDetailsAction;

    public AlertPopupWindow(string title, string message, Action? seeDetailsAction = null)
    {
        InitializeComponent();

        _seeDetailsAction = seeDetailsAction;
        TitleTextBlock.Text = title;
        MessageTextBlock.Text = message;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        PositionBottomRight();
    }

    private void PositionBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 24;
        Top = workArea.Bottom - Height - 24;
    }

    private void Notification_Click(object sender, MouseButtonEventArgs e)
    {
        Close();
    }

    private void CloseButton_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Close();
    }

    private void SeeDetailsButton_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _seeDetailsAction?.Invoke();
        Close();
    }
}
