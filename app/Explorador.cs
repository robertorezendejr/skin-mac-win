// Mac Skin Explorador: janelas de pasta do Explorador do Windows viram janelas do nosso Finder.
// Quem chama explorer.exe (o "Mostrar na pasta" do navegador, "Abrir local do arquivo"...) cria uma janela
// CabinetWClass: ela é escondida na hora, o caminho (e o arquivo selecionado, no caso do /select) é lido dela
// pelo Shell.Application, a janela fecha e o Finder abre no lugar. Pastas virtuais (Painel de Controle, Este
// Computador...) continuam no Explorador. O "Abrir no Explorador" do próprio Finder grava "explorador-liberado".
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace MacSkin
{
    static class Explorador
    {
        delegate void WinEventProc(IntPtr gancho, uint ev, IntPtr h, int objeto, int filho, uint thread, uint tempo);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint de, uint ate, IntPtr mod, WinEventProc p, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr g);

        static WinEventProc proc; static IntPtr g1, g2, g3;
        static readonly HashSet<IntPtr> tratadas = new HashSet<IntPtr>();
        static readonly string Liberado = Path.Combine(Comum.Dir, "explorador-liberado");

        static void Log(string m) { Comum.Log("explorador.log", m); }

        // o Finder chama antes de abrir o Explorador de propósito
        public static void Liberar() { try { File.WriteAllText(Liberado, DateTime.Now.Ticks.ToString()); } catch { } }

        static bool EstaLiberado()
        {
            try { return File.Exists(Liberado) && (DateTime.Now - File.GetLastWriteTime(Liberado)).TotalSeconds < 6; }
            catch { return false; }
        }

        public static void Iniciar()
        {
            proc = (g, ev, h, o, f, t, tp) =>
            {
                if (o != 0 || h == IntPtr.Zero) return;
                try
                {
                    if (Win32.Classe(h) != "CabinetWClass" || EstaLiberado()) return;
                    // criada: já fica invisível (o aviso de "apareceu" chega atrasado e ela piscava na tela)
                    if (ev == 0x8000) { Win32.Invisivel(h); return; }
                    lock (tratadas) { if (!tratadas.Add(h)) return; if (tratadas.Count > 200) tratadas.Clear(); }
                    Win32.Esconder(h);   // some antes de aparecer; se não for pasta de verdade, volta
                    var th = new Thread(() => Trocar(h)); th.SetApartmentState(ApartmentState.STA); th.IsBackground = true; th.Start();
                }
                catch (Exception e) { Log("erro: " + e.Message); }
            };
            g1 = SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, proc, 0, 0, 2);   // EVENT_OBJECT_SHOW
            g2 = SetWinEventHook(0x8018, 0x8018, IntPtr.Zero, proc, 0, 0, 2);   // EVENT_OBJECT_UNCLOAKED
            g3 = SetWinEventHook(0x8000, 0x8000, IntPtr.Zero, proc, 0, 0, 2);   // EVENT_OBJECT_CREATE
        }

        public static void Parar()
        {
            foreach (var g in new[] { g1, g2, g3 }) if (g != IntPtr.Zero) UnhookWinEvent(g);
            g1 = g2 = g3 = IntPtr.Zero;
        }

        static void Trocar(IntPtr h)
        {
            string pasta = null, arquivo = null;
            for (int i = 0; i < 30 && pasta == null; i++)   // a navegação do Explorador termina um pouco depois
            {
                Lugar(h, out pasta, out arquivo);
                if (pasta == null) Thread.Sleep(100);
            }
            string zip = pasta == null ? null : ZipDe(pasta);
            if (zip != null)
            {
                // zip aberto no Explorador: como no Mac, o Finder descompacta ao lado e mostra a pasta
                Win32.PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero);   // WM_CLOSE
                Log("descompactar: " + zip);
                try { Process.Start(Process.GetCurrentProcess().MainModule.FileName, "--descompactar \"" + zip + "\""); } catch (Exception e) { Log("não abriu: " + e.Message); }
                return;
            }
            if (pasta == null || !Directory.Exists(pasta)) { Log("mantido no Explorador: " + (pasta ?? "sem pasta")); Win32.MostrarSemAtivar(h); return; }
            // o /select preenche o foco um instante depois do caminho
            for (int i = 0; i < 5 && arquivo == null; i++) { Thread.Sleep(100); string p; Lugar(h, out p, out arquivo); }
            Win32.PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero);   // WM_CLOSE
            string args = "--finder \"" + (pasta.EndsWith("\\") ? pasta + "\\" : pasta) + "\"" + (arquivo == null ? "" : " \"" + arquivo + "\"");
            Log("Finder: " + args);
            try { Process.Start(Process.GetCurrentProcess().MainModule.FileName, args); } catch (Exception e) { Log("não abriu: " + e.Message); }
        }

        // "C:\...\x.zip" ou uma pasta dentro dele ("C:\...\x.zip\sub"): o caminho do zip; null se não for zip
        static string ZipDe(string p)
        {
            int i = p.IndexOf(".zip", StringComparison.OrdinalIgnoreCase);
            while (i >= 0)
            {
                int fim = i + 4;
                if (fim == p.Length || p[fim] == System.IO.Path.DirectorySeparatorChar) { string z = p.Substring(0, fim); if (File.Exists(z)) return z; }
                i = p.IndexOf(".zip", fim, StringComparison.OrdinalIgnoreCase);
            }
            return null;
        }

        static object Ler(object o, string nome, params object[] a)
        {
            return o.GetType().InvokeMember(nome, a.Length > 0 && nome == "Item" ? BindingFlags.InvokeMethod : BindingFlags.GetProperty, null, o, a.Length > 0 ? a : null);
        }

        // pasta e item em foco da janela h, pelo Shell.Application (late-binding: sem referência extra)
        static void Lugar(IntPtr h, out string pasta, out string arquivo)
        {
            pasta = arquivo = null;
            try
            {
                var tipo = Type.GetTypeFromProgID("Shell.Application");
                object app = Activator.CreateInstance(tipo);
                object janelas = tipo.InvokeMember("Windows", BindingFlags.InvokeMethod, null, app, null);
                int n = Convert.ToInt32(Ler(janelas, "Count"));
                for (int i = 0; i < n; i++)
                {
                    object j = Ler(janelas, "Item", i);
                    if (j == null || Convert.ToInt64(Ler(j, "HWND")) != h.ToInt64()) continue;
                    object doc = Ler(j, "Document");
                    string caminho = (string)Ler(Ler(Ler(doc, "Folder"), "Self"), "Path");
                    if (!string.IsNullOrEmpty(caminho) && ((caminho.Length > 1 && caminho[1] == ':') || caminho.StartsWith("\\\\"))) pasta = caminho;
                    try { object foco = Ler(doc, "FocusedItem"); if (foco != null) arquivo = (string)Ler(foco, "Path"); } catch { }
                    return;
                }
            }
            catch { }
        }
    }
}
