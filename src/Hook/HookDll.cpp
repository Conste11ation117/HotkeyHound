// HotkeyHound 原生 hook DLL
//
// 职责：作为一个 WH_GETMESSAGE 全局钩子被注入到每个 GUI 进程里，监听该进程
// 消息队列中的 WM_HOTKEY。WH_GETMESSAGE 的 hook 过程在「目标进程」的上下文里
// 执行，因此这个函数里能直接拿到目标进程的 pid —— 这正是引擎 B 需要的归属信息。
//
// 纪律：绝不吞键，绝不修改消息，处理完必须 CallNextHookEx。
//
// 与控制器的握手走共享内存 Local\HotkeyHound.Ctl：
//   { DWORD magic; DWORD arm; DWORD ctlLo; DWORD ctlHi; }
// 控制器先创建并写入 ctl 窗口句柄，DLL 每次收到 WM_HOTKEY 都重新读取一次，
// 这样控制器重开也能跟上（不用重装钩子）。
//
// 这份源码同时编 x64 与 x86 两个版本（HotkeyHound.Hook.dll / HotkeyHound.Hook32.dll）：
// 钩子只能注入与安装者同位数的进程，32 位进程需要另一份 32 位 DLL + 32 位宿主。
// 两边读写同一块共享内存，所以控制块里绝不能出现指针宽度相关的字段：
// 曾经用 HWND（x86 4 字节、x64 8 字节）直接放进去，跨位数时 arm 会错位。

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#define HKS_MAGIC      0x484B5354u   // 'HKST'
#define HKS_MAP_NAME   L"Local\\HotkeyHound.Ctl"
#define HKS_WM_REPORT  (WM_APP + 1)  // 与控制器约定：wParam=pid, lParam=HotKey 的 lParam

// 这个 hook 跑在别人的进程里，一旦静默失败几乎没法从外部观察。
// 用 /DHKS_DEBUG 编一份调试版，它会把每一步追加到 %TEMP%\hks-hook-debug.log。
#ifdef HKS_DEBUG
#include <stdio.h>
#include <stdarg.h>

static void HksDbg(const char* fmt, ...)
{
    WCHAR tmp[MAX_PATH];
    if (GetTempPathW(MAX_PATH, tmp) == 0) return;
    WCHAR path[MAX_PATH];
    if (swprintf_s(path, MAX_PATH, L"%shks-hook-debug.log", tmp) < 0) return;

    HANDLE f = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
                           NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (f == INVALID_HANDLE_VALUE) return;

    char body[512];
    va_list ap;
    va_start(ap, fmt);
    _vsnprintf_s(body, sizeof(body), _TRUNCATE, fmt, ap);
    va_end(ap);

    char line[640];
    _snprintf_s(line, sizeof(line), _TRUNCATE, "[%lu] %s\r\n", GetCurrentProcessId(), body);
    DWORD written = 0;
    WriteFile(f, line, (DWORD)lstrlenA(line), &written, NULL);
    CloseHandle(f);
}
#else
#define HksDbg(...) ((void)0)
#endif

#pragma pack(push, 1)
struct HksControl
{
    DWORD magic;
    DWORD arm;
    // 句柄拆成两个 32 位字段：x86 与 x64 的 HWND 宽度不同，布局必须与位数无关。
    DWORD ctlLo;
    DWORD ctlHi;
};
#pragma pack(pop)

// 两种位数必须得到同一块 16 字节布局；改字段就同时改 AttributionEngine 的偏移。
static_assert(sizeof(HksControl) == 16, "HksControl must stay 16 bytes in both x86 and x64");

static HWND ControlWindow(const HksControl* ctl)
{
    ULONGLONG v = ((ULONGLONG)ctl->ctlHi << 32) | (ULONGLONG)ctl->ctlLo;
    return (HWND)(ULONG_PTR)v;
}

// 只做一次性初始化：INIT_ONCE 保证「成功或失败都只尝试一次」。
// 钩子过程跑在别人的消息循环里，绝不能反复做文件映射/加锁这类重活，
// 也不能因为控制器暂时不在就每次热键都重试一遍。
static HANDLE      g_hMap = NULL;
static HksControl* g_pCtl = NULL;
static INIT_ONCE   g_once = INIT_ONCE_STATIC_INIT;

static BOOL CALLBACK InitControlOnce(PINIT_ONCE once, PVOID param, PVOID* ctx)
{
    UNREFERENCED_PARAMETER(once);
    UNREFERENCED_PARAMETER(param);
    UNREFERENCED_PARAMETER(ctx);

    DWORD created = 0;
    HANDLE h = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, HKS_MAP_NAME);
    if (h == NULL)
    {
        DWORD openErr = GetLastError();
        h = CreateFileMappingW(INVALID_HANDLE_VALUE, NULL, PAGE_READWRITE,
                               sizeof(HksControl), 0, HKS_MAP_NAME);
        if (h != NULL) created = 1;
        HksDbg("OpenFileMapping 失败(err=%lu)，自建映射=%s hMap=%p",
               openErr, created ? "成功" : "失败", h);
    }
    if (h == NULL) return TRUE;

    void* p = MapViewOfFile(h, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, sizeof(HksControl));
    if (p == NULL)
    {
        HksDbg("MapViewOfFile 失败 err=%lu", GetLastError());
        CloseHandle(h);
        return TRUE;
    }

    HksControl* ctl = (HksControl*)p;
    if (ctl->magic != HKS_MAGIC)
    {
        // 还没人写过（或者对端已退出被复位）：先清干净，等控制器来初始化
        ctl->magic = HKS_MAGIC;
        ctl->arm   = 0;
        ctl->ctlLo = 0;
        ctl->ctlHi = 0;
    }
    g_hMap = h;
    g_pCtl = ctl;
    HksDbg("映射就绪 magic=0x%08lX ctl=%p arm=%lu（本人创建=%lu）",
           (unsigned long)ctl->magic, (void*)ControlWindow(ctl), (unsigned long)ctl->arm, created);
    return TRUE;
}

static HksControl* GetControl()
{
    if (g_pCtl != NULL) return g_pCtl;
    InitOnceExecuteOnce(&g_once, InitControlOnce, NULL, NULL);
    return g_pCtl;
}

// 上报函数的地址在每个被注入的进程里不一样，用函数指针结构不行；
// 这里靠一个静态成员函数指针在 DLL 内部（同一份映像）共享是安全的：
// 每个进程各自加载一份 DLL，各自的静态变量互不影响。
extern "C" __declspec(dllexport) LRESULT CALLBACK HotkeyHoundHookProc(int nCode, WPARAM wParam, LPARAM lParam)
{
#ifdef HKS_DEBUG
    static DWORD s_calls = 0;
    DWORD callNo = InterlockedIncrement((volatile LONG*)&s_calls);
    if (callNo <= 5)
        HksDbg("hook 第 %lu 次被调用 nCode=%d wParam=%lu msg=0x%04X",
               (unsigned long)callNo, nCode, (unsigned long)wParam,
               (nCode == HC_ACTION && wParam == PM_REMOVE && lParam != 0) ? ((MSG*)lParam)->message : 0);
#endif
    if (nCode == HC_ACTION && wParam == PM_REMOVE && lParam != 0)
    {
        MSG* msg = (MSG*)lParam;
        if (msg->message == WM_HOTKEY)
        {
            HksDbg("拦到 WM_HOTKEY hwnd=%p id=%lu lParam=0x%08lX",
                   (void*)msg->hwnd, (unsigned long)msg->wParam, (unsigned long)msg->lParam);
            HksControl* ctl = GetControl();
            if (ctl != NULL)
            {
                HWND ctlWindow = ControlWindow(ctl);
                HksDbg(" 控制块 magic=0x%08lX ctl=%p arm=%lu",
                       (unsigned long)ctl->magic, (void*)ctlWindow, (unsigned long)ctl->arm);
                if (ctl->magic == HKS_MAGIC && ctlWindow != NULL && ctl->arm != 0)
                {
                    // wParam = 本进程 pid（归属答案），lParam = 热键的修饰键+VK
                    BOOL ok = PostMessageW(ctlWindow, HKS_WM_REPORT,
                                           (WPARAM)GetCurrentProcessId(),
                                           (LPARAM)msg->lParam);
                    HksDbg(" 上报 PostMessage(%p, 0x%X, pid=%lu) = %s（err=%lu）",
                           (void*)ctlWindow, (unsigned)HKS_WM_REPORT, (unsigned long)GetCurrentProcessId(),
                           ok ? "成功" : "失败", ok ? 0 : GetLastError());
                }
            }
            else
            {
                HksDbg(" 控制块不可用，丢弃这条上报");
            }
        }
    }
    // 永远放行，绝不吞键
    return CallNextHookEx(NULL, nCode, wParam, lParam);
}

// DllMain 里除了 DisableThreadLibraryCalls 什么也不做。
// 这里曾经会 Initialize/DeleteCriticalSection —— 加载器锁下调 CRT 和内核对象是
// 自找麻烦：进程启动/退出路径上千奇百怪，一旦在这里出错就是整个进程没了。
// 映射与视图不在这里释放：进程退出时系统会收回，提前 Unmap 反而可能踩到别人。
BOOL APIENTRY DllMain(HANDLE hModule, DWORD reason, LPVOID reserved)
{
    UNREFERENCED_PARAMETER(reserved);

    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls((HMODULE)hModule);
    }

    return TRUE;
}
