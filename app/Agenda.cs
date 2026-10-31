// Mac Skin Agenda: compromissos do Outlook (clássico) para o calendário da barra.
// Só lê quando o Outlook já está aberto (nunca o abre) e guarda uma cópia em agenda.txt, usada com o Outlook fechado.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Threading;

namespace MacSkin
{
    class Compromisso
    {
        public DateTime Ini, Fim; public bool DiaTodo; public string Cor, Titulo, Local;
        // dias que o compromisso ocupa (um evento de dia inteiro termina à meia-noite do dia seguinte)
        public IEnumerable<DateTime> Dias()
        {
            var ultimo = (Fim > Ini ? Fim.AddSeconds(-1) : Ini).Date;
            for (var d = Ini.Date; d <= ultimo; d = d.AddDays(1)) yield return d;
        }
    }

    static class Agenda
    {
        public const string Azul = "#0A84FF", Laranja = "#FF9F0A";
        static List<Compromisso> itens = Carregar();
        static Dispatcher ui; static int lendo; static DispatcherTimer t;
        public static List<Compromisso> Itens { get { return itens; } }
        public static event Action Mudou;   // sempre na thread da interface

        static string Arq { get { return Path.Combine(Comum.Dir, "agenda.txt"); } }
        static void Log(string m) { Comum.Log("barra.log", "agenda: " + m); }

        // relê a cada 10 min (só se o Outlook estiver aberto) e quando o calendário abre
        public static void Iniciar(Dispatcher d)
        {
            ui = d;
            t = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
            t.Tick += delegate { Atualizar(); }; t.Start();
            Atualizar();
        }

        public static void Atualizar()
        {
            if (ui == null || Interlocked.Exchange(ref lendo, 1) == 1) return;
            var th = new Thread(() =>
            {
                try
                {
                    var novo = Ler();
                    if (novo == null) return;
                    string antes = Texto(itens), depois = Texto(novo);
                    if (antes == depois) return;
                    try { File.WriteAllText(Arq, depois, Encoding.UTF8); } catch { }
                    ui.BeginInvoke(new Action(() => { itens = novo; var m = Mudou; if (m != null) m(); }));
                }
                catch (Exception e) { Log(e.Message); }
                finally { Interlocked.Exchange(ref lendo, 0); }
            });
            th.IsBackground = true; th.SetApartmentState(ApartmentState.STA);   // COM do Outlook
            th.Start();
        }

        // cores das categorias do Outlook (OlCategoryColor 1..10); as demais ficam cinza
        static readonly string[] CoresCategoria = { null, "#FF453A", "#FF9F0A", "#FFB86B", "#FFD60A", "#30D158", "#40C8E0", "#8DB600", "#0A84FF", "#BF5AF2", "#C0567F" };

        static List<Compromisso> Ler()
        {
            if (Process.GetProcessesByName("OUTLOOK").Length == 0) return null;   // fechado: não abre, usa a cópia
            dynamic o;
            try { o = Marshal.GetActiveObject("Outlook.Application"); }
            catch { return null; }
            var cores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (dynamic c in o.Session.Categories)
                {
                    int n = c.Color;
                    cores[(string)c.Name] = n >= 1 && n < CoresCategoria.Length ? CoresCategoria[n] : "#8E8E93";
                }
            }
            catch { }
            dynamic itensOl = o.Session.GetDefaultFolder(9).Items;   // olFolderCalendar
            itensOl.IncludeRecurrences = true;
            itensOl.Sort("[Start]");
            var hoje = DateTime.Today;
            var ini = new DateTime(hoje.Year, hoje.Month, 1).AddMonths(-1);
            var fim = ini.AddMonths(5);
            string filtro = string.Format("[Start] >= '{0}' AND [Start] < '{1}'", ini.ToString("g"), fim.ToString("g"));
            var r = new List<Compromisso>();
            foreach (dynamic e in itensOl.Restrict(filtro))
            {
                try
                {
                    int status = e.MeetingStatus;
                    if (status == 5 || status == 7) continue;   // cancelada
                    string titulo = (e.Subject as string ?? "").Trim();
                    if (titulo.StartsWith("Cancelado:", StringComparison.OrdinalIgnoreCase) || titulo.StartsWith("Canceled:", StringComparison.OrdinalIgnoreCase)) continue;
                    bool diaTodo = e.AllDayEvent;
                    string cat = (e.Categories as string ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).FirstOrDefault();
                    string cor;
                    if (cat == null || !cores.TryGetValue(cat, out cor)) cor = diaTodo ? Laranja : Azul;
                    r.Add(new Compromisso { Ini = e.Start, Fim = e.End, DiaTodo = diaTodo, Cor = cor, Titulo = titulo, Local = (e.Location as string ?? "").Trim() });
                }
                catch { }
                if (r.Count > 2000) break;
            }
            return r.OrderBy(x => x.Ini).ToList();
        }

        // ---------- cópia em arquivo ----------
        const string Fmt = "yyyy-MM-ddTHH:mm";
        static string Limpo(string s) { return (s ?? "").Replace("|", "/").Replace("\r", " ").Replace("\n", " "); }

        static string Texto(List<Compromisso> l)
        {
            var sb = new StringBuilder();
            foreach (var c in l)
                sb.Append(c.Ini.ToString(Fmt)).Append('|').Append(c.Fim.ToString(Fmt)).Append('|').Append(c.DiaTodo ? '1' : '0').Append('|')
                  .Append(c.Cor).Append('|').Append(Limpo(c.Local)).Append('|').Append(Limpo(c.Titulo)).Append('\n');
            return sb.ToString();
        }

        static List<Compromisso> Carregar()
        {
            var r = new List<Compromisso>();
            try
            {
                if (!File.Exists(Arq)) return r;
                foreach (var linha in File.ReadAllLines(Arq, Encoding.UTF8))
                {
                    var p = linha.Split(new[] { '|' }, 6);
                    if (p.Length < 6) continue;
                    r.Add(new Compromisso
                    {
                        Ini = DateTime.ParseExact(p[0], Fmt, CultureInfo.InvariantCulture), Fim = DateTime.ParseExact(p[1], Fmt, CultureInfo.InvariantCulture),
                        DiaTodo = p[2] == "1", Cor = p[3], Local = p[4], Titulo = p[5]
                    });
                }
            }
            catch (Exception e) { Log("cópia: " + e.Message); }
            return r;
        }
    }
}
