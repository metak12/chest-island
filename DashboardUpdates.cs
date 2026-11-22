using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

internal sealed partial class ChestIsland
{
    private Task<ReleaseUpdate> updateCheck;
    private string updateError;
    private bool updateApplying;
    private bool deferUpdate;
    private DateTime lastUpdateCheck;

    private async Task CheckForUpdates(bool manual)
    {
        if (updateApplying) return;
        if (!manual && updateCheck == null && DateTime.UtcNow - lastUpdateCheck < TimeSpan.FromMinutes(2)) return;
        var button = Find<Button>("UpdateButton");
        if (updateCheck == null)
        {
            button.IsEnabled = false;
            updateCheck = ReleaseUpdate.Check();
        }
        var pending = updateCheck;
        try
        {
            update = await pending;
            updateError = null;
            button.Content = update == null ? "Проверить обновления" : "Обновить до " + update.Version;
            if (manual && update == null) Say("Установлена актуальная версия " + ReleaseUpdate.VersionText + ".");
        }
        catch (Exception ex)
        {
            updateError = ex.Message;
            if (manual) Say("Не удалось проверить обновления. " + updateError);
        }
        finally
        {
            if (ReferenceEquals(updateCheck, pending))
            {
                lastUpdateCheck = DateTime.UtcNow;
                updateCheck = null;
                button.IsEnabled = !updateApplying;
            }
        }
    }

    private async Task<bool> AllowStart(bool automatic)
    {
        if (updateApplying || exiting) return false;
        if (!automatic && DateTime.UtcNow - lastUpdateCheck >= TimeSpan.FromMinutes(2))
            Say("Проверяю обновления перед запуском…");
        await CheckForUpdates(false);
        if (updateApplying || exiting) return false;
        if (update == null || deferUpdate) return true;

        if (automatic && !window.IsVisible)
        {
            Say("Доступно обновление " + update.Version + ". Открой Chest Island, чтобы обновиться или продолжить.");
            if (tray.Visible) tray.ShowBalloonTip(6000, "Обновление Chest Island", "Открой приложение, чтобы обновиться или запустить счётчик.", Forms.ToolTipIcon.Info);
            return false;
        }
        if (automatic)
        {
            Say("Доступно обновление " + update.Version + ". Выбери «Обновить» или запусти счётчик вручную.");
            return false;
        }
        if (MessageBox.Show(window, "Доступна версия " + update.Version + ". Обновить сейчас?\n\n«Нет» — запустить счётчик с текущей версией.",
            "Обновление Chest Island", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            await UpdateClick();
            return false;
        }
        deferUpdate = true;
        return true;
    }

    private async Task UpdateClick()
    {
        if (updateApplying) return;
        if (update == null)
        {
            await CheckForUpdates(true);
            return;
        }
        var button = Find<Button>("UpdateButton");
        updateApplying = true;
        button.IsEnabled = false;
        Say("Скачиваю обновление и проверяю SHA-256…");
        bool resumeLive = false, resumeOverlay = false;
        try
        {
            var selected = update;
            string path = await Task.Run(() => selected.Download());
            if (exiting) return;

            string install = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Chest Island");
            bool installed = string.Equals(folder.TrimEnd(Path.DirectorySeparatorChar), install, StringComparison.OrdinalIgnoreCase);
            string args = "";
            if (installed)
            {
                // Stop only scripts from this installation. Another copy may still hold the shared lock.
                if (IsLive())
                {
                    if (!Stop("Live.ps1")) throw new IOException("Останови распознавание, запущенное из другой папки.");
                    resumeLive = true;
                }
                if (IsOverlay())
                {
                    if (!Stop("Overlay.ps1")) throw new IOException("Скрой оверлей, запущенный из другой папки.");
                    resumeOverlay = true;
                }
                using (var current = Process.GetCurrentProcess())
                    args = "--apply-update " + current.Id + (resumeLive ? " --resume-live" : "") + (resumeOverlay ? " --resume-overlay" : "");
            }
            Say("Устанавливаю обновление. Chest Island перезапустится…");
            Process.Start(new ProcessStartInfo(path, args) { UseShellExecute = true });
            exiting = true;
            window.Close();
        }
        catch (Exception ex)
        {
            Say("Не удалось обновить Chest Island. " + ex.Message);
            if (resumeLive && !IsLive()) using (Start("Live.ps1", false)) { }
            if (resumeOverlay && !IsOverlay()) using (Start("Overlay.ps1", false)) { }
        }
        finally
        {
            updateApplying = false;
            button.IsEnabled = true;
        }
    }
}
