using MyNotes.Application.Contracts.Media.Models;

namespace MyNotes.Common.Helpers;

internal static class GeometryHelper
{
  public static SizeInt32 ToSizeInt32(Size size) => new((int)size.Width, (int)size.Height);
  public static SizeInt32 ToSizeInt32Rounded(Size size) => new((int)Math.Round(size.Width), (int)Math.Round(size.Height));
  public static SizeInt32 ToScaledSizeInt32(Size size, double scale, bool isRounded = false) => isRounded
    ? new((int)Math.Round(size.Width * scale), (int)Math.Round(size.Height * scale))
    : new((int)(size.Width * scale), (int)(size.Height * scale));

  public static Size ToSize(SizeInt32 size) => new(size.Width, size.Height);

  public static PointInt32 ToPointInt32(Point point) => new((int)point.X, (int)point.Y);
  public static PointInt32 ToPointInt32Rounded(Point point) => new((int)Math.Round(point.X), (int)Math.Round(point.Y));
  public static PointInt32 ToScaledPointInt32(Point point, double scale, bool isRounded = false) => isRounded
    ? new((int)Math.Round(point.X * scale), (int)Math.Round(point.Y * scale))
    : new((int)(point.X * scale), (int)(point.Y * scale));

  public static Point ToPoint(PointInt32 point) => new(point.X, point.Y);

  public static RectInt32 ToRectInt32(Rect rect) => new((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
  public static RectInt32 ToRectInt32Rounded(Rect rect) => new((int)Math.Round(rect.X), (int)Math.Round(rect.Y), (int)Math.Round(rect.Width), (int)Math.Round(rect.Height));
  public static RectInt32 ToScaledRectInt32(Rect rect, double scale, bool isRounded = false) => isRounded
    ? new(
      _X: (int)Math.Round(rect.X * scale), _Y: (int)Math.Round(rect.Y * scale),
      _Width: (int)Math.Round(rect.Width * scale), _Height: (int)Math.Round(rect.Height * scale))
    : new((int)(rect.X * scale), (int)(rect.Y * scale), (int)(rect.Width * scale), (int)(rect.Height * scale));

  public static Rect ToRect(RectInt32 rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

  public static AlignmentPosition GetAlignmentPosition(AlignmentX x, AlignmentY y) => (x, y) switch
  {
    (AlignmentX.Left, AlignmentY.Top) => AlignmentPosition.TopLeft,
    (AlignmentX.Left, AlignmentY.Center) => AlignmentPosition.CenterLeft,
    (AlignmentX.Left, AlignmentY.Bottom) => AlignmentPosition.BottomLeft,
    (AlignmentX.Center, AlignmentY.Top) => AlignmentPosition.TopCenter,
    (AlignmentX.Center, AlignmentY.Center) => AlignmentPosition.Center,
    (AlignmentX.Center, AlignmentY.Bottom) => AlignmentPosition.BottomCenter,
    (AlignmentX.Right, AlignmentY.Top) => AlignmentPosition.TopRight,
    (AlignmentX.Right, AlignmentY.Center) => AlignmentPosition.CenterRight,
    (AlignmentX.Right, AlignmentY.Bottom) => AlignmentPosition.BottomRight,
    _ => throw new InvalidOperationException()
  };

  public static AlignmentX GetAlignmentX(AlignmentPosition alignmentPosition) => alignmentPosition switch
  {
    AlignmentPosition.TopLeft or AlignmentPosition.CenterLeft or AlignmentPosition.BottomLeft => AlignmentX.Left,
    AlignmentPosition.TopCenter or AlignmentPosition.Center or AlignmentPosition.BottomCenter => AlignmentX.Center,
    AlignmentPosition.TopRight or AlignmentPosition.CenterRight or AlignmentPosition.BottomRight => AlignmentX.Right,
    _ => throw new InvalidOperationException()
  };

  public static AlignmentY GetAlignmentY(AlignmentPosition alignmentPosition) => alignmentPosition switch
    {
      AlignmentPosition.TopLeft or AlignmentPosition.TopCenter or AlignmentPosition.TopRight => AlignmentY.Top,
      AlignmentPosition.CenterLeft or AlignmentPosition.Center or AlignmentPosition.CenterRight => AlignmentY.Center,
      AlignmentPosition.BottomLeft or AlignmentPosition.BottomCenter or AlignmentPosition.BottomRight => AlignmentY.Bottom,
      _ => throw new InvalidOperationException()
    };

// Extensions
// 확장 메서드 및 확장 멤버(정적/인스턴스 모두 가능) 정의
extension(Size size)
  {
    public SizeInt32 SizeInt32 => ToSizeInt32(size);
    public SizeInt32 RoundedSizeInt32 => ToSizeInt32Rounded(size);
    public SizeInt32 AsScaledSizeInt32(double scale, bool isRounded = false) => ToScaledSizeInt32(size, scale, isRounded);
  }

  extension(SizeInt32 size)
  {
    public Size Size => ToSize(size);
  }

  extension(Point point)
  {
    public PointInt32 PointInt32 => ToPointInt32(point);
    public PointInt32 RoundedPointInt32 => ToPointInt32Rounded(point);

    public PointInt32 AsScaledPointInt32(double scale, bool isRounded = false) => ToScaledPointInt32(point, scale, isRounded);
  }

  extension(PointInt32 point)
  {
    public Point Point => ToPoint(point);
  }

  extension(Rect rect)
  {
    public RectInt32 RectInt32 => ToRectInt32(rect);
    public RectInt32 RoundedRectInt32 => ToRectInt32Rounded(rect);

    public RectInt32 AsScaledRectInt32(double scale, bool isRounded = false) => ToScaledRectInt32(rect, scale, isRounded);
  }

  extension(RectInt32 rect)
  {
    public Rect Rect => ToRect(rect);
  }
}
