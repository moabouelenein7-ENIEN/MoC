using System;
using System.Drawing;
using System.Windows.Forms;

namespace EtabsHelper.UI
{
    public static class Theme
    {
        // Dark / Slate Engineering Theme
        public static readonly Color SidebarBg = Color.FromArgb(24, 30, 42);
        public static readonly Color SidebarActive = Color.FromArgb(41, 128, 185);
        public static readonly Color SidebarHover = Color.FromArgb(36, 45, 62);
        public static readonly Color WindowBg = Color.FromArgb(245, 247, 250);
        public static readonly Color CardBg = Color.White;
        public static readonly Color BorderColor = Color.FromArgb(220, 224, 230);

        public static readonly Color Primary = Color.FromArgb(24, 119, 242);
        public static readonly Color PrimaryHover = Color.FromArgb(10, 100, 220);
        public static readonly Color Success = Color.FromArgb(39, 174, 96);
        public static readonly Color Warning = Color.FromArgb(243, 156, 18);
        public static readonly Color Danger = Color.FromArgb(231, 76, 60);

        public static readonly Color TextDark = Color.FromArgb(33, 37, 41);
        public static readonly Color TextMuted = Color.FromArgb(108, 117, 125);
        public static readonly Color TextLight = Color.FromArgb(240, 242, 245);

        public static readonly Font HeaderFont = new Font("Segoe UI", 12f, FontStyle.Bold);
        public static readonly Font SubheaderFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        public static readonly Font BodyFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        public static readonly Font BoldFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        public static readonly Font SmallFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        public static readonly Font MonospaceFont = new Font("Consolas", 9.5f, FontStyle.Regular);

        public static Button CreatePrimaryButton(string text, int width = 160, int height = 36)
        {
            var btn = new Button
            {
                Text = text,
                Width = width,
                Height = height,
                BackColor = Primary,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = BoldFont,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.MouseEnter += (s, e) => btn.BackColor = PrimaryHover;
            btn.MouseLeave += (s, e) => btn.BackColor = Primary;
            return btn;
        }

        public static Button CreateSecondaryButton(string text, int width = 140, int height = 36)
        {
            var btn = new Button
            {
                Text = text,
                Width = width,
                Height = height,
                BackColor = Color.White,
                ForeColor = TextDark,
                FlatStyle = FlatStyle.Flat,
                Font = BoldFont,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = BorderColor;
            btn.FlatAppearance.BorderSize = 1;
            btn.MouseEnter += (s, e) => btn.BackColor = Color.FromArgb(240, 242, 245);
            btn.MouseLeave += (s, e) => btn.BackColor = Color.White;
            return btn;
        }

        public static Button CreateSuccessButton(string text, int width = 160, int height = 36)
        {
            var btn = new Button
            {
                Text = text,
                Width = width,
                Height = height,
                BackColor = Success,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = BoldFont,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.MouseEnter += (s, e) => btn.BackColor = Color.FromArgb(30, 150, 80);
            btn.MouseLeave += (s, e) => btn.BackColor = Success;
            return btn;
        }

        public static Button CreateDangerButton(string text, int width = 160, int height = 36)
        {
            var btn = new Button
            {
                Text = text,
                Width = width,
                Height = height,
                BackColor = Danger,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = BoldFont,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.MouseEnter += (s, e) => btn.BackColor = Color.FromArgb(200, 50, 40);
            btn.MouseLeave += (s, e) => btn.BackColor = Danger;
            return btn;
        }

        public static DataGridView CreateStyledGrid()
        {
            var g = new DataGridView
            {
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(235, 238, 242),
                RowHeadersVisible = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                Font = BodyFont,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(245, 247, 250);
            g.ColumnHeadersDefaultCellStyle.ForeColor = TextDark;
            g.ColumnHeadersDefaultCellStyle.Font = BoldFont;
            g.ColumnHeadersHeight = 36;
            g.RowTemplate.Height = 32;
            g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(220, 235, 252);
            g.DefaultCellStyle.SelectionForeColor = TextDark;
            return g;
        }
    }
}
