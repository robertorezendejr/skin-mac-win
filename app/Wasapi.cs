// Mac Skin: saída de áudio de baixa latência (WASAPI compartilhado, orientado a evento) e captura "loopback" para os testes.
// Só COM do Windows, sem dependência. Usado pelo Som de teclado: o SoundPlayer/MediaPlayer do .NET levam 50 a 150 ms e
// cortam o som anterior; aqui o atraso é o período do motor de áudio (3 a 10 ms) e a mistura é nossa.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MacSkin
{
    static class Wasapi
    {
        // ---------- dispositivos ----------
        // pelo CLSID, sem "new" da classe: o Volume.cs declara a mesma classe COM, e o .NET devolve o objeto (único no
        // processo) com o tipo de quem o criou primeiro; a conversão para a classe daqui falhava. Para a interface, funciona.
        static readonly Type tipoEnumerador = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
        public static IMMDeviceEnumerator Enumerador() { return (IMMDeviceEnumerator)Activator.CreateInstance(tipoEnumerador); }

        public static IMMDevice Dispositivo(string id)
        {
            var en = Enumerador();
            IMMDevice d = null;
            if (!string.IsNullOrEmpty(id)) { try { en.GetDevice(id, out d); int st; d.GetState(out st); if (st != 1) d = null; } catch { d = null; } }
            if (d == null) en.GetDefaultAudioEndpoint(0, 1, out d);   // saída, multimídia
            return d;
        }

        public static string Id(IMMDevice d) { string s; d.GetId(out s); return s; }

        public static string Nome(IMMDevice d)
        {
            try
            {
                IPropertyStore ps; d.OpenPropertyStore(0, out ps);
                var chave = new PROPERTYKEY { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 };   // PKEY_Device_FriendlyName
                PROPVARIANT v; ps.GetValue(ref chave, out v);
                string n = v.vt == 31 ? Marshal.PtrToStringUni(v.p) : "";
                PropVariantClear(ref v);
                return n ?? "";
            }
            catch { return ""; }
        }

        // saídas ativas: (id, nome)
        public static List<KeyValuePair<string, string>> Saidas()
        {
            var r = new List<KeyValuePair<string, string>>();
            try
            {
                IMMDeviceCollection col; Enumerador().EnumAudioEndpoints(0, 1, out col);
                uint n; col.GetCount(out n);
                for (uint i = 0; i < n; i++) { IMMDevice d; col.Item(i, out d); r.Add(new KeyValuePair<string, string>(Id(d), Nome(d))); }
            }
            catch { }
            return r;
        }

        // ---------- formato ----------
        public class Formato
        {
            public int Taxa, Canais, Bits; public bool Float;
            public static Formato De(IntPtr p)
            {
                var f = new Formato();
                int tag = Marshal.ReadInt16(p, 0) & 0xFFFF;
                f.Canais = Marshal.ReadInt16(p, 2); f.Taxa = Marshal.ReadInt32(p, 4); f.Bits = Marshal.ReadInt16(p, 14);
                if (tag == 3) f.Float = true;
                else if (tag == 0xFFFE)
                {
                    var b = new byte[16]; Marshal.Copy(p + 24, b, 0, 16);
                    f.Float = new Guid(b) == new Guid("00000003-0000-0010-8000-00AA00389B71");   // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT
                }
                return f;
            }
            public override string ToString() { return Taxa + " Hz, " + Canais + " canais, " + Bits + " bits" + (Float ? " float" : " inteiro"); }
        }

        // ---------- saída ----------
        public class Saida : IDisposable
        {
            IAudioClient cli; IAudioClient3 cli3; IAudioRenderClient render;
            public Formato Fmt; public uint Buffer, Periodo; public IntPtr Evento; public string Id, Nome; public bool BaixaLatencia;
            float[] tmpF; short[] tmpS; int[] tmpI;

            public static Saida Abrir(string id)
            {
                var s = new Saida();
                var dev = Dispositivo(id);
                s.Id = Wasapi.Id(dev); s.Nome = Nome(dev);
                s.Evento = CreateEvent(IntPtr.Zero, false, false, null);
                IntPtr mix = IntPtr.Zero;
                try
                {
                    object o = null;
                    var g3 = typeof(IAudioClient3).GUID;
                    try { dev.Activate(ref g3, 23, IntPtr.Zero, out o); s.cli3 = (IAudioClient3)o; } catch { s.cli3 = null; }
                    if (s.cli3 != null)
                    {
                        s.cli3.GetMixFormat(out mix);
                        uint padrao, fund, min, max;
                        s.cli3.GetSharedModeEnginePeriod(mix, out padrao, out fund, out min, out max);
                        try { s.cli3.InitializeSharedAudioStream(0x40000 /* EVENTCALLBACK */, min, mix, IntPtr.Zero); s.Periodo = min; s.BaixaLatencia = min < padrao; }
                        catch { Marshal.ReleaseComObject(s.cli3); s.cli3 = null; Marshal.FreeCoTaskMem(mix); mix = IntPtr.Zero; }
                    }
                    if (s.cli3 == null)
                    {
                        var g = typeof(IAudioClient).GUID;
                        dev.Activate(ref g, 23, IntPtr.Zero, out o); s.cli = (IAudioClient)o;
                        s.cli.GetMixFormat(out mix);
                        long padrao, min; s.cli.GetDevicePeriod(out padrao, out min);
                        s.cli.Initialize(0, 0x40000, 0, 0, mix, IntPtr.Zero);
                        s.Periodo = (uint)(padrao * Formato.De(mix).Taxa / 10000000);
                    }
                    s.Fmt = Formato.De(mix);
                    if (!s.Fmt.Float && s.Fmt.Bits != 16 && s.Fmt.Bits != 32) throw new NotSupportedException("formato " + s.Fmt);
                    object rc; var gr = typeof(IAudioRenderClient).GUID;
                    if (s.cli3 != null) { s.cli3.SetEventHandle(s.Evento); s.cli3.GetBufferSize(out s.Buffer); s.cli3.GetService(ref gr, out rc); }
                    else { s.cli.SetEventHandle(s.Evento); s.cli.GetBufferSize(out s.Buffer); s.cli.GetService(ref gr, out rc); }
                    s.render = (IAudioRenderClient)rc;
                    int n = (int)s.Buffer * s.Fmt.Canais;
                    if (s.Fmt.Float) s.tmpF = new float[n]; else if (s.Fmt.Bits == 16) s.tmpS = new short[n]; else s.tmpI = new int[n];
                    return s;
                }
                catch { s.Dispose(); throw; }
                finally { if (mix != IntPtr.Zero) Marshal.FreeCoTaskMem(mix); }
            }

            public uint Livre()
            {
                uint pad;
                if (cli3 != null) cli3.GetCurrentPadding(out pad); else cli.GetCurrentPadding(out pad);
                return Buffer - pad;
            }
            public void Iniciar() { if (cli3 != null) cli3.Start(); else cli.Start(); }
            public void Parar() { if (cli3 != null) { cli3.Stop(); cli3.Reset(); } else { cli.Stop(); cli.Reset(); } }
            public long LatenciaHns() { long l = 0; try { if (cli3 != null) cli3.GetStreamLatency(out l); else cli.GetStreamLatency(out l); } catch { } return l; }

            // entrega n quadros de esquerda/direita (float -1..1) espalhados nos canais do dispositivo
            public void Escrever(float[] esq, float[] dir, int n)
            {
                IntPtr p; render.GetBuffer((uint)n, out p);
                int c = Fmt.Canais, k = 0;
                if (tmpF != null)
                {
                    for (int i = 0; i < n; i++) { if (c == 1) tmpF[k++] = (esq[i] + dir[i]) * 0.5f; else { tmpF[k++] = esq[i]; tmpF[k++] = dir[i]; for (int j = 2; j < c; j++) tmpF[k++] = 0; } }
                    Marshal.Copy(tmpF, 0, p, k);
                }
                else if (tmpS != null)
                {
                    for (int i = 0; i < n; i++) { if (c == 1) tmpS[k++] = S16((esq[i] + dir[i]) * 0.5f); else { tmpS[k++] = S16(esq[i]); tmpS[k++] = S16(dir[i]); for (int j = 2; j < c; j++) tmpS[k++] = 0; } }
                    Marshal.Copy(tmpS, 0, p, k);
                }
                else
                {
                    for (int i = 0; i < n; i++) { if (c == 1) tmpI[k++] = S32((esq[i] + dir[i]) * 0.5f); else { tmpI[k++] = S32(esq[i]); tmpI[k++] = S32(dir[i]); for (int j = 2; j < c; j++) tmpI[k++] = 0; } }
                    Marshal.Copy(tmpI, 0, p, k);
                }
                render.ReleaseBuffer((uint)n, 0);
            }
            public void Silencio(int n) { IntPtr p; render.GetBuffer((uint)n, out p); render.ReleaseBuffer((uint)n, 2 /* SILENT */); }
            static short S16(float v) { return (short)(v >= 1 ? 32767 : v <= -1 ? -32767 : v * 32767); }
            static int S32(float v) { return (int)(v >= 1 ? int.MaxValue : v <= -1 ? -int.MaxValue : v * 2147483647.0); }

            public void Dispose()
            {
                try { if (cli3 != null) cli3.Stop(); else if (cli != null) cli.Stop(); } catch { }
                if (render != null) { try { Marshal.ReleaseComObject(render); } catch { } render = null; }
                if (cli3 != null) { try { Marshal.ReleaseComObject(cli3); } catch { } cli3 = null; }
                if (cli != null) { try { Marshal.ReleaseComObject(cli); } catch { } cli = null; }
                if (Evento != IntPtr.Zero) { CloseHandle(Evento); Evento = IntPtr.Zero; }
            }
        }

        // ---------- captura do que está tocando (loopback), para medir o som nos testes ----------
        public class Captura : IDisposable
        {
            IAudioClient cli; IAudioCaptureClient cap; public Formato Fmt;
            public static Captura Abrir(string id)
            {
                var c = new Captura(); string passo = "dispositivo";
                try
                {
                    var dev = Dispositivo(id);
                    object o; var g = typeof(IAudioClient).GUID;
                    passo = "activate"; dev.Activate(ref g, 23, IntPtr.Zero, out o); c.cli = (IAudioClient)o;
                    passo = "mixformat"; IntPtr mix; c.cli.GetMixFormat(out mix);
                    try { c.Fmt = Formato.De(mix); passo = "initialize"; c.cli.Initialize(0, 0x20000 /* LOOPBACK */, 2000000, 0, mix, IntPtr.Zero); }
                    finally { Marshal.FreeCoTaskMem(mix); }
                    passo = "getservice"; object rc; var gc = typeof(IAudioCaptureClient).GUID; c.cli.GetService(ref gc, out rc); c.cap = (IAudioCaptureClient)rc;
                    passo = "start"; c.cli.Start();
                    return c;
                }
                catch (Exception e) { c.Dispose(); throw new Exception("captura (" + passo + "): 0x" + e.HResult.ToString("X8") + " " + e.Message, e); }
            }
            // devolve os pacotes disponíveis: para cada quadro, o maior |valor| entre os canais, e o QPC do 1º quadro do pacote
            public void Ler(Action<float[], int, long, bool> pacote)
            {
                uint prox; cap.GetNextPacketSize(out prox);
                while (prox > 0)
                {
                    IntPtr p; uint n, flags; ulong pos, qpc;
                    cap.GetBuffer(out p, out n, out flags, out pos, out qpc);
                    var pico = new float[n];
                    bool silencio = (flags & 2) != 0;
                    if (!silencio && Fmt.Float && Fmt.Bits == 32)
                    {
                        var tmp = new float[n * Fmt.Canais]; Marshal.Copy(p, tmp, 0, tmp.Length);
                        for (int i = 0; i < n; i++) { float m = 0; for (int j = 0; j < Fmt.Canais; j++) m = Math.Max(m, Math.Abs(tmp[i * Fmt.Canais + j])); pico[i] = m; }
                    }
                    else if (!silencio && Fmt.Bits == 16)
                    {
                        var tmp = new short[n * Fmt.Canais]; Marshal.Copy(p, tmp, 0, tmp.Length);
                        for (int i = 0; i < n; i++) { float m = 0; for (int j = 0; j < Fmt.Canais; j++) m = Math.Max(m, Math.Abs(tmp[i * Fmt.Canais + j] / 32768f)); pico[i] = m; }
                    }
                    cap.ReleaseBuffer(n);
                    // o qpc da captura vem em unidades de 100 ns
                    pacote(pico, (int)n, (long)qpc, (flags & 1) != 0);
                    cap.GetNextPacketSize(out prox);
                }
            }
            public void Dispose()
            {
                try { if (cli != null) cli.Stop(); } catch { }
                if (cap != null) { try { Marshal.ReleaseComObject(cap); } catch { } cap = null; }
                if (cli != null) { try { Marshal.ReleaseComObject(cli); } catch { } cli = null; }
            }
        }

        // ---------- aviso de troca do dispositivo padrão ----------
        [ComVisible(true)]
        public class Avisos : IMMNotificationClient
        {
            public Action AoMudarPadrao;
            public void OnDeviceStateChanged(string id, int st) { }
            public void OnDeviceAdded(string id) { }
            public void OnDeviceRemoved(string id) { }
            public void OnDefaultDeviceChanged(int fluxo, int papel, string id) { if (fluxo == 0 && papel == 1 && AoMudarPadrao != null) AoMudarPadrao(); }
            public void OnPropertyValueChanged(string id, PROPERTYKEY k) { }
        }

        // ---------- COM ----------

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMDeviceEnumerator
        {
            void EnumAudioEndpoints(int fluxo, int estados, out IMMDeviceCollection col);
            void GetDefaultAudioEndpoint(int fluxo, int papel, out IMMDevice dev);
            void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice dev);
            void RegisterEndpointNotificationCallback(IMMNotificationClient c);
            void UnregisterEndpointNotificationCallback(IMMNotificationClient c);
        }
        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMDeviceCollection { void GetCount(out uint n); void Item(uint i, out IMMDevice d); }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMDevice
        {
            void Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
            void OpenPropertyStore(int acesso, out IPropertyStore ps);
            void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            void GetState(out int estado);
        }
        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IPropertyStore
        {
            void GetCount(out int n); void GetAt(int i, out PROPERTYKEY k); void GetValue(ref PROPERTYKEY k, out PROPVARIANT v);
            void SetValue(ref PROPERTYKEY k, ref PROPVARIANT v); void Commit();
        }
        [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMNotificationClient
        {
            void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, int st);
            void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
            void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
            void OnDefaultDeviceChanged(int fluxo, int papel, [MarshalAs(UnmanagedType.LPWStr)] string id);
            void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PROPERTYKEY k);
        }
        [StructLayout(LayoutKind.Sequential)] public struct PROPERTYKEY { public Guid fmtid; public int pid; }
        [StructLayout(LayoutKind.Sequential)] public struct PROPVARIANT { public ushort vt, r1, r2, r3; public IntPtr p, p2; }
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT v);

        [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioClient
        {
            void Initialize(int modo, uint flags, long duracao, long periodo, IntPtr fmt, IntPtr sessao);
            void GetBufferSize(out uint n);
            void GetStreamLatency(out long l);
            void GetCurrentPadding(out uint n);
            [PreserveSig] int IsFormatSupported(int modo, IntPtr fmt, out IntPtr perto);
            void GetMixFormat(out IntPtr fmt);
            void GetDevicePeriod(out long padrao, out long minimo);
            void Start(); void Stop(); void Reset();
            void SetEventHandle(IntPtr h);
            void GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object o);
        }
        [ComImport, Guid("7ED4EE07-8E67-4CD4-8C1A-2B7A5987AD42"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioClient3
        {
            void Initialize(int modo, uint flags, long duracao, long periodo, IntPtr fmt, IntPtr sessao);
            void GetBufferSize(out uint n);
            void GetStreamLatency(out long l);
            void GetCurrentPadding(out uint n);
            [PreserveSig] int IsFormatSupported(int modo, IntPtr fmt, out IntPtr perto);
            void GetMixFormat(out IntPtr fmt);
            void GetDevicePeriod(out long padrao, out long minimo);
            void Start(); void Stop(); void Reset();
            void SetEventHandle(IntPtr h);
            void GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object o);
            // IAudioClient2
            void IsOffloadCapable(int categoria, out int sim);
            void SetClientProperties(IntPtr p);
            void GetBufferSizeLimits(IntPtr fmt, int evento, out long min, out long max);
            // IAudioClient3
            void GetSharedModeEnginePeriod(IntPtr fmt, out uint padrao, out uint fundamental, out uint min, out uint max);
            void GetCurrentSharedModeEnginePeriod(out IntPtr fmt, out uint periodo);
            void InitializeSharedAudioStream(uint flags, uint periodo, IntPtr fmt, IntPtr sessao);
        }
        [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioRenderClient { void GetBuffer(uint n, out IntPtr p); void ReleaseBuffer(uint n, uint flags); }
        [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioCaptureClient
        {
            void GetBuffer(out IntPtr p, out uint n, out uint flags, out ulong pos, out ulong qpc);
            void ReleaseBuffer(uint n);
            void GetNextPacketSize(out uint n);
        }

        [DllImport("kernel32.dll")] public static extern IntPtr CreateEvent(IntPtr a, bool manual, bool inicial, string nome);
        [DllImport("kernel32.dll")] public static extern bool SetEvent(IntPtr h);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] public static extern uint WaitForMultipleObjects(uint n, IntPtr[] h, bool todos, uint ms);
        [DllImport("avrt.dll", CharSet = CharSet.Unicode)] public static extern IntPtr AvSetMmThreadCharacteristics(string tarefa, ref uint indice);
        [DllImport("avrt.dll")] public static extern bool AvRevertMmThreadCharacteristics(IntPtr h);
    }
}
