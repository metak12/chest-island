using System;
using System.Diagnostics;
using System.IO;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

internal static class Setup
{
    private const string AppName = "Chest Island";
    private static readonly string[] Files = { "Chest Island.exe", "ChestIsland.xaml", "Live.ps1", "Overlay.ps1" };

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--extract-test")
            {
                Extract(Path.GetFullPath(args[1]));
                return 0;
            }

            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
            string app = Path.Combine(directory, Files[0]);
            foreach (var process in Process.GetProcessesByName(AppName))
                using (process)
                    if (string.Equals(process.MainModule.FileName, app, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Закрой Chest Island и запусти установщик снова.");

            ShowInstaller(directory, app);
            return 0;
        }
        catch (Exception ex)
        {
            if (args.Length == 0) MessageBox.Show(ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void ShowInstaller(string directory, string app)
    {
        Application.EnableVisualStyles();
        using (var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
        using (var form = new Form())
        {
            form.Icon = icon;
            form.Text = "Chest Island · Установка";
            form.ClientSize = new Size(540, 345);
            form.StartPosition = FormStartPosition.CenterScreen;
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.MaximizeBox = false;
            form.BackColor = Color.FromArgb(18, 17, 26);
            form.ForeColor = Color.FromArgb(245, 239, 255);
            form.Font = new Font("Segoe UI", 10);
            var hero = new Panel { Bounds = new Rectangle(22, 22, 496, 142) };
            hero.Paint += (sender, args) =>
            {
                using (var gradient = new System.Drawing.Drawing2D.LinearGradientBrush(hero.ClientRectangle,
                    Color.FromArgb(73, 44, 107), Color.FromArgb(36, 29, 59), 22f))
                    args.Graphics.FillRectangle(gradient, hero.ClientRectangle);
            };
            if (icon != null) {
                    hero.Paint += (sender, args) => args.Graphics.DrawIcon(icon, new Rectangle(24, 37, 48, 48));
            }
            hero.Controls.Add(Label("Chest Island", 86, 22, 390, 59, 34));
            hero.Controls.Add(Label("Счётчик сундуков Fortnite", 91, 84, 380, 28, 13));
            form.Controls.Add(hero);
            form.Controls.Add(Label("Установка для текущего пользователя", 22, 183, 490, 29, 13));
            var status = Label("Появятся ярлыки на рабочем столе и в меню «Пуск».", 22, 218, 490, 38, 10);
            form.Controls.Add(status);
            var close = new Button { Text = "Закрыть", Bounds = new Rectangle(267, 281, 109, 41), FlatStyle = FlatStyle.Flat };
            var install = new Button
            {
                Text = File.Exists(app) ? "Обновить" : "Установить",
                Bounds = new Rectangle(387, 281, 131, 41), FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(30, 19, 48), BackColor = Color.FromArgb(216, 185, 255)
            };
            close.Click += (sender, args) => form.Close();
            bool installed = false;
            install.Click += (sender, args) =>
            {
                try
                {
                    if (installed)
                    {
                        Process.Start(new ProcessStartInfo(app) { WorkingDirectory = directory });
                        form.Close();
                        return;
                    }
                    Extract(directory);
                    Shortcut(Environment.SpecialFolder.DesktopDirectory, app, directory);
                    Shortcut(Environment.SpecialFolder.Programs, app, directory);
                    installed = true;
                    status.Text = "Готово. Можно открыть Chest Island.";
                    install.Text = "Открыть";
                }
                catch (Exception ex) { status.Text = ex.Message; }
            };
            form.AcceptButton = install;
            form.Controls.Add(close);
            form.Controls.Add(install);
            Application.Run(form);
        }
    }

    private static Label Label(string text, int x, int y, int width, int height, int size)
    {
        return new Label
        {
            Text = text, Bounds = new Rectangle(x, y, width, height),
            BackColor = Color.Transparent, Font = new Font("Segoe UI", size)
        };
    }

    private static void Extract(string directory)
    {
        Directory.CreateDirectory(directory);
        var assembly = Assembly.GetExecutingAssembly();
        foreach (string name in Files)
        {
            string path = Path.Combine(directory, name);
            string pending = path + ".setup-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var resource = assembly.GetManifestResourceStream(name))
                {
                    if (resource == null) throw new InvalidDataException("В установщике отсутствует " + name);
                    using (var output = File.Create(pending)) resource.CopyTo(output);
                }
                if (File.Exists(path)) File.Replace(pending, path, null);
                else File.Move(pending, path);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }
    }

    private static void Shortcut(Environment.SpecialFolder destination, string target, string directory)
    {
        string link = Path.Combine(Environment.GetFolderPath(destination), AppName + ".lnk");
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        dynamic shortcut = shell.CreateShortcut(link);
        shortcut.TargetPath = target;
        shortcut.WorkingDirectory = directory;
        shortcut.Description = "Счётчик сундуков Fortnite";
        shortcut.Save();
    }
}
