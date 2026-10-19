// Mac Skin: utilidades comuns (pasta, log, tema, atalhos .lnk, ícones, cores).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace MacSkin
{
    static class Comum
    {
        // o exe roda de %LOCALAPPDATA%\MacSkin, ao lado de icones\, dock\ e macskin.ps1
        public static readonly string Dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');

        public static void Log(string arquivo, string m)
        {
            try { File.AppendAllText(Path.Combine(Dir, arquivo), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + m + Environment.NewLine); }
            catch { }
        }

        public static bool TemaClaro()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                return v == null || (int)v != 0;
            }
        }

        public static SolidColorBrush Cor(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b;
        }

        public static LinearGradientBrush Degrade(string cima, string baixo)
        {
            var g = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(cima), (Color)ColorConverter.ConvertFromString(baixo), 90);
            g.Freeze(); return g;
        }

        public static ImageSource Icone(string arq)
        {
            try
            {
                if (arq.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) && File.Exists(arq))
                {
                    var d = BitmapDecoder.Create(new Uri(arq), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    return d.Frames.OrderByDescending(f => f.PixelWidth).First();
                }
                using (var i = System.Drawing.Icon.ExtractAssociatedIcon(arq))
                {
                    var s = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(i.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    s.Freeze(); return s;
                }
            }
            catch (Exception e) { Log("dock.log", "ícone: " + arq + " (" + e.Message + ")"); return null; }
        }
    }

    // atalho .lnk lido pelo WScript.Shell (sem referência a COM no projeto: via reflexão)
    class Atalho
    {
        public string Caminho, Alvo, Argumentos, Icone, Descricao;

        static object shell;
        public static Atalho Ler(string lnk)
        {
            if (shell == null) shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            object l = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            Func<string, string> p = n => (l.GetType().InvokeMember(n, BindingFlags.GetProperty, null, l, null) ?? "").ToString();
            return new Atalho { Caminho = lnk, Alvo = p("TargetPath"), Argumentos = p("Arguments"), Icone = p("IconLocation"), Descricao = p("Description") };
        }
    }
}
