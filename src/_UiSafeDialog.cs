using System;
using System.Windows.Forms;

namespace CdJsonModManager
{
    internal static class UiSafe
    {
        public static DialogResult Msg(string text)
        {
            return Msg(text, "", MessageBoxButtons.OK, MessageBoxIcon.None);
        }

        public static DialogResult Msg(string text, string caption)
        {
            return Msg(text, caption, MessageBoxButtons.OK, MessageBoxIcon.None);
        }

        public static DialogResult Msg(string text, string caption, MessageBoxButtons buttons)
        {
            return Msg(text, caption, buttons, MessageBoxIcon.None);
        }

        public static DialogResult Msg(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            Form main = null;
            try
            {
                if (Application.OpenForms != null && Application.OpenForms.Count > 0)
                    main = Application.OpenForms[0];
            }
            catch { }

            if (main != null && main.InvokeRequired)
            {
                try
                {
                    return (DialogResult)main.Invoke(new Func<DialogResult>(
                        () => MessageBox.Show(main, text, caption, buttons, icon)));
                }
                catch
                {
                    return MessageBox.Show(text, caption, buttons, icon);
                }
            }

            return main != null
                ? MessageBox.Show(main, text, caption, buttons, icon)
                : MessageBox.Show(text, caption, buttons, icon);
        }
    }
}
