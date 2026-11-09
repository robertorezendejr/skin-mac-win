// Mac Skin: um só programa nativo no lugar dos scripts PowerShell (dock, barra de menus e cores dos botões).
//   MacSkin.exe           abre dock + barra + botões (uma instância só)
//   MacSkin.exe --vigia   abre o de cima e reabre se ele cair (é o que roda no login)
//   MacSkin.exe --finder [pasta]   abre uma janela do Finder
//   MacSkin.exe --descompactar arquivo.zip   descompacta ao lado e mostra no Finder (zip aberto no Explorador)
//   MacSkin.exe --holofote        só o holofote (Ctrl duas vezes), para testar sem reiniciar o resto
//   MacSkin.exe --escolher hwnd    troca a caixa Abrir/Salvar/Selecionar pasta de outro programa pelo Finder
// "Desligar Mac Skin" cria o arquivo "desligado" na pasta e fecha tudo; o vigia respeita.
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace MacSkin
{
    static class Programa
    {
        const int JaAberto = 3;

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--vigia") return Vigia();            if (args.Length > 0 && args[0] == "--finder") return AbrirFinder(args.Length > 1 ? args[1] : null, null, args.Length > 2 ? args[2] : null);
            if (args.Length > 1 && args[0] == "--descompactar") return AbrirFinder(Path.GetDirectoryName(args[1]), null, null, args[1]);
            if (args.Length > 1 && args[0] == "--escolher") return Escolha.Abrir(new IntPtr(long.Parse(args[1])));
            if (args.Length > 2 && args[0] == "--medir-finder") return AbrirFinder(args[1], args[2]);
            if (args.Length > 0 && args[0] == "--holofote") { Holofote.Iniciar(); Thread.Sleep(Timeout.Infinite); return 0; }   // teste: só o holofote
            if (args.Length > 0 && args[0] == "--janelas")   // diagnóstico: o que a dock enxerga como app aberto
            {
                var linhas = new System.Collections.Generic.List<string>();
                foreach (var j in Janelas.Abertas()) linhas.Add(j.Chave + "\t" + Janelas.Nome(j) + "\t" + j.Titulo + "\t" + Janelas.LogoDoPacote(j.Aumid));
                File.WriteAllLines(Path.Combine(Comum.Dir, "janelas.txt"), linhas);
                return 0;
            }
            return Abrir();
        }

        // cada janela do Finder é um processo à parte: se uma travar numa pasta de rede, as outras e a dock seguem
        static int AbrirFinder(string pasta, string medirEm = null, string selecionar = null, string descompactar = null)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnLastWindowClose };
            app.DispatcherUnhandledException += (s, e) => { Comum.Log("finder.log", "falha: " + e.Exception); e.Handled = true; };
            // a leitura da pasta (Task) devolve o resultado para a tela por este contexto; sem ele, só existiria após o Run
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            try { var f = new Finder(pasta, selecionar); f.Mostrar(); if (medirEm != null) f.Medir(medirEm); if (descompactar != null) f.Descompactar(descompactar); }
            catch (Exception e) { Comum.Log("finder.log", "não abriu: " + e); return 1; }
            return app.Run();
        }

        static int Abrir()
        {
            bool novo;
            var mutex = new Mutex(true, @"Local\MacSkinNativo", out novo);
            if (!novo) return JaAberto;

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            // erro inesperado vai para o log em vez de fechar (se fechar mesmo assim, o vigia reabre)
            app.DispatcherUnhandledException += (s, e) => { Comum.Log("macskin.log", "falha: " + e.Exception); e.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Comum.Log("macskin.log", "falha fatal: " + e.ExceptionObject);

            Bandeja.Iniciar();   // antes da barra e da dock: as reservas de tela delas passam por aqui
            var barra = new Barra();
            var dock = new Dock();
            var botoes = new Botoes();
            var volume = new Volume();
            // fechar qualquer janela principal (Desligar Mac Skin manda WM_CLOSE) encerra tudo, devolvendo os espaços
            // reservados e a barra do Windows
            EventHandler encerrar = null;
            encerrar = delegate
            {
                barra.Janela.Closed -= encerrar; dock.Janela.Closed -= encerrar;
                barra.Fechar(); dock.Fechar(); volume.Parar(); Substituicao.Parar(); Confirmacao.Parar(); Explorador.Parar(); Escolha.Parar(); Arrastar3Dedos.Parar(); Holofote.Parar(); Bandeja.Parar();
                app.Shutdown(0);
            };
            barra.Janela.Closed += encerrar; dock.Janela.Closed += encerrar;

            barra.Mostrar(); dock.Mostrar(); botoes.Mostrar(); volume.Iniciar(); Confirmacao.Iniciar(); Explorador.Iniciar(); Escolha.Iniciar(); Substituicao.Iniciar(); Arrastar3Dedos.Iniciar(); Holofote.Iniciar();
            // mais leve: devolve ao Windows a memória que o .NET não está usando, mas só com o computador parado (2 min
            // sem mouse/teclado) e uma vez por pausa. Fazer isso a cada minuto com o usuário mexendo deixava a dock e a
            // barra lentas no próximo toque, porque tudo voltava do disco aos poucos.
            bool limpou = false;
            var leve = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            leve.Tick += delegate
            {
                var li = new LASTINPUTINFO { cbSize = 8 };
                if (!GetLastInputInfo(ref li)) return;
                bool parado = (uint)Environment.TickCount - li.dwTime > 120000;
                if (!parado) { limpou = false; return; }
                if (limpou) return;
                limpou = true;
                GC.Collect(); GC.WaitForPendingFinalizers();
                SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1));
            };
            leve.Start();
            int r = app.Run();
            GC.KeepAlive(mutex);
            return r;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr h, IntPtr min, IntPtr max);
        struct LASTINPUTINFO { public uint cbSize, dwTime; }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO i);

        // reabre o Mac Skin se ele cair; não fica consultando nada, só espera o processo terminar
        static int Vigia()
        {
            bool novo;
            var mutex = new Mutex(true, @"Local\MacSkinVigiaNativo", out novo);
            if (!novo) return JaAberto;
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            string desligado = Path.Combine(Comum.Dir, "desligado");
            while (true)
            {
                if (File.Exists(desligado)) { Thread.Sleep(3000); continue; }
                try
                {
                    using (var p = Process.Start(exe))
                    {
                        p.WaitForExit();
                        if (p.ExitCode == JaAberto) { Thread.Sleep(10000); continue; }   // outro já está aberto
                        if (!File.Exists(desligado)) Comum.Log("vigia.log", "Mac Skin fechou (código " + p.ExitCode + "), reabrindo");
                    }
                }
                catch (Exception e) { Comum.Log("vigia.log", "erro: " + e.Message); }
                Thread.Sleep(2000);
            }
        }
    }
}
