using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexRouterSwitch
{
    // ------------------------------------------------------------------
    // 模型管理窗口：供应商 → 模型公司 → 模型 三层抽屉（可展开/折叠），
    // 每层使用勾选框；父层勾选联动全部子项，子项部分选中时父层显示半选态。
    // 更改在点击「应用更改」后统一提交（一次发布，避免逐条等待）。
    // ------------------------------------------------------------------

    internal enum TreeRowKind
    {
        Provider,
        Company,
        Model,
    }

    internal sealed class TreeCheckRow : Control
    {
        private static readonly Font TitleFont = new Font("Microsoft YaHei UI", 9.75F);
        private static readonly Font DetailFont = new Font("Microsoft YaHei UI", 8.5F);

        public TreeRowKind Kind;
        public string RowKey;
        public string Slug;
        public string ProviderId;
        public string CompanyId;
        public string Title;
        public string Detail;
        public bool Selectable = true;
        public bool Checked;
        public bool Partial;
        public new bool HasChildren;
        public bool Expanded;
        public int IndentLevel;
        private bool hovered;

        public event EventHandler CheckToggled;
        public event EventHandler ExpandToggled;

        public int RowHeight
        {
            get
            {
                if (Kind == TreeRowKind.Provider)
                {
                    return 42;
                }
                return Kind == TreeRowKind.Company ? 36 : 32;
            }
        }

        public TreeCheckRow()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true
            );
            BackColor = Color.White;
            Margin = Padding.Empty;
            TabStop = false;
        }

        public void SetHovered(bool value)
        {
            if (hovered == value)
            {
                return;
            }
            hovered = value;
            Cursor = HasChildren || Selectable ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color background = hovered ? ModernUi.PrimarySoft : Color.White;
            using (SolidBrush fill = new SolidBrush(background))
            {
                graphics.FillRectangle(fill, ClientRectangle);
            }
            if (Kind != TreeRowKind.Model)
            {
                using (Pen divider = new Pen(ModernUi.Divider))
                {
                    graphics.DrawLine(divider, 0, Height - 1, Width, Height - 1);
                }
            }

            int indent = 14 + IndentLevel * 22;
            int centerY = Height / 2;

            // 展开箭头（有子项时）
            if (HasChildren)
            {
                PointF[] triangle = Expanded
                    ? new PointF[]
                    {
                        new PointF(indent, centerY - 2F),
                        new PointF(indent + 9F, centerY - 2F),
                        new PointF(indent + 4.5F, centerY + 4F),
                    }
                    : new PointF[]
                    {
                        new PointF(indent + 1F, centerY - 5F),
                        new PointF(indent + 1F, centerY + 4F),
                        new PointF(indent + 7F, centerY - 0.5F),
                    };
                using (SolidBrush arrow = new SolidBrush(ModernUi.MutedText))
                {
                    graphics.FillPolygon(arrow, triangle);
                }
            }

            int boxX = indent + 20;
            int boxY = centerY - 8;
            Rectangle box = new Rectangle(boxX, boxY, 16, 16);
            DrawCheckBox(graphics, box);

            int textX = boxX + 26;
            Color titleColor = Selectable ? ModernUi.Text : ModernUi.MutedText;
            Font titleFont =
                Kind == TreeRowKind.Provider
                    ? new Font(TitleFont, FontStyle.Bold)
                    : TitleFont;
            Size titleSize = TextRenderer.MeasureText(graphics, Title, titleFont);
            TextRenderer.DrawText(
                graphics,
                Title,
                titleFont,
                new Point(textX, centerY - titleSize.Height / 2),
                titleColor
            );

            if (!String.IsNullOrEmpty(Detail))
            {
                Size detailSize = TextRenderer.MeasureText(graphics, Detail, DetailFont);
                int detailX = Math.Min(
                    Math.Max(textX + titleSize.Width + 10, Width - detailSize.Width - 16),
                    Width - detailSize.Width - 10
                );
                TextRenderer.DrawText(
                    graphics,
                    Detail,
                    DetailFont,
                    new Point(Math.Max(textX + titleSize.Width + 6, detailX), centerY - detailSize.Height / 2),
                    ModernUi.MutedText
                );
            }

            if (Kind == TreeRowKind.Provider)
            {
                using (Font small = new Font("Microsoft YaHei UI", 8.5F))
                using (SolidBrush caption = new SolidBrush(ModernUi.MutedText))
                {
                    // 在标题上方不再绘制；保持简洁。
                }
            }
        }

        private void DrawCheckBox(Graphics graphics, Rectangle box)
        {
            int radius = 4;
            using (GraphicsPath path = ModernUi.CreateRoundedRectangle(box, radius))
            {
                if (!Selectable)
                {
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(242, 244, 248)))
                    using (Pen border = new Pen(ModernUi.Divider))
                    {
                        graphics.FillPath(fill, path);
                        graphics.DrawPath(border, path);
                    }
                    if (Checked)
                    {
                        DrawCheckGlyph(graphics, box, Color.FromArgb(148, 163, 184));
                    }
                    return;
                }

                if (Checked || Partial)
                {
                    using (SolidBrush fill = new SolidBrush(ModernUi.Primary))
                    {
                        graphics.FillPath(fill, path);
                    }
                    if (Partial)
                    {
                        using (Pen mark = new Pen(Color.White, 2F))
                        {
                            graphics.DrawLine(
                                mark,
                                box.Left + 3,
                                box.Top + box.Height / 2,
                                box.Right - 3,
                                box.Top + box.Height / 2
                            );
                        }
                    }
                    else
                    {
                        DrawCheckGlyph(graphics, box, Color.White);
                    }
                }
                else
                {
                    using (SolidBrush fill = new SolidBrush(Color.White))
                    using (Pen border = new Pen(Color.FromArgb(176, 186, 198)))
                    {
                        graphics.FillPath(fill, path);
                        graphics.DrawPath(border, path);
                    }
                }
            }
        }

        private static void DrawCheckGlyph(Graphics graphics, Rectangle box, Color color)
        {
            using (Pen mark = new Pen(color, 2F))
            {
                mark.StartCap = LineCap.Round;
                mark.EndCap = LineCap.Round;
                graphics.DrawLines(
                    mark,
                    new Point[]
                    {
                        new Point(box.Left + 3, box.Top + 8),
                        new Point(box.Left + 7, box.Top + 12),
                        new Point(box.Left + 13, box.Top + 4),
                    }
                );
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            SetHovered(true);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SetHovered(false);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            int indent = 14 + IndentLevel * 22;
            int boxX = indent + 20;
            bool inCheckBox = e.X >= boxX - 6 && e.X <= boxX + 24;

            if (inCheckBox)
            {
                if (Selectable && CheckToggled != null)
                {
                    CheckToggled(this, EventArgs.Empty);
                }
                return;
            }

            if (Kind != TreeRowKind.Model && HasChildren)
            {
                if (ExpandToggled != null)
                {
                    ExpandToggled(this, EventArgs.Empty);
                }
                return;
            }

            if (Kind == TreeRowKind.Model && Selectable && CheckToggled != null)
            {
                CheckToggled(this, EventArgs.Empty);
            }
        }
    }

    internal sealed class ModelPanelForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr windowHandle,
            int message,
            IntPtr wParam,
            IntPtr lParam
        );

        private readonly ModelPanelService service;
        private PanelSnapshot snapshot;
        private readonly Dictionary<string, bool> selection =
            new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> initial =
            new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> expanded =
            new Dictionary<string, bool>(StringComparer.Ordinal);

        private readonly FlowLayoutPanel rowsHost;
        private readonly TextBox searchBox;
        private readonly Label statusLabel;
        private readonly Label summaryLabel;
        private readonly ModernButton applyButton;
        private readonly ModernButton refreshButton;
        private readonly ModernButton discoverButton;
        private readonly ModernButton expandAllButton;
        private readonly ModernButton collapseAllButton;
        private readonly System.Windows.Forms.Timer searchTimer;

        private bool busy;
        private bool loading;
        private string pendingFilter = "";

        public ModelPanelForm(RouterController controller)
        {
            service = new ModelPanelService(controller);

            Text = "Codex 模型管理";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(940, 700);
            MinimumSize = new Size(840, 560);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            Padding = new Padding(1);
            BackColor = ModernUi.WindowBorder;
            Font = new Font("Microsoft YaHei UI", 10F);
            DoubleBuffered = true;
            KeyPreview = true;
            AccessibleName = "Codex 模型管理";
            StartPosition = FormStartPosition.CenterScreen;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Margin = Padding.Empty;
            root.Padding = Padding.Empty;
            root.BackColor = ModernUi.Canvas;
            root.ColumnCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowCount = 5;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            Controls.Add(root);

            // 标题栏
            Panel titleBar = new Panel();
            titleBar.Dock = DockStyle.Fill;
            titleBar.Margin = Padding.Empty;
            titleBar.BackColor = ModernUi.TitleBar;
            root.Controls.Add(titleBar, 0, 0);
            titleBar.MouseDown += TitleBarMouseDown;

            Label title = new Label();
            title.Text = "模型管理";
            title.Font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold);
            title.ForeColor = ModernUi.Text;
            title.AutoSize = false;
            title.Location = new Point(20, 0);
            title.Size = new Size(220, 52);
            title.TextAlign = ContentAlignment.MiddleLeft;
            titleBar.Controls.Add(title);
            title.MouseDown += TitleBarMouseDown;

            summaryLabel = new Label();
            summaryLabel.Text = "正在读取模型目录...";
            summaryLabel.ForeColor = ModernUi.MutedText;
            summaryLabel.Font = new Font("Microsoft YaHei UI", 9F);
            summaryLabel.AutoSize = false;
            summaryLabel.Location = new Point(130, 0);
            summaryLabel.Size = new Size(560, 52);
            summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
            titleBar.Controls.Add(summaryLabel);

            FlowLayoutPanel titleActions = new FlowLayoutPanel();
            titleActions.Dock = DockStyle.Right;
            titleActions.Width = 54;
            titleActions.FlowDirection = FlowDirection.LeftToRight;
            titleActions.WrapContents = false;
            titleActions.Padding = new Padding(4, 8, 8, 8);
            titleActions.Margin = Padding.Empty;
            titleActions.BackColor = ModernUi.TitleBar;
            titleBar.Controls.Add(titleActions);

            ModernButton closeTitleButton = new ModernButton();
            closeTitleButton.Kind = ModernButtonKind.Ghost;
            closeTitleButton.IconGlyph = "\uE8BB";
            closeTitleButton.Size = new Size(40, 34);
            closeTitleButton.Margin = Padding.Empty;
            closeTitleButton.CornerRadius = 8;
            closeTitleButton.AccessibleName = "关闭";
            closeTitleButton.Click += delegate { Close(); };
            titleActions.Controls.Add(closeTitleButton);

            SeparatorControl titleDivider = new SeparatorControl();
            titleDivider.Dock = DockStyle.Fill;
            titleDivider.Margin = Padding.Empty;
            root.Controls.Add(titleDivider, 0, 1);

            // 工具条
            Panel toolbar = new Panel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.Margin = Padding.Empty;
            toolbar.BackColor = ModernUi.Navigation;
            root.Controls.Add(toolbar, 0, 2);

            RoundedPanel searchHost = new RoundedPanel();
            searchHost.Size = new Size(300, 34);
            searchHost.Location = new Point(20, 11);
            searchHost.CornerRadius = 8;
            searchHost.FillColor = Color.White;
            searchHost.BorderColor = ModernUi.Divider;
            toolbar.Controls.Add(searchHost);

            searchBox = new TextBox();
            searchBox.BorderStyle = BorderStyle.None;
            searchBox.Font = new Font("Microsoft YaHei UI", 9.5F);
            searchBox.Location = new Point(12, 8);
            searchBox.Size = new Size(276, 18);
            searchBox.AccessibleName = "搜索模型";
            searchHost.Controls.Add(searchBox);

            Label searchHint = new Label();
            searchHint.Text = "输入关键字过滤模型";
            searchHint.ForeColor = Color.FromArgb(160, 170, 184);
            searchHint.Font = new Font("Microsoft YaHei UI", 9.5F);
            searchHint.AutoSize = true;
            searchHint.BackColor = Color.White;
            searchHint.Location = new Point(13, 8);
            searchHost.Controls.Add(searchHint);
            searchHint.BringToFront();
            searchHint.Click += delegate { searchBox.Focus(); };
            searchBox.TextChanged += delegate
            {
                searchHint.Visible = searchBox.Text.Length == 0;
                searchTimer.Stop();
                searchTimer.Start();
            };
            searchBox.GotFocus += delegate { searchHint.Visible = false; };
            searchBox.LostFocus += delegate
            {
                searchHint.Visible = searchBox.Text.Length == 0;
            };

            searchTimer = new System.Windows.Forms.Timer();
            searchTimer.Interval = 250;
            searchTimer.Tick += delegate
            {
                searchTimer.Stop();
                string next = searchBox.Text.Trim();
                if (next != pendingFilter)
                {
                    pendingFilter = next;
                    RebuildRows();
                }
            };

            int actionLeft = 340;
            expandAllButton = CreateToolbarButton("展开全部", actionLeft, 100);
            expandAllButton.Click += delegate { SetAllGroupsExpanded(true); };
            toolbar.Controls.Add(expandAllButton);

            collapseAllButton = CreateToolbarButton("收起全部", actionLeft + 108, 100);
            collapseAllButton.Click += delegate { SetAllGroupsExpanded(false); };
            toolbar.Controls.Add(collapseAllButton);

            refreshButton = CreateToolbarButton("重新读取", actionLeft + 216, 108);
            refreshButton.Click += async delegate { await ReloadAsync(true); };
            toolbar.Controls.Add(refreshButton);

            // 树区域
            rowsHost = new FlowLayoutPanel();
            rowsHost.Dock = DockStyle.Fill;
            rowsHost.FlowDirection = FlowDirection.TopDown;
            rowsHost.WrapContents = false;
            rowsHost.AutoScroll = true;
            rowsHost.Padding = new Padding(0, 6, 0, 6);
            rowsHost.Margin = Padding.Empty;
            rowsHost.BackColor = Color.White;
            root.Controls.Add(rowsHost, 0, 3);
            rowsHost.Resize += delegate { ResizeRows(); };

            // 底部栏
            Panel footer = new Panel();
            footer.Dock = DockStyle.Fill;
            footer.Margin = Padding.Empty;
            footer.BackColor = ModernUi.Footer;
            root.Controls.Add(footer, 0, 4);

            SeparatorControl footerDivider = new SeparatorControl();
            footerDivider.Dock = DockStyle.Top;
            footerDivider.Height = 1;
            footer.Controls.Add(footerDivider);

            statusLabel = new Label();
            statusLabel.Text = "所有更改在点击「应用更改」后统一生效。";
            statusLabel.ForeColor = ModernUi.MutedText;
            statusLabel.Font = new Font("Microsoft YaHei UI", 9F);
            statusLabel.AutoSize = false;
            statusLabel.Location = new Point(20, 10);
            statusLabel.Size = new Size(420, 46);
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            footer.Controls.Add(statusLabel);

            FlowLayoutPanel footerActions = new FlowLayoutPanel();
            footerActions.Dock = DockStyle.Right;
            footerActions.Width = 512;
            footerActions.FlowDirection = FlowDirection.RightToLeft;
            footerActions.WrapContents = false;
            footerActions.Padding = new Padding(0, 12, 20, 10);
            footerActions.Margin = Padding.Empty;
            footerActions.BackColor = ModernUi.Footer;
            footer.Controls.Add(footerActions);

            ModernButton closeButton = CreateFooterButton("关闭", ModernButtonKind.Secondary, "\uE711", 92);
            closeButton.Click += delegate { Close(); };
            footerActions.Controls.Add(closeButton);

            applyButton = CreateFooterButton("应用更改", ModernButtonKind.Primary, "\uE73E", 138);
            applyButton.Click += async delegate { await ApplyAsync(); };
            footerActions.Controls.Add(applyButton);

            discoverButton = CreateFooterButton("检查新模型", ModernButtonKind.Secondary, "\uE774", 150);
            discoverButton.Click += DiscoverClicked;
            footerActions.Controls.Add(discoverButton);

            Shown += async delegate { await ReloadAsync(false); };
            KeyDown += delegate (object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    Close();
                }
            };
        }

        private ModernButton CreateToolbarButton(string text, int left, int width)
        {
            ModernButton button = new ModernButton();
            button.Kind = ModernButtonKind.Secondary;
            button.Text = text;
            button.Size = new Size(width, 34);
            button.Location = new Point(left, 11);
            button.Font = new Font("Microsoft YaHei UI", 9.5F);
            return button;
        }

        private static ModernButton CreateFooterButton(
            string text,
            ModernButtonKind kind,
            string glyph,
            int width
        )
        {
            ModernButton button = new ModernButton();
            button.Kind = kind;
            button.Text = text;
            button.IconGlyph = glyph;
            button.Size = new Size(width, 44);
            return button;
        }

        private void TitleBarMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.Clicks != 1)
            {
                return;
            }
            ReleaseCapture();
            SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
        }

        // 自检支持：等待首次异步加载完成（仅自检使用）。
        internal bool WaitForInitialLoad(int timeoutMilliseconds)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (loading && DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(50);
            }
            Application.DoEvents();
            return !loading && snapshot != null;
        }

        // 自检支持：统计当前渲染的树行数量。
        internal int CountRowsForSelfTest()
        {
            int count = 0;
            foreach (Control control in rowsHost.Controls)
            {
                if (control is TreeCheckRow) count++;
            }
            return count;
        }

        private async Task ReloadAsync(bool announce)
        {
            if (busy || loading)
            {
                return;
            }
            loading = true;
            statusLabel.Text = "正在读取模型目录...";
            try
            {
                PanelSnapshot next = await Task.Run(delegate { return service.Load(); });
                snapshot = next;
                selection.Clear();
                initial.Clear();
                expanded.Clear();
                foreach (ProviderGroup provider in snapshot.Providers)
                {
                    expanded["p:" + provider.Id] = true;
                    foreach (CompanyGroup company in provider.Companies)
                    {
                        expanded["c:" + provider.Id + ":" + company.Id] = false;
                        foreach (ModelEntry model in company.Models)
                        {
                            if (model.Toggleable)
                            {
                                selection[model.Slug] = model.Visible;
                                initial[model.Slug] = model.Visible;
                            }
                        }
                    }
                }
                RebuildRows();
                UpdateSummary();
                UpdateDirtyUI();
                string modeText = snapshot.CodexMode == "router"
                    ? "本地路由"
                    : snapshot.CodexMode == "native"
                        ? "原生 Codex"
                        : "未知模式";
                statusLabel.Text = "当前连接模式：" + modeText +
                    "。勾选状态在点击「应用更改」后统一提交并重新发布模型目录。";
                if (announce)
                {
                    statusLabel.Text = "模型目录已重新读取。" + statusLabel.Text;
                }
                foreach (string warning in snapshot.Warnings)
                {
                    statusLabel.Text = warning;
                }
            }
            catch (Exception error)
            {
                statusLabel.Text = "读取失败：" + error.Message;
                MessageBox.Show(
                    this,
                    error.Message,
                    "模型管理",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            finally
            {
                loading = false;
            }
        }

        private void UpdateSummary()
        {
            if (snapshot == null)
            {
                return;
            }
            summaryLabel.Text = String.Format(
                "共 {0} 个模型，已勾选 {1} 个（勾选 = 出现在 Codex 模型列表中）",
                snapshot.Total,
                snapshot.VisibleCount
            );
        }

        // ------------------------------------------------------------------
        // 行构建与刷新
        // ------------------------------------------------------------------

        private void RebuildRows()
        {
            if (snapshot == null)
            {
                return;
            }
            int scroll = rowsHost.VerticalScroll.Value;
            string filter = pendingFilter;
            string filterLower = filter.ToLowerInvariant();

            rowsHost.SuspendLayout();
            List<Control> toDispose = new List<Control>();
            foreach (Control control in rowsHost.Controls)
            {
                toDispose.Add(control);
            }
            rowsHost.Controls.Clear();
            foreach (Control control in toDispose)
            {
                control.Dispose();
            }

            int rowWidth = Math.Max(
                420,
                rowsHost.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4
            );

            foreach (ProviderGroup provider in snapshot.Providers)
            {
                List<ModelEntry> providerModels = CollectModels(provider);
                if (filterLower.Length > 0 && !GroupMatches(providerModels, filterLower))
                {
                    continue;
                }

                bool providerExpanded = filterLower.Length > 0 || IsGroupExpanded("p:" + provider.Id, true);
                TreeCheckRow providerRow = new TreeCheckRow();
                providerRow.Kind = TreeRowKind.Provider;
                providerRow.RowKey = "p:" + provider.Id;
                providerRow.ProviderId = provider.Id;
                providerRow.Title = provider.Name;
                providerRow.Detail = ProviderDetail(provider, providerModels);
                providerRow.IndentLevel = 0;
                providerRow.HasChildren = true;
                providerRow.Expanded = providerExpanded;
                ApplyGroupState(providerRow, providerModels);
                providerRow.AccessibleName = providerRow.Title + "，" + providerRow.Detail;
                providerRow.CheckToggled += GroupCheckToggled;
                providerRow.ExpandToggled += GroupExpandToggled;
                AddRow(providerRow, rowWidth);

                if (!providerExpanded)
                {
                    continue;
                }

                foreach (CompanyGroup company in provider.Companies)
                {
                    if (filterLower.Length > 0 && !GroupMatches(company.Models, filterLower))
                    {
                        continue;
                    }
                    bool companyExpanded = filterLower.Length > 0 ||
                        IsGroupExpanded("c:" + provider.Id + ":" + company.Id, false);

                    TreeCheckRow companyRow = new TreeCheckRow();
                    companyRow.Kind = TreeRowKind.Company;
                    companyRow.RowKey = "c:" + provider.Id + ":" + company.Id;
                    companyRow.ProviderId = provider.Id;
                    companyRow.CompanyId = company.Id;
                    companyRow.Title = company.Name;
                    companyRow.Detail = GroupDetail(company.Models);
                    companyRow.IndentLevel = 1;
                    companyRow.HasChildren = true;
                    companyRow.Expanded = companyExpanded;
                    ApplyGroupState(companyRow, company.Models);
                    companyRow.AccessibleName = companyRow.Title + "，" + companyRow.Detail;
                    companyRow.CheckToggled += GroupCheckToggled;
                    companyRow.ExpandToggled += GroupExpandToggled;
                    AddRow(companyRow, rowWidth);

                    if (!companyExpanded)
                    {
                        continue;
                    }

                    foreach (ModelEntry model in company.Models)
                    {
                        if (filterLower.Length > 0 && !ModelMatches(model, filterLower))
                        {
                            continue;
                        }
                        TreeCheckRow modelRow = new TreeCheckRow();
                        modelRow.Kind = TreeRowKind.Model;
                        modelRow.Slug = model.Slug;
                        modelRow.Title = model.Name;
                        if (String.IsNullOrEmpty(model.Note) && !model.Toggleable)
                        {
                            modelRow.Detail = "由 Codex 管理";
                        }
                        else
                        {
                            modelRow.Detail = model.Note;
                        }
                        modelRow.IndentLevel = 2;
                        modelRow.Selectable = model.Toggleable;
                        modelRow.Checked = model.Toggleable
                            ? CurrentSelection(model.Slug, model.Visible)
                            : model.Visible;
                        modelRow.AccessibleName = model.Name;
                        modelRow.CheckToggled += ModelCheckToggled;
                        AddRow(modelRow, rowWidth);
                    }
                }
            }

            rowsHost.ResumeLayout(true);
            rowsHost.VerticalScroll.Value = Math.Min(scroll, rowsHost.VerticalScroll.Maximum);
        }

        private void AddRow(TreeCheckRow row, int width)
        {
            row.Width = width;
            row.Height = row.RowHeight;
            rowsHost.Controls.Add(row);
        }

        private void ResizeRows()
        {
            int rowWidth = Math.Max(
                420,
                rowsHost.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4
            );
            rowsHost.SuspendLayout();
            foreach (Control control in rowsHost.Controls)
            {
                TreeCheckRow row = control as TreeCheckRow;
                if (row != null)
                {
                    row.Width = rowWidth;
                }
            }
            rowsHost.ResumeLayout(true);
        }

        private static List<ModelEntry> CollectModels(ProviderGroup provider)
        {
            List<ModelEntry> models = new List<ModelEntry>();
            foreach (CompanyGroup company in provider.Companies)
            {
                models.AddRange(company.Models);
            }
            return models;
        }

        private static string ProviderDetail(ProviderGroup provider, List<ModelEntry> models)
        {
            int selected = 0;
            int eligible = 0;
            foreach (ModelEntry model in models)
            {
                if (!model.Toggleable)
                {
                    continue;
                }
                eligible++;
            }
            selected = eligible;
            return provider.Companies.Count + " 个模型公司";
        }

        private static string GroupDetail(List<ModelEntry> models)
        {
            int eligible = 0;
            foreach (ModelEntry model in models)
            {
                if (model.Toggleable)
                {
                    eligible++;
                }
            }
            return eligible > 0 ? eligible + " 个可管理模型" : "由 Codex 管理";
        }

        private bool IsGroupExpanded(string key, bool fallback)
        {
            bool value;
            return expanded.TryGetValue(key, out value) ? value : fallback;
        }

        private bool CurrentSelection(string slug, bool fallback)
        {
            bool value;
            return selection.TryGetValue(slug, out value) ? value : fallback;
        }

        private void ApplyGroupState(TreeCheckRow row, List<ModelEntry> models)
        {
            int selected = 0;
            int eligible = 0;
            foreach (ModelEntry model in models)
            {
                if (!model.Toggleable)
                {
                    continue;
                }
                eligible++;
                if (CurrentSelection(model.Slug, model.Visible))
                {
                    selected++;
                }
            }
            if (eligible == 0)
            {
                row.Selectable = false;
                row.Checked = false;
                row.Partial = false;
                return;
            }
            row.Selectable = true;
            row.Checked = selected == eligible;
            row.Partial = selected > 0 && selected < eligible;
        }

        private static bool ModelMatches(ModelEntry model, string filterLower)
        {
            return model.Slug.ToLowerInvariant().Contains(filterLower) ||
                (model.Name != null && model.Name.ToLowerInvariant().Contains(filterLower));
        }

        private bool GroupMatches(List<ModelEntry> models, string filterLower)
        {
            foreach (ModelEntry model in models)
            {
                if (ModelMatches(model, filterLower))
                {
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------
        // 勾选与展开交互
        // ------------------------------------------------------------------

        private void ModelCheckToggled(object sender, EventArgs e)
        {
            TreeCheckRow row = sender as TreeCheckRow;
            if (row == null || String.IsNullOrEmpty(row.Slug))
            {
                return;
            }
            bool current;
            selection.TryGetValue(row.Slug, out current);
            selection[row.Slug] = !current;
            RefreshAllRowStates();
            UpdateDirtyUI();
        }

        private void GroupCheckToggled(object sender, EventArgs e)
        {
            TreeCheckRow row = sender as TreeCheckRow;
            if (row == null)
            {
                return;
            }
            List<ModelEntry> models = GroupModels(row);
            if (models == null)
            {
                return;
            }
            bool fullySelected = true;
            bool anyEligible = false;
            foreach (ModelEntry model in models)
            {
                if (!model.Toggleable)
                {
                    continue;
                }
                anyEligible = true;
                if (!CurrentSelection(model.Slug, model.Visible))
                {
                    fullySelected = false;
                    break;
                }
            }
            if (!anyEligible)
            {
                return;
            }
            bool next = !fullySelected;
            foreach (ModelEntry model in models)
            {
                if (model.Toggleable)
                {
                    selection[model.Slug] = next;
                }
            }
            RefreshAllRowStates();
            UpdateDirtyUI();
        }

        private void GroupExpandToggled(object sender, EventArgs e)
        {
            TreeCheckRow row = sender as TreeCheckRow;
            if (row == null || !row.HasChildren)
            {
                return;
            }
            bool current = IsGroupExpanded(row.RowKey, row.Kind == TreeRowKind.Provider);
            expanded[row.RowKey] = !current;
            RebuildRows();
        }

        private List<ModelEntry> GroupModels(TreeCheckRow row)
        {
            if (snapshot == null)
            {
                return null;
            }
            foreach (ProviderGroup provider in snapshot.Providers)
            {
                if (row.Kind == TreeRowKind.Provider && provider.Id == row.ProviderId)
                {
                    return CollectModels(provider);
                }
                if (row.Kind == TreeRowKind.Company && provider.Id == row.ProviderId)
                {
                    foreach (CompanyGroup company in provider.Companies)
                    {
                        if (company.Id == row.CompanyId)
                        {
                            return company.Models;
                        }
                    }
                }
            }
            return null;
        }

        private void RefreshAllRowStates()
        {
            rowsHost.SuspendLayout();
            foreach (Control control in rowsHost.Controls)
            {
                TreeCheckRow row = control as TreeCheckRow;
                if (row == null)
                {
                    continue;
                }
                if (row.Kind == TreeRowKind.Model)
                {
                    bool fallback = row.Checked;
                    row.Checked = row.Selectable ? CurrentSelection(row.Slug, fallback) : row.Checked;
                }
                else
                {
                    List<ModelEntry> models = GroupModels(row);
                    if (models != null)
                    {
                        ApplyGroupState(row, models);
                    }
                }
                row.Invalidate();
            }
            rowsHost.ResumeLayout(true);
        }

        private void SetAllGroupsExpanded(bool value)
        {
            if (snapshot == null)
            {
                return;
            }
            foreach (ProviderGroup provider in snapshot.Providers)
            {
                expanded["p:" + provider.Id] = true;
                foreach (CompanyGroup company in provider.Companies)
                {
                    expanded["c:" + provider.Id + ":" + company.Id] = value;
                }
            }
            RebuildRows();
        }

        private void UpdateDirtyUI()
        {
            bool dirty = false;
            foreach (KeyValuePair<string, bool> pair in selection)
            {
                bool before;
                if (!initial.TryGetValue(pair.Key, out before) || before != pair.Value)
                {
                    dirty = true;
                    break;
                }
            }
            applyButton.Enabled = !busy && dirty;
            if (!busy)
            {
                statusLabel.ForeColor = dirty ? ModernUi.Warning : ModernUi.MutedText;
                if (dirty)
                {
                    statusLabel.Text = "有未应用的勾选更改。点击「应用更改」提交并重新发布模型目录。";
                }
            }
        }

        private void SetBusy(bool value, string text)
        {
            busy = value;
            searchBox.Enabled = !value;
            refreshButton.Enabled = !value;
            discoverButton.Enabled = !value;
            expandAllButton.Enabled = !value;
            collapseAllButton.Enabled = !value;
            rowsHost.Enabled = !value;
            if (value)
            {
                statusLabel.ForeColor = ModernUi.Primary;
                statusLabel.Text = text;
                applyButton.Enabled = false;
            }
            else
            {
                UpdateDirtyUI();
            }
        }

        // ------------------------------------------------------------------
        // 应用与发现
        // ------------------------------------------------------------------

        private async Task ApplyAsync()
        {
            if (busy || snapshot == null)
            {
                return;
            }
            List<string> show = new List<string>();
            List<string> hide = new List<string>();
            foreach (KeyValuePair<string, bool> pair in selection)
            {
                bool before;
                if (!initial.TryGetValue(pair.Key, out before) || before == pair.Value)
                {
                    continue;
                }
                if (pair.Value)
                {
                    show.Add(pair.Key);
                }
                else
                {
                    hide.Add(pair.Key);
                }
            }
            if (show.Count == 0 && hide.Count == 0)
            {
                statusLabel.Text = "没有需要应用的更改。";
                return;
            }

            SetBusy(
                true,
                String.Format(
                    "正在应用 {0} 个显示、{1} 个隐藏更改，并重新发布模型目录...",
                    show.Count,
                    hide.Count
                )
            );
            try
            {
                PanelApplyResult result = await Task.Run(
                    delegate { return service.Apply(show, hide); }
                );
                await ReloadAsync(false);
                string backup = String.IsNullOrEmpty(result.BackupDir)
                    ? ""
                    : "\r\n备份目录：" + result.BackupDir;
                MessageBox.Show(
                    this,
                    String.Format(
                        "已应用：显示 {0} 个、隐藏 {1} 个。{2}\r\n\r\n完全退出并重新打开 Codex 后生效。" +
                        (result.Warnings.Count > 0
                            ? "\r\n\r\n注意：" + String.Join("\r\n", result.Warnings.ToArray())
                            : ""),
                        result.Shown,
                        result.Hidden,
                        backup
                    ),
                    "模型管理",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception error)
            {
                statusLabel.ForeColor = ModernUi.Error;
                statusLabel.Text = "应用失败：" + error.Message;
                MessageBox.Show(
                    this,
                    error.Message,
                    "模型管理",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            finally
            {
                SetBusy(false, "");
            }
        }

        private async void DiscoverClicked(object sender, EventArgs e)
        {
            if (busy || snapshot == null)
            {
                return;
            }
            SetBusy(true, "正在联网检查各供应商的模型目录（可能需要十几秒）...");
            try
            {
                PanelDiscoverResult result = await Task.Run(
                    delegate { return service.Discover(); }
                );
                using (ModelDiscoverForm form = new ModelDiscoverForm(service, result))
                {
                    if (form.ShowDialog(this) == DialogResult.OK)
                    {
                        await ReloadAsync(false);
                    }
                }
            }
            catch (Exception error)
            {
                statusLabel.ForeColor = ModernUi.Error;
                statusLabel.Text = "联网检查失败：" + error.Message;
                MessageBox.Show(
                    this,
                    error.Message,
                    "模型管理",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            finally
            {
                SetBusy(false, "");
            }
        }
    }
}
