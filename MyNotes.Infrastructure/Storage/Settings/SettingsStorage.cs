
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using MyNotes.Application.Contracts.Settings;
using MyNotes.Debugging;

using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;

namespace MyNotes.Infrastructure.Storage.Settings;

/// <summary>
/// 로컬 애플리케이션 데이터 저장소에 애플리케이션 설정을 저장, 검색하기 위한 메서드를 제공합니다.
/// </summary>
/// <remarks>
///  Windows.Storage.ApplicationDataContainer와 호환되는 다양한 데이터 형식을 지원합니다.
/// 지원되는 데이터 형식 및 사용 패턴에 대한 자세한 내용은 <see href="https://learn.microsoft.com/windows/apps/design/app-settings/store-and-retrieve-app-data">설정과 기타 앱 데이터의 저장 및 검색</see>을 참고하세요.
/// </remarks>
internal sealed class SettingsStorage : ISettingsStorage
{
  public static ApplicationDataContainer LocalSettings { get; } = ApplicationData.Current.LocalSettings;
  private readonly IPropertySet _settingsSet = LocalSettings.Values;

  /// <remarks>
  /// 앱 설정에 사용할 수 있는 데이터 유형(Type)은 다음과 같습니다.
  /// byte, short, ushort, int, uint, long, ulong, float, double,
  /// bool, char, string,
  /// System.DateTimeOffset, System.TimeSpan,
  /// System.Guid, Windows.Foundation.Point, Windows.Foundation.Size, Windows.Foundation.Rect,
  /// Windows.Storage.ApplicationDataCompositeValue
  /// </remarks>
  public bool IsValid(object value)
  {
    return value switch
    {
      byte or short or ushort or int or uint or long or ulong or float or double or
      bool or char or string or DateTimeOffset or TimeSpan or
      Guid or Point or Size or Rect or
      ApplicationDataCompositeValue => true,
      _ => false,
    };
  }

  public bool IsValidType(Type type) =>
    type == typeof(byte) || type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint) ||
    type == typeof(long) || type == typeof(ulong) || type == typeof(float) || type == typeof(double) ||
    type == typeof(bool) || type == typeof(char) || type == typeof(string) ||
    type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(Guid) ||
    type == typeof(Point) || type == typeof(Size) || type == typeof(Rect) ||
    type == typeof(ApplicationDataCompositeValue);

  public void Save<T>(string settingsKey, T settingsValue) where T : notnull
  {
    if (!IsValid(settingsValue))
    {
      return;
    }
    ConsoleHelper.WriteLine(true, "{0} ({1}): {2} ({3})", "Settings Saved", settingsKey, settingsValue, settingsValue.GetType().Name);
    if (_settingsSet.TryGetValue(settingsKey, out var oldSettingsValue) && settingsValue.Equals(oldSettingsValue))
    {
      return;
    }
    _settingsSet[settingsKey] = settingsValue;
  }

  public bool TryLoad<T>(string settingsKey, [NotNullWhen(true)] out T? settingsValue) where T : notnull
  {
    if (_settingsSet.TryGetValue(settingsKey, out var value) && value is T TValue)
    {
      settingsValue = TValue;
      return true;
    }

    settingsValue = default;
    return false;
  }

  public void SaveToComposite<T>(string settingsKey, KeyValuePair<string, T> pair) where T : notnull
  {
    if (!IsValid(pair.Value))
    {
      return;
    }

    if (_settingsSet.TryGetValue(settingsKey, out var composite)
      && composite is ApplicationDataCompositeValue settingsPairs)
    {
      var oldSettingsValue = settingsPairs[pair.Key];
      settingsPairs[pair.Key] = pair.Value;
    }
    else
    {
      ApplicationDataCompositeValue compositeValue = new();
      compositeValue[pair.Key] = pair.Value;
      _settingsSet[settingsKey] = compositeValue;
    }
  }

  public IReadOnlyDictionary<string, T> LoadFromComposite<T>(string settingsKey) where T : notnull
  {
    Dictionary<string, T> pairs = new();
    if (_settingsSet.TryGetValue(settingsKey, out var composite)
      && composite is ApplicationDataCompositeValue settingsPairs)
    {
      foreach (var kv in settingsPairs)
      {
        if (kv.Value is T TValue)
        {
          pairs.Add(kv.Key, TValue);
        }
      }
    }
    return pairs.AsReadOnly();
  }
}