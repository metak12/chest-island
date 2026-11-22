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
            if (args.Length >= 2 && args[0] == "--apply-update")
            {
                ApplyUpdate(directory, app, args);
                return 0;
            }
            foreach (var process in Process.GetProcessesByName(AppName))
                using (process)
                    if (string.Equals(process.MainModule.FileName, app, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Закрой Chest Island и запусти установщик снова.");

            ShowInstaller(directory, app);
            return 0;
        }
        catch (Exception ex)
        {
            if (args.Length == 0 || args[0] == "--apply-update") MessageBox.Show(ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void ApplyUpdate(string directory, string app, string[] args)
    {
        int id;
        if (!int.TryParse(args[1], out id) || id <= 0 || !File.Exists(app))
            throw new InvalidOperationException("Не найдена установленная копия Chest Island.");

        Process parent = null;
        try { parent = Process.GetProcessById(id); }
        catch (ArgumentException) { }
        using (parent)
        {
            if (parent != null && !parent.HasExited)
            {
                if (!string.Equals(parent.MainModule.FileName, app, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Процесс обновления не принадлежит Chest Island.");
                if (!parent.WaitForExit(15000))
                    throw new InvalidOperationException("Chest Island не успел закрыться. Повтори обновление.");
            }
        }
        string resume = (Array.IndexOf(args, "--resume-live") >= 0 ? "--resume-live " : "") +
                        (Array.IndexOf(args, "--resume-overlay") >= 0 ? "--resume-overlay" : "");
        try { ReplaceInstalledFiles(directory); }
        catch
        {
            if (File.Exists(app)) Process.Start(new ProcessStartInfo(app, resume) { WorkingDirectory = directory });
            throw;
        }
        Process.Start(new ProcessStartInfo(app, resume) { WorkingDirectory = directory });
    }

    private static void ReplaceInstalledFiles(string directory)
    {
        string stage = Path.Combine(directory, ".update-" + Guid.NewGuid().ToString("N"));
        var committed = new System.Collections.Generic.List<string>();
        bool keepBackups = false;
        try
        {
            Extract(stage);
            // Replace the executable last. Backups let the previous version restart if a file is locked.
            for (int i = Files.Length - 1; i >= 0; i--)
            {
                string name = Files[i], destination = Path.Combine(directory, name);
                string pending = Path.Combine(stage, name), backup = pending + ".previous";
                if (File.Exists(destination)) File.Replace(pending, destination, backup);
                else File.Move(pending, destination);
                committed.Add(name);
            }
        }
        catch
        {
            try
            {
                committed.Reverse();
                foreach (string name in committed)
                {
                    string target = Path.Combine(directory, name);
                    string backup = Path.Combine(stage, name + ".previous");
                    if (File.Exists(backup)) File.Replace(backup, target, null);
                    else File.Delete(target);
                }
            }
            catch { keepBackups = true; throw; }
            throw;
        }
        finally
        {
            if (!keepBackups && Directory.Exists(stage))
            {
                foreach (string name in Files)
                {
                    TryDelete(Path.Combine(stage, name));
                    TryDelete(Path.Combine(stage, name + ".previous"));
                }
                try { Directory.Delete(stage); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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
