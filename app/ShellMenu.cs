// Mac Skin Finder: Menu de contexto nativo do Windows Shell (o mesmo do Explorador de Arquivos).
// Suporta todas as extensões do sistema (Kaspersky, Blip, Adobe PDF, PowerRename, 7-Zip, Abrir com, etc.)
// e inclui o submenu nativo "Compactar para..." (Arquivo ZIP, Arquivo 7z, Arquivo TAR) usando tar.exe do Windows.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;

namespace MacSkin
{
    static class ShellMenu
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

        [DllImport("shell32.dll")]
        static extern int SHBindToParent(IntPtr pidl, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellFolder ppv, out IntPtr ppidlLast);

        [DllImport("shell32.dll")]
        static extern int SHGetDesktopFolder(out IShellFolder ppshf);

        [DllImport("ole32.dll")]
        static extern void CoTaskMemFree(IntPtr pv);

        [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
        static extern int SetPreferredAppMode(int appMode);

        [DllImport("uxtheme.dll", EntryPoint = "#136", SetLastError = true)]
        static extern void FlushMenuThemes();

        [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellFolder
        {
            void ParseDisplayName(IntPtr hwnd, IntPtr pbc, string pszDisplayName, out uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);
            void EnumObjects();
            [PreserveSig]
            int BindToObject(IntPtr pidl, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
            void BindToStorage();
            void CompareIDs();
            [PreserveSig]
            int CreateViewObject(IntPtr hwndOwner, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
            void GetAttributesOf();
            [PreserveSig]
            int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] apidl, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, IntPtr rgfReserved, out IntPtr ppv);
        }

        [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IContextMenu
        {
            [PreserveSig]
            int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
            [PreserveSig]
            int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
            [PreserveSig]
            int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, StringBuilder pszName, uint cchMax);
        }

        [ComImport, Guid("000214f4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IContextMenu2 : IContextMenu
        {
            [PreserveSig]
            new int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
            [PreserveSig]
            new int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
            [PreserveSig]
            new int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, StringBuilder pszName, uint cchMax);
            [PreserveSig]
            int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
        }

        [ComImport, Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IContextMenu3 : IContextMenu2
        {
            [PreserveSig]
            new int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
            [PreserveSig]
            new int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
            [PreserveSig]
            new int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, StringBuilder pszName, uint cchMax);
            [PreserveSig]
            new int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
            [PreserveSig]
            int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct CMINVOKECOMMANDINFOEX
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            public IntPtr lpVerb;
            public string lpParameters;
            public string lpDirectory;
            public int nShow;
            public uint dwHotKey;
            public IntPtr hIcon;
            public string lpTitle;
            public IntPtr lpVerbW;
            public string lpParametersW;
            public string lpDirectoryW;
            public string lpTitleW;
            public POINT ptInvoke;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int x; public int y; }

        [DllImport("user32.dll")]
        static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll")]
        static extern bool DestroyMenu(IntPtr hMenu);
        [DllImport("user32.dll")]
        static extern uint TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);
        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool InsertMenu(IntPtr hMenu, uint uPosition, uint uFlags, IntPtr uIDNewItem, string lpNewItem);
        [DllImport("user32.dll")]
        static extern int GetMenuItemCount(IntPtr hMenu);

        const uint CMF_EXPLORE = 0x00000004;
        const uint CMF_CANRENAME = 0x00000010;

        const uint TPM_RETURNCMD = 0x0100;
        const uint TPM_RIGHTBUTTON = 0x0002;

        const uint IDM_FIRST = 1;
        const uint IDM_LAST = 0x5FFF;

        const uint CMD_CUSTOM_ZIP    = 0x6001;
        const uint CMD_CUSTOM_7Z     = 0x6002;
        const uint CMD_CUSTOM_TAR    = 0x6003;
        const uint CMD_NOVA_ABA      = 0x6004;
        const uint CMD_NOVA_JANELA   = 0x6005;
        const uint CMD_PREVIA        = 0x6006;
        const uint CMD_COPIAR_CAMINHO= 0x6007;

        const uint CMD_FUNDO_NOVA_PASTA = 0x6010;
        const uint CMD_FUNDO_COLAR      = 0x6011;
        const uint CMD_FUNDO_ATUALIZAR  = 0x6012;
        const uint CMD_FUNDO_EXPLORADOR = 0x6013;

        const uint MF_BYPOSITION = 0x00000400;
        const uint MF_POPUP      = 0x00000010;
        const uint MF_SEPARATOR  = 0x00000800;
        const uint MF_GRAYED     = 0x00000001;

        static void AplicarTemaMenu()
        {
            try
            {
                // 3 = Light, 2 = Dark
                SetPreferredAppMode(Comum.TemaClaro() ? 3 : 2);
                FlushMenuThemes();
            }
            catch { }
        }

        public static void MostrarMenu(IntPtr hwndOwner, IList<string> caminhos, Action recarregar, Action<string, bool> abrirAbaOuJanela = null, Action previa = null)
        {
            if (caminhos == null || caminhos.Count == 0) return;

            AplicarTemaMenu();

            var pidls = new List<IntPtr>();
            var childPidls = new List<IntPtr>();
            IShellFolder parentFolder = null;

            try
            {
                foreach (var c in caminhos)
                {
                    IntPtr pidl; uint sf;
                    if (SHParseDisplayName(c, IntPtr.Zero, out pidl, 0, out sf) == 0)
                    {
                        pidls.Add(pidl);
                        IShellFolder parent; IntPtr child;
                        if (SHBindToParent(pidl, typeof(IShellFolder).GUID, out parent, out child) == 0)
                        {
                            if (parentFolder == null) parentFolder = parent;
                            else Marshal.ReleaseComObject(parent);
                            childPidls.Add(child);
                        }
                    }
                }

                if (parentFolder == null || childPidls.Count == 0) return;

                IntPtr cmPtr;
                int hr = parentFolder.GetUIObjectOf(hwndOwner, (uint)childPidls.Count, childPidls.ToArray(), typeof(IContextMenu).GUID, IntPtr.Zero, out cmPtr);
                if (hr != 0 || cmPtr == IntPtr.Zero) return;

                IContextMenu cm = null;
                IContextMenu2 cm2 = null;
                IContextMenu3 cm3 = null;

                try
                {
                    cm = (IContextMenu)Marshal.GetObjectForIUnknown(cmPtr);
                    cm2 = cm as IContextMenu2;
                    cm3 = cm as IContextMenu3;

                    IntPtr hMenu = CreatePopupMenu();
                    try
                    {
                        cm.QueryContextMenu(hMenu, 0, IDM_FIRST, IDM_LAST, CMF_EXPLORE | CMF_CANRENAME);

                        // Submenu "Compactar para..." idêntico ao Windows 11
                        IntPtr hSubZip = CreatePopupMenu();
                        InsertMenu(hSubZip, 0, MF_BYPOSITION, (IntPtr)CMD_CUSTOM_ZIP, "Arquivo ZIP");
                        InsertMenu(hSubZip, 1, MF_BYPOSITION, (IntPtr)CMD_CUSTOM_7Z, "Arquivo 7z");
                        InsertMenu(hSubZip, 2, MF_BYPOSITION, (IntPtr)CMD_CUSTOM_TAR, "Arquivo TAR");

                        // Insere próximo ao topo (posição 2 ou 3)
                        uint posInserir = (uint)Math.Min(2, GetMenuItemCount(hMenu));
                        InsertMenu(hMenu, posInserir, MF_BYPOSITION | MF_POPUP, hSubZip, "Compactar para...");

                        // Se for pasta, adiciona opções de abrir em nova aba / janela do Finder
                        bool temPasta = caminhos.Any(Directory.Exists);
                        if (temPasta && abrirAbaOuJanela != null)
                        {
                            InsertMenu(hMenu, 1, MF_BYPOSITION, (IntPtr)CMD_NOVA_ABA, "Abrir em Nova Aba");
                            InsertMenu(hMenu, 2, MF_BYPOSITION, (IntPtr)CMD_NOVA_JANELA, "Abrir em Nova Janela");
                        }

                        // Opção de Visualização Rápida (Quick Look)
                        if (previa != null)
                        {
                            InsertMenu(hMenu, 0, MF_BYPOSITION, (IntPtr)CMD_PREVIA, "Visualização Rápida (Espaço)");
                        }

                        POINT pt;
                        GetCursorPos(out pt);

                        HwndSourceHook hook = (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                        {
                            if (cm3 != null)
                            {
                                IntPtr res;
                                if (cm3.HandleMenuMsg2((uint)msg, wParam, lParam, out res) == 0)
                                {
                                    handled = true;
                                    return res;
                                }
                            }
                            else if (cm2 != null)
                            {
                                if (cm2.HandleMenuMsg((uint)msg, wParam, lParam) == 0)
                                {
                                    handled = true;
                                    return IntPtr.Zero;
                                }
                            }
                            return IntPtr.Zero;
                        };

                        HwndSource src = HwndSource.FromHwnd(hwndOwner);
                        if (src != null) src.AddHook(hook);

                        uint cmd;
                        try
                        {
                            cmd = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.x, pt.y, hwndOwner, IntPtr.Zero);
                        }
                        finally
                        {
                            if (src != null) src.RemoveHook(hook);
                        }

                        if (cmd == CMD_CUSTOM_ZIP)
                        {
                            ExecutarCompactacao(caminhos, "zip", recarregar);
                        }
                        else if (cmd == CMD_CUSTOM_7Z)
                        {
                            ExecutarCompactacao(caminhos, "7z", recarregar);
                        }
                        else if (cmd == CMD_CUSTOM_TAR)
                        {
                            ExecutarCompactacao(caminhos, "tar", recarregar);
                        }
                        else if (cmd == CMD_NOVA_ABA && abrirAbaOuJanela != null)
                        {
                            foreach (var p in caminhos.Where(Directory.Exists)) abrirAbaOuJanela(p, false);
                        }
                        else if (cmd == CMD_NOVA_JANELA && abrirAbaOuJanela != null)
                        {
                            foreach (var p in caminhos.Where(Directory.Exists)) abrirAbaOuJanela(p, true);
                        }
                        else if (cmd == CMD_PREVIA && previa != null)
                        {
                            previa();
                        }
                        else if (cmd >= IDM_FIRST && cmd <= IDM_LAST)
                        {
                            var ici = new CMINVOKECOMMANDINFOEX();
                            ici.cbSize = Marshal.SizeOf(typeof(CMINVOKECOMMANDINFOEX));
                            ici.fMask = 0x00004000 /* CMIC_MASK_UNICODE */ | 0x00000040 /* CMIC_MASK_PTINVOKE */;
                            ici.hwnd = hwndOwner;
                            ici.lpVerb = (IntPtr)(cmd - IDM_FIRST);
                            ici.lpVerbW = (IntPtr)(cmd - IDM_FIRST);
                            ici.nShow = 1; // SW_SHOWNORMAL
                            ici.ptInvoke = pt;
                            cm.InvokeCommand(ref ici);

                            if (recarregar != null) recarregar();
                        }
                    }
                    finally
                    {
                        DestroyMenu(hMenu);
                    }
                }
                finally
                {
                    if (cmPtr != IntPtr.Zero) Marshal.Release(cmPtr);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("ShellMenu erro: " + ex.Message);
            }
            finally
            {
                if (parentFolder != null) Marshal.ReleaseComObject(parentFolder);
                foreach (var p in pidls) CoTaskMemFree(p);
            }
        }

        public static void MostrarMenuFundo(IntPtr hwndOwner, string pasta, Action recarregar, Action novaPasta, Action colar, Func<bool> podeColar, Action mostrarNoExplorador)
        {
            if (string.IsNullOrEmpty(pasta) || !Directory.Exists(pasta)) return;

            AplicarTemaMenu();

            IntPtr pidl = IntPtr.Zero;
            IShellFolder folder = null;
            IntPtr cmPtr = IntPtr.Zero;

            try
            {
                uint sf;
                if (SHParseDisplayName(pasta, IntPtr.Zero, out pidl, 0, out sf) != 0 || pidl == IntPtr.Zero) return;

                IShellFolder desktop;
                if (SHGetDesktopFolder(out desktop) != 0 || desktop == null) return;

                IntPtr folderPtr;
                int hr = desktop.BindToObject(pidl, IntPtr.Zero, typeof(IShellFolder).GUID, out folderPtr);
                Marshal.ReleaseComObject(desktop);

                if (hr != 0 || folderPtr == IntPtr.Zero) return;
                folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderPtr);

                hr = folder.CreateViewObject(hwndOwner, typeof(IContextMenu).GUID, out cmPtr);

                IntPtr hMenu = CreatePopupMenu();
                try
                {
                    IContextMenu cm = null;
                    IContextMenu2 cm2 = null;
                    IContextMenu3 cm3 = null;

                    if (hr == 0 && cmPtr != IntPtr.Zero)
                    {
                        cm = (IContextMenu)Marshal.GetObjectForIUnknown(cmPtr);
                        cm2 = cm as IContextMenu2;
                        cm3 = cm as IContextMenu3;
                        cm.QueryContextMenu(hMenu, 0, IDM_FIRST, IDM_LAST, 0);
                    }

                    // Adiciona itens úteis no início do menu de fundo
                    InsertMenu(hMenu, 0, MF_BYPOSITION, (IntPtr)CMD_FUNDO_NOVA_PASTA, "Nova Pasta");
                    uint flagsColar = MF_BYPOSITION | ((podeColar != null && podeColar()) ? 0 : MF_GRAYED);
                    InsertMenu(hMenu, 1, flagsColar, (IntPtr)CMD_FUNDO_COLAR, "Colar Item");
                    InsertMenu(hMenu, 2, MF_BYPOSITION, (IntPtr)CMD_FUNDO_ATUALIZAR, "Atualizar (F5)");
                    InsertMenu(hMenu, 3, MF_BYPOSITION, (IntPtr)CMD_FUNDO_EXPLORADOR, "Abrir no Explorador");
                    InsertMenu(hMenu, 4, MF_BYPOSITION | MF_SEPARATOR, IntPtr.Zero, null);

                    POINT pt;
                    GetCursorPos(out pt);

                    HwndSourceHook hook = (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                    {
                        if (cm3 != null)
                        {
                            IntPtr res;
                            if (cm3.HandleMenuMsg2((uint)msg, wParam, lParam, out res) == 0)
                            {
                                handled = true;
                                return res;
                            }
                        }
                        else if (cm2 != null)
                        {
                            if (cm2.HandleMenuMsg((uint)msg, wParam, lParam) == 0)
                            {
                                handled = true;
                                return IntPtr.Zero;
                            }
                        }
                        return IntPtr.Zero;
                    };

                    HwndSource src = HwndSource.FromHwnd(hwndOwner);
                    if (src != null) src.AddHook(hook);

                    uint cmd;
                    try
                    {
                        cmd = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.x, pt.y, hwndOwner, IntPtr.Zero);
                    }
                    finally
                    {
                        if (src != null) src.RemoveHook(hook);
                    }

                    if (cmd == CMD_FUNDO_NOVA_PASTA && novaPasta != null) novaPasta();
                    else if (cmd == CMD_FUNDO_COLAR && colar != null) colar();
                    else if (cmd == CMD_FUNDO_ATUALIZAR && recarregar != null) recarregar();
                    else if (cmd == CMD_FUNDO_EXPLORADOR && mostrarNoExplorador != null) mostrarNoExplorador();
                    else if (cm != null && cmd >= IDM_FIRST && cmd <= IDM_LAST)
                    {
                        var ici = new CMINVOKECOMMANDINFOEX();
                        ici.cbSize = Marshal.SizeOf(typeof(CMINVOKECOMMANDINFOEX));
                        ici.fMask = 0x00004000 | 0x00000040;
                        ici.hwnd = hwndOwner;
                        ici.lpVerb = (IntPtr)(cmd - IDM_FIRST);
                        ici.lpVerbW = (IntPtr)(cmd - IDM_FIRST);
                        ici.nShow = 1;
                        ici.ptInvoke = pt;
                        cm.InvokeCommand(ref ici);

                        if (recarregar != null) recarregar();
                    }
                }
                finally
                {
                    DestroyMenu(hMenu);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("ShellMenu fundo erro: " + ex.Message);
            }
            finally
            {
                if (cmPtr != IntPtr.Zero) Marshal.Release(cmPtr);
                if (folder != null) Marshal.ReleaseComObject(folder);
                if (pidl != IntPtr.Zero) CoTaskMemFree(pidl);
            }
        }

        static void ExecutarCompactacao(IList<string> caminhos, string formato, Action recarregar)
        {
            try
            {
                if (caminhos == null || caminhos.Count == 0) return;
                string pastaPai = Path.GetDirectoryName(caminhos[0]);
                string nomeBase;
                if (caminhos.Count == 1)
                {
                    nomeBase = Path.GetFileNameWithoutExtension(caminhos[0]);
                    if (string.IsNullOrEmpty(nomeBase)) nomeBase = Path.GetFileName(caminhos[0]);
                }
                else
                {
                    nomeBase = Path.GetFileName(pastaPai);
                    if (string.IsNullOrEmpty(nomeBase)) nomeBase = "Arquivo";
                }

                string ext = "." + formato;
                string arqDestino = Path.Combine(pastaPai, nomeBase + ext);
                int k = 2;
                while (File.Exists(arqDestino) || Directory.Exists(arqDestino))
                {
                    arqDestino = Path.Combine(pastaPai, nomeBase + " (" + k + ")" + ext);
                    k++;
                }

                var sbArgs = new StringBuilder();
                if (formato == "tar") sbArgs.Append("-c -f ");
                else sbArgs.Append("-a -c -f ");

                sbArgs.Append("\"").Append(Path.GetFileName(arqDestino)).Append("\"");
                foreach (var c in caminhos)
                {
                    sbArgs.Append(" \"").Append(Path.GetFileName(c)).Append("\"");
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "tar.exe",
                    Arguments = sbArgs.ToString(),
                    WorkingDirectory = pastaPai,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var p = Process.Start(psi);
                if (p != null)
                {
                    p.EnableRaisingEvents = true;
                    p.Exited += (s, e) =>
                    {
                        if (recarregar != null)
                        {
                            var app = System.Windows.Application.Current;
                            if (app != null && app.Dispatcher != null) app.Dispatcher.BeginInvoke(recarregar);
                        }
                        p.Dispose();
                    };
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Erro ao compactar: " + ex.Message);
            }
        }
    }
}
