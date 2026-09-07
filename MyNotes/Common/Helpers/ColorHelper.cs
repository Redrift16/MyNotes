namespace MyNotes.Common.Helpers;

internal static class ColorHelper
{
  public static SolidColorBrush ToBrush(Color? c) => new(c ?? Colors.Transparent);
  public static AcrylicBrush ToAcrylicBrush(Color c, double tintOpacity = 1.0, double tintLuminosityOpacity = 1.0) => new() { FallbackColor = c, TintColor = c, TintOpacity = tintOpacity, TintLuminosityOpacity = tintLuminosityOpacity };

  public static SolidColorBrush ToAcrylicSolidBrush(bool enable, Color tintColor, double tintOpacity, double tintLuminosityOpacity, Color backgroundColor) => enable ? AcrylicSolidColorConverter.Create(tintColor, tintOpacity, tintLuminosityOpacity, Colors.White) : new(tintColor);

  public const double WCAG_AA_Normal = 4.5;
  public const double WCAG_AA_Large = 3.0;
  public const double WCAG_AAA_Normal = 7.0;
  public const double WCAG_AAA_Large = 4.5;

  public static double GetRelativeLuminance(Color c)
  {
    var rSRGB = c.R / 255.0;
    var gSRGB = c.G / 255.0;
    var bSRGB = c.B / 255.0;

    var r = rSRGB <= 0.04045 ? rSRGB / 12.92 : Math.Pow((rSRGB + 0.055) / 1.055, 2.4);
    var g = gSRGB <= 0.04045 ? gSRGB / 12.92 : Math.Pow((gSRGB + 0.055) / 1.055, 2.4);
    var b = bSRGB <= 0.04045 ? bSRGB / 12.92 : Math.Pow((bSRGB + 0.055) / 1.055, 2.4);
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
  }

  public static double GetContrastRatio(Color c1, Color c2)
  {
    var relLuminance1 = GetRelativeLuminance(c1);
    var relLuminance2 = GetRelativeLuminance(c2);
    return (Math.Max(relLuminance1, relLuminance2) + 0.05)
        / (Math.Min(relLuminance1, relLuminance2) + 0.05);
  }

  public static Color CompositeAlpha(Color color, Color background)
  {
    double alpha1 = color.A / 255.0;
    double alpha2 = 1.0 - alpha1;

    return new Color()
    {
      A = 255,
      R = (byte)((color.R * alpha1) + (background.R * alpha2)),
      G = (byte)((color.G * alpha1) + (background.G * alpha2)),
      B = (byte)((color.B * alpha1) + (background.B * alpha2)),
    };
  }

  public static bool IsSimilar(Color c1, Color c2, double threshold = 2.3)
  {
    LabColor lab1 = c1.Lab;
    LabColor lab2 = c2.Lab;

    double deltaE = Math.Sqrt(Math.Pow(lab1.L - lab2.L, 2) + Math.Pow(lab1.A - lab2.A, 2) + Math.Pow(lab1.B - lab2.B, 2));
    return deltaE <= threshold;
  }

  public static bool HasTransparency(Color color) => color.A < 255;

  public static LabColor ToLab(Color color)
  {
    // 1. sRGB(0~255) → Linear RGB(0~1) 변환
    double r = SrgbToLinear(color.R / 255.0);
    double g = SrgbToLinear(color.G / 255.0);
    double b = SrgbToLinear(color.B / 255.0);

    // 2. Linear RGB → XYZ 변환
    // 표준 D65 변환 매트릭스 사용
    double x = r * 0.4124 + g * 0.3576 + b * 0.1805;
    double y = r * 0.2126 + g * 0.7152 + b * 0.0722;
    double z = r * 0.0193 + g * 0.1192 + b * 0.9505;

    // 3. XYZ → Lab 변환 (참고 표준 백색점 D65)
    // 백색점
    double Xn = 0.95047, Yn = 1.00000, Zn = 1.08883;

    double fx = Fxyz(x / Xn);
    double fy = Fxyz(y / Yn);
    double fz = Fxyz(z / Zn);

    double l = 116.0 * fy - 16.0;
    double a = 500.0 * (fx - fy);
    double b2 = 200.0 * (fy - fz);

    return new LabColor(l, a, b2);
  }

  private static double SrgbToLinear(double c) => (c > 0.04045) ? Math.Pow((c + 0.055) / 1.055, 2.4) : c / 12.92;

  private static double Fxyz(double t) => (t > Math.Pow(6.0 / 29.0, 3.0)) ? Math.Pow(t, 1.0 / 3.0) : (t / (3 * Math.Pow(6.0 / 29.0, 2.0)) + 4.0 / 29.0);

  public static Color GetComplementary(Color c) => Color.FromArgb(c.A, (byte)(255 - c.R), (byte)(255 - c.G), (byte)(255 - c.B));

  public static ElementTheme GetThemeFromColor(Color color)
  {
    color = color.CompositeAlphaWith(Colors.White);

    double preferLight = color.ContrastRatioTo(Colors.Black);
    double preferDark = color.ContrastRatioTo(Colors.White);

    return preferLight >= preferDark ? ElementTheme.Light : ElementTheme.Dark;
  }

  public static Color GetFallbackColor(Color color, double opacity) => Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B);

  extension(Color color)
  {
    public SolidColorBrush SolidColorBrush => new(color);
    public LabColor Lab => ToLab(color);
    public double RelativeLuminance => GetRelativeLuminance(color);
    public Color Complementary => GetComplementary(color);

    public double ContrastRatioTo(Color other) => GetContrastRatio(color, other);

    public Color CompositeAlphaWith(Color background) => CompositeAlpha(color, background);

    public bool IsSimilarTo(Color other, double threshold = 2.3) => IsSimilar(color, other, threshold);
  }
}

public readonly struct LabColor(double l, double a, double b)
{
  public double L { get; } = l;
  public double A { get; } = a;
  public double B { get; } = b;
}

/// <summary>
/// Acrylic의 Tint/Luminosity 합성을 단색 배경 기준으로 계산합니다.
/// Blur와 Noise 텍스처는 제외하고 대표 단색을 반환합니다.
/// </summary>
public static class AcrylicSolidColorConverter
{
  private static readonly Color White =
      Color.FromArgb(255, 255, 255, 255);

  private static readonly Color Black =
      Color.FromArgb(255, 0, 0, 0);

  /// <summary>
  /// 백색 배경에서 Acrylic이 표현할 대표색을 계산합니다.
  /// </summary>
  public static SolidColorBrush CreateOnWhite(
      Color tintColor,
      double tintOpacity,
      double tintLuminosityOpacity)
  {
    return Create(
        tintColor,
        tintOpacity,
        tintLuminosityOpacity,
        White);
  }

  /// <summary>
  /// 흑색 배경에서 Acrylic이 표현할 대표색을 계산합니다.
  /// </summary>
  public static SolidColorBrush CreateOnBlack(
      Color tintColor,
      double tintOpacity,
      double tintLuminosityOpacity)
  {
    return Create(
        tintColor,
        tintOpacity,
        tintLuminosityOpacity,
        Black);
  }

  /// <summary>
  /// 지정한 불투명 단색 배경에서 Acrylic이 표현할 대표색을 계산합니다.
  /// </summary>
  /// <param name="tintColor">
  /// Acrylic의 TintColor입니다. Alpha 채널도 계산에 반영됩니다.
  /// </param>
  /// <param name="tintOpacity">
  /// TintColor의 적용 강도입니다. 유효 범위는 0~1입니다.
  /// </param>
  /// <param name="tintLuminosityOpacity">
  /// 배경의 밝기를 TintColor의 밝기 쪽으로 이동시키는 강도입니다.
  /// 유효 범위는 0~1입니다.
  /// </param>
  /// <param name="backgroundColor">
  /// Acrylic 아래에 있는 불투명 단색 배경입니다.
  /// </param>
  /// <returns>
  /// Acrylic의 노이즈를 제외한 대표색을 가진 불투명 SolidColorBrush입니다.
  /// </returns>
  /// <exception cref="ArgumentOutOfRangeException">
  /// opacity 값이 0~1 범위를 벗어나거나 NaN인 경우 발생합니다.
  /// </exception>
  /// <exception cref="ArgumentException">
  /// backgroundColor가 불투명하지 않은 경우 발생합니다.
  /// </exception>
  public static SolidColorBrush Create(
      Color tintColor,
      double tintOpacity,
      double tintLuminosityOpacity,
      Color backgroundColor)
  {
    ValidateOpacity(tintOpacity, nameof(tintOpacity));
    ValidateOpacity(tintLuminosityOpacity, nameof(tintLuminosityOpacity));

    /*
     * 반투명 배경색만으로는 최종 색을 결정할 수 없습니다.
     * 그 아래에 또 어떤 색이 존재하는지 알아야 하기 때문입니다.
     */
    if (backgroundColor.A != byte.MaxValue)
    {
      throw new ArgumentException(
          "backgroundColor는 불투명한 단색이어야 합니다.",
          nameof(backgroundColor));
    }

    Rgb tintRgb = Rgb.FromColor(tintColor);
    Rgb backgroundRgb = Rgb.FromColor(backgroundColor);

    /*
     * WinUI는 TintColor alpha와 TintOpacity를 곱한 뒤
     * byte 단위로 반올림하여 실제 Tint alpha로 사용합니다.
     *
     * TintLuminosityOpacity를 명시적으로 전달하는 경우에는
     * 자동 TintOpacity 보정이 적용되지 않습니다.
     */
    byte effectiveTintAlphaByte = RoundToByte(tintColor.A * tintOpacity);

    double effectiveTintAlpha = effectiveTintAlphaByte / 255.0;

    Rgb resultRgb;

    /*
     * 1단계: Luminosity blend
     *
     * 배경색의 색조와 채도는 유지하면서,
     * 밝기를 TintColor의 luminosity로 변경한 색을 계산합니다.
     */
    double tintLuminosity = GetLuminosity(tintRgb);

    Rgb fullLuminosityBlendColor = SetLuminosity(backgroundRgb, tintLuminosity);

    /*
     * TintLuminosityOpacity에 따라 원래 배경과
     * luminosity blend 결과를 보간합니다.
     */
    Rgb luminosityResult = Lerp(backgroundRgb, fullLuminosityBlendColor, tintLuminosityOpacity);

    /*
     * 2단계: Color blend
     *
     * TintColor의 색조와 채도는 유지하면서,
     * 밝기는 luminosity 처리된 배경의 밝기를 사용합니다.
     */
    double backgroundLuminosity = GetLuminosity(luminosityResult);

    Rgb fullTintBlendColor = SetLuminosity(tintRgb, backgroundLuminosity);

    /*
     * TintOpacity와 TintColor.A가 결합된 실제 alpha로
     * luminosity 결과와 TintColor 결과를 합성합니다.
     */
    resultRgb = Lerp(luminosityResult, fullTintBlendColor, effectiveTintAlpha);

    return new SolidColorBrush(resultRgb.ToOpaqueColor());
  }

  /// <summary>
  /// Color blend에서 사용하는 SetLum 연산입니다.
  /// 입력 색의 색조·채도를 유지하면서 목표 luminosity를 적용합니다.
  /// </summary>
  private static Rgb SetLuminosity(
      Rgb color,
      double targetLuminosity)
  {
    double difference =
        targetLuminosity - GetLuminosity(color);

    Rgb shiftedColor = new(
        color.R + difference,
        color.G + difference,
        color.B + difference);

    return ClipColor(shiftedColor);
  }

  /// <summary>
  /// out-of-gamut RGB를 색조와 luminosity를 최대한 보존하면서
  /// 0~1 범위로 조정합니다.
  /// </summary>
  private static Rgb ClipColor(Rgb color)
  {
    double luminosity = GetLuminosity(color);

    double minimum = Math.Min(
        color.R,
        Math.Min(color.G, color.B));

    double maximum = Math.Max(
        color.R,
        Math.Max(color.G, color.B));

    Rgb result = color;

    if (minimum < 0.0)
    {
      double denominator =
          luminosity - minimum;

      if (denominator > double.Epsilon)
      {
        result = new Rgb(
            luminosity +
            ((result.R - luminosity) *
             luminosity / denominator),

            luminosity +
            ((result.G - luminosity) *
             luminosity / denominator),

            luminosity +
            ((result.B - luminosity) *
             luminosity / denominator));
      }
    }

    if (maximum > 1.0)
    {
      double denominator =
          maximum - luminosity;

      if (denominator > double.Epsilon)
      {
        result = new Rgb(
            luminosity +
            ((result.R - luminosity) *
             (1.0 - luminosity) / denominator),

            luminosity +
            ((result.G - luminosity) *
             (1.0 - luminosity) / denominator),

            luminosity +
            ((result.B - luminosity) *
             (1.0 - luminosity) / denominator));
      }
    }

    return result.Clamp();
  }

  /// <summary>
  /// Direct2D Color/Luminosity blend가 사용하는 luminosity입니다.
  /// </summary>
  private static double GetLuminosity(Rgb color) => (0.30 * color.R) + (0.59 * color.G) + (0.11 * color.B);

  private static Rgb Lerp(Rgb from, Rgb to, double amount) => new(from.R + ((to.R - from.R) * amount), from.G + ((to.G - from.G) * amount), from.B + ((to.B - from.B) * amount));

  private static void ValidateOpacity(double value, string parameterName)
  {
    if (double.IsNaN(value) || double.IsInfinity(value) || value is < 0.0 or > 1.0)
    {
      throw new ArgumentOutOfRangeException(parameterName, value, "Opacity 값은 0 이상 1 이하여야 합니다.");
    }
  }

  /// <summary>
  /// 양수 값을 가장 가까운 byte로 반올림합니다.
  /// </summary>
  private static byte RoundToByte(double value) => (byte)Math.Clamp((int)Math.Floor(value + 0.5), byte.MinValue, byte.MaxValue);

  private readonly record struct Rgb(double R, double G, double B)
  {
    public static Rgb FromColor(Color color) => new(color.R / 255.0, color.G / 255.0, color.B / 255.0);

    public Rgb Clamp() => new(Math.Clamp(R, 0.0, 1.0), Math.Clamp(G, 0.0, 1.0), Math.Clamp(B, 0.0, 1.0));

    public Color ToOpaqueColor()
    {
      Rgb clamped = Clamp();

      return Color.FromArgb(byte.MaxValue, RoundToByte(clamped.R * 255.0), RoundToByte(clamped.G * 255.0), RoundToByte(clamped.B * 255.0));
    }
  }
}