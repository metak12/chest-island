using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Controls;

internal sealed partial class ChestIsland
{
    private DateTime liveFirstSeen, overlayFirstSeen;

    private string WorkerState(string statusName, string script, out string problem)
    {
        problem = "Версия процесса не подтверждена";
        try
        {
            var fields = File.ReadAllLines(Path.Combine(data, statusName));
            int pid;
            DateTimeOffset stamp;
            if (fields.Length != 5 || !int.TryParse(fields[1], out pid) || pid <= 0 ||
                !DateTimeOffset.TryParse(fields[4], CultureInfo.InvariantCulture, DateTimeStyles.None, out stamp) ||
                DateTimeOffset.UtcNow - stamp > TimeSpan.FromSeconds(8) || stamp > DateTimeOffset.UtcNow.AddSeconds(2))
                return null;
            using (var worker = Process.GetProcessById(pid))
                if (worker.HasExited || !string.Equals(worker.ProcessName, "powershell", StringComparison.OrdinalIgnoreCase)) return null;
            if (!string.Equals(fields[0], ReleaseUpdate.VersionText, StringComparison.Ordinal))
            {
                problem = "Запущена другая версия: " + fields[0];
                return null;
            }
            if (!string.Equals(fields[2], Path.Combine(folder, script), StringComparison.OrdinalIgnoreCase))
            {
                problem = "Запущено из другой папки";
                return null;
            }
            problem = null;
            return fields[3];
        }
        catch (IOException) { return null; }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    private void ShowWorkerStatus(bool running, bool overlay)
    {
        var label = Find<TextBlock>(overlay ? "OverlayStatus" : "LiveStatus");
        if (!running)
        {
            if (overlay) overlayFirstSeen = DateTime.MinValue; else liveFirstSeen = DateTime.MinValue;
            label.Text = overlay ? "Скрыт" : "Остановлено";
            label.ToolTip = null;
            return;
        }
        DateTime firstSeen = overlay ? overlayFirstSeen : liveFirstSeen;
        if (firstSeen == DateTime.MinValue)
        {
            firstSeen = DateTime.UtcNow;
            if (overlay) overlayFirstSeen = firstSeen; else liveFirstSeen = firstSeen;
        }
        string problem;
        string state = WorkerState(overlay ? "overlay.worker" : "live-total.txt.worker", overlay ? "Overlay.ps1" : "Live.ps1", out problem);
        if (state == null)
        {
            bool starting = DateTime.UtcNow - firstSeen < TimeSpan.FromSeconds(4) && problem == "Версия процесса не подтверждена";
            label.Text = starting ? "Проверяю версию процесса…" : problem;
            label.ToolTip = starting ? null : "Останови прежний процесс и запусти его из этой версии Chest Island.";
            return;
        }
        label.ToolTip = "Проверена версия " + ReleaseUpdate.VersionText;
        switch (state)
        {
            case "game-closed": label.Text = "Ожидание запуска Fortnite"; break;
            case "background": label.Text = "Fortnite не в фокусе"; break;
            case "minimized": label.Text = "Fortnite свёрнут"; break;
            case "capture-error": label.Text = "Не удалось получить кадр"; break;
            case "spectating": label.Text = "Пауза · наблюдение"; break;
            case "other-toast": label.Text = "Уведомление без слова «сундук»"; break;
            case "chest": label.Text = "Уведомление о сундуке"; break;
            case "visible": label.Text = "Виден в Fortnite"; break;
            default: label.Text = overlay ? "Жду активное окно Fortnite" : "Жду уведомление о сундуке"; break;
        }
    }
}
