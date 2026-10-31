// Mac Skin Vigia (nativo, em C): abre o MacSkin.exe no login e reabre se ele cair.
// Não consulta nada em intervalo: espera o Windows avisar que o processo terminou (ou que o mutex dele foi largado).
// "Desligar Mac Skin" cria o arquivo "desligado" na pasta; enquanto ele existir, o vigia não reabre.
// Compilar: gcc -O2 -s -mwindows -municode vigia.c -o MacSkinVigia.exe
#include <windows.h>
#include <stdio.h>
#include <wchar.h>

static wchar_t dir[MAX_PATH], exe[MAX_PATH], desligado[MAX_PATH], logArq[MAX_PATH];

static void Log(const wchar_t *msg)
{
    FILE *f = _wfopen(logArq, L"a, ccs=UTF-8");
    if (!f) return;
    SYSTEMTIME t; GetLocalTime(&t);
    fwprintf(f, L"%04d-%02d-%02d %02d:%02d:%02d %ls\n", t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute, t.wSecond, msg);
    fclose(f);
}

static BOOL Existe(const wchar_t *p) { return GetFileAttributesW(p) != INVALID_FILE_ATTRIBUTES; }

int WINAPI wWinMain(HINSTANCE a, HINSTANCE b, PWSTR c, int d)
{
    // uma instância só (o mesmo nome do vigia antigo em C#: os dois nunca rodam juntos)
    HANDLE eu = CreateMutexW(NULL, TRUE, L"Local\\MacSkinVigiaNativo");
    if (GetLastError() == ERROR_ALREADY_EXISTS) return 3;

    GetModuleFileNameW(NULL, dir, MAX_PATH);
    wchar_t *barra = wcsrchr(dir, L'\\'); if (barra) *barra = 0;
    swprintf(exe, MAX_PATH, L"%ls\\MacSkin.exe", dir);
    swprintf(desligado, MAX_PATH, L"%ls\\desligado", dir);
    swprintf(logArq, MAX_PATH, L"%ls\\vigia.log", dir);
    SetProcessWorkingSetSize(GetCurrentProcess(), (SIZE_T)-1, (SIZE_T)-1);

    for (;;)
    {
        if (Existe(desligado)) { Sleep(3000); continue; }

        // já aberto (ex.: aberto à mão)? espera o mutex dele ser largado, sem relançar nada
        HANDLE outro = OpenMutexW(SYNCHRONIZE, FALSE, L"Local\\MacSkinNativo");
        if (outro)
        {
            DWORD r = WaitForSingleObject(outro, INFINITE);   // WAIT_ABANDONED quando o processo some
            if (r == WAIT_OBJECT_0 || r == WAIT_ABANDONED) ReleaseMutex(outro);
            CloseHandle(outro);
            if (!Existe(desligado)) Log(L"Mac Skin fechou, reabrindo");
            Sleep(1500);
            continue;
        }

        STARTUPINFOW si = { sizeof si }; PROCESS_INFORMATION pi;
        wchar_t cmd[MAX_PATH + 4]; swprintf(cmd, MAX_PATH + 4, L"\"%ls\"", exe);
        if (!CreateProcessW(exe, cmd, NULL, NULL, FALSE, 0, NULL, dir, &si, &pi))
        {
            wchar_t m[128]; swprintf(m, 128, L"erro ao abrir o MacSkin.exe (%lu)", GetLastError()); Log(m);
            Sleep(5000); continue;
        }
        CloseHandle(pi.hThread);
        WaitForSingleObject(pi.hProcess, INFINITE);
        DWORD codigo = 0; GetExitCodeProcess(pi.hProcess, &codigo); CloseHandle(pi.hProcess);
        if (codigo == 3) { Sleep(1000); continue; }   // outro abriu antes: a próxima volta espera por ele
        if (!Existe(desligado)) { wchar_t m[96]; swprintf(m, 96, L"Mac Skin fechou (código %ld), reabrindo", (long)codigo); Log(m); }
        Sleep(2000);
    }
    (void)eu; (void)a; (void)b; (void)c; (void)d;
}
