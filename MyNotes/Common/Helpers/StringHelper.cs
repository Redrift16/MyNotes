using System.Globalization;

using Windows.Globalization;

namespace MyNotes.Common.Helpers;

internal static class StringHelper
{
  public static string? NullIfWhiteSpace(string? str) => string.IsNullOrWhiteSpace(str) ? null : str;

  public static string SizeToString(int width, int height) => $"{width} × {height}";

  public static bool IsNullOrWhiteSpace(string? str) => string.IsNullOrWhiteSpace(str);
  public static bool IsNullOrEmpty(string? str) => string.IsNullOrEmpty(str);
  public static bool IsFilled(string? str) => !string.IsNullOrWhiteSpace(str);
  public static bool IsNotEmpty(string? str) => !string.IsNullOrEmpty(str);

  public static string WrapWithQuotes(string str) => $"\"{str}\"";

  public static string Affix(string word, string prefix = "", string suffix = "") => $"{prefix}{word}{suffix}";

  public static string Inflect(string word)
  {
    string? currentLanguage = ApplicationLanguages.PrimaryLanguageOverride ?? ApplicationLanguages.Languages.FirstOrDefault();
    if (currentLanguage is null || string.IsNullOrEmpty(word))
    {
      return string.Empty;
    }

    switch (CultureInfo.GetCultureInfo(currentLanguage).TwoLetterISOLanguageName)
    {
      case "ko":
        var lastChar = word[^1];
        if (char.GetUnicodeCategory(lastChar) != UnicodeCategory.OtherLetter)
        {
          return word + "을";
        }

        int code = lastChar - 0xAC00;
        bool hasJongseong = (code % 28) != 0;
        return word + (hasJongseong ? "을" : "를");
    }

    return word;
  }

  public static string GetFileName(string path) => Path.GetFileName(path);
}
