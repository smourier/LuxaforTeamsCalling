using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LuxaforTeamsCalling.Properties;
using UIAutomationClient;

namespace LuxaforTeamsCalling
{
    public partial class Main : Form
    {
        // we declare ids so we can embed COM interop
        private const int UIA_Window_WindowOpenedEventId = 20016;
        private const int UIA_Window_WindowClosedEventId = 20017;

        internal static readonly CUIAutomation8 _automation = new CUIAutomation8();
        private readonly AutomationEventHandler _eventHandler;
        private readonly NotifyIcon _notifyIcon = new NotifyIcon();
        private bool _doClose;

        public Main()
        {
            InitializeComponent();
            Icon = Resources.LuxaforTeamsCallingIcon;
            Text = ProductName;
            labelText.Text = ProductName + Environment.NewLine +
                "Version " + Assembly.GetEntryAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>().Version + Environment.NewLine + Environment.NewLine +
                "Copyright (C) 2021-" + DateTime.Now.Year + " Simon Mourier. All rights reserved.";

            _eventHandler = new AutomationEventHandler(this);

            ToolStripMenuItem item;
            _notifyIcon.Icon = Icon;
            _notifyIcon.ContextMenuStrip = new ContextMenuStrip();
            item = (ToolStripMenuItem)_notifyIcon.ContextMenuStrip.Items.Add("About...", null, (s, e) => RestoreFromTray());

            _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            _notifyIcon.ContextMenuStrip.Items.Add("E&xit", null, (s, e) => Quit());
            _notifyIcon.Visible = true;

            _notifyIcon.DoubleClick += (s, e) => RestoreFromTray();
            _notifyIcon.Text = Text;
        }

        protected override void WndProc(ref Message m)
        {
            Program.Singleton.OnWndProc(this, m, true);
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
            {
                components.Dispose();
                _notifyIcon?.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _automation.AddAutomationEventHandler(UIA_Window_WindowOpenedEventId, _automation.GetRootElement(), TreeScope.TreeScope_Subtree, null, _eventHandler);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            base.OnHandleDestroyed(e);
            _automation.RemoveAutomationEventHandler(UIA_Window_WindowOpenedEventId, _automation.GetRootElement(), _eventHandler);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ResetLuxafor();
            Thread.Sleep(1000);
            ResetLuxafor();
            base.OnFormClosed(e);
        }

        // minimized if closed
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_doClose || e.CloseReason == CloseReason.WindowsShutDown)
            {
                base.OnFormClosing(e);
                return;
            }

#if DEBUG
            if (ModifierKeys.HasFlag(Keys.Control) && ModifierKeys.HasFlag(Keys.Shift))
            {
                e.Cancel = true;
                base.OnFormClosing(e);
                MinimizeToTray();
            }
            base.OnFormClosing(e);
#else
            // if closing when minimized (right click on icon) or if CTRL+SHIFT is pressed, do close
            if (WindowState == FormWindowState.Minimized || (ModifierKeys.HasFlag(Keys.Control) && ModifierKeys.HasFlag(Keys.Shift)))
            {
                base.OnFormClosing(e);
                return;
            }

            e.Cancel = true;
            base.OnFormClosing(e);
            MinimizeToTray();
#endif
        }

        private void MinimizeToTray()
        {
            WindowState = FormWindowState.Minimized;
            Hide();
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
        }

        // don't remove base, as OnClosing is overriden
        private void Quit()
        {
            _doClose = true;
#pragma warning disable IDE0002
            base.Close();
#pragma warning restore IDE0002
        }

        private LuxaforDevice GetLuxaforDevice() => LuxaforDevice.Enumerate().FirstOrDefault(d => d.IsPresent);

        private CancellationTokenSource _cancel;

        private void OnTeamsCallingStart()
        {
            var luxafor = GetLuxaforDevice();
            if (luxafor == null)
                return;

            if (_cancel != null)
            {
                _cancel.Cancel(false);
            }

            _cancel = new CancellationTokenSource();
            Task.Run(() =>
            {
                do
                {
                    luxafor.SetPattern(LuxaforPattern.RainbowWave, 0);
                    Thread.Sleep(1000);
                }
                while (!_cancel.IsCancellationRequested);
                _cancel = null;
                ResetLuxafor();
            }, _cancel.Token);
        }

        private void OnTeamsCallingEnd()
        {
            if (_cancel == null)
                return;

            _cancel.Cancel(false);
        }

        private void ResetLuxafor()
        {
            var luxafor = GetLuxaforDevice();
            if (luxafor == null)
                return;

            luxafor.FadeTo(Color.Black, LuxaforLedTarget.All, 0);
            luxafor.SetColor(Color.Black);
        }

        private void buttonClose_Click(object sender, EventArgs e) => Close();
        private void buttonInfo_Click(object sender, EventArgs e)
        {
            if (ModifierKeys.HasFlag(Keys.Shift | Keys.Control))
            {
                OnTeamsCallingStart();
                return;
            }

            var luxafor = GetLuxaforDevice();
            if (luxafor == null)
            {
                MessageBox.Show(this, "No luxafor device was found.", ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show(this, "Luxafor device found: " + luxafor.Product, ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private class AutomationEventHandler : IUIAutomationEventHandler
        {
            private readonly Main _main;

            public AutomationEventHandler(Main main)
            {
                _main = main;
            }

            public void HandleAutomationEvent(IUIAutomationElement sender, int eventId)
            {
                try
                {
                    switch (eventId)
                    {
                        case UIA_Window_WindowOpenedEventId:
                            if (string.Compare(sender.CurrentName, "Microsoft Teams Notification", StringComparison.Ordinal) != 0)
                                return;

                            const int UIA_AriaRolePropertyId = 30101;
                            var alertElement = sender.FindFirst(TreeScope.TreeScope_Subtree, _automation.CreatePropertyCondition(UIA_AriaRolePropertyId, "alert"));
                            if (alertElement == null)
                                return;

                            _automation.AddAutomationEventHandler(UIA_Window_WindowClosedEventId, sender, TreeScope.TreeScope_Element, null, this);
                            _main.OnTeamsCallingStart();
                            break;

                        case UIA_Window_WindowClosedEventId:
                            _main.OnTeamsCallingEnd();
                            break;
                    }
                }
                catch
                {
                    // continue
                }
            }
        }
    }
}
