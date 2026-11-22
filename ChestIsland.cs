using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

internal sealed class ChestIsland
{
    private readonly string folder = AppDomain.CurrentDomain.BaseDirectory;
    private readonly string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FortniteChestCounter");
    private readonly Window window;
    private readonly DispatcherTimer timer;
    private long? initial;
    private int? previous;
    private Process editor;
    private ReleaseUpdate update;

    [STAThread]
    private static void Main()
    {
        bool first;
        using (var instance = new Mutex(true, @"Local\ChestIslandDashboard", out first))
        {
            if (!first) { MessageBox.Show("Chest Island уже открыт.", "Chest Island"); return; }
            try { new Application().Run(new ChestIsland().window); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Chest Island", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }

    private ChestIsland()
    {
        using (var layout = File.OpenRead(Path.Combine(folder, "ChestIsland.xaml")))
            window = (Window)XamlReader.Load(layout);
        Find<Button>("LiveToggle").Click += (s, e) => Toggle("Live.ps1", IsLive);
        Find<Button>("OverlayToggle").Click += (s, e) => Toggle("Overlay.ps1", IsOverlay);
        Find<Button>("EditCount").Click += (s, e) => EditCount();
        Find<Button>("MoveOverlay").Click += (s, e) => Move();
        Find<Button>("UpdateButton").Click += async (s, e) => await UpdateClick();
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        timer.Tick += (s, e) => Refresh();
        window.Loaded += (s, e) =>
        {
            if (SystemParameters.ClientAreaAnimation)
                Find<Grid>("Dashboard").BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)));
            timer.Start();
            CheckForUpdates(false);
        };
        window.Closed += (s, e) => timer.Stop();
        Refresh();
    }

    private T Find<T>(string name) where T : class { return (T)window.FindName(name); }
    private void Say(string message) { Find<TextBlock>("Feedback").Text = message; }

    private bool IsLive()
    {
        string path = Path.Combine(data, "live-total.txt.lock");
        if (!File.Exists(path)) return false;
        try { using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) return false; }
        catch (IOException ex) { return (ex.HResult & 0xffff) == 32 || (ex.HResult & 0xffff) == 33; }
    }

    private static bool IsOverlay()
    {
        Mutex mutex;
        if (!Mutex.TryOpenExisting(@"Local\FortniteChestCounterOverlay", out mutex)) return false;
        mutex.Dispose();
        return true;
    }

    private void Refresh()
    {
        bool live = IsLive(), overlay = IsOverlay();
        var game = Process.GetProcessesByName("FortniteClient-Win64-Shipping");
        Find<TextBlock>("Game").Text = game.Length > 0 ? "●  Fortnite открыт" : "○  Fortnite закрыт";
        foreach (var process in game) process.Dispose();
        Find<TextBlock>("LiveStatus").Text = live ? "Включено" : "Остановлено";
        Find<TextBlock>("OverlayStatus").Text = overlay ? "Включён · виден в Fortnite" : "Скрыт";
        Find<Button>("LiveToggle").Content = live ? "Остановить" : "Запустить";
        Find<Button>("OverlayToggle").Content = overlay ? "Скрыть" : "Показать";
        Find<Button>("MoveOverlay").IsEnabled = editor == null || editor.HasExited;

        try
        {
            var types = File.ReadAllText(Path.Combine(data, "live-total.txt.types")).Trim().Split(',');
            long rare, spirit;
            if (types.Length == 2 && long.TryParse(types[0], out rare) && long.TryParse(types[1], out spirit))
                Find<TextBlock>("Types").Text = string.Format("Редкие: {0}   ·   С духом: {1}", rare, spirit);
        }
        catch (IOException) { }
        int value;
        try { if (!int.TryParse(File.ReadAllText(Path.Combine(data, "live-total.txt")), out value)) return; }
        catch (IOException) { return; }
        if (!initial.HasValue) initial = value;
        if (previous == value) return;
        var count = Find<TextBlock>("Count");
        count.Text = value.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"));
        Find<TextBlock>("Session").Text = string.Format("За время работы приложения: {0:+0;-0;0}", value - initial.Value);
        if (previous.HasValue && SystemParameters.ClientAreaAnimation)
        {
            var scale = (ScaleTransform)count.RenderTransform;
            var animation = new DoubleAnimation(1.12, 1, TimeSpan.FromMilliseconds(290))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }
        previous = value;
    }

    private void Toggle(string script, Func<bool> active)
    {
        try
        {
            if (active())
            {
                if (Stop(script)) Say("Остановлено.");
                else Say("Закрой прежнее окно запуска, чтобы остановить этот процесс.");
                Refresh();
                return;
            }
            var process = Start(script, false);
            Say("Запускаю…");
            var button = Find<Button>(script == "Live.ps1" ? "LiveToggle" : "OverlayToggle");
            button.IsEnabled = false;
            var ready = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            ready.Tick += (s, e) =>
            {
                ready.Stop();
                button.IsEnabled = true;
                if (Find<TextBlock>("Feedback").Text == "Запускаю…")
                    Say(active() ? "Готово. Вернись в Fortnite." : "Запуск не подтвердился. Проверь окно запуска.");
                process.Dispose();
                Refresh();
            };
            ready.Start();
        }
        catch (Exception ex) { Say(ex.Message); }
    }

    private async void CheckForUpdates(bool manual)
    {
        var button = Find<Button>("UpdateButton");
        button.IsEnabled = false;
        try
        {
            update = await ReleaseUpdate.Check();
            if (update != null) button.Content = "Обновить до " + update.Version;
            else if (manual) Say("Установлена актуальная версия " + ReleaseUpdate.VersionText + ".");
        }
        catch (Exception ex)
        {
            if (manual) Say("Не удалось проверить обновления. " + ex.Message);
        }
        finally { button.IsEnabled = true; }
    }

    private async System.Threading.Tasks.Task UpdateClick()
    {
        if (update == null) { CheckForUpdates(true); return; }
        var button = Find<Button>("UpdateButton");
        button.IsEnabled = false;
        Say("Скачиваю и проверяю установщик…");
        try
        {
            var selected = update;
            string path = await System.Threading.Tasks.Task.Run(() => selected.Download());
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            window.Close();
        }
        catch (Exception ex) { Say("Не удалось запустить обновление. " + ex.Message); }
        finally { button.IsEnabled = true; }
    }

    private void EditCount()
    {
        var dialog = new Window
        {
            Title = "Изменить счёт", Owner = window, Width = 360, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = window.Background, Foreground = window.Foreground, ShowInTaskbar = false
        };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Resources = window.Resources;
        panel.Children.Add(new TextBlock { Text = "Сколько сундуков уже открыто?", FontSize = 17 });
        var entry = new TextBox
        {
            Text = previous.GetValueOrDefault(0).ToString(CultureInfo.InvariantCulture),
            MaxLength = 10, FontSize = 22, Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(8), Background = new SolidColorBrush(Color.FromRgb(39, 34, 52)),
            Foreground = window.Foreground
        };
        panel.Children.Add(entry);
        var error = new TextBlock { Foreground = Brushes.LightPink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 9, 0, 0) };
        var cancel = new Button { Content = "Отмена", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = "Сохранить", IsDefault = true };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.Loaded += (s, e) => { entry.Focus(); entry.SelectAll(); };
        save.Click += (s, e) =>
        {
            int value;
            if (!int.TryParse(entry.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value))
            {
                error.Text = "Введи целое число от 0 до 2 147 483 647.";
                entry.Focus();
                return;
            }
            try
            {
                int before = SaveCount(value);
                if (initial.HasValue) initial += value - before;
                dialog.DialogResult = true;
                Refresh();
                Say("Счёт сохранён.");
            }
            catch (Exception ex) { error.Text = ex.Message; }
        };
        dialog.ShowDialog();
    }

    private int SaveCount(int value)
    {
        string path = Path.Combine(data, "live-total.txt");
        Directory.CreateDirectory(data);
        using (var writeLock = new Mutex(false, @"Local\FortniteChestTotalWrite"))
        {
            try
            {
                if (!writeLock.WaitOne(TimeSpan.FromSeconds(2)))
                    throw new IOException("Счётчик занят. Нажми «Сохранить» ещё раз.");
            }
            catch (AbandonedMutexException) { }
            string temp = path + ".manual";
            try
            {
                bool exists = File.Exists(path);
                int before = exists ? int.Parse(File.ReadAllText(path), CultureInfo.InvariantCulture) : 0;
                File.WriteAllText(temp, value.ToString(CultureInfo.InvariantCulture));
                if (exists) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
                return before;
            }
            finally
            {
                writeLock.ReleaseMutex();
            }
        }
    }

    private void Move()
    {
        try
        {
            if (editor != null) editor.Dispose();
            editor = Start("Overlay.ps1", true);
            Say("Перетащи окно настройки и нажми «Сохранить позицию».");
            Refresh();
        }
        catch (Exception ex) { Say(ex.Message); }
    }

    private Process Start(string script, bool move)
    {
        string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        string args = "-NoProfile -STA -ExecutionPolicy Bypass -File \"" + Path.Combine(folder, script) + "\"" + (move ? " -Move" : "");
        if (move)
            return Process.Start(new ProcessStartInfo(exe, args) { WorkingDirectory = folder, WindowStyle = ProcessWindowStyle.Normal });
        return Process.Start(new ProcessStartInfo(exe, args) { WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true });
    }

    private bool Stop(string script)
    {
        var pids = new System.Collections.Generic.List<int>();
        string pattern = @"(?i)-File\s+""?" + Regex.Escape(Path.Combine(folder, script)) + @"""?\s*$";
        using (var search = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='powershell.exe'"))
        using (var rows = search.Get())
            foreach (ManagementObject row in rows)
                using (row)
                    if (Regex.IsMatch(Convert.ToString(row["CommandLine"]), pattern))
                        pids.Add(Convert.ToInt32(row["ProcessId"]));
        if (pids.Count != 1) return false;
        try { using (var worker = Process.GetProcessById(pids[0])) worker.Kill(); }
        catch (ArgumentException) { }
        return true;
    }
}
