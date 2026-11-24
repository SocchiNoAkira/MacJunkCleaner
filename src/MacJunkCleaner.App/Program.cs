using System.Drawing.Imaging;

namespace MacJunkCleaner.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // 冒烟自检：只构造窗体，不显示，用于验证界面初始化不抛异常。
        if (args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            using var form = new MainForm();
            return;
        }

        // 离屏截图：不显示窗口，仅把界面渲染成 PNG，用于检查排版。
        var shotIndex = Array.FindIndex(args, a => string.Equals(a, "--screenshot", StringComparison.OrdinalIgnoreCase));
        if (shotIndex >= 0 && shotIndex + 1 < args.Length)
        {
            using var form = new MainForm
            {
                SuppressActivation = true,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
            };

            form.Show();
            form.PerformLayout();
            for (var i = 0; i < 8; i++)
            {
                Application.DoEvents();
                Thread.Sleep(40);
            }

            form.PerformLayout();

            var size = form.Size;
            using var bitmap = new Bitmap(size.Width, size.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
            bitmap.Save(args[shotIndex + 1], ImageFormat.Png);
            form.Close();
            return;
        }

        Application.ThreadException += (_, e) => ShowFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowFatal(e.ExceptionObject as Exception);

        Application.Run(new MainForm());
    }

    private static void ShowFatal(Exception? ex)
    {
        MessageBox.Show(
            $"程序出现异常：\n{ex?.Message}\n\n{ex?.StackTrace}",
            "错误",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
