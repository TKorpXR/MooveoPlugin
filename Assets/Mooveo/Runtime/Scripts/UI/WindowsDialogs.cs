using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Boîtes de dialogue WINDOWS natives : choix d'un dossier, choix d'un exécutable, réduction de la
/// fenêtre du jeu.
///
/// ⚠ Trois règles, toutes nécessaires :
/// - la boîte est POSSÉDÉE par la fenêtre GraffWall (handle relevé sur le fil principal). Le jeu est
///   en plein écran FENÊTRÉ (<c>fullscreenMode: 1</c>) : une boîte possédée s'affiche donc devant.
///   En plein écran EXCLUSIF elle forcerait la réduction du jeu — à revoir si ce réglage change ;
/// - elle s'ouvre sur un fil STA DÉDIÉ. Sur le fil principal, elle figerait l'image au projecteur et
///   SteamVR pendant tout le choix ;
/// - le sélecteur moderne (<c>IFileOpenDialog</c>) passe par l'interop COM, qui n'a pas les mêmes
///   garanties sous IL2CPP que sous Mono : en cas d'échec, repli sur <c>SHBrowseForFolder</c>, en
///   P/Invoke pur. À valider sur un BUILD, pas seulement en éditeur.
/// </summary>
public static class WindowsDialogs
{
    public static bool Supported =>
        Application.platform == RuntimePlatform.WindowsPlayer ||
        Application.platform == RuntimePlatform.WindowsEditor;

    private static IntPtr _gameWindow;

    /// <summary>Relève la fenêtre du jeu. FIL PRINCIPAL uniquement : GetActiveWindow est par fil.</summary>
    private static IntPtr CaptureGameWindow()
    {
        if (!Supported) return IntPtr.Zero;
        IntPtr handle = GetActiveWindow();
        // Jeu pas au premier plan : sa fenêtre principale, JAMAIS celle du premier plan, qui peut
        // appartenir à une autre application — la boîte lui serait rattachée.
        if (handle == IntPtr.Zero)
        {
            try { handle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle; }
            catch { /* processus inaccessible */ }
        }
        if (handle != IntPtr.Zero) _gameWindow = handle;
        return _gameWindow;
    }

    /// <summary>Choix d'un dossier. Résultat null si l'opérateur annule.</summary>
    public static Task<string> PickFolderAsync(string title, string startFolder)
    {
        if (!Supported) return Task.FromResult<string>(null);
        IntPtr owner = CaptureGameWindow();

        return RunOnStaThread(() =>
        {
            try { return PickFolderModern(owner, title, startFolder); }
            catch (Exception e)
            {
                Debug.LogWarning($"[WindowsDialogs] Sélecteur moderne indisponible ({e.Message}), repli.");
                return PickFolderLegacy(owner, title);
            }
        });
    }

    /// <summary>Choix d'un fichier .exe. Résultat null si l'opérateur annule.</summary>
    public static Task<string> PickExecutableAsync(string title, string startFolder)
    {
        if (!Supported) return Task.FromResult<string>(null);
        IntPtr owner = CaptureGameWindow();
        return RunOnStaThread(() => PickFile(owner, title, startFolder, "Programmes\0*.exe\0\0"));
    }

    /// <summary>
    /// Réduit la fenêtre du jeu, pour laisser voir le navigateur ou l'installeur ouvert derrière.
    /// Sans effet en éditeur : réduire l'éditeur entier serait une gêne, pas une aide.
    /// </summary>
    public static void MinimizeGame()
    {
        if (!Supported || Application.isEditor) return;
        IntPtr window = CaptureGameWindow();
        if (window != IntPtr.Zero) ShowWindow(window, SW_MINIMIZE);
    }

    // =========================================================================
    // Fil STA
    // =========================================================================

    private static Task<string> RunOnStaThread(Func<string> body)
    {
        var done = new TaskCompletionSource<string>();

        var thread = new Thread(() =>
        {
            // Initialisation COM explicite : SetApartmentState n'est pas garanti sous tous les runtimes.
            bool initialized = false;
            try
            {
                initialized = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED) >= 0;
                done.SetResult(body());
            }
            catch (Exception e) { done.TrySetException(e); }
            finally { if (initialized) CoUninitialize(); }
        })
        { IsBackground = true, Name = "Mooveo dialog" };

        try { thread.SetApartmentState(ApartmentState.STA); }
        catch { /* CoInitializeEx s'en charge */ }

        thread.Start();
        return done.Task;
    }

    // =========================================================================
    // Sélecteur de dossier moderne — IFileOpenDialog
    // =========================================================================

    private static string PickFolderModern(IntPtr owner, string title, string startFolder)
    {
        Guid clsid = CLSID_FileOpenDialog;
        Guid iid = IID_IFileOpenDialog;
        int hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iid, out IFileOpenDialog dialog);
        if (hr < 0 || dialog == null) throw new COMException("CoCreateInstance", hr);

        try
        {
            dialog.GetOptions(out uint options);
            dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST | FOS_NOCHANGEDIR);
            if (!string.IsNullOrEmpty(title)) dialog.SetTitle(title);

            if (!string.IsNullOrEmpty(startFolder) && System.IO.Directory.Exists(startFolder))
            {
                Guid shellItem = IID_IShellItem;
                if (SHCreateItemFromParsingName(startFolder, IntPtr.Zero, ref shellItem, out IShellItem folder) >= 0 &&
                    folder != null)
                {
                    dialog.SetFolder(folder);
                    Marshal.ReleaseComObject(folder);
                }
            }

            hr = dialog.Show(owner);
            if (hr == ERROR_CANCELLED) return null;
            if (hr < 0) throw new COMException("IFileOpenDialog.Show", hr);

            dialog.GetResult(out IShellItem result);
            if (result == null) return null;
            try
            {
                result.GetDisplayName(SIGDN_FILESYSPATH, out string path);
                return path;
            }
            finally { Marshal.ReleaseComObject(result); }
        }
        finally { Marshal.ReleaseComObject(dialog); }
    }

    // =========================================================================
    // Repli — SHBrowseForFolder, sans COM managé
    // =========================================================================

    private static string PickFolderLegacy(IntPtr owner, string title)
    {
        IntPtr display = Marshal.AllocHGlobal(MAX_PATH * 2);
        try
        {
            var info = new BROWSEINFO
            {
                hwndOwner = owner,
                pszDisplayName = display,
                lpszTitle = title,
                ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE
            };

            IntPtr pidl = SHBrowseForFolder(ref info);
            if (pidl == IntPtr.Zero) return null;

            try
            {
                var path = new StringBuilder(MAX_PATH);
                return SHGetPathFromIDList(pidl, path) ? path.ToString() : null;
            }
            finally { CoTaskMemFree(pidl); }
        }
        finally { Marshal.FreeHGlobal(display); }
    }

    // =========================================================================
    // Choix de fichier — GetOpenFileName
    // =========================================================================

    private static string PickFile(IntPtr owner, string title, string startFolder, string filter)
    {
        var ofn = new OPENFILENAME();
        ofn.lStructSize = Marshal.SizeOf(ofn);
        ofn.hwndOwner = owner;
        ofn.lpstrFilter = filter;
        ofn.lpstrFile = new string('\0', MAX_PATH * 2);
        ofn.nMaxFile = ofn.lpstrFile.Length;
        ofn.lpstrInitialDir = startFolder;
        ofn.lpstrTitle = title;
        ofn.Flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;

        if (!GetOpenFileName(ofn)) return null;
        string file = ofn.lpstrFile;
        int end = file.IndexOf('\0');
        return end >= 0 ? file.Substring(0, end) : file;
    }

    // =========================================================================
    // Interop
    // =========================================================================

    private const int MAX_PATH = 260;
    private const int SW_MINIMIZE = 6;
    private const uint COINIT_APARTMENTTHREADED = 0x2;
    private const uint CLSCTX_INPROC_SERVER = 0x1;
    private const int ERROR_CANCELLED = unchecked((int)0x800704C7);

    private const uint FOS_NOCHANGEDIR = 0x8;
    private const uint FOS_PICKFOLDERS = 0x20;
    private const uint FOS_FORCEFILESYSTEM = 0x40;
    private const uint FOS_PATHMUSTEXIST = 0x800;
    private const uint SIGDN_FILESYSPATH = 0x80058000;

    private const uint BIF_RETURNONLYFSDIRS = 0x1;
    private const uint BIF_NEWDIALOGSTYLE = 0x40;

    private const int OFN_NOCHANGEDIR = 0x8;
    private const int OFN_PATHMUSTEXIST = 0x800;
    private const int OFN_FILEMUSTEXIST = 0x1000;
    private const int OFN_EXPLORER = 0x80000;

    private static readonly Guid CLSID_FileOpenDialog = new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
    private static readonly Guid IID_IFileOpenDialog = new Guid("D57C7288-D4AD-4768-BE02-9D969532D960");
    private static readonly Guid IID_IShellItem = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint coInit);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("ole32.dll")] private static extern void CoTaskMemFree(IntPtr pv);

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IFileOpenDialog instance);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHBrowseForFolder(ref BROWSEINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder path);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileName([In, Out] OPENFILENAME ofn);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BROWSEINFO
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public IntPtr pszDisplayName;
        public string lpszTitle;
        public uint ulFlags;
        public IntPtr lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private class OPENFILENAME
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public string lpstrFile;
        public int nMaxFile;
        public string lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    // ⚠ L'ORDRE des méthodes EST la table virtuelle COM : ne rien réordonner, ne rien retirer, même
    // ce qui ne sert pas. Une méthode décalée appelle silencieusement sa voisine.
    [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr parent);                                   // IModalWindow
        void SetFileTypes(uint count, IntPtr filterSpec);                        // IFileDialog…
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem item);
        void SetFolder(IShellItem item);
        void GetFolder(out IShellItem item);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
        void AddPlace(IShellItem item, int placement);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr filter);
        void GetResults(out IntPtr items);                                       // IFileOpenDialog
        void GetSelectedItems(out IntPtr items);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint type, [MarshalAs(UnmanagedType.LPWStr)] out string name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }
}
