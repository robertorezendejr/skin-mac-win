// Mac Skin: janelas de apps abertas, com o mesmo critério da barra de tarefas do Windows, e a identidade de
// cada app (AppUserModelID). Pela identidade, um app web instalado (Google Keep, que roda dentro do Edge)
// não se confunde com o Edge, e cada app aparece uma vez só na dock.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MacSkin
{
    class JanelaApp
    {
        public IntPtr H;
        public uint Pid;
        public string Processo;   // nome do processo em minúsculas (msedge, outlook...)
        public string Caminho;    // executável
        public string Aumid;      // identidade de app; vazia em muitos programas clássicos
        public string Titulo;
        // chave do grupo na dock: a identidade quando existe, senão o executável
        public string Chave { get { return string.IsNullOrEmpty(Aumid) ? "exe:" + Caminho.ToLowerInvariant() : Aumid; } }
    }

    static class Janelas
    {
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int n);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint acesso, bool herda, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int f, StringBuilder s, ref int n);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetApplicationUserModelId(IntPtr h, ref int n, StringBuilder s);

        // identidade gravada na janela (a barra de tarefas usa esta): IPropertyStore, PKEY_AppUserModel_ID
        [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public uint pid; }
        [StructLayout(LayoutKind.Explicit)] struct PROPVARIANT { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr p; [FieldOffset(16)] public IntPtr p2; }
        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint n);
            [PreserveSig] int GetAt(uint i, out PROPERTYKEY k);
            [PreserveSig] int GetValue(ref PROPERTYKEY k, out PROPVARIANT v);
            [PreserveSig] int SetValue(ref PROPERTYKEY k, ref PROPVARIANT v);
            [PreserveSig] int Commit();
        }
        [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr h, ref Guid iid, out IPropertyStore ps);
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT v);
        static PROPERTYKEY chaveAumid = new PROPERTYKEY { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };

        static string AumidDaJanela(IntPtr h)
        {
            IPropertyStore ps = null;
            try
            {
                var iid = typeof(IPropertyStore).GUID;
                if (SHGetPropertyStoreForWindow(h, ref iid, out ps) != 0 || ps == null) return null;
                PROPVARIANT v;
                if (ps.GetValue(ref chaveAumid, out v) != 0) return null;
                string s = v.vt == 31 ? Marshal.PtrToStringUni(v.p) : null;   // VT_LPWSTR
                PropVariantClear(ref v);
                return s;
            }
            catch { return null; }
            finally { if (ps != null) Marshal.ReleaseComObject(ps); }
        }

        // por processo: executável e identidade de pacote (apps da Store e Teams)
        class InfoProcesso { public string Caminho, Processo, Pacote; public DateTime Visto; }
        static readonly Dictionary<uint, InfoProcesso> processos = new Dictionary<uint, InfoProcesso>();
        static InfoProcesso Processo(uint pid)
        {
            InfoProcesso i;
            if (processos.TryGetValue(pid, out i)) { i.Visto = DateTime.Now; return i; }
            i = new InfoProcesso { Caminho = "", Processo = "", Visto = DateTime.Now };
            IntPtr h = OpenProcess(0x1000, false, pid);   // QUERY_LIMITED_INFORMATION: funciona sem admin
            if (h != IntPtr.Zero)
            {
                try
                {
                    var sb = new StringBuilder(1024); int n = sb.Capacity;
                    if (QueryFullProcessImageName(h, 0, sb, ref n)) i.Caminho = sb.ToString();
                    var ab = new StringBuilder(512); int m = ab.Capacity;
                    if (GetApplicationUserModelId(h, ref m, ab) == 0) i.Pacote = ab.ToString();
                }
                finally { CloseHandle(h); }
            }
            i.Processo = System.IO.Path.GetFileNameWithoutExtension(i.Caminho).ToLowerInvariant();
            if (processos.Count > 400)   // processos que já fecharam
            {
                var velhos = new List<uint>();
                foreach (var kv in processos) if ((DateTime.Now - kv.Value.Visto).TotalMinutes > 5) velhos.Add(kv.Key);
                foreach (var k in velhos) processos.Remove(k);
            }
            processos[pid] = i;
            return i;
        }

        // na ordem da tela (a de cima primeiro)
        public static List<JanelaApp> Abertas()
        {
            var r = new List<JanelaApp>();
            EnumWindows((h, l) =>
            {
                if (!Win32.IsWindowVisible(h) || GetWindow(h, 4) != IntPtr.Zero) return true;            // invisível ou janela "filha" (tem dono)
                if ((Win32.GetWindowLong(h, -20) & Win32.EX_TOOL) != 0) return true;                       // janela de ferramenta
                int camuflada; if (DwmGetWindowAttribute(h, 14, out camuflada, 4) == 0 && camuflada != 0) return true;   // app da Store suspenso, outra área de trabalho
                string t = Win32.Titulo(h);
                if (t.Length == 0 || t.StartsWith("Mac Skin")) return true;
                string c = Win32.Classe(h);
                if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return true;
                uint pid = Win32.Pid(h);
                var p = Processo(pid);
                string aumid = AumidDaJanela(h);
                if (string.IsNullOrEmpty(aumid) && p.Processo != "applicationframehost") aumid = p.Pacote;
                r.Add(new JanelaApp { H = h, Pid = pid, Processo = p.Processo, Caminho = p.Caminho, Aumid = aumid ?? "", Titulo = t });
                return true;
            }, IntPtr.Zero);
            return r;
        }

        // nome do app para o rótulo da dock
        static readonly Dictionary<string, string> descricoes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static string Nome(JanelaApp j)
        {
            // apps da Store e apps web instalados: o título da janela é o nome do app ("Google Keep")
            if (!string.IsNullOrEmpty(j.Aumid) && (j.Processo == "applicationframehost" || j.Processo == "msedge" || j.Processo == "chrome"))
            {
                var partes = j.Titulo.Split(new[] { " - ", " – ", " — " }, StringSplitOptions.None);
                return partes[partes.Length - 1].Trim();
            }
            string d;
            if (!descricoes.TryGetValue(j.Caminho, out d))
            {
                try { d = FileVersionInfo.GetVersionInfo(j.Caminho).FileDescription; } catch { d = null; }
                if (string.IsNullOrWhiteSpace(d)) d = j.Titulo;
                descricoes[j.Caminho] = d;
            }
            return d;
        }
    }
}
