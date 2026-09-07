namespace MyNotes.Common.Helpers;

internal static class BooleanHelper
{
  public static bool Negate(bool value) => !value;

  public static bool And(bool v1, bool v2) => v1 && v2;

  public static bool And(bool v1, bool v2, bool v3) => v1 && v2 && v3;

  public static bool Or(bool v1, bool v2) => v1 || v2;

  public static bool Or(bool v1, bool v2, bool v3) => v1 || v2 || v3;

  public static bool GetValueOrDefault(bool? value) => GetValueOrDefault(value, false);

  public static bool GetValueOrDefault(bool? value, bool nullValue = false) => value ?? nullValue;

  public static Visibility ToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
  public static Visibility ToVisibility(bool? value) => value is not null
  ? value.Value
    ? Visibility.Visible : Visibility.Collapsed
  : Visibility.Collapsed;

  public static Visibility ToInverseVisibility(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

  public static Visibility ToInverseVisibility(bool? value) => value is not null
    ? value.Value
      ? Visibility.Collapsed : Visibility.Visible
    : Visibility.Collapsed;

  public static Visibility VisibleWhenAll(bool v1, bool v2) => v1 && v2 ? Visibility.Visible : Visibility.Collapsed;
}