using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace WallpaperScheduler
{
    static class Theme
    {
        public static readonly Color Header = Color.FromArgb(22, 50, 79);
        public static readonly Color HeaderSub = Color.FromArgb(198, 216, 232);
        public static readonly Color AppBack = Color.FromArgb(244, 246, 248);
        public static readonly Color CardBorder = Color.FromArgb(213, 220, 227);
        public static readonly Color Text = Color.FromArgb(28, 36, 48);
        public static readonly Color Muted = Color.FromArgb(92, 107, 122);
        public static readonly Color Accent = Color.FromArgb(47, 111, 237);
        public static readonly Color AccentHover = Color.FromArgb(36, 94, 214);
        public static readonly Color Good = Color.FromArgb(14, 122, 61);
        public static readonly Color Bad = Color.FromArgb(161, 38, 34);
        public static readonly Color InfoBack = Color.FromArgb(232, 240, 254);
        public static readonly Color Line = Color.FromArgb(226, 232, 238);
        public static readonly Color Select = Color.FromArgb(217, 230, 251);
    }

    static class PictureFiles
    {
        public static readonly string[] Extensions = { ".jpg", ".jpeg", ".jfif", ".png", ".bmp", ".dib", ".gif", ".tif", ".tiff" };

        public const string Filter = "Pictures (jpg, png, bmp, gif, tiff)|*.jpg;*.jpeg;*.jfif;*.png;*.bmp;*.dib;*.gif;*.tif;*.tiff|All files|*.*";

        public static bool IsKnown(string path)
        {
            string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
            foreach (var known in Extensions)
            {
                if (ext == known) return true;
            }
            return false;
        }
    }

    static class Ui
    {
        public static Font BaseFont = new Font("Segoe UI", 9f);
        public static Font BoldFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        public static Font TitleFont = new Font("Segoe UI", 16f, FontStyle.Bold);

        public static Button Primary(string text, int width)
        {
            var button = new Button();
            button.Text = text;
            button.Width = width;
            button.Height = 32;
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Theme.Accent;
            button.ForeColor = Color.White;
            button.Font = BoldFont;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Theme.AccentHover;
            button.Cursor = Cursors.Hand;
            return button;
        }

        public static Button Secondary(string text, int width)
        {
            var button = new Button();
            button.Text = text;
            button.Width = width;
            button.Height = 32;
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Color.White;
            button.ForeColor = Theme.Text;
            button.Font = BaseFont;
            button.FlatAppearance.BorderColor = Theme.CardBorder;
            button.FlatAppearance.BorderSize = 1;
            button.Cursor = Cursors.Hand;
            return button;
        }

        public static Label Caption(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = Theme.Text,
                Font = BoldFont,
                Margin = new Padding(0, 10, 0, 4)
            };
        }

        public static Bitmap LoadCover(string path, int width, int height)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || width < 1 || height < 1)
                return null;
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var image = Image.FromStream(stream))
                {
                    var bitmap = new Bitmap(width, height);
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.Clear(Color.FromArgb(232, 236, 240));
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        float scale = Math.Max((float)width / image.Width, (float)height / image.Height);
                        int drawWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
                        int drawHeight = Math.Max(1, (int)Math.Round(image.Height * scale));
                        graphics.DrawImage(image, (width - drawWidth) / 2, (height - drawHeight) / 2, drawWidth, drawHeight);
                    }
                    return bitmap;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
