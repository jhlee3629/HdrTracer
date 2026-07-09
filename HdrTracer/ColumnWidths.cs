using System.ComponentModel;
using System.Windows;

namespace HdrTracer.App;

/// <summary>
/// 헤더와 모든 결과 행이 공유하는 단일 컬럼 너비 소스.
/// 기존 SharedSizeGroup의 "암묵적 자동 동기화"가 경로(*) 컬럼과 충돌해
/// 검색 결과가 있을 때 드래그가 어긋나던 문제를 없애기 위해,
/// 헤더 드래그가 이 객체의 값만 바꾸면 바인딩된 헤더·모든 행(가상화로 새로 생기는
/// 행 포함)이 한 번에 같은 너비로 갱신된다. 경로 컬럼은 *라 바인딩하지 않아도
/// 다른 네 컬럼이 동일하면 자동으로 같은 폭이 된다.
/// </summary>
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

    // 픽셀 값 편의 접근자 (드래그 로직에서 숫자로 다루기 쉽게)
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
