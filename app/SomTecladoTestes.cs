// Mac Skin: bateria de testes do Som de teclado.  MacSkin.exe --testar-som-teclado [relatorio.txt]
// Parte 1 (unidade): ajustes, atalho, mapa das teclas, síntese, WAV, pacotes, mistura, fila, gancho, desempenho.
// Parte 2 (de ponta a ponta, no áudio real): abre a saída, manda teclas F24 (não digitam nada em programa nenhum) pelo
// SendInput e escuta o que sai do Windows por captura "loopback", medindo a latência pelo relógio do motor de áudio.
// Usa uma pasta de dados temporária (MACSKIN_DADOS): os ajustes do usuário não são tocados.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MacSkin
{
    static class SomTecladoTestes
    {
        static readonly List<string> linhas = new List<string>();
        static bool args_depurar;
        static int ok, falhas, pulados; static bool soSenha;

        static void Caso(string nome, Func<string> f)
        {
            string r;
            var sw = Stopwatch.StartNew();
            try { r = f(); } catch (Exception e) { r = "exceção: " + e.GetType().Name + ": " + e.Message; }
            string ms = " (" + sw.ElapsedMilliseconds + " ms)";
            if (r == null || r.StartsWith("ok:")) { ok++; linhas.Add("OK      " + nome + (r != null ? " — " + r.Substring(3).Trim() : "") + ms); }
            else if (r.StartsWith("pulado:")) { pulados++; linhas.Add("PULADO  " + nome + " — " + r.Substring(7).Trim()); }
            else { falhas++; linhas.Add("FALHOU  " + nome + " — " + r + ms); }
        }
        static string Se(bool cond, string falha, string info = null) { return cond ? (info != null ? "ok: " + info : null) : falha; }
        static string F(double v, string fmt = "0.0") { return v.ToString(fmt, CultureInfo.InvariantCulture); }

        public static int Rodar(string[] args)
        {
            string relatorio = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "som-teclado-testes.txt");
            string dados = Path.Combine(Path.GetTempPath(), "macskin-som-teste-" + Process.GetCurrentProcess().Id);
            Directory.CreateDirectory(dados);
            Environment.SetEnvironmentVariable("MACSKIN_DADOS", dados);
            linhas.Add("Bateria de testes do Som de teclado — " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
            linhas.Add("Dados de teste em " + dados);
            linhas.Add("");
            args_depurar = args.Any(a => a == "--depurar"); soSenha = args.Any(a => a == "--so-navegador");
            if (soSenha) { SomTeclado.Iniciar(); SomTeclado.DepurarFoco = args_depurar; Caso("campo de senha no navegador", () => TestarSenhaNavegador(dados)); Caso("login que abre na senha", () => TestarSenhaNavegador(dados, true)); SomTeclado.Parar(); File.WriteAllLines(relatorio, linhas, new UTF8Encoding(false)); return falhas == 0 ? 0 : 1; }
            linhas.Add("== Unidade ==");
            Unidade(dados);
            linhas.Add("");
            linhas.Add("== Ponta a ponta (áudio real) ==");
            bool soUnidade = args.Any(a => a == "--so-unidade");

            if (!soUnidade) PontaAPonta(dados);
            linhas.Add("");
            linhas.Add("Resultado: " + ok + " ok, " + falhas + " falharam, " + pulados + " pulados");
            File.WriteAllLines(relatorio, linhas, new UTF8Encoding(false));
            try { Directory.Delete(dados, true); } catch { }
            return falhas == 0 ? 0 : 1;
        }

        // MacSkin.exe --foto-som-teclado <pasta>: PNGs da janela Som de Teclado (dados temporários, sem ativar janela nenhuma)
        public static int Foto(string[] args)
        {
            string pasta = args[1];
            string dados = Path.Combine(Path.GetTempPath(), "macskin-som-foto-" + Process.GetCurrentProcess().Id);
            Directory.CreateDirectory(dados);
            Environment.SetEnvironmentVariable("MACSKIN_DADOS", dados);
            try
            {
                var e = new EdicaoSom { Id = "meu-som-1", Base = "cristal", Nome = "Meu Cristal", Tom = 64, Clique = 80, Soltar = 40 };
                e.Grupos[SomTeclado.GEspaco] = 50; e.Gravar();
                new EdicaoSom { Id = "seda", Base = "seda", Brilho = 30 }.Gravar();
                SomTeclado.Iniciar();
                EditorSomTeclado.Foto(Path.Combine(pasta, "editor-grafite.png"), "grafite", 0);
                EditorSomTeclado.Foto(Path.Combine(pasta, "editor-proprio.png"), "meu-som-1", 0);
                EditorSomTeclado.Foto(Path.Combine(pasta, "editor-proprio-baixo.png"), "meu-som-1", 2000);
                SomTeclado.Parar();
                return 0;
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(pasta, "foto-erro.txt"), ex.ToString()); return 1; }
            finally { try { Directory.Delete(dados, true); } catch { } }
        }

        // =====================================================================================================
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class EnumeradorDeOutroArquivo { }

        static void Unidade(string dados)
        {
            Caso("áudio: enumerador de dispositivos funciona mesmo se outra parte do programa o criou antes (regressão)", () =>
            {
                var outro = new EnumeradorDeOutroArquivo();   // como o Volume.cs fazia
                var lista = Wasapi.Saidas(); var en = Wasapi.Enumerador();
                Wasapi.IMMDevice d; en.GetDefaultAudioEndpoint(0, 1, out d);
                GC.KeepAlive(outro);
                return Se(lista.Count > 0 && d != null, "não listou as saídas", lista.Count + " saídas");
            });
            string arq = Path.Combine(dados, "aj-teste.txt");

            Caso("ajustes: arquivo ausente dá os padrões", () =>
            {
                var a = SomTeclado.Ajustes.Ler(Path.Combine(dados, "nao-existe.txt"), null);
                return Se(!a.Ligado && a.Volume == 70 && a.Perfil == "grafite" && a.PausaSenha && a.PausaTelaCheia && a.Atalho == "ctrl+alt+k" && a.Saida == "", "padrões errados");
            });
            Caso("ajustes: lê todos os campos", () =>
            {
                File.WriteAllText(arq, "# comentário\nligado=1\nvolume=35\nperfil=Cristal\nmodificadores=sim\nmouse=true\nrolagem=1\nespacial=0\nvariacao=100\npausa_tela_cheia=0\npausa_senha=não\natalho=Shift+F9\nsaida={0.0.0.00000000}.{abc}\n");
                var a = SomTeclado.Ajustes.Ler(arq, null);
                return Se(a.Ligado && a.Volume == 35 && a.Perfil == "cristal" && a.Modificadores && a.Mouse && a.Rolagem && a.Espacial == 0 && a.Variacao == 100 && !a.PausaTelaCheia && !a.PausaSenha && a.Atalho == "shift+f9" && a.Saida == "{0.0.0.00000000}.{abc}", "algum campo não foi lido");
            });
            Caso("ajustes: valores inválidos ficam no padrão e avisam", () =>
            {
                File.WriteAllText(arq, "ligado=talvez\nvolume=150\nespacial=-1\nvariacao=abc\natalho=ctrl+?\nperfil=a/b\nqualquer=1\n");
                var avisos = new List<string>();
                var a = SomTeclado.Ajustes.Ler(arq, avisos.Add);
                return Se(!a.Ligado && a.Volume == 70 && a.Espacial == 60 && a.Variacao == 50 && a.Atalho == "ctrl+alt+k" && a.Perfil == "grafite" && avisos.Count == 7, "aceitou valor inválido ou não avisou (" + avisos.Count + " avisos)", avisos.Count + " avisos");
            });
            Caso("ajustes: grava e lê de volta igual", () =>
            {
                var a = new SomTeclado.Ajustes { Ligado = true, Volume = 12, Perfil = "seda", Modificadores = true, Espacial = 33, Variacao = 77, PausaTelaCheia = false, Atalho = "ctrl+shift+f12", Saida = "x" };
                a.Gravar(arq); a.Volume = 13; a.Gravar(arq);   // a segunda gravação substitui o arquivo
                var b = SomTeclado.Ajustes.Ler(arq, null);
                return Se(b.Ligado && b.Volume == 13 && b.Perfil == "seda" && b.Modificadores && b.Espacial == 33 && b.Variacao == 77 && !b.PausaTelaCheia && b.Atalho == "ctrl+shift+f12" && b.Saida == "x" && !File.Exists(arq + ".tmp"), "ida e volta diferente");
            });
            Caso("atalho: interpreta combinações", () =>
            {
                uint m, vk; var erros = new List<string>();
                if (!SomTeclado.LerAtalho("ctrl+alt+k", out m, out vk) || m != 3 || vk != 0x4B) erros.Add("ctrl+alt+k");
                if (!SomTeclado.LerAtalho("Shift + F9", out m, out vk) || m != 4 || vk != 0x78) erros.Add("shift+f9");
                if (!SomTeclado.LerAtalho("f13", out m, out vk) || m != 0 || vk != 0x7C) erros.Add("f13 sozinho");
                if (!SomTeclado.LerAtalho("win+espaço", out m, out vk) || m != 8 || vk != 0x20) erros.Add("win+espaço");
                if (SomTeclado.LerAtalho("k", out m, out vk)) erros.Add("k sem modificador aceito");
                if (SomTeclado.LerAtalho("ctrl+alt", out m, out vk)) erros.Add("sem tecla aceito");
                if (SomTeclado.LerAtalho("ctrl+a+b", out m, out vk)) erros.Add("duas teclas aceitas");
                if (SomTeclado.LerAtalho("ctrl+f25", out m, out vk)) erros.Add("f25 aceito");
                if (SomTeclado.AtalhoLegivel("ctrl+alt+k") != "Ctrl+Alt+K") erros.Add("legível: " + SomTeclado.AtalhoLegivel("ctrl+alt+k"));
                return Se(erros.Count == 0, string.Join(", ", erros));
            });
            Caso("teclas: grupos (espaço, Enter, apagar, modificadores, comuns)", () =>
            {
                var esperado = new Dictionary<uint, int> { { 0x20, SomTeclado.GEspaco }, { 0x0D, SomTeclado.GEnter }, { 0x08, SomTeclado.GApagar }, { 0x2E, SomTeclado.GApagar },
                    { 0xA0, SomTeclado.GMod }, { 0xA3, SomTeclado.GMod }, { 0x5B, SomTeclado.GMod }, { 0x41, SomTeclado.GComum }, { 0x70, SomTeclado.GComum }, { 0x60, SomTeclado.GComum }, { 0x14, SomTeclado.GComum }, { 0xBA, SomTeclado.GComum } };
                var erros = esperado.Where(kv => SomTeclado.Grupo(kv.Key) != kv.Value).Select(kv => "vk " + kv.Key.ToString("X2")).ToList();
                return Se(erros.Count == 0, string.Join(", ", erros));
            });
            Caso("teclas: posição esquerda→direita pelo código de varredura", () =>
            {
                float esc = SomTeclado.Coluna(0x01, false), q = SomTeclado.Coluna(0x10, false), p = SomTeclado.Coluna(0x19, false), a = SomTeclado.Coluna(0x1E, false), l = SomTeclado.Coluna(0x26, false),
                      z = SomTeclado.Coluna(0x2C, false), m = SomTeclado.Coluna(0x32, false), esp = SomTeclado.Coluna(0x39, false), num5 = SomTeclado.Coluna(0x4C, false), dir = SomTeclado.Coluna(0x4D, true), enter = SomTeclado.Coluna(0x1C, false);
                bool bom = esc == 0 && q < p && a < l && z < m && p < num5 && enter > p && dir > enter && num5 > dir && Math.Abs(esp - 0.33f) < 0.05f && num5 <= 1;
                return Se(bom, "ordem errada: esc " + F(esc, "0.00") + " q " + F(q, "0.00") + " p " + F(p, "0.00") + " espaço " + F(esp, "0.00") + " num5 " + F(num5, "0.00"), "Q " + F(q, "0.00") + " · P " + F(p, "0.00") + " · espaço " + F(esp, "0.00") + " · num5 " + F(num5, "0.00"));
            });

            SomTeclado.Perfil[] ps = null;
            Caso("síntese: 3 perfis próprios gerados", () =>
            {
                var sw = Stopwatch.StartNew();
                ps = SomTeclado.CarregarPerfis(new string[0]);
                return Se(ps.Length == 3 && ps[0].Id == "grafite" && ps[1].Id == "cristal" && ps[2].Id == "seda", "perfis: " + string.Join(",", ps.Select(x => x.Id)), "em " + sw.ElapsedMilliseconds + " ms");
            });
            Caso("síntese: todo grupo tem 6 variações válidas (sem NaN, pico 0,05–0,86, até 150 ms)", () =>
            {
                var erros = new List<string>();
                foreach (var p in ps)
                    for (int g = 0; g < SomTeclado.NGrupos; g++)
                        for (int d = 0; d < 2; d++)
                        {
                            var vars = p.Amostras[g * 2 + d];
                            string nome = p.Id + "/" + SomTeclado.NomesGrupo[g] + "-" + (d == 0 ? "desce" : "sobe");
                            if (g == SomTeclado.GRolagem && d == 1) { if (vars != null) erros.Add(nome + " não devia existir"); continue; }
                            if (vars == null || vars.Length != 6) { erros.Add(nome + " sem 6 variações"); continue; }
                            foreach (var v in vars)
                            {
                                float pico = v.Max(x => Math.Abs(x));
                                if (v.Any(x => float.IsNaN(x) || float.IsInfinity(x))) erros.Add(nome + " NaN");
                                if (pico < 0.05f || pico > 0.86f) erros.Add(nome + " pico " + F(pico, "0.00"));
                                if (v.Length > Sintese.Taxa * 0.15) erros.Add(nome + " longo demais");
                                if (Math.Abs(v[v.Length - 1]) > 1e-3) erros.Add(nome + " termina com estalo");
                            }
                        }
                return Se(erros.Count == 0, string.Join("; ", erros.Take(6)));
            });
            Caso("síntese: o som começa logo (pico nos primeiros 15 ms do toque)", () =>
            {
                var erros = new List<string>();
                foreach (var p in ps) foreach (int g in new[] { SomTeclado.GComum, SomTeclado.GEspaco, SomTeclado.GEnter })
                    {
                        var v = p.Amostras[g * 2][0]; int ip = 0; for (int i = 1; i < v.Length; i++) if (Math.Abs(v[i]) > Math.Abs(v[ip])) ip = i;
                        if (ip > Sintese.Taxa * 0.015) erros.Add(p.Id + "/" + SomTeclado.NomesGrupo[g] + " pico em " + F(ip * 1000.0 / Sintese.Taxa) + " ms");
                    }
                return Se(erros.Count == 0, string.Join("; ", erros));
            });
            Caso("síntese: variações do mesmo grupo são diferentes entre si", () =>
            {
                var v = ps[0].Amostras[0]; double minDif = double.MaxValue;
                for (int i = 0; i < v.Length; i++) for (int j = i + 1; j < v.Length; j++)
                    {
                        int n = Math.Min(v[i].Length, v[j].Length); double d = 0; for (int k = 0; k < n; k++) d += Math.Abs(v[i][k] - v[j][k]);
                        minDif = Math.Min(minDif, d / n);
                    }
                return Se(minDif > 0.005, "duas variações quase iguais (" + F(minDif, "0.0000") + ")", "menor diferença média " + F(minDif, "0.000"));
            });
            Caso("síntese: caráter dos perfis (Cristal mais agudo que Grafite; Seda mais suave)", () =>
            {
                Func<float[], double> zcr = s => { int c = 0; for (int i = 1; i < s.Length; i++) if ((s[i - 1] < 0) != (s[i] < 0)) c++; return c * (double)Sintese.Taxa / s.Length; };
                Func<float[], double> energia = s => s.Sum(x => (double)x * x);
                double zg = ps[0].Amostras[0].Average(zcr), zc = ps[1].Amostras[0].Average(zcr), zs = ps[2].Amostras[0].Average(zcr);
                double eg = ps[0].Amostras[0].Average(energia), es = ps[2].Amostras[0].Average(energia);
                double eUp = ps[0].Amostras[1].Average(energia);
                return Se(zc > zg * 1.3 && es < eg && eUp < eg, "cruzamentos/s: grafite " + F(zg, "0") + ", cristal " + F(zc, "0") + ", seda " + F(zs, "0") + "; energia seda " + F(es, "0.0") + " grafite " + F(eg, "0.0"),
                    "cruzamentos por segundo: Grafite " + F(zg, "0") + ", Cristal " + F(zc, "0") + ", Seda " + F(zs, "0") + " · subida mais fraca que descida");
            });
            Caso("síntese: espaço é mais grave e mais longo que tecla comum", () =>
            {
                var c = ps[0].Amostras[0][0]; var e = ps[0].Amostras[SomTeclado.GEspaco * 2][0];
                Func<float[], int> fim = s => { int k = s.Length - 1; while (k > 0 && Math.Abs(s[k]) < 0.01) k--; return k; };
                return Se(fim(e) > fim(c), "espaço não é mais longo", "comum " + F(fim(c) * 1000.0 / Sintese.Taxa, "0") + " ms, espaço " + F(fim(e) * 1000.0 / Sintese.Taxa, "0") + " ms");
            });

            // WAV
            string pastaWav = Path.Combine(dados, "wav"); Directory.CreateDirectory(pastaWav);
            Caso("WAV: 16 bits 44,1 kHz mono → 48 kHz", () =>
            {
                var s = new float[4410]; for (int i = 0; i < s.Length; i++) s[i] = (float)Math.Sin(i * 0.1) * 0.5f;
                string f = Path.Combine(pastaWav, "a.wav"); Wav.Gravar16(f, s, 44100);
                var r = Wav.LerMono(f, 48000);
                return Se(Math.Abs(r.Length - 4800) <= 1 && Math.Abs(r.Max() - 0.5f) < 0.01f, "tamanho " + r.Length + ", pico " + F(r.Max(), "0.000"));
            });
            Caso("WAV: 24 bits estéreo e float 32 bits", () =>
            {
                string f24 = Path.Combine(pastaWav, "b.wav"), f32 = Path.Combine(pastaWav, "c.wav");
                EscreverWav(f24, 1, 24, 2, 48000, new[] { 0.5, -0.5, 0.25, 0.25 });
                EscreverWav(f32, 3, 32, 1, 48000, new[] { 0.75, -0.25 });
                var a = Wav.LerMono(f24, 48000); var b = Wav.LerMono(f32, 48000);
                return Se(a.Length == 2 && Math.Abs(a[0]) < 0.001 && Math.Abs(a[1] - 0.25) < 0.001 && b.Length == 2 && Math.Abs(b[0] - 0.75) < 1e-6, "valores lidos errados: " + string.Join(",", a) + " / " + string.Join(",", b));
            });
            Caso("WAV: arquivo inválido é recusado sem derrubar", () =>
            {
                string f = Path.Combine(pastaWav, "ruim.wav"); File.WriteAllText(f, "isto não é um wav, mas tem mais de quarenta e quatro bytes de texto aqui");
                try { Wav.LerMono(f, 48000); return "aceitou arquivo inválido"; } catch (InvalidDataException) { return null; }
            });
            Caso("pacote de WAV: carrega, usa 'comum' no que faltar e ignora pasta sem comum-desce", () =>
            {
                string raiz = Path.Combine(dados, "sons"), meu = Path.Combine(raiz, "meu"), vazio = Path.Combine(raiz, "vazio"), sobrescreve = Path.Combine(raiz, "grafite");
                Directory.CreateDirectory(meu); Directory.CreateDirectory(vazio); Directory.CreateDirectory(sobrescreve);
                var s = new float[2400]; for (int i = 0; i < s.Length; i++) s[i] = (float)(Math.Sin(i * 0.3) * Math.Exp(-i / 300.0) * 0.6);
                Wav.Gravar16(Path.Combine(meu, "comum-desce-1.wav"), s, 48000); Wav.Gravar16(Path.Combine(meu, "comum-desce-2.wav"), s, 44100);
                Wav.Gravar16(Path.Combine(meu, "enter-desce-1.wav"), s, 48000);
                File.WriteAllText(Path.Combine(meu, "perfil.txt"), "nome=Meu teclado\nlicenca=CC0\n");
                Wav.Gravar16(Path.Combine(vazio, "comum-sobe-1.wav"), s, 48000);
                Wav.Gravar16(Path.Combine(sobrescreve, "comum-desce-1.wav"), s, 48000);
                var lista = SomTeclado.CarregarPerfis(new[] { raiz });
                var p = lista.FirstOrDefault(x => x.Id == "meu");
                var erros = new List<string>();
                if (p == null) return "pacote não carregou";
                if (p.Nome != "Meu teclado" || p.Licenca != "CC0") erros.Add("perfil.txt não lido");
                if (p.De(SomTeclado.GComum, true).Length != 2) erros.Add("comum-desce não tem 2");
                if (p.De(SomTeclado.GEspaco, true) != p.De(SomTeclado.GComum, true)) erros.Add("espaço não caiu no comum");
                if (p.De(SomTeclado.GEnter, true) == p.De(SomTeclado.GComum, true)) erros.Add("enter próprio ignorado");
                if (p.De(SomTeclado.GMouse, true) != null) erros.Add("mouse devia ficar calado");
                if (p.De(SomTeclado.GComum, false) != null) erros.Add("subida inventada");
                if (lista.Any(x => x.Id == "vazio")) erros.Add("pasta sem comum-desce entrou");
                var g = lista.First(x => x.Id == "grafite");
                if (g.Origem == null || !g.Origem.EndsWith("grafite") || lista.Count(x => x.Id == "grafite") != 1) erros.Add("pacote 'grafite' não substituiu o sintetizado");
                return Se(erros.Count == 0, string.Join("; ", erros), lista.Length + " perfis na lista");
            });

            // editor de sons (sons-editados)
            Caso("editor: ajustes gravam e voltam iguais (e valores fora da faixa são cortados)", () =>
            {
                var e = new EdicaoSom { Id = "teste-ida", Base = "cristal", Nome = "Meu = teste", Tom = 10, Corpo = 90, Clique = 0, Brilho = 100, Duracao = 33, Soltar = 0 };
                e.Grupos[SomTeclado.GEspaco] = 20; e.Gravar(); e.Tom = 11; e.Gravar();
                var l = EdicaoSom.LerTodas().FirstOrDefault(x => x.Id == "teste-ida");
                File.AppendAllText(Path.Combine(EdicaoSom.Pasta, "teste-ida.txt"), "corpo=500\nvolume-enter=-3\nlixo\n");
                var l2 = EdicaoSom.LerTodas().First(x => x.Id == "teste-ida");
                EdicaoSom.Apagar("teste-ida");
                bool bom = l != null && l.Base == "cristal" && l.Nome == "Meu = teste" && l.Tom == 11 && l.Corpo == 90 && l.Clique == 0 && l.Brilho == 100 && l.Duracao == 33 && l.Soltar == 0
                    && l.Grupos[SomTeclado.GEspaco] == 20 && l.Grupos[0] == EdicaoSom.GrupoNeutro && l2.Corpo == 100 && l2.Grupos[SomTeclado.GEnter] == 0
                    && !File.Exists(Path.Combine(EdicaoSom.Pasta, "teste-ida.txt")) && !File.Exists(Path.Combine(EdicaoSom.Pasta, "teste-ida.txt.tmp"));
                return Se(bom, "ida e volta diferente");
            });
            Caso("editor: controles no meio não mudam o som; 67 = 100%", () =>
            {
                var e = new EdicaoSom { Id = "grafite", Base = "grafite" };
                var a = Sintese.Grafite(); var b = Sintese.Grafite(e);
                double dif = 0; var x = a.De(SomTeclado.GComum, true)[2]; var y = b.De(SomTeclado.GComum, true)[2];
                for (int i = 0; i < Math.Min(x.Length, y.Length); i++) dif = Math.Max(dif, Math.Abs(x[i] - y[i]));
                return Se(e.Neutra && EdicaoSom.PorCento(EdicaoSom.GrupoNeutro) == 100 && x.Length == y.Length && dif < 0.02, "som mudou com tudo neutro (dif " + F(dif, "0.000") + ")", "diferença máx " + F(dif, "0.0000"));
            });
            Caso("editor: tom, brilho, duração e volume fazem o que dizem", () =>
            {
                Func<float[], double> zcr = s => { int c = 0; for (int i = 1; i < s.Length; i++) if ((s[i - 1] < 0) != (s[i] < 0)) c++; return c * (double)Sintese.Taxa / s.Length; };
                Func<float[], double> pico = s => s.Max(v => Math.Abs(v));
                Func<float[], int> fim = s => { double p = pico(s); int u = 0; for (int i = 0; i < s.Length; i++) if (Math.Abs(s[i]) > p * 0.02) u = i; return u; };
                Func<EdicaoSom, float[]> som = e => Sintese.Seda(e).De(SomTeclado.GComum, true)[0];
                var n = som(new EdicaoSom { Id = "seda" });
                var grave = som(new EdicaoSom { Id = "seda", Tom = 0 }); var agudo = som(new EdicaoSom { Id = "seda", Tom = 100 });
                var longo = som(new EdicaoSom { Id = "seda", Duracao = 100 }); var curto = som(new EdicaoSom { Id = "seda", Duracao = 0 });
                var baixo = new EdicaoSom { Id = "seda" }; baixo.Grupos[SomTeclado.GComum] = 20; var vb = som(baixo);
                var erros = new List<string>();
                if (!(zcr(grave) < zcr(n) && zcr(n) < zcr(agudo))) erros.Add("tom: " + F(zcr(grave), "0") + "/" + F(zcr(n), "0") + "/" + F(zcr(agudo), "0") + " Hz");
                if (!(fim(longo) > fim(n) && fim(n) >= fim(curto))) erros.Add("duração");
                if (!(Math.Abs(pico(vb) / pico(n) - 0.3) < 0.03)) erros.Add("volume " + F(pico(vb) / pico(n), "0.00"));
                if (longo.Length > Sintese.Taxa * 0.31) erros.Add("cauda passou de 300 ms");
                var calado = new EdicaoSom { Id = "seda", Soltar = 0 };
                var sp = Sintese.Seda(calado);
                if (sp.De(SomTeclado.GComum, false) != null) erros.Add("soltar 0% ainda toca");
                return Se(erros.Count == 0, string.Join("; ", erros), "cruzamentos " + F(zcr(grave), "0") + " < " + F(zcr(n), "0") + " < " + F(zcr(agudo), "0") + " por s");
            });
            Caso("editor: perfil próprio a partir de um de fábrica entra na lista; edição de fábrica mantém o id", () =>
            {
                new EdicaoSom { Id = "meu-som-1", Base = "cristal", Nome = "Meu Cristal", Tom = 70 }.Gravar();
                new EdicaoSom { Id = "seda", Base = "seda", Nome = "Seda escritório", Brilho = 20 }.Gravar();
                new EdicaoSom { Id = "orfao", Base = "nao-existe" }.Gravar();
                var lista = SomTeclado.CarregarPerfis(new string[0]);
                EdicaoSom.Apagar("meu-som-1"); EdicaoSom.Apagar("seda"); EdicaoSom.Apagar("orfao");
                var m = lista.FirstOrDefault(x => x.Id == "meu-som-1"); var s = lista.FirstOrDefault(x => x.Id == "seda");
                var erros = new List<string>();
                if (m == null) return "perfil próprio não carregou";
                if (!m.Proprio || m.Base != "cristal" || m.Nome != "Meu Cristal" || m.Wav) erros.Add("próprio com campos errados");
                if (s == null || s.Proprio || !s.Editado || s.Nome != "Seda escritório") erros.Add("edição de fábrica errada");
                if (lista.Count(x => x.Id == "seda") != 1) erros.Add("seda duplicado");
                if (lista.Any(x => x.Id == "orfao")) erros.Add("perfil sem base entrou");
                if (EdicaoSom.NovoId(lista.Select(x => x.Id)) != "meu-som-2") erros.Add("novo id " + EdicaoSom.NovoId(lista.Select(x => x.Id)));
                return Se(erros.Count == 0, string.Join("; ", erros), lista.Length + " perfis");
            });
            Caso("editor: pacote de WAV muda só os volumes", () =>
            {
                string raiz = Path.Combine(dados, "sons");
                var e = new EdicaoSom { Id = "meu", Base = "meu", Tom = 0 }; e.Grupos[SomTeclado.GComum] = 33;
                e.Gravar();
                var lista = SomTeclado.CarregarPerfis(new[] { raiz });
                EdicaoSom.Apagar("meu");
                var orig = SomTeclado.CarregarPerfis(new[] { raiz }).First(x => x.Id == "meu");
                var p = lista.First(x => x.Id == "meu");
                var a = orig.De(SomTeclado.GComum, true)[0]; var b = p.De(SomTeclado.GComum, true)[0];
                double r = b.Max(v => Math.Abs(v)) / a.Max(v => Math.Abs(v));
                return Se(p.Wav && p.Editado && a.Length == b.Length && Math.Abs(r - 0.495) < 0.01, "razão " + F(r, "0.000") + ", tamanhos " + a.Length + "/" + b.Length, "volume ×" + F(r, "0.00") + ", mesmo tamanho");
            });

            // mistura
            Caso("mistura: 40 teclas de uma vez → 32 vozes, as mais velhas cedem a vaga, sem passar de 1", () =>
            {
                var m = new SomTeclado.Mixer();
                for (int i = 0; i < 40; i++) m.Evento(SomTeclado.Codigo(SomTeclado.GEnter, true, i / 40f, 0, 0), ps, 0, 48000, 100, 100);
                int ativas = m.Ativas;
                var e = new float[480]; var d = new float[480];
                float pico = 0; bool nan = false;
                for (int b = 0; b < 30; b++) { m.Misturar(e, d, 480, 1f, ps, 0, 48000, 100, 100); for (int i = 0; i < 480; i++) { pico = Math.Max(pico, Math.Max(Math.Abs(e[i]), Math.Abs(d[i]))); nan |= float.IsNaN(e[i]) || float.IsNaN(d[i]); } }
                return Se(ativas == 32 && m.Roubadas == 8 && pico <= 1f && !nan && pico > 0.5f, "ativas " + ativas + ", roubadas " + m.Roubadas + ", pico " + F(pico, "0.000"), "pico " + F(pico, "0.000") + " (limitador suave)");
            });
            Caso("mistura: som espacial (tecla da esquerda soa à esquerda; espacial 0 = centro)", () =>
            {
                Func<float, int, double[]> medir = (pos, esp) =>
                {
                    var m = new SomTeclado.Mixer(); m.Evento(SomTeclado.Codigo(SomTeclado.GComum, true, pos, 0, 0), ps, 0, 48000, esp, 0);
                    var e = new float[9600]; var d = new float[9600]; m.Misturar(e, d, 9600, 1f, ps, 0, 48000, esp, 0);
                    return new[] { e.Sum(x => (double)x * x), d.Sum(x => (double)x * x) };
                };
                var esq = medir(0f, 100); var dir = medir(1f, 100); var centro = medir(0f, 0);
                return Se(esq[0] > esq[1] * 20 && dir[1] > dir[0] * 20 && Math.Abs(centro[0] - centro[1]) < 1e-6 * (centro[0] + 1),
                    "esq " + F(esq[0], "0.00") + "/" + F(esq[1], "0.00") + " dir " + F(dir[0], "0.00") + "/" + F(dir[1], "0.00") + " centro " + F(centro[0], "0.00") + "/" + F(centro[1], "0.00"));
            });
            Caso("mistura: variação 100 muda tom/volume a cada toque; variação 0 não", () =>
            {
                Func<int, double> espalhamento = variacao =>
                {
                    var m = new SomTeclado.Mixer(); var energias = new List<double>();
                    var um = new[] { new SomTeclado.Perfil { Id = "x", Taxa = 48000 } };
                    um[0].Amostras[0] = new[] { ps[0].Amostras[0][0] };   // uma variação só: a diferença vem só do tom/volume
                    for (int i = 0; i < 12; i++)
                    {
                        m.Evento(SomTeclado.Codigo(0, true, 0.5f, 0, 0), um, 0, 48000, 0, variacao);
                        var e = new float[9600]; var d = new float[9600]; m.Misturar(e, d, 9600, 1f, um, 0, 48000, 0, variacao);
                        energias.Add(e.Sum(x => (double)x * x));
                    }
                    return (energias.Max() - energias.Min()) / energias.Average();
                };
                double v0 = espalhamento(0), v100 = espalhamento(100);
                return Se(v0 < 1e-6 && v100 > 0.2, "espalhamento com 0: " + F(v0, "0.0000") + ", com 100: " + F(v100, "0.00"), "energia varia " + F(v100 * 100, "0") + "% com variação 100");
            });
            Caso("mistura: evento com atraso (prévia) toca na hora certa", () =>
            {
                var m = new SomTeclado.Mixer();
                m.Evento(SomTeclado.Codigo(SomTeclado.GComum, true, 0.5f, 0, 10), ps, 0, 48000, 0, 0);   // 100 ms
                var e = new float[480]; var d = new float[480]; int primeiro = -1;
                for (int b = 0; b < 40 && primeiro < 0; b++) { m.Misturar(e, d, 480, 1f, ps, 0, 48000, 0, 0); for (int i = 0; i < 480; i++) if (Math.Abs(e[i]) > 1e-4) { primeiro = b * 480 + i; break; } }
                double ms = primeiro * 1000.0 / 48000;
                return Se(primeiro >= 0 && ms >= 89 && ms <= 111, "começou em " + F(ms) + " ms", "começou em " + F(ms) + " ms");
            });
            Caso("volume: curva 0 → mudo, sobe sempre, 100 abaixo de 1", () =>
            {
                bool cresce = true; for (int v = 1; v <= 100; v++) cresce &= SomTeclado.Mestre(v) > SomTeclado.Mestre(v - 1);
                return Se(SomTeclado.Mestre(0) == 0 && cresce && SomTeclado.Mestre(100) <= 1, "curva errada");
            });
            Caso("pausa em tela cheia: estados do Windows", () =>
            {
                bool bom = SomTeclado.PausaPorEstado(2) && SomTeclado.PausaPorEstado(3) && SomTeclado.PausaPorEstado(4) && !SomTeclado.PausaPorEstado(5) && !SomTeclado.PausaPorEstado(1) && !SomTeclado.PausaPorEstado(6);
                return Se(bom, "mapeamento errado");
            });

            // gancho (a lógica, sem o Windows)
            Caso("gancho: segurar a tecla não repete o som; soltar toca uma vez", () =>
            {
                SomTeclado.Alterar(a => { a.Ligado = true; a.Modificadores = false; a.PausaSenha = true; });
                SomTeclado.DefinirSenhaParaTeste(false); SomTeclado.EsvaziarFila();
                bool d1 = SomTeclado.Tecla(0x100, 0x41, 0x1E, 0), d2 = SomTeclado.Tecla(0x100, 0x41, 0x1E, 0), d3 = SomTeclado.Tecla(0x100, 0x41, 0x1E, 0), u1 = SomTeclado.Tecla(0x101, 0x41, 0x1E, 0), u2 = SomTeclado.Tecla(0x101, 0x41, 0x1E, 0);
                int n = SomTeclado.EsvaziarFila();
                return Se(d1 && !d2 && !d3 && u1 && !u2 && n == 2, "desce " + d1 + d2 + d3 + " sobe " + u1 + u2 + " fila " + n);
            });
            Caso("gancho: modificadores só com a opção ligada; Alt (WM_SYSKEYDOWN) conta", () =>
            {
                bool sem = SomTeclado.Tecla(0x100, 0xA0, 0x2A, 0); SomTeclado.Tecla(0x101, 0xA0, 0x2A, 0);
                SomTeclado.Alterar(a => a.Modificadores = true);
                bool com = SomTeclado.Tecla(0x104, 0xA4, 0x38, 0x20); SomTeclado.Tecla(0x105, 0xA4, 0x38, 0x20);
                SomTeclado.Alterar(a => a.Modificadores = false); SomTeclado.EsvaziarFila();
                return Se(!sem && com, "sem opção: " + sem + ", com opção: " + com);
            });
            Caso("gancho: campo de senha e som desligado não tocam", () =>
            {
                SomTeclado.DefinirSenhaParaTeste(true);
                bool senha = SomTeclado.Tecla(0x100, 0x42, 0x30, 0); SomTeclado.Tecla(0x101, 0x42, 0x30, 0);
                SomTeclado.DefinirSenhaParaTeste(false);
                SomTeclado.Alterar(a => a.Ligado = false);
                bool deslig = SomTeclado.Tecla(0x100, 0x42, 0x30, 0); SomTeclado.Tecla(0x101, 0x42, 0x30, 0);
                SomTeclado.Alterar(a => a.Ligado = true);
                bool calandoAoLigar = SomTeclado.EmSenha();   // ao ligar, confere o foco atual: calado até a resposta
                SomTeclado.DefinirSenhaParaTeste(false);
                bool normal = SomTeclado.Tecla(0x100, 0x42, 0x30, 0); SomTeclado.Tecla(0x101, 0x42, 0x30, 0);
                SomTeclado.EsvaziarFila();
                return Se(!senha && !deslig && normal && calandoAoLigar, "senha " + senha + ", desligado " + deslig + ", normal " + normal + ", calado ao ligar " + calandoAoLigar);
            });
            Caso("fila: transbordar descarta sem travar", () =>
            {
                SomTeclado.EsvaziarFila(); int antes = SomTeclado.Descartados;
                for (int i = 0; i < 700; i++) SomTeclado.Enfileirar(SomTeclado.Codigo(0, true, 0.5f, 0, 0));
                int n = SomTeclado.EsvaziarFila();
                return Se(n == 511 && SomTeclado.Descartados - antes == 189, "na fila " + n + ", descartados " + (SomTeclado.Descartados - antes));
            });
            Caso("desempenho: caminho do gancho (decidir + enfileirar)", () =>
            {
                SomTeclado.EsvaziarFila();
                var sw = Stopwatch.StartNew(); int n = 0;
                for (int i = 0; i < 100000; i++)
                {
                    uint vk = (uint)(0x41 + i % 26);
                    SomTeclado.Tecla(0x100, vk, 0x1E, 0); SomTeclado.Tecla(0x101, vk, 0x1E, 0); n += 2;
                    if ((i & 127) == 0) SomTeclado.EsvaziarFila();
                }
                sw.Stop(); SomTeclado.EsvaziarFila();
                double us = sw.Elapsed.TotalMilliseconds * 1000 / n;
                return Se(us < 3, F(us, "0.00") + " µs por evento (limite 3)", F(us, "0.00") + " µs por evento");
            });
            Caso("desempenho: misturar 1 s de áudio com 8 vozes tocando", () =>
            {
                var m = new SomTeclado.Mixer(); var e = new float[480]; var d = new float[480];
                var sw = Stopwatch.StartNew();
                for (int b = 0; b < 100; b++)
                {
                    if (b % 2 == 0) for (int k = 0; k < 4; k++) m.Evento(SomTeclado.Codigo(k % 3, true, 0.3f, b % 3, 0), ps, 0, 48000, 60, 50);
                    m.Misturar(e, d, 480, 0.5f, ps, 0, 48000, 60, 50);
                }
                sw.Stop();
                double pct = sw.Elapsed.TotalMilliseconds / 1000 * 100;
                return Se(pct < 2, F(pct, "0.00") + "% de um núcleo", F(sw.Elapsed.TotalMilliseconds, "0.0") + " ms por segundo de áudio (" + F(pct, "0.00") + "% de um núcleo)");
            });
            SomTeclado.Alterar(a => a.Ligado = false);
        }

        static void EscreverWav(string f, int tag, int bits, int canais, int taxa, double[] amostras)
        {
            using (var w = new BinaryWriter(File.Create(f)))
            {
                int bps = bits / 8, dados = amostras.Length * bps;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dados); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16); w.Write((short)tag); w.Write((short)canais); w.Write(taxa); w.Write(taxa * bps * canais); w.Write((short)(bps * canais)); w.Write((short)bits);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dados);
                foreach (var v in amostras)
                {
                    if (tag == 3) w.Write((float)v);
                    else { int x = (int)(v * 8388607); w.Write((byte)(x & 255)); w.Write((byte)((x >> 8) & 255)); w.Write((byte)((x >> 16) & 255)); }
                }
            }
        }

        // =====================================================================================================
        // ouvinte: captura o que o Windows toca e marca os começos de som (ataques)
        class Ouvinte : IDisposable
        {
            readonly Wasapi.Captura cap; readonly Thread t; volatile bool fim;
            readonly List<long> ataques = new List<long>(); public float Pico; public float Limiar = 0.004f;
            long ultimoSom = long.MinValue / 2; public int Taxa;
            public Ouvinte()
            {
                cap = Wasapi.Captura.Abrir(null); Taxa = cap.Fmt.Taxa;
                t = new Thread(() =>
                {
                    while (!fim)
                    {
                        try
                        {
                            cap.Ler((pico, n, qpc, desc) =>
                            {
                                for (int i = 0; i < n; i++)
                                {
                                    if (pico[i] > Pico) Pico = pico[i];
                                    if (pico[i] < Limiar) continue;
                                    long quando = qpc + (long)i * 10000000 / Taxa;
                                    lock (ataques) { if (quando - ultimoSom > 400000) ataques.Add(quando); ultimoSom = quando; }   // 40 ms de silêncio separam dois sons
                                }
                            });
                        }
                        catch { }
                        Thread.Sleep(1);
                    }
                }) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
                t.Start();
            }
            public List<long> Desde(long t0) { lock (ataques) return ataques.Where(a => a >= t0).ToList(); }
            public void Zerar() { lock (ataques) ataques.Clear(); Pico = 0; }
            public void Dispose() { fim = true; t.Join(500); cap.Dispose(); }
        }

        static long Agora100ns() { return (long)(Stopwatch.GetTimestamp() * (10000000.0 / Stopwatch.Frequency)); }

        static void PontaAPonta(string dados)
        {
            Thread.Sleep(700);   // deixa a última gravação atrasada da parte de unidade acontecer antes de escrever os ajustes
            try { Directory.Delete(Path.Combine(dados, "sons"), true); } catch { }   // o pacote "grafite" de teste substituiria o sintetizado
            File.WriteAllText(Path.Combine(dados, "som-teclado.txt"), "ligado=1\nvolume=70\nperfil=grafite\nespacial=60\nvariacao=50\npausa_tela_cheia=0\natalho=ctrl+alt+k\n");
            SomTeclado.Iniciar();
            Thread.Sleep(300);
            Ouvinte ouv = null;
            Caso("começa desligado mesmo com ligado=1 gravado; o resto dos ajustes é lido", () => Se(!SomTeclado.Atual.Ligado && SomTeclado.Atual.Perfil == "grafite" && !SomTeclado.Atual.PausaTelaCheia, "ligado " + SomTeclado.Atual.Ligado));
            SomTeclado.Alterar(a => a.Ligado = true); Thread.Sleep(300);

            Caso("saída de áudio abre (WASAPI)", () =>
            {
                SomTeclado.Clique();
                for (int i = 0; i < 40 && SomTeclado.SaidaInfo == ""; i++) Thread.Sleep(50);
                var s = SomTeclado.SaidaAtual;
                return Se(SomTeclado.SaidaInfo != "", "não abriu: " + SomTeclado.UltimoErro, SomTeclado.SaidaInfo + (s != null ? ", buffer " + F(s.Buffer * 1000.0 / s.Fmt.Taxa) + " ms" : ""));
            });
            Caso("gancho de teclado instalado com o som ligado; perfis sintetizados carregados", () => Se(SomTeclado.GanchoTeclado && SomTeclado.Perfis.Length == 3 && SomTeclado.Atual.Ligado, "gancho " + SomTeclado.GanchoTeclado + ", perfis " + SomTeclado.Perfis.Length));

            // escuta pelo próprio motor: quantas vozes começaram, o pico escrito no buffer e quando foi escrito
            var mx = SomTeclado.Misturador;
            Func<int, int, float, long, int> tocar = (vk, segurarMs, _x, extra) => { long n0 = mx.Iniciadas; Tecla((ushort)vk, false, extra); Thread.Sleep(segurarMs); Tecla((ushort)vk, true, extra); Thread.Sleep(200); return (int)(mx.Iniciadas - n0); };
            double latDisp = 0;
            Caso("atraso informado pelo dispositivo (GetStreamLatency)", () =>
            {
                var s0 = SomTeclado.SaidaAtual; if (s0 == null) return "sem saída";
                latDisp = s0.LatenciaHns() / 10000.0;
                return "ok: " + F(latDisp) + " ms";
            });
            Caso("tecla F24 real (SendInput → gancho → fila → mistura → buffer): latência", () =>
            {
                var lat = new List<double>(); var gancho = new List<double>(); int semSom = 0;
                double f = 1000.0 / Stopwatch.Frequency;
                for (int i = 0; i < 20; i++)
                {
                    Thread.Sleep(i % 5 == 0 ? 2200 : 150);   // a cada 5, espera o fluxo de áudio parar: mede também a partida a frio
                    long n0 = mx.Iniciadas, e0 = SomTeclado.MedEscrita; long t0 = Stopwatch.GetTimestamp();
                    Tecla(0x87, false, 0);
                    Thread.Sleep(60);
                    if (mx.Iniciadas == n0 || SomTeclado.MedEscrita == e0) { semSom++; Tecla(0x87, true, 0); continue; }
                    gancho.Add((SomTeclado.MedFila - t0) * f);
                    lat.Add((SomTeclado.MedEscrita - t0) * f + SomTeclado.MedNaFrenteMs + latDisp);
                    Tecla(0x87, true, 0);
                }
                if (lat.Count == 0) return "nenhum som em 20 teclas";
                lat.Sort(); gancho.Sort();
                double med = lat[lat.Count / 2], p95 = lat[Math.Min(lat.Count - 1, (int)(lat.Count * 0.95))];
                string info = "da tecla ao alto-falante ~" + F(med) + " ms (mediana; p95 " + F(p95) + ", mín " + F(lat[0]) + "); só o gancho " + F(gancho[gancho.Count / 2], "0.00") + " ms; " + semSom + " sem som";
                return Se(semSom == 0 && med < 30, info, info);
            });
            Caso("segurar a tecla (repetição automática) toca só descer + subir", () =>
            {
                Thread.Sleep(300); long n0 = mx.Iniciadas; int antes = SomTeclado.Contados;
                for (int i = 0; i < 6; i++) { Tecla(0x87, false, 0); Thread.Sleep(35); }
                Thread.Sleep(250); Tecla(0x87, true, 0); Thread.Sleep(250);
                int vozes = (int)(mx.Iniciadas - n0), eventos = SomTeclado.Contados - antes;
                return Se(eventos == 2 && vozes == 2, "eventos " + eventos + ", vozes " + vozes, "6 descidas repetidas + 1 subida → 2 sons");
            });
            Caso("teclas da Substituição de texto (marcadas) não tocam", () =>
            {
                int v = tocar(0x87, 40, 0, 0x4D534B4E);
                return Se(v == 0, v + " sons");
            });
            Caso("desligado não toca e tira o gancho", () =>
            {
                SomTeclado.Alterar(a => a.Ligado = false); Thread.Sleep(300);
                bool gancho = SomTeclado.GanchoTeclado;
                int v = tocar(0x87, 40, 0, 0);
                SomTeclado.Alterar(a => a.Ligado = true); Thread.Sleep(300);
                int depois = tocar(0x87, 40, 0, 0);
                return Se(!gancho && v == 0 && depois == 2, "gancho " + gancho + ", sons desligado " + v + ", religado " + depois);
            });
            Caso("prévia de perfil toca com o som desligado (3 toques de descer + 3 de subir)", () =>
            {
                SomTeclado.Alterar(a => a.Ligado = false); Thread.Sleep(200);
                long n0 = mx.Iniciadas; SomTeclado.Previa("cristal"); Thread.Sleep(700);
                int v = (int)(mx.Iniciadas - n0);
                SomTeclado.Alterar(a => a.Ligado = true); Thread.Sleep(300);
                return Se(v == 6, v + " sons", "6 sons em ~400 ms");
            });
            Caso("editor ao vivo: salvar refaz o perfil sem parar o áudio; perfil próprio toca; restaurar e excluir voltam atrás", () =>
            {
                var erros = new List<string>();
                var pronto = new AutoResetEvent(false);
                var antes = SomTeclado.PerfilPorId("cristal");
                SomTeclado.SalvarEdicao(new EdicaoSom { Id = "cristal", Base = "cristal", Tom = 80 }, () => pronto.Set());
                if (!pronto.WaitOne(3000)) return "salvar não terminou";
                var depois = SomTeclado.PerfilPorId("cristal");
                if (depois == null || !depois.Editado || depois == antes) erros.Add("cristal não foi refeito");
                SomTeclado.SalvarEdicao(new EdicaoSom { Id = "meu-som-9", Base = "cristal", Nome = "Teste ao vivo" }, () => pronto.Set());
                pronto.WaitOne(3000);
                if (SomTeclado.PerfilPorId("meu-som-9") == null) erros.Add("perfil próprio não entrou");
                SomTeclado.Alterar(a => a.Ligado = false); Thread.Sleep(200);
                long n0 = mx.Iniciadas; SomTeclado.PreviaGrupo("meu-som-9", SomTeclado.GEspaco); Thread.Sleep(400);
                if (mx.Iniciadas - n0 != 2) erros.Add("prévia do espaço tocou " + (mx.Iniciadas - n0));
                n0 = mx.Iniciadas; SomTeclado.TesteTecla("meu-som-9", 0x41, true); SomTeclado.TesteTecla("meu-som-9", 0x41, false); Thread.Sleep(300);
                if (mx.Iniciadas - n0 != 2) erros.Add("caixa de teste tocou " + (mx.Iniciadas - n0));
                SomTeclado.Alterar(a => a.Perfil = "meu-som-9");
                SomTeclado.ApagarEdicao("meu-som-9", () => pronto.Set()); pronto.WaitOne(3000);
                if (SomTeclado.PerfilPorId("meu-som-9") != null) erros.Add("excluir não tirou");
                if (SomTeclado.Atual.Perfil != "cristal") erros.Add("perfil em uso não voltou à base (" + SomTeclado.Atual.Perfil + ")");
                SomTeclado.ApagarEdicao("cristal", () => pronto.Set()); pronto.WaitOne(3000);
                var r = SomTeclado.PerfilPorId("cristal");
                if (r == null || r.Editado) erros.Add("restaurar não voltou ao original");
                if (File.Exists(Path.Combine(EdicaoSom.Pasta, "cristal.txt")) || File.Exists(Path.Combine(EdicaoSom.Pasta, "meu-som-9.txt"))) erros.Add("arquivos ficaram");
                SomTeclado.Alterar(a => a.Ligado = true); Thread.Sleep(300);
                return Se(erros.Count == 0, string.Join("; ", erros), SomTeclado.Perfis.Length + " perfis no fim");
            });
            Caso("volume: 0 mudo; 100 mais alto que 30 (pico escrito no buffer)", () =>
            {
                Func<int, float> pico = v => { SomTeclado.Alterar(a => a.Volume = v); Thread.Sleep(150); mx.Pico = 0; tocar(0x87, 50, 0, 0); return mx.Pico; };
                float p0 = pico(0), p30 = pico(30), p100 = pico(100);
                SomTeclado.Alterar(a => a.Volume = 70);
                return Se(p0 < 1e-6f && p100 > p30 * 5 && p100 <= 1, "picos " + F(p0, "0.000") + "/" + F(p30, "0.000") + "/" + F(p100, "0.000"), "pico com volume 0 → " + F(p0, "0.000") + ", 30 → " + F(p30, "0.000") + ", 100 → " + F(p100, "0.000"));
            });
            Caso("rajada de 30 teclas/s por 2 s: nada se perde", () =>
            {
                Thread.Sleep(300); int antes = SomTeclado.Contados, desc = SomTeclado.Descartados; long n0 = mx.Iniciadas;
                for (int i = 0; i < 60; i++) { Tecla(0x87, false, 0); Thread.Sleep(12); Tecla(0x87, true, 0); Thread.Sleep(21); }
                Thread.Sleep(300);
                int ev = SomTeclado.Contados - antes, v = (int)(mx.Iniciadas - n0);
                return Se(ev == 120 && v == 120 && SomTeclado.Descartados == desc, "eventos " + ev + "/120, sons " + v + ", descartados " + (SomTeclado.Descartados - desc), "120 eventos, 120 sons");
            });
            Caso("captura do que sai no Windows (loopback), para conferir fora do motor", () =>
            {
                try { ouv = new Ouvinte(); }
                catch (Exception e) { return "pulado: o Windows recusou a captura loopback neste ambiente de teste (" + e.Message.Split('\n')[0].Trim() + ")"; }
                Thread.Sleep(400); ouv.Zerar(); long t0 = Agora100ns();
                tocar(0x87, 50, 0, 0); Thread.Sleep(200);
                var at = ouv.Desde(t0 - 20000);
                return Se(at.Count >= 1, "nada captado", at.Count + " ataques captados, primeiro a " + (at.Count > 0 ? F((at[0] - t0) / 10000.0) : "?") + " ms");
            });
            Caso("atalho Ctrl+Alt+K liga e desliga", () =>
            {
                if (!SomTeclado.AtalhoRegistrado) return "pulado: o atalho já está registrado por outro programa (o Mac Skin instalado, se estiver aberto)";
                bool antes = SomTeclado.Atual.Ligado;
                Combinacao(new ushort[] { 0xA2, 0xA4 }, 0x4B); Thread.Sleep(400);
                bool depois1 = SomTeclado.Atual.Ligado;
                Combinacao(new ushort[] { 0xA2, 0xA4 }, 0x4B); Thread.Sleep(400);
                bool depois2 = SomTeclado.Atual.Ligado;
                return Se(depois1 != antes && depois2 == antes, "antes " + antes + ", 1º " + depois1 + ", 2º " + depois2);
            });
            Caso("campo de senha detectado pelo foco (caixa de texto Win32 com ES_PASSWORD)", () => TestarSenha());
            Caso("campo de senha detectado numa janela WPF (PasswordBox)", () => TestarSenhaWpf());
            Caso("campo de senha detectado no navegador (Edge, perfil temporário)", () => { SomTeclado.DepurarFoco = args_depurar; try { return TestarSenhaNavegador(dados); } finally { SomTeclado.DepurarFoco = false; } });
            Caso("página de login que já abre com foco na senha (Edge)", () => TestarSenhaNavegador(dados, true));
            Caso("apps Electron (VS Code etc.) não são consultados; navegadores sim", () =>
            {
                var erros = new List<string>(); int vistos = 0;
                foreach (var p in Process.GetProcesses())
                {
                    IntPtr h; try { h = p.MainWindowHandle; } catch { continue; }
                    if (h == IntPtr.Zero || !Win32.Classe(h).StartsWith("Chrome_")) continue;
                    vistos++;
                    bool nav = SomTeclado.Navegador(h), esperado = new[] { "msedge", "chrome", "brave", "opera", "vivaldi" }.Contains(p.ProcessName.ToLowerInvariant());
                    if (nav != esperado) erros.Add(p.ProcessName);
                }
                if (vistos == 0) return "pulado: nenhuma janela Chromium aberta para conferir";
                return Se(erros.Count == 0, "classificados errado: " + string.Join(", ", erros), vistos + " janelas Chromium conferidas");
            });
            Caso("gancho de mouse só com clique/rolagem ligados", () =>
            {
                bool antes = SomTeclado.GanchoMouse;
                SomTeclado.Alterar(a => a.Mouse = true); Thread.Sleep(300); bool com = SomTeclado.GanchoMouse;
                SomTeclado.Alterar(a => { a.Mouse = false; a.Rolagem = true; }); Thread.Sleep(300); bool rol = SomTeclado.GanchoMouse;
                SomTeclado.Alterar(a => a.Rolagem = false); Thread.Sleep(300); bool depois = SomTeclado.GanchoMouse;
                return Se(!antes && com && rol && !depois, "antes " + antes + ", mouse " + com + ", rolagem " + rol + ", depois " + depois);
            });
            Caso("trocar a saída de áudio reabre no dispositivo escolhido", () =>
            {
                var saidas = Wasapi.Saidas();
                if (saidas.Count == 0) return "nenhuma saída listada";
                var alvo = saidas[saidas.Count - 1];
                SomTeclado.Alterar(a => a.Saida = alvo.Key); Thread.Sleep(200); SomTeclado.Clique(); Thread.Sleep(500);
                var s = SomTeclado.SaidaAtual; bool certo = s != null && s.Id == alvo.Key;
                SomTeclado.Alterar(a => a.Saida = ""); Thread.Sleep(200); SomTeclado.Clique(); Thread.Sleep(500);
                var s2 = SomTeclado.SaidaAtual;
                return Se(certo && s2 != null, "não abriu em " + alvo.Value, saidas.Count + " saídas; abriu em \"" + alvo.Value + "\" e voltou ao padrão");
            });
            Caso("saída escolhida que não existe cai no padrão", () =>
            {
                SomTeclado.Alterar(a => a.Saida = "{nao-existe}"); Thread.Sleep(200); SomTeclado.Clique(); Thread.Sleep(500);
                var s = SomTeclado.SaidaAtual; bool ok2 = s != null;
                SomTeclado.Alterar(a => a.Saida = ""); Thread.Sleep(200);
                return Se(ok2, "não abriu nada: " + SomTeclado.UltimoErro);
            });
            Caso("ajustes gravados no arquivo depois de mudar", () =>
            {
                SomTeclado.Alterar(a => a.Perfil = "seda"); Thread.Sleep(900);
                var lido = SomTeclado.Ajustes.Ler(Path.Combine(dados, "som-teclado.txt"), null);
                SomTeclado.Alterar(a => a.Perfil = "grafite");
                return Se(lido.Perfil == "seda", "arquivo tem perfil=" + lido.Perfil);
            });
            Caso("contagem de teclas de hoje", () => Se(SomTeclado.TeclasHoje > 0, "zero", SomTeclado.TeclasHoje + " teclas contadas"));
            Caso("CPU parado (som ligado, sem digitar): abaixo de 0,5% de um núcleo", () =>
            {
                Thread.Sleep(2500);   // o fluxo de áudio para depois de 1,5 s quieto
                var p = Process.GetCurrentProcess(); var t0 = p.TotalProcessorTime; var sw = Stopwatch.StartNew();
                Thread.Sleep(5000); p.Refresh();
                double pct = (p.TotalProcessorTime - t0).TotalMilliseconds / sw.Elapsed.TotalMilliseconds * 100;
                return Se(pct < 0.5, F(pct, "0.00") + "%", F(pct, "0.00") + "% (processo de teste inteiro)");
            });
            Caso("CPU digitando 10 teclas/s: abaixo de 3% de um núcleo", () =>
            {
                var p = Process.GetCurrentProcess(); var t0 = p.TotalProcessorTime; var sw = Stopwatch.StartNew();
                for (int i = 0; i < 40; i++) { Tecla(0x87, false, 0); Thread.Sleep(40); Tecla(0x87, true, 0); Thread.Sleep(60); }
                p.Refresh();
                double pct = (p.TotalProcessorTime - t0).TotalMilliseconds / sw.Elapsed.TotalMilliseconds * 100;
                return Se(pct < 3, F(pct, "0.00") + "%", F(pct, "0.00") + "%" + (ouv != null ? " (inclui a captura de teste)" : ""));
            });
            if (ouv != null) ouv.Dispose();
            SomTeclado.Parar();
            Caso("parar encerra as threads e solta o gancho", () => Se(!SomTeclado.GanchoTeclado, "gancho ficou"));
        }

        // caixa de senha real: uma janela própria com uma caixa normal e uma de senha (ES_PASSWORD); o foco muda por SetFocus
        static string TestarSenha()
        {
            string r = null; var pronto = new ManualResetEvent(false);
            var t = new Thread(() =>
            {
                try
                {
                    var f = new System.Windows.Forms.Form { Text = "Mac Skin — teste do som de teclado", Width = 360, Height = 160, StartPosition = System.Windows.Forms.FormStartPosition.Manual, Left = 40, Top = 60, ShowInTaskbar = false, TopMost = true };
                    var normal = new System.Windows.Forms.TextBox { Left = 10, Top = 10, Width = 300 };
                    var senha = new System.Windows.Forms.TextBox { Left = 10, Top = 50, Width = 300, UseSystemPasswordChar = true };
                    f.Controls.Add(normal); f.Controls.Add(senha);
                    f.Shown += delegate
                    {
                        var tm = new System.Windows.Forms.Timer { Interval = 50 }; int passo = 0; var res = new List<string>();
                        IntPtr frente = Win32.GetForegroundWindow();
                        tm.Tick += delegate
                        {
                            passo++;
                            if (passo == 1) { keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); SetForegroundWindow(f.Handle); f.Activate(); }
                            if (passo == 4) { if (Win32.GetForegroundWindow() != f.Handle) res.Add("janela de teste não veio para a frente"); senha.Focus(); }
                            if (passo == 12) { res.Add("senha:" + SomTeclado.EmSenha()); normal.Focus(); }
                            if (passo == 20) { res.Add("normal:" + SomTeclado.EmSenha()); senha.Focus(); }
                            if (passo == 21) res.Add("logo depois de focar:" + SomTeclado.EmSenha());
                            if (passo == 30)
                            {
                                res.Add("senha2:" + SomTeclado.EmSenha());
                                tm.Stop(); f.Close();
                                if (frente != IntPtr.Zero) SetForegroundWindow(frente);
                                string s = string.Join(", ", res);
                                if (res.Any(x => x.StartsWith("janela"))) r = "pulado: " + s;
                                else r = s == "senha:True, normal:False, logo depois de focar:True, senha2:True" ? "ok: senha → calado, caixa normal → toca" : s;
                            }
                        };
                        tm.Start();
                    };
                    System.Windows.Forms.Application.Run(f);
                }
                catch (Exception e) { r = "exceção: " + e.Message; }
                finally { pronto.Set(); }
            });
            t.SetApartmentState(ApartmentState.STA); t.Start();
            pronto.WaitOne(10000);
            return r ?? "sem resposta";
        }

        static string TestarSenhaWpf()
        {
            string r = null; var pronto = new ManualResetEvent(false);
            var t = new Thread(() =>
            {
                try
                {
                    var w = new System.Windows.Window { Title = "Mac Skin — teste WPF", Width = 360, Height = 160, Left = 40, Top = 60, Topmost = true, ShowInTaskbar = false };
                    var pilha = new System.Windows.Controls.StackPanel();
                    var normal = new System.Windows.Controls.TextBox { Margin = new System.Windows.Thickness(8) };
                    var senha = new System.Windows.Controls.PasswordBox { Margin = new System.Windows.Thickness(8) };
                    pilha.Children.Add(normal); pilha.Children.Add(senha); w.Content = pilha;
                    IntPtr frente = Win32.GetForegroundWindow();
                    var res = new List<string>(); int passo = 0;
                    var tm = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                    tm.Tick += delegate
                    {
                        passo++;
                        var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                        if (passo == 1) { keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); SetForegroundWindow(h); w.Activate(); }
                        if (passo == 4) { if (Win32.GetForegroundWindow() != h) res.Add("janela de teste não veio para a frente"); senha.Focus(); }
                        if (passo == 14) { res.Add("senha:" + SomTeclado.EmSenha()); normal.Focus(); }
                        if (passo == 24) { res.Add("normal:" + SomTeclado.EmSenha()); }
                        if (passo == 25)
                        {
                            tm.Stop(); w.Close();
                            if (frente != IntPtr.Zero) SetForegroundWindow(frente);
                            string s = string.Join(", ", res);
                            r = res.Any(x => x.StartsWith("janela")) ? "pulado: " + s : s == "senha:True, normal:False" ? "ok: PasswordBox → calado, TextBox → toca" : s;
                            System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
                        }
                    };
                    w.Show(); tm.Start();
                    System.Windows.Threading.Dispatcher.Run();
                }
                catch (Exception e) { r = "exceção: " + e.Message; }
                finally { pronto.Set(); }
            });
            t.SetApartmentState(ApartmentState.STA); t.Start();
            pronto.WaitOne(10000);
            return r ?? "sem resposta";
        }

        // página local num Edge com perfil temporário (não toca no navegador do usuário). Com a janela do teste na frente,
        // Tab vai da caixa normal para a de senha e Shift+Tab volta, como uma pessoa faria.
        static string TestarSenhaNavegador(string dados) { return TestarSenhaNavegador(dados, false); }
        static string TestarSenhaNavegador(string dados, bool abreNaSenha)
        {
            string edge = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
            if (!File.Exists(edge)) return "pulado: Edge não encontrado";
            string perfil = Path.Combine(dados, "edge-perfil"), pagina = Path.Combine(dados, "senha.html");
            File.WriteAllText(pagina, abreNaSenha
                ? "<!doctype html><meta charset=utf-8><title>macskin-teste-senha</title>Usuário <input id=a> Senha <input id=b type=password autofocus>"
                : "<!doctype html><meta charset=utf-8><title>macskin-teste-senha</title><input id=a autofocus> <input id=b type=password>", new UTF8Encoding(false));
            IntPtr antes = Win32.GetForegroundWindow();
            var res = new List<string>();
            try
            {
                Process.Start(edge, "--user-data-dir=\"" + perfil + "\" --no-first-run --no-default-browser-check --disable-sync --window-position=40,60 --window-size=460,220 --app=\"file:///" + pagina.Replace(Path.DirectorySeparatorChar, '/') + "\"");
                IntPtr jan = IntPtr.Zero; var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 8000 && jan == IntPtr.Zero) { Thread.Sleep(200); jan = AcharJanela("macskin-teste-senha"); }
                if (jan == IntPtr.Zero) return "pulado: a janela do Edge de teste não abriu";
                Thread.Sleep(1200);
                keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); SetForegroundWindow(jan);
                Thread.Sleep(1500);
                Func<bool> naFrente = () => Win32.GetForegroundWindow() == jan;
                if (!naFrente()) return "pulado: não consegui trazer a janela do Edge de teste para a frente (Windows bloqueou a troca de foco)";
                if (abreNaSenha) { res.Add("abriu na senha:" + SomTeclado.EmSenha()); goto fim; }
                res.Add("normal:" + SomTeclado.EmSenha());
                if (!naFrente()) return "pulado: perdeu a frente";
                Tecla(0x09, false, 0); Tecla(0x09, true, 0); Thread.Sleep(1200);
                res.Add("senha:" + SomTeclado.EmSenha());
                if (!naFrente()) return "pulado: perdeu a frente";
                Tecla(0x10, false, 0); Tecla(0x09, false, 0); Tecla(0x09, true, 0); Tecla(0x10, true, 0); Thread.Sleep(1200);
                res.Add("normal2:" + SomTeclado.EmSenha());
            fim: ;
            }
            finally
            {
                try
                {
                    foreach (var q in Process.GetProcessesByName("msedge"))
                        try
                        {
                            using (var s = new System.Management.ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId=" + q.Id))
                                foreach (System.Management.ManagementObject o in s.Get())
                                    if (((o["CommandLine"] as string) ?? "").Contains(perfil)) q.Kill();
                        }
                        catch { }
                }
                catch { }
                if (antes != IntPtr.Zero) SetForegroundWindow(antes);
            }
            string r = string.Join(", ", res);
            if (abreNaSenha) return Se(r == "abriu na senha:True", r, "página de login que já abre com foco na senha → calado");
            return Se(r == "normal:False, senha:True, normal2:False", r, "caixa normal → toca, Tab para a senha → calado, Shift+Tab → toca");
        }

        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        static IntPtr AcharJanela(string titulo)
        {
            IntPtr achou = IntPtr.Zero;
            EnumWindows((h, l) => { if (Win32.IsWindowVisible(h) && Win32.Titulo(h).Contains(titulo)) { achou = h; return false; } return true; }, IntPtr.Zero);
            return achou;
        }

        // ---------- teclas de teste ----------
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public KEYBDINPUT ki; public ulong pad; }
        [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] i, int tam);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        static void Tecla(ushort vk, bool solta, long extra)
        {
            var i = new INPUT { type = 1 }; i.ki.vk = vk; i.ki.flags = solta ? 2u : 0u; i.ki.extra = new IntPtr(extra);
            SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT)));
        }
        static void Combinacao(ushort[] mods, ushort vk)
        {
            foreach (var m in mods) Tecla(m, false, 0);
            Tecla(vk, false, 0); Thread.Sleep(30); Tecla(vk, true, 0);
            for (int k = mods.Length - 1; k >= 0; k--) Tecla(mods[k], true, 0);
        }
    }
}
