// Mac Skin Finder: ícones, miniaturas e nomes de tipo vindos do próprio Windows (os mesmos do Explorador).
// As miniaturas saem numa thread STA à parte, em fila, para a janela nunca travar.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MacSkin
{
    static class Shell
    {
        // ---------- miniatura ou ícone de qualquer arquivo/pasta, no tamanho pedido ----------
        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory { [PreserveSig] int GetImage(TAMANHO t, int flags, out IntPtr hbm); }
        [StructLayout(LayoutKind.Sequential)] struct TAMANHO { public int cx, cy; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHCreateItemFromParsingName(string caminho, IntPtr bc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItemImageFactory item);

        [StructLayout(LayoutKind.Sequential)] struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
        [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
        [StructLayout(LayoutKind.Sequential)] struct DIBSECTION { public BITMAP dsBm; public BITMAPINFOHEADER dsBmih; public uint b0, b1, b2; public IntPtr dshSection; public uint dsOffset; }
        [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int n, ref DIBSECTION d);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);

        static BitmapSource Imagem(string caminho, int px)
        {
            IShellItemImageFactory f;
            if (SHCreateItemFromParsingName(caminho, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out f) != 0 || f == null) return null;
            try
            {
                IntPtr hbm;
                if (f.GetImage(new TAMANHO { cx = px, cy = px }, 0, out hbm) != 0) return null;
                try { return Converter(hbm); } finally { DeleteObject(hbm); }
            }
            finally { Marshal.ReleaseComObject(f); }
        }

        // HBITMAP 32 bits com alfa (pré-multiplicado) -> BitmapSource; a conversão pronta do WPF perde a transparência
        static BitmapSource Converter(IntPtr hbm)
        {
            var d = new DIBSECTION();
            if (GetObject(hbm, Marshal.SizeOf(d), ref d) == 0 || d.dsBm.bmBits == IntPtr.Zero || d.dsBm.bmBitsPixel != 32)
            {
                var s = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                s.Freeze(); return s;
            }
            int w = d.dsBm.bmWidth, h = d.dsBm.bmHeight, passo = d.dsBm.bmWidthBytes;
            var px = new byte[passo * h];
            Marshal.Copy(d.dsBm.bmBits, px, 0, px.Length);
            if (d.dsBmih.biHeight > 0)   // de baixo para cima: inverte as linhas
            {
                var inv = new byte[px.Length];
                for (int y = 0; y < h; y++) Buffer.BlockCopy(px, y * passo, inv, (h - 1 - y) * passo, passo);
                px = inv;
            }
            bool temAlfa = false;
            for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) { temAlfa = true; break; }
            var b = BitmapSource.Create(w, h, 96, 96, temAlfa ? PixelFormats.Pbgra32 : PixelFormats.Bgr32, null, px, passo);
            b.Freeze(); return b;
        }

        // fila de pedidos atendida por uma thread STA (o shell prefere STA); a resposta volta pelo Dispatcher da tela
        class Pedido { public string Caminho; public int Px; public Action<ImageSource> Pronto; }
        static readonly BlockingCollection<Pedido> fila = new BlockingCollection<Pedido>();
        static readonly ConcurrentDictionary<string, ImageSource> cache = new ConcurrentDictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        static Thread trabalhador;
        static System.Windows.Threading.Dispatcher tela;

        public static void PedirImagem(string caminho, int px, Action<ImageSource> pronto)
        {
            ImageSource img;
            string chave = caminho + "|" + px;
            if (cache.TryGetValue(chave, out img)) { pronto(img); return; }
            if (trabalhador == null)
            {
                tela = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                trabalhador = new Thread(Trabalhar) { IsBackground = true, Name = "miniaturas" };
                trabalhador.SetApartmentState(ApartmentState.STA);
                trabalhador.Start();
            }
            fila.Add(new Pedido { Caminho = caminho, Px = px, Pronto = pronto });
        }

        // descarta pedidos antigos (ao trocar de pasta, as miniaturas da pasta anterior não interessam mais)
        public static void LimparFila() { Pedido p; while (fila.TryTake(out p)) { } }

        static void Trabalhar()
        {
            foreach (var p in fila.GetConsumingEnumerable())
            {
                ImageSource img = null;
                try { img = Imagem(p.Caminho, p.Px); } catch { }
                if (img == null) continue;
                if (cache.Count > 3000) cache.Clear();
                cache[p.Caminho + "|" + p.Px] = img;
                var pr = p.Pronto; var i = img;
                tela.BeginInvoke(new Action(() => pr(i)));
            }
        }

        // ---------- nome do tipo ("Planilha do Microsoft Excel"), por extensão ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO { public IntPtr hIcon; public int iIcon; public uint dwAttributes; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string p, uint attr, ref SHFILEINFO i, int n, uint flags);
        static readonly Dictionary<string, string> tipos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static string Tipo(string nome, bool pasta)
        {
            if (pasta) return "Pasta";
            string ext = Path.GetExtension(nome);
            string t;
            if (tipos.TryGetValue(ext, out t)) return t;
            var i = new SHFILEINFO();
            SHGetFileInfo(string.IsNullOrEmpty(ext) ? "arquivo" : "x" + ext, 0x80, ref i, Marshal.SizeOf(i), 0x400 | 0x10);   // TYPENAME | USEFILEATTRIBUTES
            t = string.IsNullOrEmpty(i.szTypeName) ? ext.TrimStart('.').ToUpperInvariant() : i.szTypeName;
            tipos[ext] = t;
            return t;
        }

        // ordem "natural" do Windows: arquivo2 antes de arquivo10
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] public static extern int StrCmpLogicalW(string a, string b);

        // abre a janela de Propriedades do Windows
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHELLEXECUTEINFO { public int cbSize; public uint fMask; public IntPtr hwnd; public string lpVerb, lpFile, lpParameters, lpDirectory; public int nShow; public IntPtr hInstApp, lpIDList; public string lpClass; public IntPtr hkeyClass; public uint dwHotKey; public IntPtr hIcon, hProcess; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO i);
        public static void Propriedades(string caminho)
        {
            var i = new SHELLEXECUTEINFO { lpVerb = "properties", lpFile = caminho, nShow = 5, fMask = 0x0000000C };
            i.cbSize = Marshal.SizeOf(i);
            ShellExecuteEx(ref i);
        }
        // "Abrir com..." do Windows
        public static void AbrirCom(string caminho)
        {
            try { System.Diagnostics.Process.Start("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + caminho); } catch { }
        }
    }
}
