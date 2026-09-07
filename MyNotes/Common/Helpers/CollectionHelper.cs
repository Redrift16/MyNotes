namespace MyNotes.Common.Helpers;

internal static class CollectionHelper
{
  /// <summary>
  /// 지정한 정수 시퀀스에서 최장 증가 부분 수열(LIS)에 포함되는 인덱스를 표시합니다.
  /// 반환 컬렉션에서 값이 true인 항목의 인덱스가 LIS에 포함된 원본 컬렉션 항목의 인덱스입니다.
  /// <remarks>
  /// collection: [ 3 1 4 5 2 ] -> LIS: [ 3 4 5 ] -> result: [ T F T T F ]
  /// </remarks>
  /// </summary>
  /// <param name="collection"></param>
  public static IReadOnlyList<bool> FindLISIndexFlags(IReadOnlyList<int> collection)
  {
    int count = collection.Count;
    bool[] result = new bool[count];

    if (count == 0)
    {
      return result;
    }

    int[] previousIndexes = new int[count];
    int[] tailIndexes = new int[count];

    Array.Fill(previousIndexes, -1);

    int lisLength = 0;

    for (int currentIndex = 0; currentIndex < count; currentIndex++)
    {
      int left = 0;
      int right = lisLength;

      while (left < right)
      {
        int middle = left + ((right - left) / 2);

        if (collection[tailIndexes[middle]] < collection[currentIndex])
        {
          left = middle + 1;
        }
        else
        {
          right = middle;
        }
      }

      if (left > 0)
      {
        previousIndexes[currentIndex] = tailIndexes[left - 1];
      }

      tailIndexes[left] = currentIndex;

      if (left == lisLength)
      {
        lisLength++;
      }
    }

    for (int lisIndex = tailIndexes[lisLength - 1]; lisIndex >= 0; lisIndex = previousIndexes[lisIndex])
    {
      result[lisIndex] = true;
    }

    return result;
  }

  /// <summary>
  /// 지정한 시퀀스에서 최장 증가 부분 수열(LIS)에 포함되는 인덱스를 표시합니다.
  /// 반환 컬렉션에서 값이 true인 항목의 인덱스가 LIS에 포함된 원본 컬렉션 항목의 인덱스입니다.
  /// <remarks>
  /// collection: [ C A D E B ], comparer: StringComparer.OrdinalIgnoreCase  -> LIS: [ C D E ] -> result: [ T F T T F ]
  /// </remarks>
  /// </summary>
  /// <param name="collection"></param>
  public static IReadOnlyList<bool> FindLISIndexFlags<T>(IReadOnlyList<T> collection, IComparer<T>? comparer = null)
  {
    int count = collection.Count;
    bool[] result = new bool[count];

    if (count == 0)
    {
      return result;
    }

    comparer ??= Comparer<T>.Default;

    int[] previousIndexes = new int[count];
    int[] tailIndexes = new int[count];

    Array.Fill(previousIndexes, -1);

    int lisLength = 0;

    for (int currentIndex = 0; currentIndex < count; currentIndex++)
    {
      int left = 0;
      int right = lisLength;

      while (left < right)
      {
        int middle = left + ((right - left) / 2);

        T tailValue = collection[tailIndexes[middle]];
        T currentValue = collection[currentIndex];

        if (comparer.Compare(tailValue, currentValue) < 0)
        {
          left = middle + 1;
        }
        else
        {
          right = middle;
        }
      }

      if (left > 0)
      {
        previousIndexes[currentIndex] = tailIndexes[left - 1];
      }

      tailIndexes[left] = currentIndex;

      if (left == lisLength)
      {
        lisLength++;
      }
    }

    for (int lisIndex = tailIndexes[lisLength - 1]; lisIndex >= 0; lisIndex = previousIndexes[lisIndex])
    {
      result[lisIndex] = true;
    }

    return result;
  }

  /// <summary>
  /// 정렬된 컬렉션에서 지정한 항목의 정수 Position 값을 재배치합니다.
  /// Position 간격이 충분하면 대상 항목만 수정하고,
  /// 간격이 부족하면 주변 항목 일부를 함께 이동합니다.
  /// </summary>
  /// <typeparam name="T">Position을 가진 항목 타입입니다.</typeparam>
  /// <param name="collection">Position 기준으로 정렬되어 있고, 대상 항목이 이미 삽입된 컬렉션입니다.</param>
  /// <param name="targetIndex">새 Position을 부여할 대상 항목의 인덱스입니다.</param>
  /// <param name="getPosition">항목에서 Position 값을 읽는 Func입니다.</param>
  /// <param name="setPosition">항목에 Position 값을 쓰는 Action입니다.</param>
  /// <returns>Position 값이 실제로 변경된 항목 목록입니다.</returns>
  /// <exception cref="ArgumentNullException">필수 인자가 null인 경우 발생합니다.</exception>
  /// <exception cref="ArgumentOutOfRangeException">targetIndex가 컬렉션 범위를 벗어난 경우 발생합니다.</exception>
  public static IReadOnlyList<T> ReassignPositions<T>(IList<T> collection, int targetIndex, Func<T, int> getPosition, Action<T, int> setPosition)
  {
    ArgumentNullException.ThrowIfNull(collection);
    ArgumentNullException.ThrowIfNull(getPosition);
    ArgumentNullException.ThrowIfNull(setPosition);

    int count = collection.Count;

    // targetIndex가 허용 범위를 벗어나면 예외 발생
    if (targetIndex < 0 || targetIndex >= count)
    {
      throw new ArgumentOutOfRangeException(nameof(targetIndex));
    }

    List<T> affectedItems = new();

    if (count == 1)
    {
      SetPositionIfChanged(collection[targetIndex], 0, getPosition, setPosition, affectedItems);
      return affectedItems;
    }

    if (targetIndex == 0)
    {
      int nextPosition = getPosition(collection[1]);
      SetPositionIfChanged(collection[0], nextPosition - 1, getPosition, setPosition, affectedItems);
      return affectedItems;
    }

    if (targetIndex == count - 1)
    {
      int previousPosition = getPosition(collection[^2]);
      SetPositionIfChanged(collection[^1], previousPosition + 1, getPosition, setPosition, affectedItems);
      return affectedItems;
    }

    int previousPos = getPosition(collection[targetIndex - 1]);
    int nextPos = getPosition(collection[targetIndex + 1]);

    int candidatePos = previousPos + ((nextPos - previousPos) / 2);

    if (previousPos < candidatePos && candidatePos < nextPos)
    {
      SetPositionIfChanged(collection[targetIndex], candidatePos, getPosition, setPosition, affectedItems);
      return affectedItems;
    }

    // 좌우 간격이 없으면 근처 항목들을 최소 범위로 다시 번호 매김합니다.
    int startIndex = Math.Max(0, targetIndex - 8);
    int endIndex = Math.Min(count - 1, targetIndex + 8);

    int basePosition = startIndex == 0
      ? 0
      : getPosition(collection[startIndex - 1]) + 1;

    for (int index = startIndex; index <= endIndex; index++)
    {
      int newPosition = basePosition + (index - startIndex);
      SetPositionIfChanged(collection[index], newPosition, getPosition, setPosition, affectedItems);
    }

    return affectedItems;
  }

  private static void SetPositionIfChanged<T>(T item, int newPosition, Func<T, int> getPosition, Action<T, int> setPosition, ICollection<T> affectedItems)
  {
    if (getPosition(item) == newPosition)
    {
      return;
    }

    setPosition(item, newPosition);
    affectedItems.Add(item);
  }
}