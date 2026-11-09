// Mac Skin: SQLite local sem instalar nada (winsqlite3.dll já vem no Windows 10/11).
// Só consultas com parâmetros (nada de montar SQL com texto do usuário). Usado pela Substituição de texto.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace MacSkin
{
    sealed class BancoLocal : IDisposable
    {
        IntPtr db;
        public string Caminho { get; private set; }

        public BancoLocal(string caminho)
        {
            Caminho = caminho;
            int r = sqlite3_open_v2(Utf8(caminho), out db, 0x2 | 0x4 | 0x10000 /* READWRITE | CREATE | FULLMUTEX */, IntPtr.Zero);
            if (r != 0) { string e = Erro(); sqlite3_close_v2(db); db = IntPtr.Zero; throw new Exception("abrir " + caminho + ": " + e); }
            sqlite3_busy_timeout(db, 5000);
        }

        public void Exec(string sql, params object[] p)
        {
            IntPtr st = Preparar(sql, p);
            try { int r; while ((r = sqlite3_step(st)) == 100) { } if (r != 101) throw new Exception(Erro() + " em: " + sql); }
            finally { sqlite3_finalize(st); }
        }

        public object Escalar(string sql, params object[] p)
        {
            foreach (var l in Ler(sql, p)) return l[0];
            return null;
        }

        public List<object[]> Ler(string sql, params object[] p)
        {
            var res = new List<object[]>();
            IntPtr st = Preparar(sql, p);
            try
            {
                int r;
                while ((r = sqlite3_step(st)) == 100)
                {
                    int n = sqlite3_column_count(st);
                    var linha = new object[n];
                    for (int i = 0; i < n; i++)
                    {
                        switch (sqlite3_column_type(st, i))
                        {
                            case 1: linha[i] = sqlite3_column_int64(st, i); break;
                            case 3: linha[i] = Marshal.PtrToStringUni(sqlite3_column_text16(st, i)); break;
                            case 5: linha[i] = null; break;
                            default: linha[i] = Marshal.PtrToStringUni(sqlite3_column_text16(st, i)); break;
                        }
                    }
                    res.Add(linha);
                }
                if (r != 101) throw new Exception(Erro() + " em: " + sql);
            }
            finally { sqlite3_finalize(st); }
            return res;
        }

        public long UltimoId { get { return sqlite3_last_insert_rowid(db); } }

        IntPtr Preparar(string sql, object[] p)
        {
            IntPtr st, resto;
            if (sqlite3_prepare_v2(db, Utf8(sql), -1, out st, out resto) != 0) throw new Exception(Erro() + " em: " + sql);
            for (int i = 0; i < p.Length; i++)
            {
                object v = p[i]; int r;
                if (v == null) r = sqlite3_bind_null(st, i + 1);
                else if (v is string) r = sqlite3_bind_text16(st, i + 1, (string)v, -1, new IntPtr(-1) /* SQLITE_TRANSIENT */);
                else if (v is bool) r = sqlite3_bind_int64(st, i + 1, (bool)v ? 1 : 0);
                else r = sqlite3_bind_int64(st, i + 1, Convert.ToInt64(v));
                if (r != 0) { sqlite3_finalize(st); throw new Exception("parâmetro " + (i + 1) + ": " + Erro()); }
            }
            return st;
        }

        string Erro() { return db == IntPtr.Zero ? "?" : Marshal.PtrToStringUni(sqlite3_errmsg16(db)); }
        static byte[] Utf8(string s) { return Encoding.UTF8.GetBytes(s + "\0"); }

        public void Dispose() { if (db != IntPtr.Zero) { sqlite3_close_v2(db); db = IntPtr.Zero; } }

        const string Dll = "winsqlite3.dll";
        [DllImport(Dll, CallingConvention = CallingConvention.Winapi)] static extern int sqlite3_open_v2(byte[] arq, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(Dll, CallingConvention = CallingConvention.Winapi)] static extern int sqlite3_close_v2(IntPtr db);
        [DllImport(Dll, CallingConvention = CallingConvention.Winapi)] static extern int sqlite3_busy_timeout(IntPtr db, int ms);
        [DllImport(Dll, CallingConvention = CallingConvention.Winapi)] static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int n, out IntPtr st, out IntPtr resto);
        [DllImport(Dll, CallingConvention = CallingConvention.Winapi)] static extern int sqlite3_step(IntPtr st);
        [DllImport(Dll, CallingConvention = CallingConvention.Winapi)] static extern int sqlite3_finalize(IntPtr st);
        [DllImport(Dll, CallingConvention = CallingConvention.Winapi)] static extern int sqlite3_bind_null(IntPtr st, int i);
        [DllImport(Dll, CallingConvention = CallingConvention.Winapi)] static extern int sqlite3_bind_int64(IntPtr st, int i, long v);
        [DllImport(Dll, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode)] static extern int sqlite3_bind_text16(IntPtr st, int i, string v, int n, IntPtr destrutor);
        [DllImport(Dll, CallingConvention = CallingConvention.Winapi)] static extern int sqlite3_column_count(IntPtr st);
        [DllImport(Dll, CallingConvention = CallingConvention.Winapi)] static extern int sqlite3_column_type(IntPtr st, int i);
        [DllImport(Dll, CallingConvention = CallingConvention.Winapi)] static extern long sqlite3_column_int64(IntPtr st, int i);
        [DllImport(Dll, CallingConvention = CallingConvention.Winapi)] static extern IntPtr sqlite3_column_text16(IntPtr st, int i);
        [DllImport(Dll, CallingConvention = CallingConvention.Winapi)] static extern IntPtr sqlite3_errmsg16(IntPtr db);
        [DllImport(Dll, CallingConvention = CallingConvention.Winapi)] static extern long sqlite3_last_insert_rowid(IntPtr db);
    }
}
