namespace MyNotes.Common.Helpers;

internal static class NumericHelper
{
  public static string Round(double num, int digits) => Math.Round(num, digits).ToString();

  public static string GetPercentage(double x, double y) => ((int)Math.Round(x / y * 100, 0)).ToString();

  extension(float f)
  {
    public int GreaterThanNearestMultiple(uint n, bool exclusive = true)
    {
      if (n == 0)
      {
        throw new DivideByZeroException();
      }

      int quotient = (int)Math.Ceiling(f / n);
      if (exclusive && Math.Abs(f % n) < 1.0e-6f)
      {
        quotient++;
      }

      return quotient * (int)n;
    }

    public int LessThanNearestMultiple(uint n, bool exclusive = true)
    {
      if (n == 0)
      {
        throw new DivideByZeroException();
      }

      int quotient = (int)Math.Floor(f / n);
      if (exclusive && Math.Abs(f % n) < 1.0e-6f)
      {
        quotient--;
      }

      return quotient * (int)n;
    }
  }
}