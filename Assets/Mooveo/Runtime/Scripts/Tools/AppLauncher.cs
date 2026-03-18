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
}
