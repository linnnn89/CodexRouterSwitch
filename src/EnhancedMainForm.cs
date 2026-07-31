using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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
        private const int ContextPanelHeight = 110;
        private const int RestartPanelHeight = 128;

        private readonly RouterController controller;
        private readonly ModeOption nativeMode;
        private readonly ModeOption routerMode;
        private readonly Label connectionValue;
        private readonly Label processValue;
        private readonly Label modelValue;
        private readonly Label providerValue;
        private readonly Label checkedValue;
        private readonly Label statusHeadline;
        private readonly Label statusDetail;
        private readonly StatusDot statusDot;
        private readonly RoundedPanel statusPanel;
        private readonly Label noticeHeadline;
        private readonly Label noticeDetail;
        private readonly IconLabel noticeIcon;
        private readonly RowStyle statusRowStyle;
        private readonly ModernButton primaryAction;
        private readonly ModernButton secondaryAction;
        private readonly ModernButton refreshButton;
        private readonly ModernButton openLogButton;
        private readonly ModernButton copyDiagnosticsButton;
        private readonly BusyLine busyLine;
        private readonly RoundedPanel restartPanel;
        private readonly Label restartText;
        private readonly RowStyle restartRowStyle;
        private readonly Label systemStatusText;
        private readonly IconLabel systemStatusIcon;
        private readonly ModernButton maximizeButton;
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

            Text = "Codex 路由切换";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(820, 760);
            MinimumSize = new Size(800, 720);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = true;
            MinimizeBox = true;
            ShowIcon = false;
            Padding = new Padding(1);
            BackColor = ModernUi.WindowBorder;
            Font = new Font("Microsoft YaHei UI", 10F);
            DoubleBuffered = true;
            AccessibleName = "Codex 路由切换";

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Margin = Padding.Empty;
            root.Padding = Padding.Empty;
            root.BackColor = ModernUi.Canvas;
            root.ColumnCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowCount = 4;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 2F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78F));
            Controls.Add(root);

            Panel titleBar = new Panel();
            titleBar.Dock = DockStyle.Fill;
            titleBar.Margin = Padding.Empty;
            titleBar.BackColor = ModernUi.TitleBar;
            root.Controls.Add(titleBar, 0, 0);

            RoundedPanel appMark = new RoundedPanel();
            appMark.Size = new Size(36, 36);
            appMark.Location = new Point(18, 10);
            appMark.CornerRadius = 9;
            appMark.FillColor = ModernUi.Primary;
            appMark.BorderColor = ModernUi.Primary;
            appMark.Margin = Padding.Empty;
            titleBar.Controls.Add(appMark);

            IconLabel appMarkIcon = new IconLabel();
            appMarkIcon.Dock = DockStyle.Fill;
            appMarkIcon.IconGlyph = "\uE895";
            appMarkIcon.IconColor = Color.White;
            appMarkIcon.IconSize = 15F;
            appMarkIcon.AccessibleName = "路由切换图标";
            appMark.Controls.Add(appMarkIcon);

            Label title = new Label();
            title.Text = "Codex 路由切换";
            title.Font = new Font(
                "Microsoft YaHei UI",
                12F,
                FontStyle.Bold
            );
            title.ForeColor = ModernUi.Text;
            title.AutoSize = false;
            title.Location = new Point(67, 0);
            title.Size = new Size(280, 56);
            title.TextAlign = ContentAlignment.MiddleLeft;
            titleBar.Controls.Add(title);

            FlowLayoutPanel windowActions = new FlowLayoutPanel();
            windowActions.Dock = DockStyle.Right;
            windowActions.Width = 146;
            windowActions.FlowDirection = FlowDirection.LeftToRight;
            windowActions.WrapContents = false;
            windowActions.Padding = new Padding(2, 7, 4, 7);
            windowActions.Margin = Padding.Empty;
            windowActions.BackColor = Color.Transparent;
            titleBar.Controls.Add(windowActions);

            ModernButton minimizeButton = CreateTitleBarButton(
                "\uE921",
                "最小化"
            );
            minimizeButton.Click += delegate
            {
                WindowState = FormWindowState.Minimized;
            };
            windowActions.Controls.Add(minimizeButton);

            maximizeButton = CreateTitleBarButton("\uE922", "最大化");
            maximizeButton.Click += delegate { ToggleMaximize(); };
            windowActions.Controls.Add(maximizeButton);

            ModernButton closeButton = CreateTitleBarButton(
                "\uE8BB",
                "关闭"
            );
            closeButton.DangerOnHover = true;
            closeButton.Click += delegate { Close(); };
            windowActions.Controls.Add(closeButton);

            MakeDraggable(titleBar);
            MakeDraggable(appMark);
            MakeDraggable(appMarkIcon);
            MakeDraggable(title);

            busyLine = new BusyLine();
            busyLine.Dock = DockStyle.Fill;
            busyLine.Margin = Padding.Empty;
            root.Controls.Add(busyLine, 0, 1);

            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.Margin = Padding.Empty;
            body.Padding = Padding.Empty;
            body.BackColor = ModernUi.Canvas;
            body.ColumnCount = 3;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 286F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.RowCount = 1;
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.Controls.Add(body, 0, 2);

            TableLayoutPanel navigation = new TableLayoutPanel();
            navigation.Dock = DockStyle.Fill;
            navigation.Margin = Padding.Empty;
            navigation.Padding = new Padding(22, 24, 22, 20);
            navigation.BackColor = ModernUi.Navigation;
            navigation.ColumnCount = 1;
            navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            navigation.RowCount = 7;
            navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, 118F));
            navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, 14F));
            navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, 118F));
            navigation.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            body.Controls.Add(navigation, 0, 0);

            Label navigationTitle = new Label();
            navigationTitle.Text = "连接方式";
            navigationTitle.Font = new Font(
                "Microsoft YaHei UI",
                12F,
                FontStyle.Bold
            );
            navigationTitle.ForeColor = ModernUi.Text;
            navigationTitle.Dock = DockStyle.Fill;
            navigationTitle.TextAlign = ContentAlignment.MiddleLeft;
            navigationTitle.Margin = Padding.Empty;
            navigation.Controls.Add(navigationTitle, 0, 0);

            nativeMode = CreateModeOption(
                "原生 Codex",
                "直接连接原始 Codex 配置。",
                "\uE753"
            );
            nativeMode.CheckedChanged += ModeCheckedChanged;
            navigation.Controls.Add(nativeMode, 0, 2);

            routerMode = CreateModeOption(
                "本地路由",
                "通过本地路由连接已配置的模型服务。",
                "\uE968"
            );
            routerMode.CheckedChanged += ModeCheckedChanged;
            navigation.Controls.Add(routerMode, 0, 4);

            TableLayoutPanel navigationHint = new TableLayoutPanel();
            navigationHint.Dock = DockStyle.Fill;
            navigationHint.Margin = Padding.Empty;
            navigationHint.ColumnCount = 2;
            navigationHint.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28F));
            navigationHint.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            navigationHint.RowCount = 1;
            navigationHint.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            navigation.Controls.Add(navigationHint, 0, 6);

            IconLabel hintIcon = new IconLabel();
            hintIcon.IconGlyph = "\uE946";
            hintIcon.IconColor = ModernUi.Primary;
            hintIcon.IconSize = 13F;
            hintIcon.Dock = DockStyle.Fill;
            hintIcon.AccessibleName = "提示";
            navigationHint.Controls.Add(hintIcon, 0, 0);

            Label hintText = new Label();
            hintText.Text = "新建任务将使用所选连接方式";
            hintText.ForeColor = ModernUi.MutedText;
            hintText.Font = new Font("Microsoft YaHei UI", 9F);
            hintText.Dock = DockStyle.Fill;
            hintText.TextAlign = ContentAlignment.MiddleLeft;
            hintText.Margin = Padding.Empty;
            navigationHint.Controls.Add(hintText, 1, 0);

            SeparatorControl navigationDivider = new SeparatorControl();
            navigationDivider.Dock = DockStyle.Fill;
            navigationDivider.Margin = Padding.Empty;
            body.Controls.Add(navigationDivider, 1, 0);

            TableLayoutPanel content = new TableLayoutPanel();
            content.Dock = DockStyle.Fill;
            content.Margin = Padding.Empty;
            content.Padding = new Padding(34, 24, 34, 20);
            content.BackColor = ModernUi.Canvas;
            content.ColumnCount = 1;
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            content.RowCount = 8;
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 98F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 170F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 14F));
            statusRowStyle = new RowStyle(SizeType.Absolute, 0F);
            content.RowStyles.Add(statusRowStyle);
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 12F));
            restartRowStyle = new RowStyle(SizeType.Absolute, 0F);
            content.RowStyles.Add(restartRowStyle);
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            body.Controls.Add(content, 2, 0);

            TableLayoutPanel statusHeader = new TableLayoutPanel();
            statusHeader.Dock = DockStyle.Fill;
            statusHeader.Margin = Padding.Empty;
            statusHeader.ColumnCount = 2;
            statusHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38F));
            statusHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            statusHeader.RowCount = 2;
            statusHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            statusHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            content.Controls.Add(statusHeader, 0, 0);

            statusDot = new StatusDot();
            statusDot.Size = new Size(20, 20);
            statusDot.Margin = new Padding(0, 18, 0, 0);
            statusDot.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            statusDot.DotColor = ModernUi.Primary;
            statusHeader.Controls.Add(statusDot, 0, 0);

            statusHeadline = new Label();
            statusHeadline.Text = "正在读取连接状态";
            statusHeadline.Font = new Font(
                "Microsoft YaHei UI",
                20F,
                FontStyle.Bold
            );
            statusHeadline.ForeColor = ModernUi.Text;
            statusHeadline.Dock = DockStyle.Fill;
            statusHeadline.TextAlign = ContentAlignment.MiddleLeft;
            statusHeadline.AutoEllipsis = true;
            statusHeadline.Margin = Padding.Empty;
            statusHeader.Controls.Add(statusHeadline, 1, 0);

            FlowLayoutPanel statusMeta = new FlowLayoutPanel();
            statusMeta.Dock = DockStyle.Fill;
            statusMeta.Margin = Padding.Empty;
            statusMeta.Padding = Padding.Empty;
            statusMeta.FlowDirection = FlowDirection.LeftToRight;
            statusMeta.WrapContents = false;
            statusHeader.Controls.Add(statusMeta, 1, 1);

            statusDetail = new Label();
            statusDetail.Text = "正在读取连接信息";
            statusDetail.ForeColor = ModernUi.MutedText;
            statusDetail.Font = new Font("Microsoft YaHei UI", 10F);
            statusDetail.AutoSize = true;
            statusDetail.Margin = new Padding(0, 4, 0, 0);
            statusMeta.Controls.Add(statusDetail);

            Label metaSeparator = new Label();
            metaSeparator.Text = "·";
            metaSeparator.ForeColor = Color.FromArgb(144, 154, 168);
            metaSeparator.AutoSize = true;
            metaSeparator.Margin = new Padding(8, 4, 8, 0);
            statusMeta.Controls.Add(metaSeparator);

            checkedValue = new Label();
            checkedValue.Text = "正在检查…";
            checkedValue.ForeColor = ModernUi.MutedText;
            checkedValue.Font = new Font("Microsoft YaHei UI", 10F);
            checkedValue.AutoSize = true;
            checkedValue.Margin = new Padding(0, 4, 0, 0);
            statusMeta.Controls.Add(checkedValue);

            SeparatorControl contentDivider = new SeparatorControl();
            contentDivider.Dock = DockStyle.Fill;
            contentDivider.Margin = Padding.Empty;
            content.Controls.Add(contentDivider, 0, 1);

            TableLayoutPanel details = new TableLayoutPanel();
            details.Dock = DockStyle.Fill;
            details.Margin = Padding.Empty;
            details.Padding = new Padding(0, 20, 0, 8);
            details.ColumnCount = 2;
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            details.RowCount = 2;
            details.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            details.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            content.Controls.Add(details, 0, 2);

            processValue = AddFactBlock(details, 0, 0, "路由进程");
            connectionValue = AddFactBlock(details, 0, 1, "当前模式");
            modelValue = AddFactBlock(details, 1, 0, "当前模型");
            providerValue = AddFactBlock(details, 1, 1, "模型服务商");

            statusPanel = CreateCard();
            statusPanel.FillColor = Color.FromArgb(255, 248, 235);
            statusPanel.BorderColor = Color.FromArgb(238, 207, 151);
            statusPanel.Visible = false;
            content.Controls.Add(statusPanel, 0, 4);

            TableLayoutPanel statusLayout = new TableLayoutPanel();
            statusLayout.Dock = DockStyle.Fill;
            statusLayout.Margin = Padding.Empty;
            statusLayout.Padding = new Padding(14, 10, 14, 9);
            statusLayout.ColumnCount = 2;
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            statusLayout.RowCount = 3;
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            statusPanel.Controls.Add(statusLayout);

            noticeIcon = new IconLabel();
            noticeIcon.IconGlyph = "\uE7BA";
            noticeIcon.IconColor = ModernUi.Warning;
            noticeIcon.IconSize = 15F;
            noticeIcon.Dock = DockStyle.Fill;
            noticeIcon.AccessibleName = "状态提示";
            statusLayout.Controls.Add(noticeIcon, 0, 0);
            statusLayout.SetRowSpan(noticeIcon, 2);

            noticeHeadline = new Label();
            noticeHeadline.Text = "需要处理";
            noticeHeadline.Font = new Font(
                "Microsoft YaHei UI",
                10F,
                FontStyle.Bold
            );
            noticeHeadline.ForeColor = ModernUi.Text;
            noticeHeadline.Dock = DockStyle.Fill;
            noticeHeadline.TextAlign = ContentAlignment.MiddleLeft;
            noticeHeadline.Margin = Padding.Empty;
            statusLayout.Controls.Add(noticeHeadline, 1, 0);

            noticeDetail = new Label();
            noticeDetail.ForeColor = ModernUi.MutedText;
            noticeDetail.Font = new Font("Microsoft YaHei UI", 9F);
            noticeDetail.Dock = DockStyle.Fill;
            noticeDetail.TextAlign = ContentAlignment.TopLeft;
            noticeDetail.AutoEllipsis = true;
            noticeDetail.Margin = Padding.Empty;
            statusLayout.Controls.Add(noticeDetail, 1, 1);

            FlowLayoutPanel statusActions = new FlowLayoutPanel();
            statusActions.Dock = DockStyle.Fill;
            statusActions.FlowDirection = FlowDirection.RightToLeft;
            statusActions.WrapContents = false;
            statusActions.Margin = Padding.Empty;
            statusActions.Padding = Padding.Empty;
            statusLayout.Controls.Add(statusActions, 1, 2);

            primaryAction = CreateActionButton(
                "",
                ModernButtonKind.Primary,
                ""
            );
            primaryAction.Size = new Size(118, 34);
            primaryAction.Click += PrimaryActionClicked;
            primaryAction.Visible = false;
            statusActions.Controls.Add(primaryAction);

            secondaryAction = CreateActionButton(
                "",
                ModernButtonKind.Secondary,
                ""
            );
            secondaryAction.Size = new Size(126, 34);
            secondaryAction.Click += SecondaryActionClicked;
            secondaryAction.Visible = false;
            statusActions.Controls.Add(secondaryAction);

            restartPanel = CreateCard();
            restartPanel.FillColor = ModernUi.PrimarySoft;
            restartPanel.BorderColor = ModernUi.PrimaryBorder;
            restartPanel.Visible = false;
            content.Controls.Add(restartPanel, 0, 6);

            TableLayoutPanel restartLayout = new TableLayoutPanel();
            restartLayout.Dock = DockStyle.Fill;
            restartLayout.Margin = Padding.Empty;
            restartLayout.Padding = new Padding(14, 10, 14, 9);
            restartLayout.ColumnCount = 2;
            restartLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40F));
            restartLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            restartLayout.RowCount = 3;
            restartLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            restartLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            restartLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            restartPanel.Controls.Add(restartLayout);

            IconLabel restartIcon = new IconLabel();
            restartIcon.IconGlyph = "\uE946";
            restartIcon.IconColor = ModernUi.Primary;
            restartIcon.IconSize = 16F;
            restartIcon.Dock = DockStyle.Fill;
            restartIcon.AccessibleName = "重启提示";
            restartLayout.Controls.Add(restartIcon, 0, 0);
            restartLayout.SetRowSpan(restartIcon, 2);

            Label restartHeadline = new Label();
            restartHeadline.Text = "连接方式已更改。";
            restartHeadline.Font = new Font(
                "Microsoft YaHei UI",
                10F,
                FontStyle.Bold
            );
            restartHeadline.ForeColor = ModernUi.Text;
            restartHeadline.Dock = DockStyle.Fill;
            restartHeadline.TextAlign = ContentAlignment.MiddleLeft;
            restartHeadline.Margin = Padding.Empty;
            restartLayout.Controls.Add(restartHeadline, 1, 0);

            restartText = new Label();
            restartText.Text =
                "重启 Codex 后，新任务才会使用新的连接方式。";
            restartText.ForeColor = ModernUi.MutedText;
            restartText.Font = new Font("Microsoft YaHei UI", 9F);
            restartText.Dock = DockStyle.Fill;
            restartText.TextAlign = ContentAlignment.TopLeft;
            restartText.AutoEllipsis = true;
            restartText.Margin = Padding.Empty;
            restartLayout.Controls.Add(restartText, 1, 1);

            FlowLayoutPanel restartActions = new FlowLayoutPanel();
            restartActions.Dock = DockStyle.Fill;
            restartActions.FlowDirection = FlowDirection.RightToLeft;
            restartActions.WrapContents = false;
            restartActions.Margin = Padding.Empty;
            restartActions.Padding = Padding.Empty;
            restartLayout.Controls.Add(restartActions, 1, 2);

            ModernButton dismissRestart = CreateActionButton(
                "稍后处理",
                ModernButtonKind.Secondary,
                ""
            );
            dismissRestart.Size = new Size(96, 34);
            dismissRestart.Click += delegate
            {
                restartPanel.Visible = false;
                restartRowStyle.Height = 0F;
            };
            restartActions.Controls.Add(dismissRestart);

            ModernButton copyRestart = CreateActionButton(
                "复制重启步骤",
                ModernButtonKind.Primary,
                "\uE8C8"
            );
            copyRestart.Size = new Size(148, 34);
            copyRestart.Click += CopyRestartStepsClicked;
            restartActions.Controls.Add(copyRestart);

            Panel footerHost = new Panel();
            footerHost.Dock = DockStyle.Fill;
            footerHost.Margin = Padding.Empty;
            footerHost.BackColor = ModernUi.Footer;
            root.Controls.Add(footerHost, 0, 3);

            SeparatorControl footerDivider = new SeparatorControl();
            footerDivider.Dock = DockStyle.Top;
            footerDivider.Height = 1;
            footerHost.Controls.Add(footerDivider);

            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.Margin = Padding.Empty;
            footer.Padding = new Padding(28, 14, 28, 12);
            footer.ColumnCount = 2;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
            footer.RowCount = 1;
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            footerHost.Controls.Add(footer);

            FlowLayoutPanel footerStatus = new FlowLayoutPanel();
            footerStatus.Dock = DockStyle.Fill;
            footerStatus.FlowDirection = FlowDirection.LeftToRight;
            footerStatus.WrapContents = false;
            footerStatus.Margin = Padding.Empty;
            footerStatus.Padding = Padding.Empty;
            footer.Controls.Add(footerStatus, 0, 0);

            systemStatusIcon = new IconLabel();
            systemStatusIcon.IconGlyph = "\uE83D";
            systemStatusIcon.IconColor = ModernUi.Success;
            systemStatusIcon.IconSize = 16F;
            systemStatusIcon.Size = new Size(28, 44);
            systemStatusIcon.AccessibleName = "系统状态";
            footerStatus.Controls.Add(systemStatusIcon);

            systemStatusText = new Label();
            systemStatusText.Text = "正在检查系统状态";
            systemStatusText.ForeColor = ModernUi.MutedText;
            systemStatusText.Font = new Font("Microsoft YaHei UI", 9.5F);
            systemStatusText.AutoSize = false;
            systemStatusText.Size = new Size(180, 44);
            systemStatusText.TextAlign = ContentAlignment.MiddleLeft;
            systemStatusText.Margin = Padding.Empty;
            footerStatus.Controls.Add(systemStatusText);

            FlowLayoutPanel footerActions = new FlowLayoutPanel();
            footerActions.Dock = DockStyle.Fill;
            footerActions.FlowDirection = FlowDirection.RightToLeft;
            footerActions.WrapContents = false;
            footerActions.Margin = Padding.Empty;
            footerActions.Padding = Padding.Empty;
            footer.Controls.Add(footerActions, 1, 0);

            refreshButton = CreateActionButton(
                "刷新状态",
                ModernButtonKind.Primary,
                "\uE72C"
            );
            refreshButton.Size = new Size(138, 44);
            refreshButton.Click += RefreshClicked;
            footerActions.Controls.Add(refreshButton);

            copyDiagnosticsButton = CreateActionButton(
                "复制诊断信息",
                ModernButtonKind.Secondary,
                "\uE8C8"
            );
            copyDiagnosticsButton.Size = new Size(158, 44);
            copyDiagnosticsButton.Click += CopyDiagnosticsClicked;
            footerActions.Controls.Add(copyDiagnosticsButton);

            openLogButton = CreateActionButton(
                "打开日志",
                ModernButtonKind.Secondary,
                "\uE8E5"
            );
            openLogButton.Size = new Size(118, 44);
            openLogButton.Click += OpenLogClicked;
            footerActions.Controls.Add(openLogButton);

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
            Resize += delegate { UpdateMaximizeButton(); };

            UpdateMaximizeButton();
        }

        private static RoundedPanel CreateCard()
        {
            RoundedPanel panel = new RoundedPanel();
            panel.Dock = DockStyle.Fill;
            panel.Margin = Padding.Empty;
            panel.FillColor = Color.White;
            panel.BorderColor = Color.FromArgb(209, 218, 229);
            panel.CornerRadius = 11;
            return panel;
        }

        private static ModeOption CreateModeOption(
            string title,
            string description,
            string iconGlyph
        )
        {
            ModeOption option = new ModeOption();
            option.Text = title;
            option.Description = description;
            option.IconGlyph = iconGlyph;
            option.Font = new Font(
                "Microsoft YaHei UI",
                11F,
                FontStyle.Bold
            );
            option.Dock = DockStyle.Fill;
            option.Margin = Padding.Empty;
            option.AccessibleName = title;
            option.AccessibleDescription = description;
            return option;
        }

        private static Label AddFactBlock(
            TableLayoutPanel table,
            int row,
            int column,
            string name
        )
        {
            TableLayoutPanel block = new TableLayoutPanel();
            block.Dock = DockStyle.Fill;
            block.Margin = column == 0
                ? new Padding(0, 0, 24, 0)
                : Padding.Empty;
            block.Padding = Padding.Empty;
            block.ColumnCount = 1;
            block.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            block.RowCount = 2;
            block.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            block.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            table.Controls.Add(block, column, row);

            Label nameLabel = new Label();
            nameLabel.Text = name;
            nameLabel.ForeColor = ModernUi.MutedText;
            nameLabel.Font = new Font("Microsoft YaHei UI", 9.5F);
            nameLabel.Dock = DockStyle.Fill;
            nameLabel.TextAlign = ContentAlignment.MiddleLeft;
            nameLabel.Margin = Padding.Empty;
            block.Controls.Add(nameLabel, 0, 0);

            Label valueLabel = new Label();
            valueLabel.Text = "正在检查…";
            valueLabel.ForeColor = ModernUi.Text;
            valueLabel.Font = new Font(
                "Microsoft YaHei UI",
                12F,
                FontStyle.Bold
            );
            valueLabel.Dock = DockStyle.Fill;
            valueLabel.TextAlign = ContentAlignment.TopLeft;
            valueLabel.AutoEllipsis = true;
            valueLabel.Margin = Padding.Empty;
            block.Controls.Add(valueLabel, 0, 1);
            return valueLabel;
        }

        private static ModernButton CreateActionButton(
            string text,
            ModernButtonKind kind,
            string iconGlyph
        )
        {
            ModernButton button = new ModernButton();
            button.Text = text;
            button.Kind = kind;
            button.IconGlyph = iconGlyph;
            button.Font = new Font(
                "Microsoft YaHei UI",
                9.5F,
                FontStyle.Regular
            );
            button.CornerRadius = 9;
            button.AutoSize = false;
            button.Margin = new Padding(10, 0, 0, 0);
            button.AccessibleName = text;
            return button;
        }

        private static ModernButton CreateTitleBarButton(
            string iconGlyph,
            string accessibleName
        )
        {
            ModernButton button = CreateActionButton(
                "",
                ModernButtonKind.Ghost,
                iconGlyph
            );
            button.Size = new Size(44, 40);
            button.Margin = Padding.Empty;
            button.CornerRadius = 8;
            button.AccessibleName = accessibleName;
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
                    ? "正在启动本地路由并更新 Codex 配置…"
                    : "正在恢复原生 Codex 并停止受管理的路由进程…"
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
                ShowError(LocalizeControllerMessage(failure.Message));
                return;
            }

            if (result == null || !result.Ok)
            {
                ShowError("连接方式切换未返回成功结果。");
                return;
            }

            restartRowStyle.Height = RestartPanelHeight;
            restartPanel.Visible = true;
            restartText.Text = "重启 Codex 后，新任务才会使用新的连接方式。";
            if (result.Warnings.Count > 0)
            {
                ShowWarningPreservingActions(
                    "连接方式已更改，但存在警告",
                    LocalizeControllerMessage(result.Message)
                );
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

            SetRefreshing(true);
            if (showProgress)
            {
                SetBusy(true, "正在检查 Codex 与路由状态…");
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
                ShowError(LocalizeControllerMessage(error.Message));
                connectionValue.Text = "暂不可用";
                processValue.Text = "暂不可用";
                processValue.ForeColor = ModernUi.Error;
                modelValue.Text = "暂不可用";
                providerValue.Text = "暂不可用";
                checkedValue.Text = "检查失败";
            }
            finally
            {
                if (showProgress)
                {
                    SetBusy(false, null);
                }
                SetRefreshing(false);
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
                ? "本地路由"
                : "原生 Codex";
            if (status.State == "On")
            {
                processValue.Text = "运行正常";
                processValue.ForeColor = ModernUi.Success;
            }
            else if (status.State == "Orphaned")
            {
                processValue.Text = "运行中（未纳入管理）";
                processValue.ForeColor = ModernUi.Warning;
            }
            else if (status.State == "Degraded")
            {
                processValue.Text = "当前不可用";
                processValue.ForeColor = ModernUi.Warning;
            }
            else
            {
                processValue.Text = "已停止";
                processValue.ForeColor = ModernUi.Text;
            }
            modelValue.Text = FriendlyModel(status.Model, "未报告");
            providerValue.Text = FriendlyProvider(
                status.ModelProvider,
                status.ConfigOn ? "未报告" : "OpenAI"
            );
            checkedValue.Text = "刚刚检查";
            checkedValue.AccessibleDescription = DateTime.Now.ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.CurrentCulture
            );
            openLogButton.Enabled = File.Exists(controller.Paths.RouterLog);

            if (status.State == "On")
            {
                ShowHealthy(
                    "本地路由运行正常",
                    "127.0.0.1:4102"
                );
            }
            else if (status.State == "Degraded")
            {
                ShowWarning(
                    "本地路由当前不可用",
                    "Codex 已配置为使用本地路由，但路由进程当前不可用。"
                );
                statusDetail.Text = "127.0.0.1:4102";
                ConfigureActions(
                    "重试路由",
                    delegate { RetryRouterAction(); },
                    "恢复原生 Codex",
                    delegate { RestoreNativeAction(); }
                );
            }
            else if (status.State == "Orphaned")
            {
                ShowWarning(
                    "检测到未跟踪的路由进程",
                    "原生 Codex 当前有效。本程序不会终止端口 4102 上的未知进程。"
                );
                statusDetail.Text = "127.0.0.1:4102";
                ConfigureActions(
                    "重新检查",
                    delegate { RefreshStatusAction(); },
                    "打开任务管理器",
                    delegate { OpenTaskManager(); }
                );
            }
            else
            {
                ShowNeutral(
                    "原生 Codex 已启用",
                    "直接连接 OpenAI"
                );
            }
        }

        private static string FriendlyValue(string value, string fallback)
        {
            return String.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string FriendlyModel(string value, string fallback)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            string name = value.Trim();
            int slash = name.LastIndexOf('/');
            if (slash >= 0 && slash < name.Length - 1)
            {
                name = name.Substring(slash + 1);
            }
            if (name.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase))
            {
                return "GPT-" + name.Substring(4);
            }

            string[] parts = name
                .Replace('_', ' ')
                .Replace('-', ' ')
                .Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < parts.Length; index++)
            {
                string lower = parts[index].ToLowerInvariant();
                if (lower == "gpt")
                {
                    parts[index] = "GPT";
                }
                else if (lower == "deepseek")
                {
                    parts[index] = "DeepSeek";
                }
                else if (lower == "openai")
                {
                    parts[index] = "OpenAI";
                }
                else if (lower == "claude")
                {
                    parts[index] = "Claude";
                }
                else if (lower == "gemini")
                {
                    parts[index] = "Gemini";
                }
                else if (lower == "qwen")
                {
                    parts[index] = "Qwen";
                }
                else if (
                    lower.Length > 1 &&
                    (lower[0] == 'v' || lower[0] == 'r') &&
                    Char.IsDigit(lower[1])
                )
                {
                    parts[index] = Char.ToUpperInvariant(lower[0]) +
                        lower.Substring(1);
                }
                else if (lower.Length > 0)
                {
                    parts[index] = Char.ToUpperInvariant(lower[0]) +
                        lower.Substring(1);
                }
            }
            return String.Join(" ", parts);
        }

        private static string FriendlyProvider(string value, string fallback)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }
            if (String.Equals(value, "openai", StringComparison.OrdinalIgnoreCase))
            {
                return "OpenAI";
            }
            if (String.Equals(value, "openrouter", StringComparison.OrdinalIgnoreCase))
            {
                return "OpenRouter";
            }
            return value.Trim();
        }

        private void ShowHealthy(string headline, string detail)
        {
            SetHeaderStatus(headline, detail, ModernUi.Success);
            HideContextPanel();
            ClearActions();
            SetFooterStatus("系统状态正常", ModernUi.Success);
        }

        private void ShowNeutral(string headline, string detail)
        {
            SetHeaderStatus(headline, detail, ModernUi.Primary);
            HideContextPanel();
            ClearActions();
            SetFooterStatus("系统状态正常", ModernUi.Success);
        }

        private void ShowWarning(string headline, string detail)
        {
            SetHeaderStatus(headline, "请查看下方处理建议", ModernUi.Warning);
            ShowContextPanel(
                "需要处理",
                detail,
                Color.FromArgb(255, 248, 235),
                Color.FromArgb(238, 207, 151),
                ModernUi.Warning
            );
            ClearActions();
            SetFooterStatus("需要处理", ModernUi.Warning);
        }

        private void ShowWarningPreservingActions(string headline, string detail)
        {
            SetHeaderStatus(headline, "请查看下方警告", ModernUi.Warning);
            ShowContextPanel(
                headline,
                FriendlyValue(detail, "连接方式已更改，但路由返回了警告。"),
                Color.FromArgb(255, 248, 235),
                Color.FromArgb(238, 207, 151),
                ModernUi.Warning
            );
            SetFooterStatus("需要处理", ModernUi.Warning);
        }

        private void ShowError(string detail)
        {
            SetHeaderStatus(
                "状态暂不可用",
                "无法读取 Codex 与路由状态",
                ModernUi.Error
            );
            ShowContextPanel(
                "检查失败",
                detail,
                Color.FromArgb(255, 243, 242),
                Color.FromArgb(235, 184, 181),
                ModernUi.Error
            );
            ConfigureActions(
                "重新检查",
                delegate { RefreshStatusAction(); },
                "复制错误",
                delegate { CopyText(detail); }
            );
            SetFooterStatus("状态检查失败", ModernUi.Error);
        }

        private void SetHeaderStatus(
            string headline,
            string detail,
            Color tone
        )
        {
            statusDot.DotColor = tone;
            statusHeadline.ForeColor = tone == ModernUi.Primary
                ? ModernUi.Text
                : tone;
            statusHeadline.Text = headline;
            statusDetail.Text = detail;
        }

        private void ShowContextPanel(
            string headline,
            string detail,
            Color fill,
            Color border,
            Color tone
        )
        {
            statusPanel.FillColor = fill;
            statusPanel.BorderColor = border;
            noticeIcon.IconColor = tone;
            noticeHeadline.Text = headline;
            noticeHeadline.ForeColor = tone == ModernUi.Primary
                ? ModernUi.Text
                : tone;
            noticeDetail.Text = detail;
            statusRowStyle.Height = ContextPanelHeight;
            statusPanel.Visible = true;
        }

        private void HideContextPanel()
        {
            statusPanel.Visible = false;
            statusRowStyle.Height = 0F;
        }

        private void SetFooterStatus(string text, Color tone)
        {
            systemStatusText.Text = text;
            systemStatusText.ForeColor = tone == ModernUi.Success
                ? ModernUi.MutedText
                : tone;
            systemStatusIcon.IconColor = tone;
        }

        private void ConfigureActions(
            string primaryText,
            Action primaryHandler,
            string secondaryText,
            Action secondaryHandler
        )
        {
            primaryAction.Text = primaryText;
            primaryAction.AccessibleName = primaryText;
            primaryActionHandler = primaryHandler;
            primaryAction.Visible = true;

            secondaryAction.Text = secondaryText;
            secondaryAction.AccessibleName = secondaryText;
            secondaryActionHandler = secondaryHandler;
            secondaryAction.Visible = true;
            statusRowStyle.Height = ContextPanelHeight;
            statusPanel.Visible = true;
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
            if (!busy && !refreshing && primaryActionHandler != null)
            {
                primaryActionHandler();
            }
        }

        private void SecondaryActionClicked(object sender, EventArgs e)
        {
            if (!busy && !refreshing && secondaryActionHandler != null)
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

        private void OpenTaskManager()
        {
            try
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = "taskmgr.exe",
                        UseShellExecute = true
                    }
                );
            }
            catch (Exception error)
            {
                ShowError("无法打开任务管理器：" + error.Message);
            }
        }

        private void OpenLogClicked(object sender, EventArgs e)
        {
            string path = controller.Paths.RouterLog;
            if (!File.Exists(path))
            {
                ShowWarning(
                    "未找到路由日志",
                    "受管理的状态目录中目前没有 router.log 文件。"
                );
                return;
            }

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = "notepad.exe";
                startInfo.Arguments = "\"" + path.Replace("\"", "\"\"") + "\"";
                startInfo.UseShellExecute = true;
                Process.Start(startInfo);
            }
            catch (Exception error)
            {
                ShowError("无法打开路由日志：" + error.Message);
            }
        }

        private void CopyDiagnosticsClicked(object sender, EventArgs e)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Codex 路由切换诊断信息");
            builder.AppendLine(
                "时间：" + DateTime.Now.ToString("o", CultureInfo.InvariantCulture)
            );
            builder.AppendLine(
                "状态：" +
                (lastStatus == null
                    ? "不可用"
                    : FriendlyState(lastStatus.State))
            );
            builder.AppendLine(
                "连接方式：" +
                (lastStatus == null
                    ? "不可用"
                    : (lastStatus.ConfigOn ? "本地路由" : "原生 Codex"))
            );
            builder.AppendLine(
                "路由健康：" +
                (lastStatus == null
                    ? "不可用"
                    : (lastStatus.Healthy ? "是" : "否"))
            );
            builder.AppendLine(
                "当前模型：" +
                (lastStatus == null
                    ? "不可用"
                    : FriendlyModel(lastStatus.Model, "未报告"))
            );
            builder.AppendLine(
                "模型服务商：" +
                (lastStatus == null
                    ? "不可用"
                    : FriendlyProvider(lastStatus.ModelProvider, "未报告"))
            );
            builder.AppendLine(
                "路由目录：" + RedactUserPath(controller.Paths.RouterRoot)
            );
            builder.AppendLine(
                "Codex 目录：" + RedactUserPath(controller.Paths.CodexHome)
            );
            builder.AppendLine(
                "路由日志：" + RedactUserPath(controller.Paths.RouterLog)
            );
            builder.AppendLine(
                "本报告不包含密钥、API Key、OAuth 令牌或受管理的能力网址。"
            );
            if (CopyText(builder.ToString()))
            {
                SetFooterStatus("诊断信息已复制", ModernUi.Primary);
            }
        }

        private static string FriendlyState(string state)
        {
            if (String.Equals(state, "On", StringComparison.OrdinalIgnoreCase))
            {
                return "本地路由运行正常";
            }
            if (String.Equals(state, "Off", StringComparison.OrdinalIgnoreCase))
            {
                return "原生 Codex 已启用";
            }
            if (
                String.Equals(
                    state,
                    "Degraded",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return "本地路由不可用";
            }
            if (
                String.Equals(
                    state,
                    "Orphaned",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return "检测到未跟踪的路由进程";
            }
            return FriendlyValue(state, "不可用");
        }

        internal static string LocalizeControllerMessage(string message)
        {
            if (String.IsNullOrWhiteSpace(message))
            {
                return "未提供详细错误信息。";
            }

            string localized = message.Trim();
            localized = localized.Replace(
                "Node.js was not found. Codex Router cannot be controlled.",
                "未找到 Node.js，当前无法控制 Codex 路由。"
            );
            localized = localized.Replace(
                "Node.js did not report a version.",
                "Node.js 未返回版本信息。"
            );
            localized = localized.Replace(
                "Required Codex Router file is missing:",
                "缺少必需的 Codex Router 文件："
            );
            localized = localized.Replace(
                "A Router process not owned by this switch still responds on port 4102.",
                "端口 4102 上仍有不受本程序管理的路由进程响应。"
            );
            localized = localized.Replace(
                "Router did not become healthy within 300 seconds. Check ",
                "路由在 300 秒内未恢复健康。请检查日志："
            );
            localized = localized.Replace(
                "The previous visible Router runtime did not recover.",
                "先前的可见路由进程未能恢复。"
            );
            localized = localized.Replace(
                "Codex Router returned an invalid configuration status.",
                "Codex Router 返回了无效的配置状态。"
            );
            localized = localized.Replace(
                "The repository did not render a recognized Windows start script.",
                "路由仓库未能生成可识别的 Windows 启动脚本。"
            );
            localized = localized.Replace(
                "Router start script is missing:",
                "缺少路由启动脚本："
            );
            localized = localized.Replace(
                "Visible Router wrapper is missing:",
                "缺少可见路由控制台启动脚本："
            );
            localized = localized.Replace(
                "The visible Router console could not be started.",
                "无法启动可见的路由控制台。"
            );
            localized = localized.Replace(
                "The saved Router console state is invalid.",
                "保存的路由控制台状态无效。"
            );
            localized = localized.Replace(
                "The saved Router console state belongs to another launcher.",
                "保存的路由控制台状态属于其他启动器。"
            );
            localized = localized.Replace(
                "Could not start process:",
                "无法启动进程："
            );
            localized = localized.Replace(
                "Command timed out:",
                "命令执行超时："
            );
            localized = localized.Replace(
                "Command failed:",
                "命令执行失败："
            );
            localized = localized.Replace(
                "Expected a JSON object.",
                "预期返回 JSON 对象，但实际结果无效。"
            );
            localized = localized.Replace(
                "Rollback warning:",
                "回滚警告："
            );
            return localized;
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

            string profileRoot = userProfile.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            );
            if (String.Equals(path, profileRoot, StringComparison.OrdinalIgnoreCase))
            {
                return "%USERPROFILE%";
            }

            string profilePrefix = profileRoot + Path.DirectorySeparatorChar;
            if (path.StartsWith(profilePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return "%USERPROFILE%" + path.Substring(profileRoot.Length);
            }
            return path;
        }

        private void CopyRestartStepsClicked(object sender, EventArgs e)
        {
            if (CopyText(
                "1. 完全退出 Codex。\r\n" +
                "2. 重新打开 Codex。\r\n" +
                "3. 新建任务后再检查模型选择器。"
            ))
            {
                restartText.Text = "重启步骤已复制到剪贴板。";
            }
        }

        private static bool CopyText(string text)
        {
            try
            {
                Clipboard.SetText(text ?? "");
                return true;
            }
            catch (Exception error)
            {
                MessageBox.Show(
                    error.Message,
                    "无法复制到剪贴板",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return false;
            }
        }

        private void SetBusy(bool value, string message)
        {
            busy = value;
            bool interactionEnabled = !value && !refreshing;
            nativeMode.Enabled = interactionEnabled;
            routerMode.Enabled = interactionEnabled;
            refreshButton.Enabled = interactionEnabled;
            openLogButton.Enabled = !value && File.Exists(controller.Paths.RouterLog);
            copyDiagnosticsButton.Enabled = !value;
            primaryAction.Enabled = interactionEnabled;
            secondaryAction.Enabled = interactionEnabled;
            busyLine.Active = value;
            UseWaitCursor = value;

            if (value)
            {
                refreshTimer.Stop();
            }
            else if (Form.ActiveForm == this)
            {
                refreshTimer.Start();
            }

            if (value && !String.IsNullOrWhiteSpace(message))
            {
                SetHeaderStatus("正在处理连接", "请稍候", ModernUi.Primary);
                ShowContextPanel(
                    "正在处理",
                    message,
                    ModernUi.PrimarySoft,
                    ModernUi.PrimaryBorder,
                    ModernUi.Primary
                );
                ClearActions();
                SetFooterStatus("正在处理", ModernUi.Primary);
            }
        }

        private void SetRefreshing(bool value)
        {
            refreshing = value;
            bool interactionEnabled = !value && !busy;
            nativeMode.Enabled = interactionEnabled;
            routerMode.Enabled = interactionEnabled;
            refreshButton.Enabled = interactionEnabled;
            primaryAction.Enabled = interactionEnabled;
            secondaryAction.Enabled = interactionEnabled;
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
                "请等待当前连接操作完成。",
                "Codex 路由切换",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        private void MakeDraggable(Control control)
        {
            control.MouseDown += TitleBarMouseDown;
            control.DoubleClick += delegate { ToggleMaximize(); };
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

        private void ToggleMaximize()
        {
            WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
            UpdateMaximizeButton();
        }

        private void UpdateMaximizeButton()
        {
            if (maximizeButton == null)
            {
                return;
            }
            bool maximized = WindowState == FormWindowState.Maximized;
            maximizeButton.IconGlyph = maximized ? "\uE923" : "\uE922";
            maximizeButton.AccessibleName = maximized ? "还原" : "最大化";
        }

        internal bool RunInteractionGuardSelfTest()
        {
            Action savedPrimaryHandler = primaryActionHandler;
            bool invoked = false;
            try
            {
                primaryActionHandler = delegate { invoked = true; };
                SetRefreshing(true);
                bool controlsDisabled =
                    !nativeMode.Enabled &&
                    !routerMode.Enabled &&
                    !primaryAction.Enabled &&
                    !secondaryAction.Enabled;
                PrimaryActionClicked(this, EventArgs.Empty);
                return controlsDisabled && !invoked;
            }
            finally
            {
                primaryActionHandler = savedPrimaryHandler;
                SetRefreshing(false);
            }
        }

        internal bool RunModernUiSelfTest()
        {
            return FormBorderStyle == FormBorderStyle.None &&
                nativeMode is ModeOption &&
                routerMode is ModeOption &&
                refreshButton is ModernButton &&
                refreshButton.Kind == ModernButtonKind.Primary &&
                statusPanel.CornerRadius >= 10 &&
                restartPanel.CornerRadius >= 10 &&
                FriendlyModel("deepseek/deepseek-v4", "") == "DeepSeek V4" &&
                FriendlyModel("openai/gpt-5", "") == "GPT-5" &&
                FriendlyProvider("openrouter", "") == "OpenRouter";
        }

        internal bool RunChineseUiSelfTest()
        {
            return Text == "Codex 路由切换" &&
                nativeMode.Text == "原生 Codex" &&
                routerMode.Text == "本地路由" &&
                refreshButton.Text == "刷新状态" &&
                openLogButton.Text == "打开日志" &&
                copyDiagnosticsButton.Text == "复制诊断信息" &&
                restartText.Text.StartsWith(
                    "重启 Codex",
                    StringComparison.Ordinal
                );
        }

        internal bool RunLayoutSelfTest()
        {
            float savedRestartHeight = restartRowStyle.Height;
            bool savedRestartVisible = restartPanel.Visible;
            try
            {
                CreateControl();
                restartRowStyle.Height = RestartPanelHeight;
                restartPanel.Visible = true;
                PerformLayout();
                return restartPanel.Width > 300 &&
                    restartPanel.Height >= RestartPanelHeight - 2 &&
                    refreshButton.Width >= 130 &&
                    refreshButton.Height >= 40 &&
                    nativeMode.Width > 180 &&
                    routerMode.Width > 180;
            }
            finally
            {
                restartRowStyle.Height = savedRestartHeight;
                restartPanel.Visible = savedRestartVisible;
                PerformLayout();
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int CsDropShadow = 0x00020000;
                CreateParams parameters = base.CreateParams;
                parameters.ClassStyle |= CsDropShadow;
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int roundedCorners = 2;
                DwmSetWindowAttribute(
                    Handle,
                    33,
                    ref roundedCorners,
                    Marshal.SizeOf(typeof(int))
                );
            }
            catch
            {
            }
        }

        protected override void WndProc(ref Message message)
        {
            const int WmNcHitTest = 0x0084;
            const int HtClient = 1;
            const int HtLeft = 10;
            const int HtRight = 11;
            const int HtTop = 12;
            const int HtTopLeft = 13;
            const int HtTopRight = 14;
            const int HtBottom = 15;
            const int HtBottomLeft = 16;
            const int HtBottomRight = 17;

            base.WndProc(ref message);
            if (
                message.Msg != WmNcHitTest ||
                message.Result.ToInt32() != HtClient ||
                WindowState == FormWindowState.Maximized
            )
            {
                return;
            }

            long packed = message.LParam.ToInt64();
            Point screenPoint = new Point(
                unchecked((short)(packed & 0xFFFF)),
                unchecked((short)((packed >> 16) & 0xFFFF))
            );
            Point clientPoint = PointToClient(screenPoint);
            int grip = 8;
            bool left = clientPoint.X <= grip;
            bool right = clientPoint.X >= ClientSize.Width - grip;
            bool top = clientPoint.Y <= grip;
            bool bottom = clientPoint.Y >= ClientSize.Height - grip;

            if (left && top)
            {
                message.Result = new IntPtr(HtTopLeft);
            }
            else if (right && top)
            {
                message.Result = new IntPtr(HtTopRight);
            }
            else if (left && bottom)
            {
                message.Result = new IntPtr(HtBottomLeft);
            }
            else if (right && bottom)
            {
                message.Result = new IntPtr(HtBottomRight);
            }
            else if (left)
            {
                message.Result = new IntPtr(HtLeft);
            }
            else if (right)
            {
                message.Result = new IntPtr(HtRight);
            }
            else if (top)
            {
                message.Result = new IntPtr(HtTop);
            }
            else if (bottom)
            {
                message.Result = new IntPtr(HtBottom);
            }
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr window,
            int message,
            IntPtr wParam,
            IntPtr lParam
        );

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr window,
            int attribute,
            ref int value,
            int valueSize
        );

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
                        values["modernUi"] = form.RunModernUiSelfTest();
                        values["pureChineseUi"] = form.RunChineseUiSelfTest();
                        values["layoutSafe"] = form.RunLayoutSelfTest();
                        values["uiLanguage"] = "zh-CN";
                        values["version"] = "1.2.0";
                        values["refreshMutationGuard"] =
                            form.RunInteractionGuardSelfTest();
                        values["legacyArgsRestricted"] =
                            HasLegacyCommandLineArgument(
                                new string[] { "--self-test-file", "result.json" }
                            ) &&
                            !HasLegacyCommandLineArgument(
                                new string[] { "--unrecognized" }
                            );
                        WriteJsonResult(resultFile, values);
                    }
                    return 0;
                }

                if (HasLegacyCommandLineArgument(args))
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
                        "Codex 路由切换已在运行。",
                        "Codex 路由切换",
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
                    EnhancedMainForm.LocalizeControllerMessage(error.Message),
                    "Codex 路由切换启动失败",
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
                    "未找到兼容命令行入口。"
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

        private static bool HasLegacyCommandLineArgument(string[] args)
        {
            return HasArgument(args, "--self-test-file") ||
                HasArgument(args, "--status-file");
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
            throw new ArgumentException("必须提供结果文件路径。");
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
