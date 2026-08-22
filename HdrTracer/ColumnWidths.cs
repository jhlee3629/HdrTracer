using System.ComponentModel;
using System.Windows;

namespace HdrTracer.App;

public sealed class ColumnWidths : INotifyPropertyChanged
{
    private GridLength _drive = new(50,  GridUnitType.Star);
    private GridLength _name  = new(280, GridUnitType.Star);
    private GridLength _path  = new(300, GridUnitType.Star);
    private GridLength _size  = new(80,  GridUnitType.Star);
    private GridLength _date  = new(120, GridUnitType.Star);

    public GridLength Drive
    {
        get => _drive;
        set { if (_drive != value) { _drive = value; OnChanged(nameof(Drive)); } }
    }
    public GridLength Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnChanged(nameof(Name)); } }
    }
    public GridLength Size
    {
        get => _size;
        set { if (_size != value) { _size = value; OnChanged(nameof(Size)); } }
    }
    public GridLength Date
    {
        get => _date;
        set { if (_date != value) { _date = value; OnChanged(nameof(Date)); } }
    }
    public GridLength Path
    {
        get => _path;
        set { if (_path != value) { _path = value; OnChanged(nameof(Path)); } }
    }

    public double DrivePx => _drive.Value;
    public double NamePx  => _name.Value;
    public double SizePx  => _size.Value;
    public double DatePx  => _date.Value;
    public double PathPx  => _path.Value;

    public void SetDrive(double px) => Drive = new GridLength(px, GridUnitType.Star);
    public void SetName(double px)  => Name  = new GridLength(px, GridUnitType.Star);
    public void SetPath(double px)  => Path  = new GridLength(px, GridUnitType.Star);
    public void SetSize(double px)  => Size  = new GridLength(px, GridUnitType.Star);
    public void SetDate(double px)  => Date  = new GridLength(px, GridUnitType.Star);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
