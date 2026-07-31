using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CodexRouterSwitch
{
    internal sealed class EnhancedMainForm : Form
    {
        private readonly RouterController controller;
        private readonly RadioButton nativeMode;
        private readonly RadioButton routerMode;
        private readonly Label connectionValue;
        private readonly Label processValue;
        private readonly Label modelValue;
        private readonly Label providerValue;
        private readonly Label checkedValue;
        private readonly Label statusHeadline;
        private readonly Label statusDetail;
        private readonly RoundedPanel statusPanel;
        private readonly Button primaryAction;
        private readonly Button secondaryAction;
        private readonly Button refreshButton;
        private readonly Button openLogButton;
        private readonly Button copyDiagnosticsButton;
        private readonly BusyLine busyLine;
        private readonly RoundedPanel restartPanel;
        private readonly Label restartText;
        private readonly RowStyle restartRowStyle;
        private readonly System.Windows.Forms.Timer refreshTimer;

        private bool suppressModeEvent;
        private bool busy;
        private bool refreshing;
        private SwitchStatus lastStatus;
        private Action primaryActionHandler;
        private Action secondaryActionHandler;

        public EnhancedMainForm(RouterController controller)
        {
            this.controller = controller;

            Text = "Codex Router Switch";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(690, 640);
            MinimumSize = new Size(690, 640);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = true;
            ShowIcon = false;
            BackColor = Color.FromArgb(248, 250, 252);
            Font = new Font("Segoe UI", 10);
            DoubleBuffered = true;
            AccessibleName = "Codex Router Switch";

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(34, 24, 34, 24);
            root.ColumnCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowCount = 10;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 14F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 14F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 164F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 14F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 12F));
            restartRowStyle = new RowStyle(SizeType.Absolute, 0F);
            root.RowStyles.Add(restartRowStyle);
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.Margin = Padding.Empty;
            root.Controls.Add(header, 0, 0);

            Label title = new Label();
            title.Text = "Codex connection";
            title.Font = new Font("Segoe UI Semibold", 20);
            title.ForeColor = Color.FromArgb(32, 31, 30);
            title.AutoSize = true;
            title.Location = new Point(0, 0);
            header.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "Choose how new Codex sessions connect.";
            subtitle.ForeColor = Color.FromArgb(96, 94, 92);
            subtitle.AutoSize = true;
            subtitle.Location = new Point(2, 40);
            header.Controls.Add(subtitle);

            RoundedPanel modePanel = CreateCard();
            root.Controls.Add(modePanel, 0, 2);

            TableLayoutPanel modeLayout = new TableLayoutPanel();
            modeLayout.Dock = DockStyle.Fill;
            modeLayout.Margin = Padding.Empty;
            modeLayout.Padding = new Padding(20, 15, 20, 12);
            modeLayout.ColumnCount = 2;
            modeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            modeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            modeLayout.RowCount = 2;
            modeLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            modeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 3F));
            modePanel.Controls.Add(modeLayout);

            nativeMode = CreateModeOption(
                "Native Codex",
                "Connect directly with the original Codex configuration."
            );
            nativeMode.CheckedChanged += ModeCheckedChanged;
            modeLayout.Controls.Add(nativeMode, 0, 0);

            routerMode = CreateModeOption(
                "Local Router",
                "Use the local Router for configured external providers."
            );
            routerMode.CheckedChanged += ModeCheckedChanged;
            modeLayout.Controls.Add(routerMode, 1, 0);

            busyLine = new BusyLine();
            busyLine.Dock = DockStyle.Fill;
            busyLine.Margin = Padding.Empty;
            modeLayout.Controls.Add(busyLine, 0, 1);
            modeLayout.SetColumnSpan(busyLine, 2);

            RoundedPanel detailsPanel = CreateCard();
            root.Controls.Add(detailsPanel, 0, 4);

            TableLayoutPanel details = new TableLayoutPanel();
            details.Dock = DockStyle.Fill;
            details.Margin = Padding.Empty;
            details.Padding = new Padding(20, 13, 20, 13);
            details.ColumnCount = 2;
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            details.RowCount = 5;
            for (int index = 0; index < 5; index++)
            {
                details.RowStyles.Add(new RowStyle(SizeType.Percent, 20F));
            }
            detailsPanel.Controls.Add(details);

            connectionValue = AddDetailRow(details, 0, "Connection mode");
            processValue = AddDetailRow(details, 1, "Router process");
            modelValue = AddDetailRow(details, 2, "Current model");
            providerValue = AddDetailRow(details, 3, "Provider");
            checkedValue = AddDetailRow(details, 4, "Last checked");

            statusPanel = CreateCard();
            statusPanel.FillColor = Color.FromArgb(245, 249, 254);
            statusPanel.BorderColor = Color.FromArgb(210, 226, 244);
            root.Controls.Add(statusPanel, 0, 6);

            TableLayoutPanel statusLayout = new TableLayoutPanel();
            statusLayout.Dock = DockStyle.Fill;
            statusLayout.Margin = Padding.Empty;
            statusLayout.Padding = new Padding(16, 10, 12, 10);
            statusLayout.ColumnCount = 2;
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 238F));
            statusLayout.RowCount = 2;
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            statusPanel.Controls.Add(statusLayout);

            statusHeadline = new Label();
            statusHeadline.Font = new Font("Segoe UI Semibold", 11);
            statusHeadline.ForeColor = Color.FromArgb(32, 31, 30);
            statusHeadline.Dock = DockStyle.Fill;
            statusHeadline.TextAlign = ContentAlignment.MiddleLeft;
            statusHeadline.Margin = Padding.Empty;
            statusLayout.Controls.Add(statusHeadline, 0, 0);

            statusDetail = new Label();
            statusDetail.ForeColor = Color.FromArgb(80, 78, 76);
            statusDetail.Dock = DockStyle.Fill;
            statusDetail.TextAlign = ContentAlignment.TopLeft;
            statusDetail.AutoEllipsis = true;
            statusDetail.Margin = Padding.Empty;
            statusLayout.Controls.Add(statusDetail, 0, 1);

            FlowLayoutPanel statusActions = new FlowLayoutPanel();
            statusActions.Dock = DockStyle.Fill;
            statusActions.FlowDirection = FlowDirection.RightToLeft;
            statusActions.WrapContents = false;
            statusActions.Margin = Padding.Empty;
            statusActions.Padding = new Padding(0, 9, 0, 0);
            statusLayout.Controls.Add(statusActions, 1, 0);
            statusLayout.SetRowSpan(statusActions, 2);

            primaryAction = CreateActionButton("");
            primaryAction.Click += PrimaryActionClicked;
            primaryAction.Visible = false;
            statusActions.Controls.Add(primaryAction);

            secondaryAction = CreateActionButton("");
            secondaryAction.Click += SecondaryActionClicked;
            secondaryAction.Visible = false;
            statusActions.Controls.Add(secondaryAction);

            restartPanel = CreateCard();
            restartPanel.FillColor = Color.FromArgb(245, 249, 254);
            restartPanel.BorderColor = Color.FromArgb(210, 226, 244);
            restartPanel.Visible = false;
            root.Controls.Add(restartPanel, 0, 8);

            TableLayoutPanel restartLayout = new TableLayoutPanel();
            restartLayout.Dock = DockStyle.Fill;
            restartLayout.Margin = Padding.Empty;
            restartLayout.Padding = new Padding(16, 9, 12, 8);
            restartLayout.ColumnCount = 2;
            restartLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            restartLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 236F));
            restartPanel.Controls.Add(restartLayout);

            restartText = new Label();
            restartText.Text =
                "Restart Codex before creating a new task so it loads the updated connection.";
            restartText.ForeColor = Color.FromArgb(32, 31, 30);
            restartText.Dock = DockStyle.Fill;
            restartText.TextAlign = ContentAlignment.MiddleLeft;
            restartText.Margin = Padding.Empty;
            restartLayout.Controls.Add(restartText, 0, 0);

            FlowLayoutPanel restartActions = new FlowLayoutPanel();
            restartActions.Dock = DockStyle.Fill;
            restartActions.FlowDirection = FlowDirection.RightToLeft;
            restartActions.WrapContents = false;
            restartActions.Margin = Padding.Empty;
            restartLayout.Controls.Add(restartActions, 1, 0);

            Button dismissRestart = CreateActionButton("Dismiss");
            dismissRestart.Click += delegate
            {
                restartPanel.Visible = false;
                restartRowStyle.Height = 0F;
            };
            restartActions.Controls.Add(dismissRestart);

            Button copyRestart = CreateActionButton("Copy steps");
            copyRestart.Click += CopyRestartStepsClicked;
            restartActions.Controls.Add(copyRestart);

            FlowLayoutPanel footer = new FlowLayoutPanel();
            footer.Dock = DockStyle.Bottom;
            footer.FlowDirection = FlowDirection.RightToLeft;
            footer.WrapContents = false;
            footer.AutoSize = true;
            footer.Margin = Padding.Empty;
            root.Controls.Add(footer, 0, 9);

            refreshButton = CreateActionButton("Refresh");
            refreshButton.Click += RefreshClicked;
            footer.Controls.Add(refreshButton);

            copyDiagnosticsButton = CreateActionButton("Copy diagnostics");
            copyDiagnosticsButton.Click += CopyDiagnosticsClicked;
            footer.Controls.Add(copyDiagnosticsButton);

            openLogButton = CreateActionButton("Open log");
            openLogButton.Click += OpenLogClicked;
            footer.Controls.Add(openLogButton);

            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = 5000;
            refreshTimer.Tick += RefreshTimerTick;

            Shown += FormShown;
            FormClosing += MainFormClosing;
            Activated += delegate
            {
                if (!busy)
                {
                    refreshTimer.Start();
                }
            };
            Deactivate += delegate { refreshTimer.Stop(); };
        }

        private static RoundedPanel CreateCard()
        {
            RoundedPanel panel = new RoundedPanel();
            panel.Dock = DockStyle.Fill;
            panel.Margin = Padding.Empty;
            panel.FillColor = Color.White;
            panel.BorderColor = Color.FromArgb(209, 213, 219);
            panel.CornerRadius = 8;
            return panel;
        }

        private static RadioButton CreateModeOption(string title, string description)
        {
            RadioButton option = new RadioButton();
            option.Appearance = Appearance.Button;
            option.FlatStyle = FlatStyle.Flat;
            option.FlatAppearance.BorderSize = 1;
            option.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
            option.FlatAppearance.CheckedBackColor = Color.FromArgb(235, 245, 255);
            option.BackColor = Color.White;
            option.ForeColor = Color.FromArgb(32, 31, 30);
            option.Font = new Font("Segoe UI Semibold", 11);
            option.Text = title + Environment.NewLine + description;
            option.TextAlign = ContentAlignment.MiddleLeft;
            option.Padding = new Padding(12, 4, 10, 4);
            option.Dock = DockStyle.Fill;
            option.Margin = new Padding(0, 0, 10, 0);
            option.AccessibleName = title;
            option.AccessibleDescription = description;
            return option;
        }

        private static Label AddDetailRow(
            TableLayoutPanel table,
            int row,
            string name
        )
        {
            Label nameLabel = new Label();
            nameLabel.Text = name;
            nameLabel.ForeColor = Color.FromArgb(96, 94, 92);
            nameLabel.Dock = DockStyle.Fill;
            nameLabel.TextAlign = ContentAlignment.MiddleLeft;
            nameLabel.Margin = Padding.Empty;
            table.Controls.Add(nameLabel, 0, row);

            Label valueLabel = new Label();
            valueLabel.Text = "Checking...";
            valueLabel.ForeColor = Color.FromArgb(32, 31, 30);
            valueLabel.Font = new Font("Segoe UI Semibold", 10);
            valueLabel.Dock = DockStyle.Fill;
            valueLabel.TextAlign = ContentAlignment.MiddleLeft;
            valueLabel.AutoEllipsis = true;
            valueLabel.Margin = Padding.Empty;
            table.Controls.Add(valueLabel, 1, row);
            return valueLabel;
        }

        private static Button CreateActionButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.MinimumSize = new Size(88, 32);
            button.FlatStyle = FlatStyle.System;
            button.Margin = new Padding(8, 0, 0, 0);
            return button;
        }

        private async void FormShown(object sender, EventArgs e)
        {
            await RefreshStatusAsync(true);
            refreshTimer.Start();
        }

        private async void RefreshClicked(object sender, EventArgs e)
        {
            await RefreshStatusAsync(true);
        }

        private async void RefreshTimerTick(object sender, EventArgs e)
        {
            if (!busy)
            {
                await RefreshStatusAsync(false);
            }
        }

        private async void ModeCheckedChanged(object sender, EventArgs e)
        {
            RadioButton selected = sender as RadioButton;
            if (selected == null || !selected.Checked || suppressModeEvent || busy || refreshing)
            {
                return;
            }

            bool targetRouter = Object.ReferenceEquals(selected, routerMode);
            if (lastStatus != null && lastStatus.ConfigOn == targetRouter)
            {
                return;
            }

            await ChangeModeAsync(targetRouter);
        }

        private async Task ChangeModeAsync(bool targetRouter)
        {
            SetBusy(
                true,
                targetRouter
                    ? "Starting Local Router and updating the Codex configuration..."
                    : "Restoring Native Codex and stopping the managed Router..."
            );

            OperationResult result = null;
            Exception failure = null;
            try
            {
                result = await Task.Run(
                    delegate
                    {
                        return targetRouter
                            ? controller.EnableVisibleRouter()
                            : controller.DisableKeepSettings();
                    }
                );
            }
            catch (Exception error)
            {
                failure = error;
            }

            SetBusy(false, null);
            await RefreshStatusAsync(false);

            if (failure != null)
            {
                ShowError(failure.Message);
                return;
            }

            restartRowStyle.Height = 70F;
            restartPanel.Visible = true;
            if (result != null && result.Warnings.Count > 0)
            {
                ShowWarning("Connection changed with warnings", result.Message);
            }
        }

        private async Task RetryRouterAsync()
        {
            await ChangeModeAsync(true);
        }

        private async Task RestoreNativeAsync()
        {
            await ChangeModeAsync(false);
        }

        private async Task RefreshStatusAsync(bool showProgress)
        {
            if (busy || refreshing)
            {
                return;
            }

            refreshing = true;
            if (showProgress)
            {
                SetBusy(true, "Checking Codex and Router state...");
            }
            else
            {
                refreshButton.Enabled = false;
            }
            try
            {
                SwitchStatus status = await Task.Run(
                    delegate { return controller.GetStatus(); }
                );
                lastStatus = status;
                ApplyStatus(status);
            }
            catch (Exception error)
            {
                ShowError(error.Message);
                connectionValue.Text = "Unavailable";
                processValue.Text = "Unavailable";
                modelValue.Text = "Unavailable";
                providerValue.Text = "Unavailable";
                checkedValue.Text = DateTime.Now.ToString(
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.CurrentCulture
                );
            }
            finally
            {
                if (showProgress)
                {
                    SetBusy(false, null);
                }
                else
                {
                    refreshButton.Enabled = true;
                }
                refreshing = false;
            }
        }

        private void ApplyStatus(SwitchStatus status)
        {
            suppressModeEvent = true;
            try
            {
                nativeMode.Checked = !status.ConfigOn;
                routerMode.Checked = status.ConfigOn;
            }
            finally
            {
                suppressModeEvent = false;
            }

            connectionValue.Text = status.ConfigOn
                ? "Local Router"
                : "Native Codex";
            processValue.Text = status.Healthy
                ? "Healthy · 127.0.0.1:4102"
                : "Stopped or unavailable";
            modelValue.Text = FriendlyValue(status.Model, "Not reported");
            providerValue.Text = FriendlyValue(
                status.ModelProvider,
                status.ConfigOn ? "Not reported" : "Native OpenAI"
            );
            checkedValue.Text = DateTime.Now.ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.CurrentCulture
            );
            openLogButton.Enabled = File.Exists(controller.Paths.RouterLog);

            if (status.State == "On")
            {
                ShowHealthy(
                    "Local Router is ready",
                    "Codex is configured for the Router and the health endpoint is responding."
                );
            }
            else if (status.State == "Degraded")
            {
                ShowWarning(
                    "Router configuration is active, but the process is unavailable",
                    "Retry the managed Router or restore Native Codex before starting a new task."
                );
                ConfigureActions(
                    "Retry Router",
                    delegate { RetryRouterAction(); },
                    "Restore Native",
                    delegate { RestoreNativeAction(); }
                );
            }
            else if (status.State == "Orphaned")
            {
                ShowWarning(
                    "An untracked Router process is still running",
                    "Native Codex is active. This app will not terminate an unknown process on port 4102."
                );
                ConfigureActions(
                    "Refresh",
                    delegate { RefreshStatusAction(); },
                    "Task Manager",
                    delegate { OpenTaskManager(); }
                );
            }
            else
            {
                ShowNeutral(
                    "Native Codex is active",
                    "Router credentials, provider choices, logs, and cached settings are preserved."
                );
            }
        }

        private static string FriendlyValue(string value, string fallback)
        {
            return String.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private void ShowHealthy(string headline, string detail)
        {
            statusPanel.FillColor = Color.FromArgb(241, 250, 241);
            statusPanel.BorderColor = Color.FromArgb(190, 225, 190);
            statusHeadline.ForeColor = Color.FromArgb(16, 100, 16);
            statusHeadline.Text = headline;
            statusDetail.Text = detail;
            ClearActions();
        }

        private void ShowNeutral(string headline, string detail)
        {
            statusPanel.FillColor = Color.FromArgb(245, 249, 254);
            statusPanel.BorderColor = Color.FromArgb(210, 226, 244);
            statusHeadline.ForeColor = Color.FromArgb(32, 31, 30);
            statusHeadline.Text = headline;
            statusDetail.Text = detail;
            ClearActions();
        }

        private void ShowWarning(string headline, string detail)
        {
            statusPanel.FillColor = Color.FromArgb(255, 248, 235);
            statusPanel.BorderColor = Color.FromArgb(238, 207, 151);
            statusHeadline.ForeColor = Color.FromArgb(138, 60, 0);
            statusHeadline.Text = headline;
            statusDetail.Text = detail;
            ClearActions();
        }

        private void ShowError(string detail)
        {
            statusPanel.FillColor = Color.FromArgb(255, 243, 242);
            statusPanel.BorderColor = Color.FromArgb(235, 184, 181);
            statusHeadline.ForeColor = Color.FromArgb(196, 43, 28);
            statusHeadline.Text = "Status unavailable";
            statusDetail.Text = detail;
            ConfigureActions(
                "Retry status",
                delegate { RefreshStatusAction(); },
                "Copy error",
                delegate { CopyText(detail); }
            );
        }

        private void ConfigureActions(
            string primaryText,
            Action primaryHandler,
            string secondaryText,
            Action secondaryHandler
        )
        {
            primaryAction.Text = primaryText;
            primaryActionHandler = primaryHandler;
            primaryAction.Visible = true;

            secondaryAction.Text = secondaryText;
            secondaryActionHandler = secondaryHandler;
            secondaryAction.Visible = true;
        }

        private void ClearActions()
        {
            primaryActionHandler = null;
            secondaryActionHandler = null;
            primaryAction.Visible = false;
            secondaryAction.Visible = false;
        }

        private void PrimaryActionClicked(object sender, EventArgs e)
        {
            if (primaryActionHandler != null)
            {
                primaryActionHandler();
            }
        }

        private void SecondaryActionClicked(object sender, EventArgs e)
        {
            if (secondaryActionHandler != null)
            {
                secondaryActionHandler();
            }
        }

        private async void RetryRouterAction()
        {
            await RetryRouterAsync();
        }

        private async void RestoreNativeAction()
        {
            await RestoreNativeAsync();
        }

        private async void RefreshStatusAction()
        {
            await RefreshStatusAsync(true);
        }

        private static void OpenTaskManager()
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "taskmgr.exe",
                    UseShellExecute = true
                }
            );
        }

        private void OpenLogClicked(object sender, EventArgs e)
        {
            string path = controller.Paths.RouterLog;
            if (!File.Exists(path))
            {
                ShowWarning(
                    "Router log was not found",
                    "No router.log file currently exists in the managed state directory."
                );
                return;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "notepad.exe";
            startInfo.Arguments = "\"" + path.Replace("\"", "\"\"") + "\"";
            startInfo.UseShellExecute = true;
            Process.Start(startInfo);
        }

        private void CopyDiagnosticsClicked(object sender, EventArgs e)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Codex Router Switch diagnostics");
            builder.AppendLine(
                "Timestamp: " + DateTime.Now.ToString("o", CultureInfo.InvariantCulture)
            );
            builder.AppendLine(
                "State: " + (lastStatus == null ? "unavailable" : lastStatus.State)
            );
            builder.AppendLine(
                "Connection: " +
                (lastStatus == null
                    ? "unavailable"
                    : (lastStatus.ConfigOn ? "Local Router" : "Native Codex"))
            );
            builder.AppendLine(
                "Router healthy: " +
                (lastStatus == null
                    ? "unavailable"
                    : lastStatus.Healthy.ToString(CultureInfo.InvariantCulture))
            );
            builder.AppendLine(
                "Model: " +
                (lastStatus == null
                    ? "unavailable"
                    : FriendlyValue(lastStatus.Model, "not reported"))
            );
            builder.AppendLine(
                "Provider: " +
                (lastStatus == null
                    ? "unavailable"
                    : FriendlyValue(lastStatus.ModelProvider, "not reported"))
            );
            builder.AppendLine(
                "Router root: " + RedactUserPath(controller.Paths.RouterRoot)
            );
            builder.AppendLine(
                "Codex home: " + RedactUserPath(controller.Paths.CodexHome)
            );
            builder.AppendLine(
                "Router log: " + RedactUserPath(controller.Paths.RouterLog)
            );
            builder.AppendLine(
                "Secrets, API keys, OAuth tokens, and managed capability URLs are not included."
            );
            CopyText(builder.ToString());
            ShowNeutral(
                "Diagnostics copied",
                "The report omits credentials and replaces the user profile path."
            );
        }

        private static string RedactUserPath(string path)
        {
            string userProfile = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile
            );
            if (String.IsNullOrEmpty(path) || String.IsNullOrEmpty(userProfile))
            {
                return path;
            }

            if (path.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase))
            {
                return "%USERPROFILE%" + path.Substring(userProfile.Length);
            }
            return path;
        }

        private void CopyRestartStepsClicked(object sender, EventArgs e)
        {
            CopyText(
                "1. Fully quit Codex.\r\n" +
                "2. Reopen Codex.\r\n" +
                "3. Create a new task before checking the model picker."
            );
            restartText.Text = "Restart steps copied to the clipboard.";
        }

        private static void CopyText(string text)
        {
            try
            {
                Clipboard.SetText(text ?? "");
            }
            catch (Exception error)
            {
                MessageBox.Show(
                    error.Message,
                    "Could not copy to clipboard",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }

        private void SetBusy(bool value, string message)
        {
            busy = value;
            nativeMode.Enabled = !value;
            routerMode.Enabled = !value;
            refreshButton.Enabled = !value;
            openLogButton.Enabled = !value && File.Exists(controller.Paths.RouterLog);
            copyDiagnosticsButton.Enabled = !value;
            primaryAction.Enabled = !value;
            secondaryAction.Enabled = !value;
            busyLine.Active = value;
            UseWaitCursor = value;

            if (value && !String.IsNullOrWhiteSpace(message))
            {
                statusPanel.FillColor = Color.FromArgb(245, 249, 254);
                statusPanel.BorderColor = Color.FromArgb(210, 226, 244);
                statusHeadline.ForeColor = Color.FromArgb(0, 95, 184);
                statusHeadline.Text = "Working";
                statusDetail.Text = message;
                ClearActions();
            }
        }

        private void MainFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!busy)
            {
                refreshTimer.Stop();
                return;
            }

            e.Cancel = true;
            MessageBox.Show(
                this,
                "Wait for the current connection operation to finish.",
                "Codex Router Switch",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (refreshTimer != null)
                {
                    refreshTimer.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }

    internal static class EnhancedProgram
    {
        private static Mutex appMutex;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (HasArgument(args, "--gui-self-test-file"))
                {
                    string resultFile = FindResultFile(args, "--gui-self-test-file");
                    RouterController testController = new RouterController();
                    using (EnhancedMainForm form = new EnhancedMainForm(testController))
                    {
                        Dictionary<string, object> values =
                            new Dictionary<string, object>();
                        values["ok"] = true;
                        values["formTitle"] = form.Text;
                        values["controls"] = form.Controls.Count;
                        values["windowDisplayed"] = false;
                        values["mutationsPerformed"] = false;
                        values["enhancedUi"] = true;
                        WriteJsonResult(resultFile, values);
                    }
                    return 0;
                }

                if (args != null && args.Length > 0)
                {
                    return InvokeLegacyCommandLine(args);
                }

                string sid = WindowsIdentity.GetCurrent().User.Value;
                bool createdNew;
                appMutex = new Mutex(
                    true,
                    "Local\\CodexRouterSwitch_" + sid,
                    out createdNew
                );
                if (!createdNew)
                {
                    MessageBox.Show(
                        "Codex Router Switch is already running.",
                        "Codex Router Switch",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                    return 2;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                RouterController controller = new RouterController();
                Application.Run(new EnhancedMainForm(controller));
                return 0;
            }
            catch (Exception error)
            {
                MessageBox.Show(
                    error.Message,
                    "Codex Router Switch failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return 1;
            }
            finally
            {
                if (appMutex != null)
                {
                    try
                    {
                        appMutex.ReleaseMutex();
                    }
                    catch
                    {
                    }
                    appMutex.Dispose();
                    appMutex = null;
                }
            }
        }

        private static int InvokeLegacyCommandLine(string[] args)
        {
            MethodInfo main = typeof(Program).GetMethod(
                "Main",
                BindingFlags.NonPublic | BindingFlags.Static
            );
            if (main == null)
            {
                throw new InvalidOperationException(
                    "The legacy command-line entry point was not found."
                );
            }

            try
            {
                object result = main.Invoke(null, new object[] { args });
                return Convert.ToInt32(result, CultureInfo.InvariantCulture);
            }
            catch (TargetInvocationException error)
            {
                if (error.InnerException != null)
                {
                    throw error.InnerException;
                }
                throw;
            }
        }

        private static bool HasArgument(string[] args, string name)
        {
            if (args == null)
            {
                return false;
            }
            foreach (string argument in args)
            {
                if (String.Equals(argument, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string FindResultFile(string[] args, string name)
        {
            for (int index = 0; index < args.Length - 1; index++)
            {
                if (String.Equals(
                    args[index],
                    name,
                    StringComparison.OrdinalIgnoreCase
                ))
                {
                    return Path.GetFullPath(args[index + 1]);
                }
            }
            throw new ArgumentException("A result-file path is required.");
        }

        private static void WriteJsonResult(
            string path,
            Dictionary<string, object> values
        )
        {
            string directory = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            JavaScriptSerializer json = new JavaScriptSerializer();
            File.WriteAllText(path, json.Serialize(values), new UTF8Encoding(false));
        }
    }
}
