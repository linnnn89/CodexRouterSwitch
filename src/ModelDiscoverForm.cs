using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexRouterSwitch
{
    // 联网发现结果窗口：按供应商分组列出可加入的新模型，勾选后一键加入。
    internal sealed class ModelDiscoverForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr hWnd,
            int message,
            IntPtr wParam,
            IntPtr lParam
        );

        private readonly ModelPanelService service;
        private readonly PanelDiscoverResult discoverResult;
        private readonly FlowLayoutPanel listHost;
        private readonly Label statusLabel;
        private readonly ModernButton addButton;
        private readonly List<CheckBox> modelChecks = new List<CheckBox>();
        private readonly Dictionary<CheckBox, string> providerByCheck =
            new Dictionary<CheckBox, string>();
        private readonly Dictionary<CheckBox, DiscoveredModel> modelByCheck =
            new Dictionary<CheckBox, DiscoveredModel>();
        private bool busy;

        public ModelDiscoverForm(ModelPanelService service, PanelDiscoverResult discoverResult)
        {
            this.service = service;
            this.discoverResult = discoverResult;

            Text = "检查新模型";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(780, 640);
            MinimumSize = new Size(640, 480);
            FormBorderStyle = FormBorderStyle.None;
            BackColor = ModernUi.Canvas;
            KeyPreview = true;
            DoubleBuffered = true;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            Controls.Add(root);

            // 标题栏
            Panel titleBar = new Panel();
            titleBar.Dock = DockStyle.Fill;
            titleBar.Margin = Padding.Empty;
            titleBar.BackColor = ModernUi.TitleBar;
            titleBar.MouseDown += TitleBarMouseDown;
            root.Controls.Add(titleBar, 0, 0);

            Label titleLabel = new Label();
            titleLabel.Text = "检查新模型";
            titleLabel.Font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold);
            titleLabel.ForeColor = ModernUi.Text;
            titleLabel.AutoSize = true;
            titleLabel.Location = new Point(20, 14);
            titleLabel.MouseDown += TitleBarMouseDown;
            titleBar.Controls.Add(titleLabel);

            ModernButton closeTitle = new ModernButton();
            closeTitle.Kind = ModernButtonKind.Ghost;
            closeTitle.IconGlyph = "\uE8BB";
            closeTitle.Size = new Size(40, 34);
            closeTitle.Location = new Point(ClientSize.Width - 52, 9);
            closeTitle.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            closeTitle.CornerRadius = 8;
            closeTitle.AccessibleName = "关闭";
            closeTitle.Click += delegate { Close(); };
            titleBar.Controls.Add(closeTitle);

            SeparatorControl titleDivider = new SeparatorControl();
            titleDivider.Dock = DockStyle.Fill;
            titleDivider.Margin = Padding.Empty;
            root.Controls.Add(titleDivider, 0, 1);

            // 说明
            Label hint = new Label();
            hint.Dock = DockStyle.Fill;
            hint.Margin = Padding.Empty;
            hint.Padding = new Padding(20, 8, 20, 0);
            hint.Text =
                "以下是各供应商在线目录中尚未加入 Codex Router 的模型。勾选所需模型，点击“加入选中”写入配置并重新发布。";
            hint.ForeColor = ModernUi.MutedText;
            hint.BackColor = ModernUi.Navigation;
            hint.AutoEllipsis = true;
            root.Controls.Add(hint, 0, 2);

            // 列表
            listHost = new FlowLayoutPanel();
            listHost.Dock = DockStyle.Fill;
            listHost.Margin = Padding.Empty;
            listHost.Padding = new Padding(18, 12, 18, 12);
            listHost.FlowDirection = FlowDirection.TopDown;
            listHost.WrapContents = false;
            listHost.AutoScroll = true;
            listHost.BackColor = ModernUi.Canvas;
            root.Controls.Add(listHost, 0, 3);

            // 底栏
            Panel footer = new Panel();
            footer.Dock = DockStyle.Fill;
            footer.Margin = Padding.Empty;
            footer.BackColor = ModernUi.Footer;
            root.Controls.Add(footer, 0, 4);

            statusLabel = new Label();
            statusLabel.AutoSize = false;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.ForeColor = ModernUi.MutedText;
            statusLabel.BackColor = ModernUi.Footer;
            statusLabel.Location = new Point(20, 12);
            statusLabel.Size = new Size(ClientSize.Width - 380, 42);
            statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            footer.Controls.Add(statusLabel);

            addButton = new ModernButton();
            addButton.Kind = ModernButtonKind.Primary;
            addButton.Text = "加入选中";
            addButton.IconGlyph = "\uE710";
            addButton.Size = new Size(132, 42);
            addButton.Location = new Point(ClientSize.Width - 292, 12);
            addButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            addButton.Click += AddClicked;
            footer.Controls.Add(addButton);

            ModernButton closeButton = new ModernButton();
            closeButton.Kind = ModernButtonKind.Secondary;
            closeButton.Text = "关闭";
            closeButton.IconGlyph = "\uE711";
            closeButton.Size = new Size(96, 42);
            closeButton.Location = new Point(ClientSize.Width - 152, 12);
            closeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            closeButton.Click += delegate { Close(); };
            footer.Controls.Add(closeButton);

            BuildList();
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
            };
        }

        private void TitleBarMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
        }

        private void BuildList()
        {
            listHost.SuspendLayout();
            int width = Math.Max(420, listHost.ClientSize.Width - 60);
            int addableTotal = 0;

            foreach (DiscoveredProvider provider in discoverResult.Providers)
            {
                Label providerLabel = new Label();
                providerLabel.AutoSize = false;
                providerLabel.Width = width;
                providerLabel.Height = 30;
                providerLabel.Margin = new Padding(0, 14, 0, 2);
                providerLabel.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
                providerLabel.ForeColor = ModernUi.Text;
                providerLabel.Text = provider.Name + "（" + provider.Id + "）";
                listHost.Controls.Add(providerLabel);

                if (!String.IsNullOrEmpty(provider.Error))
                {
                    Label errorLabel = new Label();
                    errorLabel.AutoSize = false;
                    errorLabel.Width = width;
                    errorLabel.Height = 26;
                    errorLabel.Margin = new Padding(2, 0, 0, 4);
                    errorLabel.ForeColor = ModernUi.Error;
                    errorLabel.Text = "在线目录获取失败：" + provider.Error;
                    listHost.Controls.Add(errorLabel);
                }

                if (provider.Addable.Count == 0)
                {
                    Label emptyLabel = new Label();
                    emptyLabel.AutoSize = false;
                    emptyLabel.Width = width;
                    emptyLabel.Height = 26;
                    emptyLabel.Margin = new Padding(2, 0, 0, 4);
                    emptyLabel.ForeColor = ModernUi.MutedText;
                    string blockedNote = provider.BlockedTotal > 0
                        ? "（" + provider.BlockedTotal + " 个模型因上游线路协议尚未验证，暂不可加入）"
                        : "";
                    emptyLabel.Text = "没有可加入的新模型" + blockedNote;
                    listHost.Controls.Add(emptyLabel);
                    continue;
                }

                foreach (DiscoveredModel model in provider.Addable)
                {
                    CheckBox check = new CheckBox();
                    check.AutoSize = false;
                    check.Width = width;
                    check.Height = 28;
                    check.Margin = new Padding(8, 0, 0, 2);
                    check.Text = model.Id + "    " + model.Name;
                    check.ForeColor = ModernUi.Text;
                    check.BackColor = ModernUi.Canvas;
                    check.UseVisualStyleBackColor = false;
                    listHost.Controls.Add(check);
                    modelChecks.Add(check);
                    providerByCheck[check] = provider.Id;
                    modelByCheck[check] = model;
                    addableTotal++;
                }

                if (provider.BlockedTotal > 0)
                {
                    Label blockedLabel = new Label();
                    blockedLabel.AutoSize = false;
                    blockedLabel.Width = width;
                    blockedLabel.Height = 24;
                    blockedLabel.Margin = new Padding(8, 2, 0, 4);
                    blockedLabel.ForeColor = ModernUi.MutedText;
                    blockedLabel.Text = "另有 " + provider.BlockedTotal
                        + " 个模型因上游线路协议尚未验证暂不可加入。";
                    listHost.Controls.Add(blockedLabel);
                }
            }

            listHost.ResumeLayout();
            statusLabel.Text = addableTotal > 0
                ? "共发现 " + addableTotal + " 个可加入的新模型，勾选后点击“加入选中”。"
                : "未发现可加入的新模型。";
            addButton.Enabled = addableTotal > 0 && !busy;
        }

        private void AddClicked(object sender, EventArgs e)
        {
            if (busy) return;

            Dictionary<string, List<DiscoveredModel>> byProvider =
                new Dictionary<string, List<DiscoveredModel>>(StringComparer.Ordinal);
            foreach (CheckBox check in modelChecks)
            {
                if (!check.Checked) continue;
                string providerId = providerByCheck[check];
                List<DiscoveredModel> list;
                if (!byProvider.TryGetValue(providerId, out list))
                {
                    list = new List<DiscoveredModel>();
                    byProvider[providerId] = list;
                }
                list.Add(modelByCheck[check]);
            }

            if (byProvider.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "请先勾选要加入的模型。",
                    "检查新模型",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            AddSelectedAsync(byProvider);
        }

        private async void AddSelectedAsync(Dictionary<string, List<DiscoveredModel>> byProvider)
        {
            SetBusy(true, "正在加入并重新发布模型目录，请稍候（可能需要 10 秒以上）...");
            int added = 0;
            List<string> notices = new List<string>();
            try
            {
                foreach (KeyValuePair<string, List<DiscoveredModel>> pair in byProvider)
                {
                    string providerId = pair.Key;
                    List<DiscoveredModel> models = pair.Value;
                    PanelAddResult result = await Task.Run(
                        delegate { return service.Add(providerId, models); }
                    );
                    added += result.Added;
                    notices.AddRange(result.Rejections);
                    statusLabel.Text = "已处理 " + providerId + "：" + result.Added + " 个模型已加入。";
                }

                string message = "已加入 " + added + " 个模型。";
                if (notices.Count > 0)
                {
                    message += "\r\n\r\n以下模型被跳过：\r\n" + String.Join("\r\n", notices.ToArray());
                }
                message += "\r\n\r\n请完全退出并重新打开 Codex 客户端后生效。";
                MessageBox.Show(this, message, "检查新模型", MessageBoxButtons.OK, MessageBoxIcon.Information);

                if (added > 0)
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    "加入模型失败：" + exception.Message,
                    "检查新模型",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            finally
            {
                SetBusy(false, "");
            }
        }

        private void SetBusy(bool value, string status)
        {
            busy = value;
            addButton.Enabled = !value;
            Cursor = value ? Cursors.WaitCursor : Cursors.Default;
            statusLabel.Text = status ?? statusLabel.Text;
        }
    }
}
