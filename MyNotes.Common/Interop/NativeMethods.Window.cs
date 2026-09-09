using System;
using System.Runtime.InteropServices;

namespace MyNotes.Common.Interop;

internal static partial class NativeMethods
{
  #region 창 관련
  [LibraryImport("user32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool GetWindowInfo(IntPtr hwnd, ref WINDOWINFO pwi);

  [LibraryImport("user32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

  [LibraryImport("user32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool GetClientRect(IntPtr hWnd, out RECT lpRect);

  [LibraryImport("user32.dll", SetLastError = true)]
  public static partial uint GetDpiForWindow(IntPtr hWnd);

  [LibraryImport("user32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

  [LibraryImport("user32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

  [LibraryImport("user32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

  [LibraryImport("user32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

  [LibraryImport("user32.dll", SetLastError = true)]
  public static partial IntPtr GetForegroundWindow();

  [LibraryImport("user32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool SetForegroundWindow(IntPtr hWnd);

  [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
  public static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

  [LibraryImport("user32.dll", EntryPoint = "CallWindowProcW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
  public static partial IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

  public delegate IntPtr WndProcCallback(IntPtr hwnd, uint uMsg, IntPtr wParam, IntPtr lParam);

  [StructLayout(LayoutKind.Sequential)]
  public struct WINDOWINFO
  {
    public int cbSize = Marshal.SizeOf<WINDOWINFO>();
    public RECT rcWindow;
    public RECT rcClient;
    public int dwStyle;
    public int dwExStyle;
    public int dwWindowStatus;
    public uint cxWindowBorders;
    public uint cyWindowBorders;
    public ushort atomWindowType;
    public ushort wCreatorVersion;

    public WINDOWINFO() { }
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct RECT
  {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
  }

  public static double GetWindowScaleFactor(IntPtr hWnd) => GetDpiForWindow(hWnd) / 96.0;

  [LibraryImport("user32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool IsWindowArranged(IntPtr hWnd);
  #endregion
}