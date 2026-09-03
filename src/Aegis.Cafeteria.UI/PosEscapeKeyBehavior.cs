namespace Aegis.Cafeteria.UI;

using System.Reflection;
using System.Runtime.CompilerServices;

internal static class PosEscapeKeyBehavior
{
    private static readonly EscapeKeyMessageFilter MessageFilter = new();
    private static bool installed;

    [ModuleInitializer]
    internal static void Initialize()
    {
        System.Windows.Forms.Application.Idle += InstallWhenIdle;
    }

    private static void InstallWhenIdle(object? sender, EventArgs eventArgs)
    {
        System.Windows.Forms.Application.Idle -= InstallWhenIdle;

        if (installed)
        {
            return;
        }

        System.Windows.Forms.Application.AddMessageFilter(MessageFilter);
        installed = true;
    }

    private sealed class EscapeKeyMessageFilter : IMessageFilter
    {
        private const int WmKeyDown = 0x0100;
        private const int WmSysKeyDown = 0x0104;

        public bool PreFilterMessage(ref Message message)
        {
            if (message.Msg is not WmKeyDown and not WmSysKeyDown
                || (Keys)message.WParam.ToInt32() != Keys.Escape)
            {
                return false;
            }

            Control? focusedControl = Control.FromHandle(message.HWnd);
            if (focusedControl?.FindForm() is not MainForm mainForm)
            {
                return false;
            }

            FieldInfo? currentCustomerField = typeof(MainForm).GetField
            (
                  "currentCustomer"
                , BindingFlags.Instance | BindingFlags.NonPublic
            );

            if (currentCustomerField?.GetValue(mainForm) is null)
            {
                return false;
            }

            MethodInfo? cancelActiveTransaction = typeof(MainForm).GetMethod
            (
                  "CancelActiveTransaction"
                , BindingFlags.Instance | BindingFlags.NonPublic
            );

            cancelActiveTransaction?.Invoke(mainForm, null);
            return true;
        }
    }
}
