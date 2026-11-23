namespace MoxTerm.Services;

/// <summary>非打包应用的文件/目录选择器需要手动关联窗口句柄，这里统一处理。</summary>
public static class PickerHelper
{
    /// <summary>把选择器关联到主窗口；窗口尚未创建时返回 false。</summary>
    public static bool TryAttach(object picker)
    {
        if (App.Shell is not { } window)
        {
            return false;
        }

        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(window));

        return true;
    }
}
