using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

internal static class Launcher
{
    [STAThread]
    private static void Main()
    {
        try
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            string game = Path.Combine(root, "DragonBoy CommunityMod.exe");
            if (!File.Exists(game))
            {
                MessageBox.Show("Không tìm thấy file game gốc.", "NRO PRIVATE", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = game,
                WorkingDirectory = root,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "NRO PRIVATE", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
