// Mac Skin: som de teclado mecânico. Cada tecla toca um clique (um ao descer, outro ao subir), com tom e volume um pouco
// diferentes a cada toque, do lado da tecla (esquerda/direita) e sem atraso perceptível.
//
// Três threads:
//   "SomTeclado"        gancho de teclado/mouse (WH_KEYBOARD_LL / WH_MOUSE_LL), atalho, foco. O gancho só põe um número
//                       numa fila e volta: o Windows tira o gancho em silêncio se ele demorar.
//   "SomTeclado-Audio"  acorda com a fila ou com o motor de áudio (WASAPI, Wasapi.cs), mistura as vozes e entrega o buffer.
//                       Nada é alocado no laço (o coletor do .NET causaria estalos). Para o fluxo depois de 1,5 s quieto.
//   "SomTeclado-Foco"   pergunta ao app em foco (MSAA) se o campo é de senha; em campo de senha não toca nada.
//
// Os sons próprios (Grafite, Cristal, Seda) são sintetizados aqui (modos ressonantes + ruído filtrado): 100% nossos.
// Pacotes de WAV também servem: pasta sons\<perfil>\ com comum-desce-1.wav etc. (ver CarregarPasta).
// Ajustes em %APPDATA%\Mac Skin Dados\som-teclado.txt (não some ao desinstalar).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MacSkin
{
    static class SomTeclado
    {
        static void Log(string m) { Comum.Log("som-teclado.log", m); }

        // ---------- grupos de som ----------
        public const int GComum = 0, GEspaco = 1, GEnter = 2, GApagar = 3, GMod = 4, GMouse = 5, GRolagem = 6, NGrupos = 7;
        public static readonly string[] NomesGrupo = { "comum", "espaco", "enter", "apagar", "mod", "mouse", "rolagem" };

        // ---------- ajustes ----------
        public class Ajustes
        {
            public bool Ligado = false;
            public int Volume = 70;
            public string Perfil = "grafite";
            public bool Modificadores = false, Mouse = false, Rolagem = false;
            public int Espacial = 60, Variacao = 50;
            public bool PausaTelaCheia = true, PausaSenha = true;
            public string Atalho = "ctrl+alt+k";
            public string Saida = "";

            public Ajustes Copia() { return (Ajustes)MemberwiseClone(); }

            public static Ajustes Ler(string arq, Action<string> avisar)
            {
                var a = new Ajustes();
                if (!File.Exists(arq)) return a;
                foreach (var bruta in File.ReadAllLines(arq))
                {
                    var linha = bruta.Trim();
                    if (linha.Length == 0 || linha[0] == '#') continue;
                    int i = linha.IndexOf('=');
                    string k = (i < 0 ? linha : linha.Substring(0, i)).Trim().ToLowerInvariant(), v = i < 0 ? "" : linha.Substring(i + 1).Trim();
                    bool ok = true;
                    switch (k)
                    {
                        case "ligado": ok = Bool(v, ref a.Ligado); break;
                        case "volume": ok = Int(v, 0, 100, ref a.Volume); break;
                        case "perfil": if (v.Length > 0 && v.IndexOfAny(Path.GetInvalidFileNameChars()) < 0) a.Perfil = v.ToLowerInvariant(); else ok = false; break;
                        case "modificadores": ok = Bool(v, ref a.Modificadores); break;
                        case "mouse": ok = Bool(v, ref a.Mouse); break;
                        case "rolagem": ok = Bool(v, ref a.Rolagem); break;
                        case "espacial": ok = Int(v, 0, 100, ref a.Espacial); break;
                        case "variacao": ok = Int(v, 0, 100, ref a.Variacao); break;
                        case "pausa_tela_cheia": ok = Bool(v, ref a.PausaTelaCheia); break;
                        case "pausa_senha": ok = Bool(v, ref a.PausaSenha); break;
                        case "atalho": { uint m, vk; if (v.Length == 0 || LerAtalho(v, out m, out vk)) a.Atalho = v.ToLowerInvariant(); else ok = false; break; }
                        case "saida": a.Saida = v; break;
                        default: if (avisar != null) avisar("ajuste desconhecido ignorado: " + k); break;
                    }
                    if (!ok && avisar != null) avisar("valor inválido em " + k + "=" + v + ", fica o padrão");
                }
                return a;
            }

            public void Gravar(string arq)
            {
                var s = new StringBuilder();
                s.AppendLine("# Som de teclado do Mac Skin (pode editar; vale ao salvar a partir da Central de Controle ou ao reiniciar)");
                s.AppendLine("ligado=" + (Ligado ? 1 : 0));
                s.AppendLine("volume=" + Volume);
                s.AppendLine("perfil=" + Perfil);
                s.AppendLine("modificadores=" + (Modificadores ? 1 : 0));
                s.AppendLine("mouse=" + (Mouse ? 1 : 0));
                s.AppendLine("rolagem=" + (Rolagem ? 1 : 0));
                s.AppendLine("espacial=" + Espacial);
                s.AppendLine("variacao=" + Variacao);
                s.AppendLine("pausa_tela_cheia=" + (PausaTelaCheia ? 1 : 0));
                s.AppendLine("pausa_senha=" + (PausaSenha ? 1 : 0));
                s.AppendLine("atalho=" + Atalho);
                s.AppendLine("saida=" + Saida);
                Directory.CreateDirectory(Path.GetDirectoryName(arq));
                string tmp = arq + ".tmp";
                File.WriteAllText(tmp, s.ToString(), new UTF8Encoding(false));
                if (File.Exists(arq)) File.Replace(tmp, arq, null); else File.Move(tmp, arq);
            }

            static bool Bool(string v, ref bool alvo)
            {
                switch (v.ToLowerInvariant()) { case "1": case "sim": case "true": alvo = true; return true; case "0": case "nao": case "não": case "false": alvo = false; return true; }
                return false;
            }
            static bool Int(string v, int min, int max, ref int alvo)
            {
                int n; if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < min || n > max) return false;
                alvo = n; return true;
            }
        }

        public static string Arquivo { get { return Path.Combine(Substituicao.PastaDados, "som-teclado.txt"); } }
        static string ArquivoEstatistica { get { return Path.Combine(Substituicao.PastaDados, "som-teclado-estatisticas.txt"); } }
        static volatile Ajustes aj = new Ajustes();
        public static Ajustes Atual { get { return aj; } }
        public static event Action Mudou;   // chamado na thread da interface, se houver

        // "ctrl+alt+k" → MOD_CONTROL|MOD_ALT e 'K'. Pelo menos um modificador (F13–F24 dispensam).
        public static bool LerAtalho(string s, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            foreach (var p0 in s.ToLowerInvariant().Split('+'))
            {
                var p = p0.Trim();
                if (p == "ctrl" || p == "control") mods |= 2;
                else if (p == "alt") mods |= 1;
                else if (p == "shift") mods |= 4;
                else if (p == "win") mods |= 8;
                else if (vk != 0) return false;
                else if (p.Length == 1 && ((p[0] >= 'a' && p[0] <= 'z') || (p[0] >= '0' && p[0] <= '9'))) vk = char.ToUpperInvariant(p[0]);
                else if (p.Length >= 2 && p[0] == 'f') { int n; if (!int.TryParse(p.Substring(1), out n) || n < 1 || n > 24) return false; vk = (uint)(0x70 + n - 1); }
                else if (p == "espaco" || p == "espaço" || p == "space") vk = 0x20;
                else return false;
            }
            if (vk == 0) return false;
            if (mods == 0 && !(vk >= 0x7C && vk <= 0x87)) return false;
            return true;
        }

        public static string AtalhoLegivel(string s)
        {
            uint m, vk; if (!LerAtalho(s, out m, out vk)) return "nenhum";
            var r = new List<string>();
            if ((m & 2) != 0) r.Add("Ctrl"); if ((m & 1) != 0) r.Add("Alt"); if ((m & 4) != 0) r.Add("Shift"); if ((m & 8) != 0) r.Add("Win");
            r.Add(vk == 0x20 ? "Espaço" : vk >= 0x70 && vk <= 0x87 ? "F" + (vk - 0x6F) : ((char)vk).ToString());
            return string.Join("+", r);
        }

        // ---------- mapa das teclas ----------
        public static int Grupo(uint vk)
        {
            switch (vk)
            {
                case 0x20: return GEspaco;
                case 0x0D: return GEnter;
                case 0x08: case 0x2E: return GApagar;
                case 0x10: case 0x11: case 0x12: case 0xA0: case 0xA1: case 0xA2: case 0xA3: case 0xA4: case 0xA5: case 0x5B: case 0x5C: return GMod;
            }
            return GComum;
        }

        // posição horizontal da tecla (0 = esquerda, 1 = direita) pelo código de varredura: vale para qualquer layout
        static readonly float[] colNormal = new float[256], colEstendida = new float[256];
        static SomTeclado()
        {
            for (int i = 0; i < 256; i++) colNormal[i] = colEstendida[i] = 7;
            colNormal[0x01] = 0; colNormal[0x29] = 0;
            for (int i = 0; i < 12; i++) colNormal[0x02 + i] = 1 + i;          // 1 … =
            colNormal[0x0E] = 13.5f;                                             // Backspace
            colNormal[0x0F] = 0.3f; for (int i = 0; i < 12; i++) colNormal[0x10 + i] = 1.5f + i;   // Tab, Q … ]
            colNormal[0x1C] = 13.5f; colNormal[0x2B] = 13.2f;                   // Enter, \ (ABNT2: ])
            colNormal[0x3A] = 0.4f; for (int i = 0; i < 11; i++) colNormal[0x1E + i] = 1.8f + i;   // Caps, A … ~
            colNormal[0x2A] = 0.6f; colNormal[0x56] = 1.3f; for (int i = 0; i < 10; i++) colNormal[0x2C + i] = 2.3f + i;   // Shift, \, Z … ;
            colNormal[0x73] = 12.3f; colNormal[0x36] = 13.5f;                   // / (ABNT2), Shift direito
            colNormal[0x1D] = 0.3f; colNormal[0x38] = 2.7f; colNormal[0x39] = 7f;   // Ctrl, Alt, Espaço
            for (int i = 0; i < 10; i++) colNormal[0x3B + i] = 1 + i * 1.05f;   // F1 … F10
            colNormal[0x57] = 11.5f; colNormal[0x58] = 12.6f;                   // F11, F12
            colEstendida[0x5B] = 1.5f; colEstendida[0x5C] = 11.5f; colEstendida[0x5D] = 12.2f;   // Win, Win direito, Menu
            colEstendida[0x38] = 10.5f; colEstendida[0x1D] = 13.5f;            // AltGr, Ctrl direito
            foreach (int s in new[] { 0x52, 0x47, 0x49 }) colEstendida[s] = 16f;   // Insert, Home, PgUp
            foreach (int s in new[] { 0x53, 0x4F, 0x51 }) colEstendida[s] = 16.5f; // Delete, End, PgDn
            colEstendida[0x48] = 16.2f; colEstendida[0x4B] = 15.5f; colEstendida[0x50] = 16.2f; colEstendida[0x4D] = 17f;   // setas
            // teclado numérico
            colNormal[0x45] = 18; colEstendida[0x35] = 19; colNormal[0x37] = 20; colNormal[0x4A] = 21;
            colNormal[0x47] = 18; colNormal[0x48] = 19; colNormal[0x49] = 20; colNormal[0x4E] = 21;
            colNormal[0x4B] = 18; colNormal[0x4C] = 19; colNormal[0x4D] = 20;
            colNormal[0x4F] = 18; colNormal[0x50] = 19; colNormal[0x51] = 20; colEstendida[0x1C] = 21;
            colNormal[0x52] = 18.5f; colNormal[0x53] = 20;
        }
        public static float Coluna(uint scan, bool estendida)
        {
            var t = estendida ? colEstendida : colNormal;
            return Math.Max(0, Math.Min(1, t[scan & 0xFF] / 21f));
        }

        // ---------- perfis ----------
        public class Perfil
        {
            public string Id, Nome, Origem, Licenca;
            public string Base;          // perfil de origem (o próprio Id nos de fábrica e nos pacotes)
            public bool Wav;             // veio de um pacote de WAV (o timbre não se edita, só os volumes)
            public bool Editado;         // tem ajustes gravados em sons-editados
            public bool Proprio;         // criado pelo usuário no editor (pode excluir)
            public int Taxa;
            public float[][][] Amostras = new float[NGrupos * 2][][];   // [grupo*2 + (desce ? 0 : 1)][variação]
            public float[][] De(int grupo, bool desce)
            {
                var a = Amostras[grupo * 2 + (desce ? 0 : 1)];
                if (a != null && a.Length > 0) return a;
                if (grupo == GMouse || grupo == GRolagem) return null;   // mouse e rolagem sem som próprio: ficam calados
                a = Amostras[GComum * 2 + (desce ? 0 : 1)];
                if (a != null && a.Length > 0) return a;
                return null;
            }
        }
        static volatile Perfil[] perfis = new Perfil[0];
        public static Perfil[] Perfis { get { return perfis; } }
        static volatile int perfilAtual;

        static int IndicePerfil(Perfil[] ps, string id)
        {
            for (int i = 0; i < ps.Length; i++) if (ps[i].Id == id) return i;
            return ps.Length > 0 ? 0 : -1;
        }

        public static Perfil[] CarregarPerfis(IEnumerable<string> pastasExtras)
        {
            return CarregarPerfis(pastasExtras, EdicaoSom.LerTodas());
        }

        public static Perfil[] CarregarPerfis(IEnumerable<string> pastasExtras, List<EdicaoSom> edicoes)
        {
            return CarregarPerfis(pastasExtras, null, edicoes);
        }

        static Perfil[] CarregarPerfis(IEnumerable<string> pastasExtras, Perfil[] brutos, List<EdicaoSom> edicoes)
        {
            if (brutos == null) brutos = CarregarBrutos(pastasExtras);
            var r = new List<Perfil>();
            foreach (var b in brutos)
            {
                var e = edicoes.Find(x => x.Id == b.Id);
                r.Add(e != null ? Aplicar(b, e, brutos) ?? b : b);
            }
            foreach (var e in edicoes)
            {
                if (r.Exists(x => x.Id == e.Id)) continue;
                var p = Aplicar(null, e, brutos);
                if (p != null) r.Add(p); else Log("perfil próprio sem base (" + e.Base + "): " + e.Id);
            }
            return r.ToArray();
        }

        // perfil com os ajustes do editor: sintetizados são gerados de novo com o timbre mudado; pacotes de WAV só mudam os volumes
        public static Perfil Aplicar(Perfil original, EdicaoSom e, Perfil[] brutos)
        {
            string baseId = original != null ? original.Id : e.Base;
            Perfil b = original;
            if (b == null) foreach (var x in brutos) if (x.Id == baseId) b = x;
            if (b == null) return null;
            Perfil p;
            if (!b.Wav) p = Sintese.PorId(baseId, e);
            else
            {
                p = new Perfil { Taxa = b.Taxa, Origem = b.Origem, Licenca = b.Licenca, Wav = true };
                for (int g = 0; g < NGrupos; g++)
                    for (int d = 0; d < 2; d++)
                    {
                        var a = b.Amostras[g * 2 + d]; if (a == null) continue;
                        float k = e.GanhoGrupo(g) * (d == 1 ? e.FatorSoltar : 1);
                        if (k <= 0.001f) continue;
                        var n = new float[a.Length][];
                        for (int v = 0; v < a.Length; v++) { n[v] = new float[a[v].Length]; for (int i = 0; i < n[v].Length; i++) n[v][i] = a[v][i] * k; }
                        p.Amostras[g * 2 + d] = n;
                    }
                if (p.Amostras[0] == null) p.Amostras[0] = new[] { new float[1] };   // tudo zerado: fica calado, mas o perfil continua válido
            }
            if (p == null) return null;
            p.Id = e.Id; p.Base = baseId; p.Editado = true; p.Proprio = e.Id != baseId;
            p.Nome = !string.IsNullOrEmpty(e.Nome) ? e.Nome : b.Nome;
            return p;
        }

        static Perfil[] CarregarBrutos(IEnumerable<string> pastasExtras)
        {
            var r = new List<Perfil>();
            r.Add(Sintese.Grafite()); r.Add(Sintese.Cristal()); r.Add(Sintese.Seda());
            foreach (var raiz in pastasExtras)
            {
                try
                {
                    if (!Directory.Exists(raiz)) continue;
                    foreach (var d in Directory.GetDirectories(raiz))
                    {
                        try
                        {
                            var p = CarregarPasta(d);
                            if (p == null) { Log("pacote ignorado (falta comum-desce-*.wav): " + d); continue; }
                            r.RemoveAll(x => x.Id == p.Id);
                            r.Add(p);
                        }
                        catch (Exception e) { Log("pacote " + d + ": " + e.Message); }
                    }
                }
                catch (Exception e) { Log("pasta de sons " + raiz + ": " + e.Message); }
            }
            return r.ToArray();
        }

        // pasta com perfil.txt (nome=, licenca=, fonte=) e <grupo>-<desce|sobe>-<n>.wav; comum-desce é obrigatório
        public static Perfil CarregarPasta(string pasta)
        {
            var p = new Perfil { Id = Path.GetFileName(pasta).ToLowerInvariant(), Nome = Path.GetFileName(pasta), Taxa = 48000, Origem = pasta, Wav = true };
            p.Base = p.Id;
            string info = Path.Combine(pasta, "perfil.txt");
            if (File.Exists(info))
                foreach (var l in File.ReadAllLines(info))
                {
                    int i = l.IndexOf('='); if (i < 0) continue;
                    string k = l.Substring(0, i).Trim().ToLowerInvariant(), v = l.Substring(i + 1).Trim();
                    if (k == "nome" && v.Length > 0) p.Nome = v; else if (k == "licenca" || k == "licença") p.Licenca = v;
                }
            for (int g = 0; g < NGrupos; g++)
                for (int d = 0; d < 2; d++)
                {
                    var lista = new List<float[]>();
                    var arquivos = Directory.GetFiles(pasta, NomesGrupo[g] + "-" + (d == 0 ? "desce" : "sobe") + "-*.wav");
                    Array.Sort(arquivos, StringComparer.OrdinalIgnoreCase);
                    foreach (var f in arquivos)
                    {
                        try { lista.Add(Wav.LerMono(f, p.Taxa)); }
                        catch (Exception e) { Log("wav " + f + ": " + e.Message); }
                    }
                    if (lista.Count > 0) p.Amostras[g * 2 + d] = lista.ToArray();
                }
            if (p.Amostras[0] == null) return null;
            return p;
        }

        // ---------- edição (janela "Som de Teclado") ----------
        static string[] PastasSons { get { return new[] { Path.Combine(Comum.Dir, "sons"), Path.Combine(Substituicao.PastaDados, "sons") }; } }
        static Perfil[] brutos = new Perfil[0];          // perfis como vieram (sem os ajustes do editor)
        public static Perfil[] Brutos { get { return brutos; } }
        static readonly object travaPerfis = new object();
        public static event Action PerfisMudaram;

        // troca (ou acrescenta) um perfil já montado, sem parar o áudio; as vozes que estão tocando terminam com o som antigo
        static void Trocar(Perfil novo, string tirarId)
        {
            lock (travaPerfis)
            {
                var l = new List<Perfil>(perfis);
                if (tirarId != null) l.RemoveAll(x => x.Id == tirarId);
                if (novo != null)
                {
                    int i = l.FindIndex(x => x.Id == novo.Id);
                    if (i >= 0) l[i] = novo; else l.Add(novo);
                }
                var ps = l.ToArray();
                perfis = ps; perfilAtual = IndicePerfil(ps, aj.Perfil);
            }
            var m = PerfisMudaram; if (m != null) { var app = System.Windows.Application.Current; if (app != null) app.Dispatcher.BeginInvoke(m); else m(); }
            AvisarMudou();
        }

        // grava a edição e remonta só aquele perfil (em segundo plano); "pronto" roda na thread da interface
        public static void SalvarEdicao(EdicaoSom e, Action pronto)
        {
            var copia = e.Copia();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    copia.Gravar();
                    Perfil orig = null; foreach (var b in brutos) if (b.Id == copia.Id) orig = b;
                    var p = Aplicar(orig, copia, brutos);
                    if (p != null) Trocar(p, null);
                }
                catch (Exception ex) { Log("salvar edição " + copia.Id + ": " + ex.Message); }
                Avisar(pronto);
            });
        }

        // apaga a edição: perfil de fábrica volta ao original, perfil próprio some
        public static void ApagarEdicao(string id, Action pronto)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    EdicaoSom.Apagar(id);
                    Perfil orig = null; foreach (var b in brutos) if (b.Id == id) orig = b;
                    if (orig != null) Trocar(orig, null);
                    else
                    {
                        string baseId = null; foreach (var p in perfis) if (p.Id == id) baseId = p.Base;
                        if (aj.Perfil == id) Alterar(a => a.Perfil = baseId ?? "grafite");
                        Trocar(null, id);
                    }
                }
                catch (Exception ex) { Log("apagar edição " + id + ": " + ex.Message); }
                Avisar(pronto);
            });
        }

        static void Avisar(Action a)
        {
            if (a == null) return;
            var app = System.Windows.Application.Current;
            if (app != null) app.Dispatcher.BeginInvoke(a); else a();
        }

        public static string NomePerfilBruto(string id) { foreach (var p in brutos) if (p.Id == id) return p.Nome; return id; }
        public static Perfil PerfilPorId(string id) { foreach (var p in perfis) if (p.Id == id) return p; return null; }

        // um toque de um tipo de tecla (botão ▶ de cada linha do editor), mesmo com o som desligado
        public static void PreviaGrupo(string id, int grupo)
        {
            int idx = -1; var ps = perfis;
            for (int i = 0; i < ps.Length; i++) if (ps[i].Id == id) idx = i;
            if (idx < 0) return;
            if (grupo == GRolagem) { for (int i = 0; i < 4; i++) Enfileirar(Codigo(grupo, true, 0.5f, idx, i * 5)); return; }
            Enfileirar(Codigo(grupo, true, 0.5f, idx, 0));
            Enfileirar(Codigo(grupo, false, 0.5f, idx, grupo == GEspaco ? 12 : 8));
        }

        // som de uma tecla digitada na caixa de teste do editor (só quando o som geral está desligado; ligado, o gancho já toca)
        public static void TesteTecla(string id, uint vk, bool desce)
        {
            if (aj.Ligado) return;
            int idx = -1; var ps = perfis;
            for (int i = 0; i < ps.Length; i++) if (ps[i].Id == id) idx = i;
            if (idx < 0) return;
            Enfileirar(Codigo(Grupo(vk), desce, 0.5f, idx, 0));
        }

        // ---------- estado ----------
        static Thread tGancho, tAudio, tFoco;
        static volatile bool parando;
        static IntPtr hwnd, gTeclado, gMouse, gFoco, acordar, acordarFoco;
        static volatile bool reabrir;
        static uint idThreadGancho;
        public static volatile string UltimoErro = "";
        public static volatile string SaidaInfo = "";
        public static volatile bool PausadoAgora;

        public static void Iniciar()
        {
            if (tGancho != null) return;
            parando = false;
            try { aj = Ajustes.Ler(Arquivo, Log); }
            catch (Exception e) { Log("som-teclado.txt: " + e.Message); }
            aj.Ligado = false;   // sempre começa desligado (pedido do Roberto); liga pela Central, pela janela ou pelo atalho, até reiniciar
            LerEstatistica();
            acordar = Wasapi.CreateEvent(IntPtr.Zero, false, false, null);
            acordarFoco = Wasapi.CreateEvent(IntPtr.Zero, false, false, null);
            try
            {
                brutos = CarregarBrutos(PastasSons);
                var ps = CarregarPerfis(new string[0], brutos, EdicaoSom.LerTodas());
                perfis = ps; perfilAtual = IndicePerfil(ps, aj.Perfil);
            }
            catch (Exception e) { Log("perfis: " + e); }
            tAudio = new Thread(LacoAudio) { IsBackground = true, Name = "SomTeclado-Audio", Priority = ThreadPriority.Highest };
            tAudio.Start();
            tFoco = new Thread(LacoFoco) { IsBackground = true, Name = "SomTeclado-Foco" };
            tFoco.Start();
            var pronto = new ManualResetEvent(false);
            tGancho = new Thread(() => LacoGancho(pronto)) { IsBackground = true, Name = "SomTeclado" };
            tGancho.Start();
            pronto.WaitOne(3000);
            Log("som de teclado pronto (" + (aj.Ligado ? "ligado" : "desligado") + ", perfil " + aj.Perfil + ", " + perfis.Length + " perfis)");
        }

        public static void Parar()
        {
            if (tGancho == null) return;
            parando = true;
            GravarEstatistica();
            if (salvarT != null) { salvarT.Dispose(); salvarT = null; if (salvarPendente) Salvar(); }
            if (hwnd != IntPtr.Zero) PostMessage(hwnd, 0x10 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
            Wasapi.SetEvent(acordar); Wasapi.SetEvent(acordarFoco);
            tGancho.Join(1000); tAudio.Join(1000); tFoco.Join(500);
            tGancho = tAudio = tFoco = null;
        }

        // muda os ajustes (thread da interface); grava com atraso e aplica nos ganchos e no áudio
        public static void Alterar(Action<Ajustes> f)
        {
            var antes = aj; var n = antes.Copia(); f(n); aj = n;
            if (n.Perfil != antes.Perfil) perfilAtual = IndicePerfil(perfis, n.Perfil);
            if (n.Saida != antes.Saida) reabrir = true;
            if (n.Ligado && !antes.Ligado) ReverificarFoco();
            if (hwnd != IntPtr.Zero) PostMessage(hwnd, WM_APLICAR, IntPtr.Zero, IntPtr.Zero);
            Wasapi.SetEvent(acordar);
            SalvarDepois();
            AvisarMudou();
        }

        public static void Alternar()
        {
            Alterar(a => a.Ligado = !a.Ligado);
            var app = System.Windows.Application.Current;
            if (app != null) app.Dispatcher.BeginInvoke(new Action(() =>
            {
                var a = aj;
                Volume.Aviso(a.Ligado ? "Som de teclado: " + NomePerfil(a.Perfil) : "Som de teclado desligado", a.Volume / 100f, !a.Ligado);
            }));
        }

        public static string NomePerfil(string id)
        {
            foreach (var p in perfis) if (p.Id == id) return p.Nome;
            return id;
        }

        static void AvisarMudou()
        {
            var m = Mudou; if (m == null) return;
            var app = System.Windows.Application.Current;
            if (app != null) app.Dispatcher.BeginInvoke(m); else m();
        }

        static Timer salvarT; static volatile bool salvarPendente;
        static void SalvarDepois()
        {
            salvarPendente = true;
            if (salvarT == null) salvarT = new Timer(_ => Salvar(), null, 500, Timeout.Infinite);
            else salvarT.Change(500, Timeout.Infinite);
        }
        static readonly object travaArquivo = new object();
        static void Salvar()
        {
            lock (travaArquivo)
            {
                salvarPendente = false;
                try { aj.Gravar(Arquivo); } catch (Exception e) { Log("gravar ajustes: " + e.Message); }
            }
        }

        // toca uma amostra curta do perfil (mesmo com o som desligado), como se digitasse "oi "
        public static void Previa(string id)
        {
            int idx = -1; var ps = perfis;
            for (int i = 0; i < ps.Length; i++) if (ps[i].Id == id) idx = i;
            if (idx < 0) return;
            float[] pans = { 0.45f, 0.6f, 0.5f };
            int[] grupos = { GComum, GComum, GEspaco };
            for (int i = 0; i < 3; i++)
            {
                int t = i * 14;   // a cada 140 ms
                Enfileirar(Codigo(grupos[i], true, pans[i], idx, t));
                Enfileirar(Codigo(grupos[i], false, pans[i], idx, t + 7));
            }
        }

        // um toque só do perfil atual (retorno ao mexer no volume), mesmo com o som desligado
        public static void Clique()
        {
            int idx = perfilAtual; if (idx < 0) return;
            Enfileirar(Codigo(GComum, true, 0.5f, idx, 0));
            Enfileirar(Codigo(GComum, false, 0.5f, idx, 7));
        }

        // ---------- fila (gancho → áudio) ----------
        // código: bits 0-2 grupo, 3 desce, 8-15 posição (0-255), 16-23 perfil (255 = o atual), 24-30 atraso em 10 ms
        public static int Codigo(int grupo, bool desce, float pos, int perfil, int atraso10ms)
        {
            int p = (int)(Math.Max(0, Math.Min(1, pos)) * 255 + 0.5f);
            return grupo | (desce ? 8 : 0) | (p << 8) | ((perfil < 0 ? 255 : perfil) << 16) | (Math.Min(127, atraso10ms) << 24);
        }
        const int TamFila = 512;
        static readonly int[] fila = new int[TamFila];
        static int filaIni, filaFim;
        static readonly object travaFila = new object();
        public static int Descartados;
        // medida da latência (testes): quando o último evento entrou na fila, quando o som dele foi escrito no buffer e
        // quanto áudio já estava na frente dele (ms)
        public static long MedFila, MedEscrita; public static double MedNaFrenteMs;
        public static void Enfileirar(int ev)
        {
            MedFila = Stopwatch.GetTimestamp();
            lock (travaFila)
            {
                int prox = (filaFim + 1) & (TamFila - 1);
                if (prox == filaIni) { Descartados++; return; }
                fila[filaFim] = ev; filaFim = prox;
            }
            Wasapi.SetEvent(acordar);
        }
        public static int EsvaziarFila() { int n = 0, ev; while (Tirar(out ev)) n++; return n; }
        public static void DefinirSenhaParaTeste(bool v) { senha = v; focoPendente = false; }
        public static bool GanchoTeclado { get { return gTeclado != IntPtr.Zero; } }
        public static bool GanchoMouse { get { return gMouse != IntPtr.Zero; } }
        static bool Tirar(out int ev)
        {
            lock (travaFila)
            {
                if (filaIni == filaFim) { ev = 0; return false; }
                ev = fila[filaIni]; filaIni = (filaIni + 1) & (TamFila - 1); return true;
            }
        }

        // ---------- mistura ----------
        public class Mixer
        {
            public const int MaxVozes = 32;
            readonly float[][] vS = new float[MaxVozes][];
            readonly double[] vPos = new double[MaxVozes], vPasso = new double[MaxVozes];
            readonly float[] vE = new float[MaxVozes], vD = new float[MaxVozes];
            readonly long[] vIdade = new long[MaxVozes];
            long relogio; public int Roubadas; public long Iniciadas; public float Pico;
            // agenda (prévias): eventos com atraso, em quadros
            readonly int[] agEv = new int[64]; readonly long[] agQuando = new long[64]; int agN;
            public long Quadros;   // quadros já misturados
            uint semente = 0x9E3779B9;
            readonly int[] ultimaVar = new int[NGrupos * 2 * 256];

            public int Ativas { get { int n = 0; for (int i = 0; i < MaxVozes; i++) if (vS[i] != null) n++; return n; } }
            public bool Ocupado { get { return agN > 0 || Ativas > 0; } }
            public void Limpar() { for (int i = 0; i < MaxVozes; i++) vS[i] = null; agN = 0; }

            public float Aleatorio() { semente ^= semente << 13; semente ^= semente >> 17; semente ^= semente << 5; return (semente / 4294967296f) * 2 - 1; }

            public void Tocar(float[] s, double passo, float ge, float gd)
            {
                int livre = -1; long maisVelha = long.MaxValue; int velha = 0;
                for (int i = 0; i < MaxVozes; i++)
                {
                    if (vS[i] == null) { livre = i; break; }
                    if (vIdade[i] < maisVelha) { maisVelha = vIdade[i]; velha = i; }
                }
                if (livre < 0) { livre = velha; Roubadas++; }
                Iniciadas++;
                vS[livre] = s; vPos[livre] = 0; vPasso[livre] = passo; vE[livre] = ge; vD[livre] = gd; vIdade[livre] = ++relogio;
            }

            // evento da fila → voz (ou agenda, se tiver atraso)
            public void Evento(int ev, Perfil[] ps, int perfilPadrao, int taxaSaida, int espacial, int variacao)
            {
                int atraso = (ev >> 24) & 127;
                if (atraso > 0)
                {
                    if (agN < agEv.Length) { agEv[agN] = ev & 0x00FFFFFF; agQuando[agN] = Quadros + (long)atraso * taxaSaida / 100; agN++; }
                    return;
                }
                int grupo = ev & 7; bool desce = (ev & 8) != 0; float pos = ((ev >> 8) & 255) / 255f; int pi = (ev >> 16) & 255;
                if (pi == 255) pi = perfilPadrao;
                if (pi < 0 || pi >= ps.Length) return;
                var p = ps[pi];
                var vars = p.De(grupo, desce);
                if (vars == null || vars.Length == 0) return;
                // sorteia a variação, sem repetir a última do mesmo grupo
                int chave = ((pi & 255) * NGrupos + grupo) * 2 + (desce ? 0 : 1);
                int k = (int)((Aleatorio() + 1) * 0.5f * vars.Length); if (k >= vars.Length) k = vars.Length - 1;
                if (vars.Length > 1 && k == ultimaVar[chave]) k = (k + 1) % vars.Length;
                ultimaVar[chave] = k;
                float v = variacao / 100f;
                double cents = Aleatorio() * 60 * v;                       // até ±60 centésimos de semitom
                float ganho = (float)Math.Pow(10, Aleatorio() * 4 * v / 20); // até ±4 dB
                double passo = (double)p.Taxa / taxaSaida * Math.Pow(2, cents / 1200);
                float pan = (pos - 0.5f) * 2 * espacial / 100f;            // -1 … 1
                double th = (pan + 1) * Math.PI / 4;
                Tocar(vars[k], passo, (float)(Math.Cos(th) * 1.4142 * ganho), (float)(Math.Sin(th) * 1.4142 * ganho));
            }

            // mistura n quadros; devolve se houve som
            public bool Misturar(float[] esq, float[] dir, int n, float mestre, Perfil[] ps, int perfilPadrao, int taxaSaida, int espacial, int variacao)
            {
                // agenda: dispara o que vence dentro deste bloco (no começo do bloco; a precisão de 1 período basta)
                for (int i = 0; i < agN; )
                {
                    if (agQuando[i] <= Quadros + n) { int ev = agEv[i]; agN--; agEv[i] = agEv[agN]; agQuando[i] = agQuando[agN]; Evento(ev, ps, perfilPadrao, taxaSaida, espacial, variacao); }
                    else i++;
                }
                Array.Clear(esq, 0, n); Array.Clear(dir, 0, n);
                bool som = false;
                for (int v = 0; v < MaxVozes; v++)
                {
                    var s = vS[v]; if (s == null) continue;
                    som = true;
                    double pos = vPos[v], passo = vPasso[v]; float ge = vE[v] * mestre, gd = vD[v] * mestre;
                    int fim = s.Length - 1;
                    for (int i = 0; i < n; i++)
                    {
                        int j = (int)pos; if (j >= fim) { s = null; break; }
                        float fr = (float)(pos - j);
                        float x = s[j] + (s[j + 1] - s[j]) * fr;
                        esq[i] += x * ge; dir[i] += x * gd;
                        pos += passo;
                    }
                    vPos[v] = pos; if (s == null) vS[v] = null;
                }
                if (som)
                {
                    float pico = Pico;
                    for (int i = 0; i < n; i++)
                    {
                        float a = Limitar(esq[i]), b = Limitar(dir[i]); esq[i] = a; dir[i] = b;
                        if (a > pico) pico = a; else if (-a > pico) pico = -a;
                        if (b > pico) pico = b; else if (-b > pico) pico = -b;
                    }
                    Pico = pico;
                }
                Quadros += n;
                return som;
            }
            // limitador suave acima de 0,8: nunca passa de 1, sem cortar seco
            static float Limitar(float x)
            {
                float a = x < 0 ? -x : x;
                if (a <= 0.8f) return x;
                float y = 0.8f + 0.2f * (1 - 1 / (1 + (a - 0.8f) * 5));
                return x < 0 ? -y : y;
            }
        }

        static readonly Mixer mix = new Mixer();
        public static Mixer Misturador { get { return mix; } }
        static Wasapi.Saida saida;
        public static Wasapi.Saida SaidaAtual { get { return saida; } }

        public static float Mestre(int volume) { float v = volume / 100f; return v * v * 0.9f; }

        static void LacoAudio()
        {
            uint idx = 0; IntPtr mm = IntPtr.Zero;
            try { mm = Wasapi.AvSetMmThreadCharacteristics("Pro Audio", ref idx); } catch { }
            var so = new IntPtr[] { acordar }; var dois = new IntPtr[2];
            Wasapi.Avisos avisos = null; Wasapi.IMMDeviceEnumerator en = null;
            try { en = Wasapi.Enumerador(); avisos = new Wasapi.Avisos { AoMudarPadrao = () => { if (string.IsNullOrEmpty(aj.Saida)) { reabrir = true; Wasapi.SetEvent(acordar); } } }; en.RegisterEndpointNotificationCallback(avisos); }
            catch (Exception e) { Log("aviso de dispositivo: " + e.Message); }
            float[] esq = new float[1], dir = new float[1];
            bool tocando = false; int ultimoSom = Environment.TickCount, ultimaTela = Environment.TickCount - 10000, falhou = Environment.TickCount - 10000;
            bool pausaTela = false, medir = false;
            while (!parando)
            {
                try
                {
                    if (reabrir) { reabrir = false; Fechar(ref tocando); }
                    var a = aj;
                    uint r;
                    if (saida != null && tocando) { dois[0] = acordar; dois[1] = saida.Evento; r = Wasapi.WaitForMultipleObjects(2, dois, false, 200); }
                    else r = Wasapi.WaitForMultipleObjects(1, so, false, 1000);
                    if (parando) break;
                    a = aj;
                    int agora = Environment.TickCount;
                    // pausa em tela cheia (jogo, apresentação), conferida no máximo uma vez por segundo
                    if (a.PausaTelaCheia && agora - ultimaTela > 1000)
                    {
                        ultimaTela = agora; int st;
                        pausaTela = SHQueryUserNotificationState(out st) == 0 && PausaPorEstado(st);
                    }
                    else if (!a.PausaTelaCheia) pausaTela = false;
                    PausadoAgora = pausaTela;

                    int ev, nEv = 0;
                    var ps = perfis;
                    long iniciadas = mix.Iniciadas;
                    while (Tirar(out ev))
                    {
                        nEv++;
                        bool previa = ((ev >> 16) & 255) != 255;
                        if (pausaTela && !previa) continue;
                        if (saida == null && agora - falhou > 2000) { try { Abrir(); } catch (Exception e) { falhou = agora; UltimoErro = e.Message; Log("saída de áudio não abriu: " + e.Message); } }
                        if (saida == null) continue;
                        if (esq.Length < saida.Buffer) { esq = new float[saida.Buffer]; dir = new float[saida.Buffer]; }
                        mix.Evento(ev, ps, perfilAtual, saida.Fmt.Taxa, a.Espacial, a.Variacao);
                    }
                    if (saida == null)
                    {
                        mix.Limpar();
                        if (a.Ligado && agora - falhou > 5000) { try { Abrir(); esq = new float[saida.Buffer]; dir = new float[saida.Buffer]; } catch (Exception e) { falhou = agora; UltimoErro = e.Message; Log("saída de áudio não abriu: " + e.Message); } }
                        continue;
                    }
                    float mestre = Mestre(a.Volume);
                    if (!tocando)
                    {
                        if (mix.Ocupado)
                        {
                            int n = (int)saida.Livre();
                            mix.Misturar(esq, dir, n, mestre, ps, perfilAtual, saida.Fmt.Taxa, a.Espacial, a.Variacao);
                            saida.Escrever(esq, dir, n);
                            saida.Iniciar(); tocando = true; ultimoSom = agora;
                            if (mix.Iniciadas != iniciadas || medir) { MedEscrita = Stopwatch.GetTimestamp(); MedNaFrenteMs = 0; medir = false; }
                        }
                        else if (!a.Ligado && agora - ultimoSom > 5000) Fechar(ref tocando);   // desligado: devolve o dispositivo
                    }
                    else
                    {
                        int n = (int)saida.Livre();
                        if (n > 0)
                        {
                            bool ocupado = mix.Ocupado;
                            if (ocupado)
                            {
                                mix.Misturar(esq, dir, n, mestre, ps, perfilAtual, saida.Fmt.Taxa, a.Espacial, a.Variacao); saida.Escrever(esq, dir, n); ultimoSom = agora;
                                if (mix.Iniciadas != iniciadas || medir) { MedEscrita = Stopwatch.GetTimestamp(); MedNaFrenteMs = (saida.Buffer - n) * 1000.0 / saida.Fmt.Taxa; medir = false; }
                            }
                            else { mix.Quadros += n; saida.Silencio(n); }
                        }
                        else if (mix.Iniciadas != iniciadas) medir = true;   // buffer cheio: mede na próxima escrita
                        if (!mix.Ocupado && agora - ultimoSom > 1500) { saida.Parar(); tocando = false; }
                    }
                }
                catch (Exception e)
                {
                    UltimoErro = e.Message;
                    Log("áudio: " + e.Message + " (reabrindo)");
                    Fechar(ref tocando); mix.Limpar(); falhou = Environment.TickCount;
                    Thread.Sleep(200);
                }
            }
            Fechar(ref tocando);
            try { if (en != null && avisos != null) en.UnregisterEndpointNotificationCallback(avisos); } catch { }
            if (mm != IntPtr.Zero) try { Wasapi.AvRevertMmThreadCharacteristics(mm); } catch { }
        }

        public static bool PausaPorEstado(int st) { return st == 2 || st == 3 || st == 4; }   // ocupado (tela cheia), D3D em tela cheia, apresentação

        static void Abrir()
        {
            saida = Wasapi.Saida.Abrir(aj.Saida);
            SaidaInfo = saida.Nome + " · " + saida.Fmt + " · período " + (saida.Periodo * 1000.0 / saida.Fmt.Taxa).ToString("0.0", CultureInfo.InvariantCulture) + " ms" + (saida.BaixaLatencia ? " (baixa latência)" : "");
            Log("saída: " + SaidaInfo + ", buffer " + saida.Buffer + " quadros");
        }
        static void Fechar(ref bool tocando)
        {
            tocando = false;
            if (saida != null) { try { saida.Dispose(); } catch { } saida = null; }
        }

        // ---------- gancho ----------
        const int Marca = 0x4D534B4E;   // teclas mandadas pela Substituição de texto: não tocam
        const uint WM_APLICAR = 0x8001, WM_HOTKEY = 0x312, WM_TIMER = 0x113, WM_DESTROY = 0x2, WM_CLOSE = 0x10;
        static readonly bool[] baixo = new bool[256];
        static HookProc procTeclado = Teclado, procMouse = MouseProc;
        static WinEventProc procFoco = Foco;
        static WndProc procJanela = Janela;
        static int teclasNovas;
        static int ultimaRolagem;
        static int telaX, telaL = 1;
        public static int Contados;   // eventos aceitos pelo gancho (testes)

        static void LacoGancho(ManualResetEvent pronto)
        {
            try
            {
                idThreadGancho = GetCurrentThreadId();
                var wc = new WNDCLASS { lpfnWndProc = procJanela, hInstance = GetModuleHandle(null), lpszClassName = "MacSkinSomTeclado" };
                RegisterClass(ref wc);
                hwnd = CreateWindowEx(0, wc.lpszClassName, "Mac Skin Som de teclado", 0, 0, 0, 0, 0, new IntPtr(-3) /* HWND_MESSAGE */, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
                if (hwnd == IntPtr.Zero) { Log("janela não abriu: " + Marshal.GetLastWin32Error()); return; }
                try { WTSRegisterSessionNotification(hwnd, 0); } catch { }
                try { RegisterSuspendResumeNotification(hwnd, 0); } catch { }
                gFoco = SetWinEventHook(0x8005, 0x8005, IntPtr.Zero, procFoco, 0, 0, 0);   // EVENT_OBJECT_FOCUS, fora de contexto
                SetTimer(hwnd, new IntPtr(1), 60000, IntPtr.Zero);   // grava a estatística a cada minuto
                Aplicar(true);
            }
            catch (Exception e) { Log("gancho não iniciou: " + e); return; }
            finally { pronto.Set(); }
            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref msg); DispatchMessage(ref msg); }
            Desligar();
            if (gFoco != IntPtr.Zero) UnhookWinEvent(gFoco);
            gFoco = IntPtr.Zero; hwnd = IntPtr.Zero;
        }

        static string atalhoAtual = null;
        public static volatile bool AtalhoRegistrado;
        // liga/desliga os ganchos conforme os ajustes; reinstalar = tira e põe de novo (depois de suspender/bloquear)
        static void Aplicar(bool reinstalar)
        {
            var a = aj;
            if (reinstalar) Desligar();
            if (a.Ligado && gTeclado == IntPtr.Zero)
            {
                Array.Clear(baixo, 0, baixo.Length);
                gTeclado = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, procTeclado, GetModuleHandle(null), 0);
                if (gTeclado == IntPtr.Zero) Log("gancho de teclado falhou: " + Marshal.GetLastWin32Error());
            }
            else if (!a.Ligado && gTeclado != IntPtr.Zero) { UnhookWindowsHookEx(gTeclado); gTeclado = IntPtr.Zero; }
            bool querMouse = a.Ligado && (a.Mouse || a.Rolagem);
            if (querMouse && gMouse == IntPtr.Zero)
            {
                telaX = GetSystemMetrics(76); telaL = Math.Max(1, GetSystemMetrics(78));
                gMouse = SetWindowsHookEx(14 /* WH_MOUSE_LL */, procMouse, GetModuleHandle(null), 0);
                if (gMouse == IntPtr.Zero) Log("gancho de mouse falhou: " + Marshal.GetLastWin32Error());
            }
            else if (!querMouse && gMouse != IntPtr.Zero) { UnhookWindowsHookEx(gMouse); gMouse = IntPtr.Zero; }
            if (atalhoAtual != a.Atalho)
            {
                UnregisterHotKey(hwnd, 1);
                atalhoAtual = a.Atalho;
                uint m, vk;
                AtalhoRegistrado = false;
                if (LerAtalho(a.Atalho, out m, out vk))
                {
                    AtalhoRegistrado = RegisterHotKey(hwnd, 1, m | 0x4000 /* NOREPEAT */, vk);
                    if (!AtalhoRegistrado) Log("atalho " + a.Atalho + " não registrou (outro programa já usa?): " + Marshal.GetLastWin32Error());
                }
            }
        }
        static void Desligar()
        {
            if (gTeclado != IntPtr.Zero) { UnhookWindowsHookEx(gTeclado); gTeclado = IntPtr.Zero; }
            if (gMouse != IntPtr.Zero) { UnhookWindowsHookEx(gMouse); gMouse = IntPtr.Zero; }
        }

        static IntPtr Janela(IntPtr h, uint m, IntPtr w, IntPtr l)
        {
            try
            {
                switch (m)
                {
                    case WM_APLICAR: Aplicar(false); return IntPtr.Zero;
                    case WM_HOTKEY: Alternar(); return IntPtr.Zero;
                    case 0x2B1: if (w.ToInt32() == 8) { Aplicar(true); Log("sessão desbloqueada: ganchos reinstalados"); } return IntPtr.Zero;   // WTS_SESSION_UNLOCK
                    case 0x218: if (w.ToInt32() == 7 || w.ToInt32() == 0x12) { Aplicar(true); Log("voltou da suspensão: ganchos reinstalados"); } return new IntPtr(1);
                    case WM_TIMER: GravarEstatistica(); return IntPtr.Zero;
                    case WM_CLOSE: DestroyWindow(h); return IntPtr.Zero;
                    case WM_DESTROY: PostQuitMessage(0); return IntPtr.Zero;
                }
            }
            catch (Exception e) { Log("janela: " + e.Message); }
            return DefWindowProc(h, m, w, l);
        }

        // o gancho: nada de alocar ou esperar; só decide e enfileira
        static IntPtr Teclado(int n, IntPtr w, IntPtr l)
        {
            try
            {
                if (n >= 0)
                {
                    if (Marshal.ReadIntPtr(l, 16).ToInt64() != Marca)
                    {
                        uint vk = (uint)Marshal.ReadInt32(l, 0) & 0xFF, scan = (uint)Marshal.ReadInt32(l, 4), flags = (uint)Marshal.ReadInt32(l, 8);
                        Tecla(w.ToInt32(), vk, scan, flags);
                    }
                }
            }
            catch { }
            return CallNextHookEx(IntPtr.Zero, n, w, l);
        }

        // devolve se tocou (para os testes)
        public static bool Tecla(int msg, uint vk, uint scan, uint flags)
        {
            bool desce = msg == 0x100 || msg == 0x104, sobe = msg == 0x101 || msg == 0x105;
            if (!desce && !sobe) return false;
            vk &= 0xFF;
            if (desce) { if (baixo[vk]) return false; baixo[vk] = true; }   // segurar a tecla: a repetição automática não toca
            else { if (!baixo[vk]) return false; baixo[vk] = false; }
            var a = aj;
            if (!a.Ligado) return false;
            if (a.PausaSenha && EmSenha()) return false;
            int g = Grupo(vk);
            if (g == GMod && !a.Modificadores) return false;
            if (desce) teclasNovas++;
            Contados++;
            Enfileirar(Codigo(g, desce, Coluna(scan, (flags & 1) != 0), -1, 0));
            return true;
        }

        static IntPtr MouseProc(int n, IntPtr w, IntPtr l)
        {
            try
            {
                if (n >= 0)
                {
                    int m = w.ToInt32(); var a = aj;
                    int x = Marshal.ReadInt32(l, 0);
                    float pos = (x - telaX) / (float)telaL;
                    if (a.Mouse && (m == 0x201 || m == 0x204)) Enfileirar(Codigo(GMouse, true, pos, -1, 0));
                    else if (a.Mouse && (m == 0x202 || m == 0x205)) Enfileirar(Codigo(GMouse, false, pos, -1, 0));
                    else if (a.Rolagem && m == 0x20A)
                    {
                        int t = Environment.TickCount;
                        if (t - ultimaRolagem >= 30) { ultimaRolagem = t; Enfileirar(Codigo(GRolagem, true, pos, -1, 0)); }   // touchpad manda dezenas por segundo
                    }
                }
            }
            catch { }
            return CallNextHookEx(IntPtr.Zero, n, w, l);
        }

        // ---------- campo de senha ----------
        static volatile bool senha, focoPendente;
        static int focoDesde, focoGeracao;
        static IntPtr focoH; static int focoObj, focoFilho;
        static readonly object travaFoco = new object();
        public static bool EmSenha()
        {
            if (focoPendente && Environment.TickCount - focoDesde < 300) return true;   // foco acabou de mudar: na dúvida, calado
            return senha;
        }
        static void Foco(IntPtr gancho, uint ev, IntPtr h, int obj, int filho, uint thread, uint hora)
        {
            var a = aj;
            if (!a.Ligado || !a.PausaSenha) return;   // desligado: não pergunta nada a ninguém
            lock (travaFoco) { focoH = h; focoObj = obj; focoFilho = filho; focoGeracao++; }
            focoDesde = Environment.TickCount; focoPendente = true;
            Wasapi.SetEvent(acordarFoco);
        }
        static void LacoFoco()
        {
            var so = new IntPtr[] { acordarFoco };
            while (!parando)
            {
                Wasapi.WaitForMultipleObjects(1, so, false, 5000u);
                if (parando) break;
                IntPtr h; int obj, filho, ger;
                lock (travaFoco) { h = focoH; obj = focoObj; filho = focoFilho; ger = focoGeracao; }
                if (!focoPendente) continue;
                bool r = false;
                try { r = CampoDeSenha(h, obj, filho); } catch (Exception e) { if (DepurarFoco) Log("foco: " + e.Message); }
                if (DepurarFoco) Log("foco: " + Win32.Classe(h) + " obj " + obj + " filho " + filho + " → senha " + r + " (" + UltimoEstado + ")");
                lock (travaFoco) { if (ger == focoGeracao) { senha = r; focoPendente = false; } }
                // janela do Chromium recém-focada: a acessibilidade da página liga com atraso e o campo que já abre com foco
                // (login com a senha em foco) não gera aviso. Confere de novo o elemento com foco em 0,4 s e 1,2 s.
                if (!r && filho == 0 && h != IntPtr.Zero && Win32.Classe(h).StartsWith("Chrome_"))
                    foreach (uint espera in new uint[] { 400, 800 })
                    {
                        if (Wasapi.WaitForMultipleObjects(1, so, false, espera) == 0) { Wasapi.SetEvent(acordarFoco); break; }   // foco mudou: trata o novo
                        bool r2 = false;
                        try { r2 = FocoProtegido(h); } catch { }
                        if (DepurarFoco) Log("foco (nova consulta): senha " + r2 + " (" + UltimoEstado + ")");
                        lock (travaFoco) { if (ger == focoGeracao) senha = r2; else break; }
                        if (r2) break;
                    }
            }
        }
        public static volatile bool DepurarFoco; static string UltimoEstado = "";
        public static bool CampoDeSenha(IntPtr h, int obj, int filho)
        {
            UltimoEstado = "";
            if (h != IntPtr.Zero)
            {
                var c = Win32.Classe(h);
                if (c.IndexOf("edit", StringComparison.OrdinalIgnoreCase) >= 0 && (GetWindowLong(h, -16) & 0x20) != 0) return true;   // ES_PASSWORD
            }
            // Chromium (Chrome, Edge, apps Electron): a página fica numa janela filha e só liga a acessibilidade do conteúdo
            // quando alguém a consulta; aí passa a avisar o foco de cada campo. Pergunta direto qual elemento tem o foco.
            if (h != IntPtr.Zero && Win32.Classe(h).StartsWith("Chrome_"))
            {
                // só em navegador: em app Electron (VS Code, Teams...) a consulta liga o modo de leitor de tela do app
                if (!Navegador(h)) return false;
                return FocoProtegido(h);
            }
            object filhoObj; Accessibility.IAccessible acc;
            if (AccessibleObjectFromEvent(h, obj, filho, out acc, out filhoObj) != 0 || acc == null) return false;
            try
            {
                object st = acc.get_accState(filhoObj ?? 0);
                if (DepurarFoco) { string role = ""; try { role = Convert.ToString(acc.get_accRole(filhoObj ?? 0)); } catch { } UltimoEstado = "estado 0x" + (st is int ? ((int)st).ToString("X") : "?") + " papel " + role; }
                return st is int && (((int)st) & 0x20000000) != 0;   // STATE_SYSTEM_PROTECTED
            }
            finally { try { Marshal.ReleaseComObject(acc); } catch { } }
        }

        static readonly Dictionary<uint, bool> navegadores = new Dictionary<uint, bool>();
        static readonly HashSet<string> NomesNavegador = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "msedge", "chrome", "brave", "opera", "opera_gx", "vivaldi", "chromium", "arc", "thorium", "yandex" };
        public static bool Navegador(IntPtr h)
        {
            uint pid = Win32.Pid(h); bool r;
            lock (navegadores)
            {
                if (navegadores.TryGetValue(pid, out r)) return r;
                try { r = NomesNavegador.Contains(Process.GetProcessById((int)pid).ProcessName); } catch { r = false; }
                if (navegadores.Count > 200) navegadores.Clear();
                navegadores[pid] = r;
            }
            return r;
        }

        // ligou o som: confere o campo que já está em foco
        static void ReverificarFoco()
        {
            IntPtr f = GetForegroundWindow(); if (f == IntPtr.Zero) return;
            Foco(IntPtr.Zero, 0x8005, f, -4, 0, 0, 0);
        }

        // Chromium: só o UI Automation enxerga os campos da página (a acessibilidade MSAA básica mostra o documento inteiro
        // como focado). Pergunta ao UIA qual elemento tem o foco e se é de senha.
        static bool FocoProtegido(IntPtr janela)
        {
            var e = System.Windows.Automation.AutomationElement.FocusedElement;
            if (e == null) return false;
            bool r = e.Current.IsPassword;
            if (DepurarFoco) UltimoEstado = "UIA: " + e.Current.ControlType.ProgrammaticName + " senha " + r;
            return r;
        }

        // ---------- estatística ----------
        static readonly Dictionary<string, int> porDia = new Dictionary<string, int>();
        public static int TeclasHoje
        {
            get { int n; lock (porDia) porDia.TryGetValue(DateTime.Today.ToString("yyyy-MM-dd"), out n); return n + teclasNovas; }
        }
        static void LerEstatistica()
        {
            try
            {
                if (!File.Exists(ArquivoEstatistica)) return;
                lock (porDia)
                    foreach (var l in File.ReadAllLines(ArquivoEstatistica))
                    {
                        var p = l.Split('='); int n;
                        if (p.Length == 2 && int.TryParse(p[1], out n)) porDia[p[0].Trim()] = n;
                    }
            }
            catch (Exception e) { Log("estatística: " + e.Message); }
        }
        static void GravarEstatistica()
        {
            int novas = Interlocked.Exchange(ref teclasNovas, 0);
            if (novas == 0) return;
            try
            {
                var linhas = new List<string>();
                lock (porDia)
                {
                    string hoje = DateTime.Today.ToString("yyyy-MM-dd"); int n;
                    porDia.TryGetValue(hoje, out n); porDia[hoje] = n + novas;
                    var chaves = new List<string>(porDia.Keys); chaves.Sort();
                    if (chaves.Count > 400) foreach (var k in chaves.GetRange(0, chaves.Count - 400)) porDia.Remove(k);
                    foreach (var k in porDia.Keys) linhas.Add(k + "=" + porDia[k]);
                }
                linhas.Sort();
                Directory.CreateDirectory(Path.GetDirectoryName(ArquivoEstatistica));
                File.WriteAllLines(ArquivoEstatistica, linhas);
            }
            catch (Exception e) { Log("gravar estatística: " + e.Message); }
        }

        // ---------- Win32 ----------
        delegate IntPtr HookProc(int n, IntPtr w, IntPtr l);
        delegate IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l);
        delegate void WinEventProc(IntPtr gancho, uint ev, IntPtr h, int obj, int filho, uint thread, uint hora);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASS { public uint style; public WndProc lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName; }
        [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClass(ref WNDCLASS c);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateWindowEx(int ex, string classe, string nome, int estilo, int x, int y, int w, int h, IntPtr pai, IntPtr menu, IntPtr inst, IntPtr p);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern int GetMessage(out MSG m, IntPtr h, uint de, uint ate);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG m);
        [DllImport("user32.dll")] static extern void PostQuitMessage(int c);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SetTimer(IntPtr h, IntPtr id, uint ms, IntPtr f);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc f, IntPtr mod, uint tid);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint de, uint ate, IntPtr mod, WinEventProc p, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr h);
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern IntPtr RegisterSuspendResumeNotification(IntPtr h, int flags);
        [DllImport("wtsapi32.dll")] static extern bool WTSRegisterSessionNotification(IntPtr h, int flags);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string n);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int estado);
        [DllImport("oleacc.dll")] static extern int AccessibleObjectFromWindow(IntPtr h, int obj, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object o);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("oleacc.dll")] static extern int AccessibleObjectFromEvent(IntPtr h, int obj, int filho, [MarshalAs(UnmanagedType.Interface)] out Accessibility.IAccessible acc, [MarshalAs(UnmanagedType.Struct)] out object filhoObj);
    }

    // ---------- leitura de WAV (PCM 8/16/24/32 bits ou float 32), mistura para mono e reamostra ----------
    static class Wav
    {
        public static float[] LerMono(string arq, int taxaDestino)
        {
            var b = File.ReadAllBytes(arq);
            if (b.Length < 44 || Encoding.ASCII.GetString(b, 0, 4) != "RIFF" || Encoding.ASCII.GetString(b, 8, 4) != "WAVE") throw new InvalidDataException("não é WAV");
            int pos = 12, tag = 0, canais = 0, taxa = 0, bits = 0, ini = -1, tam = 0;
            while (pos + 8 <= b.Length)
            {
                string id = Encoding.ASCII.GetString(b, pos, 4); int t = BitConverter.ToInt32(b, pos + 4);
                if (t < 0) break;
                if (id == "fmt ")
                {
                    tag = BitConverter.ToUInt16(b, pos + 8); canais = BitConverter.ToUInt16(b, pos + 10); taxa = BitConverter.ToInt32(b, pos + 12); bits = BitConverter.ToUInt16(b, pos + 22);
                    if (tag == 0xFFFE && t >= 40) tag = BitConverter.ToUInt16(b, pos + 32);   // subformato do WAVE_FORMAT_EXTENSIBLE
                }
                else if (id == "data") { ini = pos + 8; tam = Math.Min(t, b.Length - ini); break; }
                pos += 8 + t + (t & 1);
            }
            if (ini < 0 || canais < 1 || taxa < 8000) throw new InvalidDataException("WAV sem dados");
            if (!(tag == 1 && (bits == 8 || bits == 16 || bits == 24 || bits == 32)) && !(tag == 3 && bits == 32)) throw new InvalidDataException("formato " + tag + "/" + bits + " não suportado");
            int bps = bits / 8, quadros = tam / (bps * canais);
            if (quadros > taxa * 3) quadros = taxa * 3;   // um clique não passa de 3 s
            var mono = new float[quadros];
            for (int i = 0; i < quadros; i++)
            {
                float s = 0;
                for (int c = 0; c < canais; c++)
                {
                    int o = ini + (i * canais + c) * bps;
                    float v;
                    if (tag == 3) v = BitConverter.ToSingle(b, o);
                    else if (bits == 8) v = (b[o] - 128) / 128f;
                    else if (bits == 16) v = BitConverter.ToInt16(b, o) / 32768f;
                    else if (bits == 24) v = ((b[o] | (b[o + 1] << 8) | ((sbyte)b[o + 2] << 16))) / 8388608f;
                    else v = BitConverter.ToInt32(b, o) / 2147483648f;
                    s += v;
                }
                mono[i] = s / canais;
            }
            return Reamostrar(mono, taxa, taxaDestino);
        }

        public static float[] Reamostrar(float[] s, int de, int para)
        {
            if (de == para || s.Length < 2) return s;
            int n = (int)((long)s.Length * para / de);
            var r = new float[Math.Max(2, n)];
            double passo = (double)de / para;
            for (int i = 0; i < r.Length; i++)
            {
                double p = i * passo; int j = (int)p; if (j >= s.Length - 1) { r[i] = s[s.Length - 1]; continue; }
                float f = (float)(p - j); r[i] = s[j] + (s[j + 1] - s[j]) * f;
            }
            return r;
        }

        public static void Gravar16(string arq, float[] s, int taxa, int canais = 1)
        {
            using (var w = new BinaryWriter(File.Create(arq)))
            {
                int dados = s.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dados); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16); w.Write((short)1); w.Write((short)canais); w.Write(taxa); w.Write(taxa * 2 * canais); w.Write((short)(2 * canais)); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dados);
                foreach (var v in s) w.Write((short)Math.Max(-32767, Math.Min(32767, v * 32767)));
            }
        }
    }

    // ---------- síntese dos sons próprios ----------
    // Um toque é a soma de "golpes" (a tecla batendo no fundo, voltando ao topo, o clique da mola, o estabilizador do espaço).
    // Cada golpe: modos ressonantes (senoides amortecidas: o corpo e a placa), um ruído filtrado em banda (o estalo) e um baque grave.
    static class Sintese
    {
        public const int Taxa = 48000;
        public struct Modo { public float F, A, Tau; public Modo(float f, float a, float tau) { F = f; A = a; Tau = tau; } }
        public class Golpe { public float Ms, Ganho = 1; public Modo[] Modos = new Modo[0]; public float RuidoA, RuidoF = 2000, RuidoQ = 1, RuidoTau = 1.5f, BaqueF = 0, BaqueA = 0, BaqueTau = 6; }
        public class Som { public Golpe[] Golpes; public float Ganho = 1, Brilho = 9000, DurMs = 90; }

        class Rng
        {
            uint s; public Rng(uint semente) { s = semente == 0 ? 1 : semente; }
            public float F() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s / 4294967296f) * 2 - 1; }
        }

        public static float[] Gerar(Som som, uint semente, float jitter)
        {
            var r = new Rng(semente);
            int n = (int)(som.DurMs * Taxa / 1000);
            var buf = new float[n];
            foreach (var g in som.Golpes)
            {
                int ini = (int)(Math.Max(0, g.Ms * (1 + 0.12f * jitter * r.F())) * Taxa / 1000);
                float gg = g.Ganho * (1 + 0.15f * jitter * r.F());
                foreach (var m in g.Modos)
                {
                    double f = m.F * (1 + 0.04 * jitter * r.F()), a = m.A * gg * (1 + 0.15 * jitter * r.F()), tau = m.Tau * (1 + 0.12 * jitter * r.F()) / 1000.0;
                    double fase = (r.F() + 1) * Math.PI, w = 2 * Math.PI * f / Taxa, dec = Math.Exp(-1.0 / (tau * Taxa));
                    double e = a;
                    for (int i = ini; i < n && e > 1e-5; i++)
                    {
                        double rampa = i - ini < 8 ? (i - ini) / 8.0 : 1;   // 0,17 ms de subida: estala sem chiar
                        buf[i] += (float)(e * rampa * Math.Sin(w * (i - ini) + fase));
                        e *= dec;
                    }
                }
                if (g.RuidoA > 0)
                {
                    // passa-banda (RBJ) sobre ruído branco, com envoltória exponencial
                    double w0 = 2 * Math.PI * g.RuidoF * (1 + 0.08 * jitter * r.F()) / Taxa, al = Math.Sin(w0) / (2 * g.RuidoQ);
                    double b0 = al, b2 = -al, a0 = 1 + al, a1 = -2 * Math.Cos(w0), a2 = 1 - al;
                    b0 /= a0; b2 /= a0; a1 /= a0; a2 /= a0;
                    double x1 = 0, x2 = 0, y1 = 0, y2 = 0, e = g.RuidoA * gg * 3, dec = Math.Exp(-1.0 / (g.RuidoTau / 1000.0 * Taxa));
                    for (int i = ini; i < n && e > 1e-5; i++)
                    {
                        double x = r.F(), y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
                        x2 = x1; x1 = x; y2 = y1; y1 = y;
                        buf[i] += (float)(y * e); e *= dec;
                    }
                }
                if (g.BaqueA > 0)
                {
                    double w = 2 * Math.PI * g.BaqueF / Taxa, e = g.BaqueA * gg, dec = Math.Exp(-1.0 / (g.BaqueTau / 1000.0 * Taxa));
                    for (int i = ini; i < n && e > 1e-5; i++) { buf[i] += (float)(e * Math.Sin(w * (i - ini))); e *= dec; }
                }
            }
            // passa-baixa (tira o brilho dos perfis abafados) e passa-alta em 40 Hz (sem deslocamento DC)
            double c = Math.Exp(-2 * Math.PI * som.Brilho / Taxa), lp = 0, hp = 0, xa = 0, ch = Math.Exp(-2 * Math.PI * 40.0 / Taxa);
            for (int i = 0; i < n; i++)
            {
                lp = (1 - c) * buf[i] + c * lp;
                hp = ch * (hp + lp - xa); xa = lp;
                buf[i] = (float)hp;
            }
            int fade = Math.Min(n, Taxa * 4 / 1000);
            for (int i = 0; i < fade; i++) buf[n - 1 - i] *= i / (float)fade;
            float pico = 0; foreach (var v in buf) pico = Math.Max(pico, Math.Abs(v));
            if (pico > 0) { float k = 0.85f * som.Ganho / pico; for (int i = 0; i < n; i++) buf[i] *= k; }
            return buf;
        }

        // 6 variações por grupo e sentido
        static SomTeclado.Perfil Montar(string id, string nome, uint semente, Func<int, bool, Som> receita, EdicaoSom ed)
        {
            var p = new SomTeclado.Perfil { Id = id, Nome = nome, Base = id, Taxa = Taxa, Origem = "sintetizado pelo Mac Skin", Licenca = "própria" };
            for (int g = 0; g < SomTeclado.NGrupos; g++)
                for (int d = 0; d < 2; d++)
                {
                    var som = receita(g, d == 0);
                    if (som != null && ed != null) som = ed.Aplicar(som, g, d == 0);
                    if (som == null) continue;
                    var vars = new float[6][];
                    for (int v = 0; v < vars.Length; v++) vars[v] = Gerar(som, semente + (uint)(g * 7919 + d * 104729 + v * 15485863), v == 0 ? 0.3f : 1f);
                    p.Amostras[g * 2 + d] = vars;
                }
            return p;
        }

        static Modo M(float f, float a, float tau) { return new Modo(f, a, tau); }

        static Som Mouse(bool desce, float brilho)
        {
            return new Som { DurMs = 22, Ganho = desce ? 0.5f : 0.32f, Brilho = brilho, Golpes = new[] { new Golpe { Modos = new[] { M(desce ? 2900 : 3400, 0.5f, 2), M(5400, 0.3f, 1) }, RuidoA = 0.5f, RuidoF = 6000, RuidoQ = 1.5f, RuidoTau = 0.7f } } };
        }
        static Som Rolagem(float brilho)
        {
            return new Som { DurMs = 12, Ganho = 0.22f, Brilho = brilho, Golpes = new[] { new Golpe { Modos = new[] { M(3800, 0.4f, 0.8f) }, RuidoA = 0.4f, RuidoF = 5000, RuidoQ = 2, RuidoTau = 0.4f } } };
        }

        // Grafite: linear, lubrificado, grave e encorpado ("thock")
        public static SomTeclado.Perfil PorId(string id, EdicaoSom ed)
        {
            switch (id) { case "grafite": return Grafite(ed); case "cristal": return Cristal(ed); case "seda": return Seda(ed); }
            return null;
        }

        public static SomTeclado.Perfil Grafite(EdicaoSom ed = null)
        {
            return Montar("grafite", "Grafite", 1234567, (g, desce) =>
            {
                if (g == SomTeclado.GMouse) return Mouse(desce, 9000);
                if (g == SomTeclado.GRolagem) return desce ? Rolagem(9000) : null;
                float tom = g == SomTeclado.GApagar ? 0.93f : g == SomTeclado.GMod ? 0.9f : 1f;
                if (!desce)
                    return new Som { DurMs = 60, Ganho = 0.42f, Brilho = 6500, Golpes = new[] { new Golpe { Modos = new[] { M(540 * tom, 0.6f, 7), M(1350 * tom, 0.3f, 4), M(2900, 0.1f, 2) }, RuidoA = 0.35f, RuidoF = 2800, RuidoQ = 0.9f, RuidoTau = 1.2f } } };
                if (g == SomTeclado.GEspaco)
                    return new Som { DurMs = 130, Ganho = 0.95f, Brilho = 5500, Golpes = new[] {
                        new Golpe { Modos = new[] { M(140, 1, 32), M(290, 0.65f, 22), M(760, 0.3f, 10), M(1600, 0.12f, 5) }, RuidoA = 0.55f, RuidoF = 1300, RuidoQ = 0.8f, RuidoTau = 2.2f, BaqueF = 80, BaqueA = 0.6f, BaqueTau = 12 },
                        new Golpe { Ms = 4, Ganho = 0.25f, Modos = new[] { M(3100, 0.4f, 3) }, RuidoA = 0.4f, RuidoF = 4200, RuidoQ = 1.5f, RuidoTau = 0.8f },
                        new Golpe { Ms = 11, Ganho = 0.15f, Modos = new[] { M(3300, 0.4f, 2.5f) }, RuidoA = 0.4f, RuidoF = 4000, RuidoQ = 1.5f, RuidoTau = 0.7f } } };
                if (g == SomTeclado.GEnter)
                    return new Som { DurMs = 110, Ganho = 1f, Brilho = 6000, Golpes = new[] {
                        new Golpe { Modos = new[] { M(180, 1, 25), M(360, 0.6f, 16), M(950, 0.3f, 8), M(2200, 0.12f, 3) }, RuidoA = 0.6f, RuidoF = 1600, RuidoQ = 0.8f, RuidoTau = 2f, BaqueF = 90, BaqueA = 0.5f, BaqueTau = 10 },
                        new Golpe { Ms = 6, Ganho = 0.15f, Modos = new[] { M(2900, 0.4f, 2.5f) }, RuidoA = 0.3f, RuidoF = 3800, RuidoQ = 1.5f, RuidoTau = 0.7f } } };
                return new Som { DurMs = 85, Ganho = g == SomTeclado.GMod ? 0.7f : 0.85f, Brilho = 6000, Golpes = new[] { new Golpe { Modos = new[] { M(225 * tom, 1, 17), M(420 * tom, 0.55f, 11), M(1150 * tom, 0.25f, 6), M(2600 * tom, 0.1f, 2.5f) }, RuidoA = 0.55f, RuidoF = 1800, RuidoQ = 0.8f, RuidoTau = 1.8f, BaqueF = 95, BaqueA = 0.45f, BaqueTau = 8 } } };
            }, ed);
        }

        // Cristal: com clique (mola que estala ao descer e ao subir), agudo e brilhante
        public static SomTeclado.Perfil Cristal(EdicaoSom ed = null)
        {
            return Montar("cristal", "Cristal", 7654321, (g, desce) =>
            {
                if (g == SomTeclado.GMouse) return Mouse(desce, 12000);
                if (g == SomTeclado.GRolagem) return desce ? Rolagem(12000) : null;
                float tom = g == SomTeclado.GApagar ? 0.95f : g == SomTeclado.GMod ? 0.92f : 1f;
                var clique = new Golpe { Modos = new[] { M(5200 * tom, 0.5f, 1.4f), M(3100 * tom, 0.45f, 2) }, RuidoA = 1f, RuidoF = 4600 * tom, RuidoQ = 2f, RuidoTau = 0.8f };
                if (!desce)
                    return new Som { DurMs = 55, Ganho = 0.6f, Brilho = 12000, Golpes = new[] {
                        new Golpe { Ganho = 0.8f, Modos = new[] { M(4800 * tom, 0.5f, 1.2f), M(2800 * tom, 0.4f, 1.8f) }, RuidoA = 0.9f, RuidoF = 4200, RuidoQ = 2, RuidoTau = 0.7f },
                        new Golpe { Ms = 3, Ganho = 0.35f, Modos = new[] { M(650 * tom, 0.5f, 5), M(1700, 0.25f, 3) }, RuidoA = 0.2f, RuidoF = 3000, RuidoTau = 1 } } };
                float corpo = g == SomTeclado.GEspaco ? 0.6f : g == SomTeclado.GEnter ? 0.8f : 1f;
                var fundo = new Golpe { Ms = 6, Ganho = 0.6f, Modos = new[] { M(380 * corpo, 0.6f, g == SomTeclado.GEspaco ? 18 : 10), M(900 * corpo, 0.35f, 6), M(2200, 0.2f, 3) }, RuidoA = 0.4f, RuidoF = 2500, RuidoQ = 1, RuidoTau = 1.4f, BaqueF = 110 * corpo, BaqueA = 0.25f, BaqueTau = 6 };
                return new Som { DurMs = g == SomTeclado.GEspaco ? 110 : 80, Ganho = g == SomTeclado.GMod ? 0.7f : 0.9f, Brilho = 12000, Golpes = new[] { clique, fundo } };
            }, ed);
        }

        // Seda: tátil e macio, discreto (bom para o escritório)
        public static SomTeclado.Perfil Seda(EdicaoSom ed = null)
        {
            return Montar("seda", "Seda", 2468013, (g, desce) =>
            {
                if (g == SomTeclado.GMouse) return Mouse(desce, 6000);
                if (g == SomTeclado.GRolagem) return desce ? Rolagem(6000) : null;
                float tom = g == SomTeclado.GApagar ? 0.94f : g == SomTeclado.GMod ? 0.9f : 1f;
                if (!desce)
                    return new Som { DurMs = 50, Ganho = 0.35f, Brilho = 4500, Golpes = new[] { new Golpe { Modos = new[] { M(700 * tom, 0.45f, 5), M(1900 * tom, 0.2f, 2.5f) }, RuidoA = 0.3f, RuidoF = 2000, RuidoQ = 0.8f, RuidoTau = 1 } } };
                float corpo = g == SomTeclado.GEspaco ? 0.62f : g == SomTeclado.GEnter ? 0.8f : 1f;
                return new Som { DurMs = g == SomTeclado.GEspaco ? 110 : 75, Ganho = g == SomTeclado.GMod ? 0.6f : g == SomTeclado.GEspaco ? 0.85f : 0.75f, Brilho = 4500, Golpes = new[] {
                    new Golpe { Ganho = 0.3f, RuidoA = 0.5f, RuidoF = 1500, RuidoQ = 0.9f, RuidoTau = 1 },   // o "degrau" tátil
                    new Golpe { Ms = 7, Modos = new[] { M(300 * corpo * tom, 0.8f, 14), M(650 * corpo * tom, 0.45f, 9), M(1700 * tom, 0.2f, 4) }, RuidoA = 0.45f, RuidoF = 1400, RuidoQ = 0.7f, RuidoTau = 1.6f, BaqueF = 100 * corpo, BaqueA = 0.3f, BaqueTau = 7 } } };
            }, ed);
        }
    }
    // ---------- ajustes do editor de sons ----------
    // Um arquivo por perfil em %APPDATA%\Mac Skin Dados\sons-editados\<id>.txt (chave=valor). Valores de 0 a 100:
    // timbre com 50 = original; soltar em %; volume de cada tipo de tecla com 67 = 100% (vai até 150%).
    class EdicaoSom
    {
        public const int Neutro = 50, GrupoNeutro = 67;
        public string Id, Base, Nome;
        public int Tom = Neutro, Corpo = Neutro, Clique = Neutro, Brilho = Neutro, Duracao = Neutro, Soltar = 100;
        public int[] Grupos = { GrupoNeutro, GrupoNeutro, GrupoNeutro, GrupoNeutro, GrupoNeutro, GrupoNeutro, GrupoNeutro };

        public static string Pasta { get { return Path.Combine(Substituicao.PastaDados, "sons-editados"); } }
        static string Arq(string id) { return Path.Combine(Pasta, id + ".txt"); }

        public EdicaoSom Copia()
        {
            var c = (EdicaoSom)MemberwiseClone(); c.Grupos = (int[])Grupos.Clone(); return c;
        }
        public bool Neutra
        {
            get
            {
                if (Tom != Neutro || Corpo != Neutro || Clique != Neutro || Brilho != Neutro || Duracao != Neutro || Soltar != 100) return false;
                foreach (var g in Grupos) if (g != GrupoNeutro) return false;
                return true;
            }
        }
        public void Restaurar()
        {
            Tom = Corpo = Clique = Brilho = Duracao = Neutro; Soltar = 100;
            for (int i = 0; i < Grupos.Length; i++) Grupos[i] = GrupoNeutro;
        }

        public static int PorCento(int grupo) { return (int)Math.Round(grupo * 1.5, MidpointRounding.ToEven); }
        public float GanhoGrupo(int g) { return g >= 0 && g < Grupos.Length ? Grupos[g] * 1.5f / 100f : 1; }
        public float FatorSoltar { get { return Soltar / 100f; } }
        static float Exp(float b, int v) { return (float)Math.Pow(b, (v - Neutro) / 50.0); }

        // receita de um som com os ajustes: tom move todas as frequências; corpo pesa o grave (até ~700 Hz) e clique o agudo
        // (de ~3 kHz para cima), com transição suave entre os dois; brilho abre o passa-baixa; duração estica as caudas
        public Sintese.Som Aplicar(Sintese.Som s, int grupo, bool desce)
        {
            float ganho = s.Ganho * GanhoGrupo(grupo) * (desce ? 1 : FatorSoltar);
            if (ganho <= 0.001f) return null;
            float tom = (float)Math.Pow(2, (Tom - Neutro) / 50.0 * 0.8), corpo = Exp(4, Corpo), clique = Exp(4, Clique), bri = Exp(3, Brilho), dur = Exp(2.5f, Duracao);
            Func<float, float> peso = f =>
            {
                double t = (Math.Log(Math.Max(20, f)) - Math.Log(700)) / (Math.Log(3000) - Math.Log(700));
                t = Math.Max(0, Math.Min(1, t));
                return (float)(Math.Pow(corpo, 1 - t) * Math.Pow(clique, t));
            };
            var n = new Sintese.Som { Ganho = Math.Min(1.15f, ganho), Brilho = Math.Min(20000, s.Brilho * bri), DurMs = Math.Min(300, s.DurMs * Math.Max(1, dur)) };
            n.Golpes = new Sintese.Golpe[s.Golpes.Length];
            for (int i = 0; i < s.Golpes.Length; i++)
            {
                var g = s.Golpes[i];
                var m = new Sintese.Modo[g.Modos.Length];
                for (int k = 0; k < m.Length; k++) m[k] = new Sintese.Modo(Math.Min(18000, g.Modos[k].F * tom), g.Modos[k].A * peso(g.Modos[k].F), g.Modos[k].Tau * dur);
                n.Golpes[i] = new Sintese.Golpe
                {
                    Ms = g.Ms, Ganho = g.Ganho, Modos = m,
                    RuidoA = g.RuidoA * peso(g.RuidoF), RuidoF = Math.Min(18000, g.RuidoF * tom), RuidoQ = g.RuidoQ, RuidoTau = g.RuidoTau * dur,
                    BaqueF = g.BaqueF * tom, BaqueA = g.BaqueA * corpo, BaqueTau = g.BaqueTau * dur
                };
            }
            return n;
        }

        // ---------- arquivo ----------
        static readonly string[] Chaves = { "tom", "corpo", "clique", "brilho", "duracao", "soltar" };
        public void Gravar()
        {
            Directory.CreateDirectory(Pasta);
            var sb = new StringBuilder();
            sb.Append("# Som de teclado do Mac Skin: ajustes do perfil (gerado pela janela Som de Teclado)\r\n");
            sb.Append("base=").Append(Base).Append("\r\n");
            sb.Append("nome=").Append((Nome ?? "").Replace("\r", " ").Replace("\n", " ")).Append("\r\n");
            int[] vs = { Tom, Corpo, Clique, Brilho, Duracao, Soltar };
            for (int i = 0; i < Chaves.Length; i++) sb.Append(Chaves[i]).Append('=').Append(vs[i]).Append("\r\n");
            for (int g = 0; g < Grupos.Length; g++) sb.Append("volume-").Append(SomTeclado.NomesGrupo[g]).Append('=').Append(Grupos[g]).Append("\r\n");
            string arq = Arq(Id), tmp = arq + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(arq)) File.Replace(tmp, arq, null); else File.Move(tmp, arq);
        }

        public static EdicaoSom Ler(string arq)
        {
            var e = new EdicaoSom { Id = Path.GetFileNameWithoutExtension(arq).ToLowerInvariant() };
            e.Base = e.Id;
            foreach (var l in File.ReadAllLines(arq, Encoding.UTF8))
            {
                if (l.StartsWith("#")) continue;
                int i = l.IndexOf('='); if (i < 0) continue;
                string k = l.Substring(0, i).Trim().ToLowerInvariant(), v = l.Substring(i + 1).Trim();
                int n; bool num = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
                n = Math.Max(0, Math.Min(100, n));
                if (k == "base" && v.Length > 0) e.Base = v.ToLowerInvariant();
                else if (k == "nome") e.Nome = v;
                else if (!num) continue;
                else if (k == "tom") e.Tom = n; else if (k == "corpo") e.Corpo = n; else if (k == "clique") e.Clique = n;
                else if (k == "brilho") e.Brilho = n; else if (k == "duracao") e.Duracao = n; else if (k == "soltar") e.Soltar = n;
                else if (k.StartsWith("volume-"))
                {
                    int g = Array.IndexOf(SomTeclado.NomesGrupo, k.Substring(7));
                    if (g >= 0) e.Grupos[g] = n;
                }
            }
            return e;
        }

        public static List<EdicaoSom> LerTodas()
        {
            var r = new List<EdicaoSom>();
            try
            {
                if (!Directory.Exists(Pasta)) return r;
                var arqs = Directory.GetFiles(Pasta, "*.txt");
                Array.Sort(arqs, StringComparer.OrdinalIgnoreCase);
                foreach (var a in arqs)
                    try { r.Add(Ler(a)); } catch (Exception e) { Comum.Log("som-teclado.log", "edição " + a + ": " + e.Message); }
            }
            catch (Exception e) { Comum.Log("som-teclado.log", "sons-editados: " + e.Message); }
            return r;
        }

        public static void Apagar(string id) { string a = Arq(id); if (File.Exists(a)) File.Delete(a); }

        // id novo para um perfil criado no editor: meu-som-1, meu-som-2 ...
        public static string NovoId(IEnumerable<string> existentes)
        {
            var usados = new HashSet<string>(existentes);
            for (int i = 1; ; i++) { string id = "meu-som-" + i; if (!usados.Contains(id) && !File.Exists(Arq(id))) return id; }
        }
    }
}
