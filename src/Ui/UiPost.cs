using System;
using System.Windows.Forms;

namespace HotkeyHound.Ui
{
    /// <summary>
    /// 统一的「后台线程 → UI 线程」投递口。
    ///
    /// 为什么不直接用 Control.BeginInvoke(Delegate, params object[])：
    /// 那个重载在消息泵里走 Delegate.DynamicInvoke（反射调用），一旦委托签名和参数个数
    /// 对不上就抛 TargetParameterCountException，而异常发生在 WinForms 内部，
    /// 堆栈里没有一行业务代码——现场只能看到一个「参数计数不匹配」的对话框，无法定位。
    ///
    /// 这里改用 MethodInvoker：WinForms 对它有专门的直接调用分支，不做反射；
    /// 而且把真正的 UI 动作包在 try/catch 里，失败时记录「来源方法名 + 完整堆栈」，
    /// 下次再出问题能直接看到是哪个回调。
    /// </summary>
    public static class UiPost
    {
        public static void Post(Control target, Action action)
        {
            if (target == null || action == null) return;
            try
            {
                if (target.IsDisposed || !target.IsHandleCreated) return;
                target.BeginInvoke(new MethodInvoker(delegate ()
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        try { Program.ReportUiFailure(ex, action.Method.Name); }
                        catch (Exception) { }
                    }
                }));
            }
            catch (Exception)
            {
                // 窗口正在销毁时 BeginInvoke 会失败，这属于正常竞态，不需要额外处理。
            }
        }
    }
}
