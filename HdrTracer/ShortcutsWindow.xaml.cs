using System.Windows;
using Loc = HdrTracer.Core.Localization;

namespace HdrTracer.App;

public partial class ShortcutsWindow : Window
{
    public ShortcutsWindow()
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
        Title          = Loc.T("sc.title");
        TitleText.Text = Loc.T("sc.title");

        SecAppMenu.Text     = Loc.T("sc.appMenu");
        RowOpenSettings.Text = Loc.T("sc.openSettings");
        RowRefresh.Text     = Loc.T("sc.refresh");
        RowZoomIn.Text      = Loc.T("sc.zoomIn");
        RowZoomOut.Text     = Loc.T("sc.zoomOut");
        RowZoomReset.Text   = Loc.T("sc.zoomReset");
        RowGlobalHotkey.Text = Loc.T("sc.globalHotkey");

        SecSearchBox.Text   = Loc.T("sc.searchBox");
        RowFocusSearch.Text = Loc.T("sc.focusSearch");
        RowClearSearch.Text = Loc.T("sc.clearSearch");
        RowGotoResults.Text = Loc.T("sc.gotoResults");
        RowPinnedSearch.Text = Loc.T("sc.pinnedSearch");

        SecResultList.Text  = Loc.T("sc.resultList");
        RowOpenItem1.Text   = Loc.T("sc.openItem");
        RowViewProps.Text   = Loc.T("sc.viewProps");
        RowCopyPath.Text    = Loc.T("sc.copyPath");
        RowCopyFile.Text    = Loc.T("sc.copyFile");
        RowCopyName.Text    = Loc.T("sc.copyName");
        RowUpFirst.Text     = Loc.T("sc.upFirst");
        RowBackToSearch.Text = Loc.T("sc.backToSearch");
        RowDblClick.Text    = Loc.T("sc.dblClick");
        RowOpenItem2.Text   = Loc.T("sc.openItem");

        SecGlobalSc.Text    = Loc.T("sc.globalSc");
        RowToggleApp.Text   = Loc.T("sc.toggleApp");

        SecSearchTips.Text  = Loc.T("sc.searchTips");
        Tip1Text.Text       = Loc.T("sc.tip1");
        Tip2Text.Text       = Loc.T("sc.tip2");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
