using System.Windows;

namespace HdrTracer.App;

public partial class MainWindow
{
    internal void DetachRemovableMonitors()
    {
        var detached = new List<string>();

        foreach (var slot in _multi.Slots)
        {
            if (!HdrTracer.Core.DriveDetector.IsRemovable(slot.DriveLetter)) continue;

            detached.Add(slot.DriveLetter);

            var monitor = slot.Monitor;
            if (monitor is null) continue;

            slot.Monitor = null;

            System.Threading.Tasks.Task.Run(() =>
            {
                try { monitor.Dispose(); } catch { }
            });
        }

        if (detached.Count == 0) return;

        for (int i = _preloaders.Count - 1; i >= 0; i--)
        {
            var letter = _preloaders[i].DriveLetter;

            bool match = false;
            foreach (var d in detached)
            {
                if (string.Equals(letter, d, StringComparison.OrdinalIgnoreCase))
                {
                    match = true;
                    break;
                }
            }
            if (!match) continue;

            try { _preloaders[i].Stop(); } catch { }
            _preloaders.RemoveAt(i);
        }
    }
}

public partial class SettingsWindow
{
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);

        if (e.Cancel) return;
        if (DialogResult != true) return;
        if (!RemovableChanged) return;   
        if (Settings.IndexRemovableDrives) return;

        (Owner as MainWindow)?.DetachRemovableMonitors();
    }
}
