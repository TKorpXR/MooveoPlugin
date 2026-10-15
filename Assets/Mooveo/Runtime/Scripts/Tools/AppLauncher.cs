using System.Diagnostics;
using System.IO;

public static class AppLauncher
{
    public static bool IsProcessLaunched(string processName)
    {
        Process[] processes = Process.GetProcessesByName(processName);
        foreach (var process in processes)
        {
            if (process.ProcessName == processName) return true;
        }

        return false;
    }

    public static bool LaunchProcess(string path)
    {
        if (!File.Exists(path))
        {
            UnityEngine.Debug.LogError($"[AppLauncher] Fichier introuvable : {path}");
            return false;
        }
        
        // SteamVR : sa configuration doit correspondre au profil matériel AVANT qu'il ne démarre.
        // Posé ICI, dans le seul lanceur, pour couvrir tous les chemins : boucle de calibration,
        // bouton de l'écran de calibration, réparation « Lancer SteamVR ».
        if (IsSteamVRLauncher(path)) SteamVRBootstrap.EnsureBeforeLaunch();

        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = path;
            startInfo.WorkingDirectory = Path.GetDirectoryName(path);
            
            Process process = Process.Start(startInfo);

            if (process == null) return false;
            if (process.HasExited) return false;

            return true;
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"[AppLauncher] Impossible de lancer {path} : {e.Message}");
            return false;
        }
    }

    private static bool IsSteamVRLauncher(string path)
    {
        string name = Path.GetFileName(path);
        return string.Equals(name, "vrmonitor.exe", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "vrstartup.exe", System.StringComparison.OrdinalIgnoreCase);
    }
}
