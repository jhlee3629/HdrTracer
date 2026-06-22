using System.Windows;
using Loc = HdrTracer.Core.Localization;

namespace HdrTracer.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        ApplyTexts();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    private void ApplyTexts()
    {
        Title          = Loc.T("about.title");
        TitleText.Text = Loc.T("about.title");
        VersionText.Text = Loc.T("about.version");
        DescText.Text  = Loc.T("about.desc");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
