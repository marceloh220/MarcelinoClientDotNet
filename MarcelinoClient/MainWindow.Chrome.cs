using System.Windows;
using System.Windows.Automation;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MarcelinoClient;

public partial class MainWindow
{
    private HwndSource? _chromeSource;

    private void InitializeWindowWorkArea()
    {
        _chromeSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _chromeSource?.AddHook(WindowWorkAreaHook);
        if (_chromeSource is null || !TryGetMonitorInfo(_chromeSource.Handle, out var monitor))
        {
            return;
        }

        var transform = _chromeSource.CompositionTarget.TransformFromDevice;
        var topLeft = transform.Transform(new Point(monitor.Work.Left, monitor.Work.Top));
        var bottomRight = transform.Transform(new Point(monitor.Work.Right, monitor.Work.Bottom));
        var work = new Rect(topLeft, bottomRight);
        MinWidth = Math.Min(MinWidth, work.Width);
        MinHeight = Math.Min(MinHeight, work.Height);
        Width = Math.Min(Width, work.Width);
        Height = Math.Min(Height, work.Height);
        if (!double.IsNaN(Left))
        {
            Left = Math.Clamp(Left, work.Left, work.Right - Width);
        }

        if (!double.IsNaN(Top))
        {
            Top = Math.Clamp(Top, work.Top, work.Bottom - Height);
        }
    }

    private static nint WindowWorkAreaHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0024 && TryGetMonitorInfo(hwnd, out var monitor))
        {
            var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            limits.MaxPosition.X = monitor.Work.Left - monitor.Bounds.Left;
            limits.MaxPosition.Y = monitor.Work.Top - monitor.Bounds.Top;
            limits.MaxSize.X = monitor.Work.Right - monitor.Work.Left;
            limits.MaxSize.Y = monitor.Work.Bottom - monitor.Work.Top;
            Marshal.StructureToPtr(limits, lParam, fDeleteOld: false);
            handled = true;
        }

        return 0;
    }

    private static bool TryGetMonitorInfo(nint window, out MonitorInfo info)
    {
        var monitor = MonitorFromWindow(window, 2);
        info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return monitor != 0 && GetMonitorInfo(monitor, ref info);
    }

    private void DisposeWindowWorkArea()
    {
        _chromeSource?.RemoveHook(WindowWorkAreaHook);
        _chromeSource = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Bounds;
        public NativeRect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void MaximizeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e) => Close();

    private void UpdateCaptionButtons()
    {
        if (MaximizeWindowButton is null)
        {
            return;
        }

        var maximized = WindowState == WindowState.Maximized;
        MaximizeWindowButton.Tag = maximized ? "\uE923" : "\uE922";
        MaximizeWindowButton.ToolTip = maximized ? Localization.Get("TrayRestore") : Localization.Get("Maximize");
        AutomationProperties.SetName(MaximizeWindowButton, maximized ? Localization.Get("TrayRestore") : Localization.Get("Maximize"));
    }
}