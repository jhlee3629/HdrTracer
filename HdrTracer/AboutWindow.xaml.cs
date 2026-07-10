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
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = string.Format(Loc.T("about.version"),
            v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}");
        DescText.Text  = Loc.T("about.desc");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
