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
// 控制器（x64 主进程）是唯一的写方，DLL 侧只读——注入到别人进程里的代码如果能写这块
// 内存，就可能在控制器刚 arm=1 的瞬间把它清零，导致全局静默丢报，而且现场查不出来。
// DLL 每次收到 WM_HOTKEY 都重新读一遍，所以控制器重开也能跟上（不用重装钩子）。
//
// 这份源码同时编 x64 与 x86 两个版本（HotkeyHound.Hook.dll / HotkeyHound.Hook32.dll）：
// 钩子只能注入与安装者同位数的进程，32 位进程需要另一份 32 位 DLL + 32 位宿主。
// 两边读写同一块共享内存，所以控制块里绝不能出现指针宽度相关的字段：
// 曾经用 HWND（x86 4 字节、x64 8 字节）直接放进去，跨位数时 arm 会错位。

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstddef>   // offsetof：下面的字段偏移断言要用
#include <excpt.h>   // __try / __except：钩子过程在别人的进程里跑，必须有异常兜底

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
    // volatile：控制块由控制器进程写、由每个被注入进程读，编译器不许把这些读缓存进寄存器，
    // 否则「控制器已经 arm=0 了」这件事在热路径里永远看不到。
    volatile DWORD magic;
    volatile DWORD arm;
    // 句柄拆成两个 32 位字段：x86 与 x64 的 HWND 宽度不同，布局必须与位数无关。
    volatile DWORD ctlLo;
    volatile DWORD ctlHi;
};
#pragma pack(pop)

// 段大小必须与控制器 AttributionEngine.ControlMapSize 一致。命名段由谁先创建就定死了，
// 后来者无法把它改大：DLL 若只建 16 字节，控制器随后 MapViewOfFile(64) 就会失败，
// 整条归属链路直接哑掉——而且哑得毫无声响。
#define HKS_MAP_SIZE     64u

// 初始化失败允许重试的次数。既不能一次失败就永久聋掉，也不能每条 WM_HOTKEY 都去
// 试一次文件映射（钩子过程跑在别人的消息循环里）。
#define HKS_MAX_INIT_TRY 8

// 两种位数必须得到同一块 16 字节布局；改字段就同时改 AttributionEngine 的偏移。
static_assert(sizeof(HksControl) == 16, "HksControl must stay 16 bytes in both x86 and x64");
// 只断言总长挡不住回退：ctlLo/ctlHi 互换、arm 挪位之后总长仍然是 16，
// 而 C# 侧是按 0/4/8/12 硬写的偏移，错位了谁也不会报错，只会静默丢归属。
static_assert(offsetof(HksControl, magic) == 0,  "magic must stay at offset 0");
static_assert(offsetof(HksControl, arm)   == 4,  "arm must stay at offset 4");
static_assert(offsetof(HksControl, ctlLo) == 8,  "ctlLo must stay at offset 8");
static_assert(offsetof(HksControl, ctlHi) == 12, "ctlHi must stay at offset 12");

static HWND ControlWindow(const HksControl* ctl)
{
    ULONGLONG v = ((ULONGLONG)ctl->ctlHi << 32) | (ULONGLONG)ctl->ctlLo;
    return (HWND)(ULONG_PTR)v;
}

static HANDLE      g_hMap = NULL;
static HksControl* g_pCtl = NULL;
static INIT_ONCE   g_once = INIT_ONCE_STATIC_INIT;
static LONG        g_tries = 0;

static BOOL CALLBACK InitControlOnce(PINIT_ONCE once, PVOID param, PVOID* ctx)
{
    UNREFERENCED_PARAMETER(once);
    UNREFERENCED_PARAMETER(param);
    UNREFERENCED_PARAMETER(ctx);

    // 这段代码在别人的进程里执行。映射失败是预期内的（控制器还没起来），
    // 但「意料之外的访问违例」不能把宿主进程带走：兜住并返回 FALSE，
    // INIT_ONCE 会保持未完成状态，下次还能重试，不会让同进程的其它线程卡死。
    __try
    {
        if (InterlockedIncrement(&g_tries) > HKS_MAX_INIT_TRY) return TRUE;   // 放弃，别再试

        DWORD created = 0;
        // 只要读权限。这块内存归控制器所有：注入到别人进程里的 DLL 一旦能写，就可能在控制器
        // 那四条非原子写（magic/arm/ctlLo/ctlHi）中间插进来把 arm 清零，全局静默丢报，
        // 而且这种故障在现场根本查不出来。所以这里连写权限都不要。
        HANDLE h = OpenFileMappingW(FILE_MAP_READ, FALSE, HKS_MAP_NAME);
        if (h == NULL)
        {
            DWORD openErr = GetLastError();
            // 参数序是 (…, dwMaximumSizeHigh, dwMaximumSizeLow, name)。这里曾经写反成
            // (sizeof, 0)，等于申请 16 * 4GiB = 64GiB 的 pagefile 段：绝大多数机器直接失败，
            // 回退路径形同不存在；真在大 pagefile 的机器上成功了，每个被注入进程都会钉住
            // 64GiB 提交，足以拖垮整机。
            h = CreateFileMappingW(INVALID_HANDLE_VALUE, NULL, PAGE_READWRITE,
                                   0, HKS_MAP_SIZE, HKS_MAP_NAME);
            if (h != NULL) created = 1;
            HksDbg("OpenFileMapping 失败(err=%lu)，自建映射=%s hMap=%p",
                   openErr, created ? "成功" : "失败", h);
        }
        // 失败必须 return FALSE：返回 TRUE 会让 INIT_ONCE 认为「已完成」，g_pCtl 永远是 NULL，
        // 这个进程从此对热键彻底失聪——即使控制器随后正常起来了也救不回来。
        if (h == NULL) return FALSE;

        // 请求 0 = 映射整段：控制器将来把段建得更大也不会因此映射失败。
        void* p = MapViewOfFile(h, FILE_MAP_READ, 0, 0, 0);
        if (p == NULL)
        {
            HksDbg("MapViewOfFile 失败 err=%lu", GetLastError());
            CloseHandle(h);
            return FALSE;
        }

        g_hMap = h;
        g_pCtl = (HksControl*)p;
        HksDbg("映射就绪 magic=0x%08lX ctl=%p arm=%lu（本人创建=%lu）",
               (unsigned long)g_pCtl->magic, (void*)ControlWindow(g_pCtl),
               (unsigned long)g_pCtl->arm, created);
        return TRUE;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        HksDbg("控制块初始化异常 0x%08lX，按失败处理", (unsigned long)GetExceptionCode());
        return FALSE;
    }
}

static HksControl* GetControl()
{
    HksControl* p = g_pCtl;
    if (p != NULL) return p;
    InitOnceExecuteOnce(&g_once, InitControlOnce, NULL, NULL);
    return g_pCtl;
}

// 上报函数的地址在每个被注入的进程里不一样，用函数指针结构不行；
// 这里靠一个静态成员函数指针在 DLL 内部（同一份映像）共享是安全的：
// 每个进程各自加载一份 DLL，各自的静态变量互不影响。
extern "C" __declspec(dllexport) LRESULT CALLBACK HotkeyHoundHookProc(int nCode, WPARAM wParam, LPARAM lParam)
{
    // 这段代码跑在别人的进程里：这里一次访问违例，就等于替宿主进程按下退出键。
    // SEH 兜底把「我们自己的 bug」降级成「丢一条上报」，异常绝不许抛进宿主的消息循环。
    __try
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
                    // 读序必须是 arm → magic → 句柄：控制器的写序是 ctlLo/ctlHi → magic →
                    // 内存屏障 → arm=1。所以只要 arm 已经是 1，句柄与 magic 必定都落地了，
                    // 不会读到「新 lo + 旧 hi」这种拼不出来的窗口句柄；反过来先读句柄就会撕裂。
                    DWORD arm = ctl->arm;
                    DWORD magic = ctl->magic;
                    if (arm != 0 && magic == HKS_MAGIC)
                    {
                        HWND ctlWindow = ControlWindow(ctl);
                        HksDbg(" 控制块 magic=0x%08lX ctl=%p arm=%lu",
                               (unsigned long)magic, (void*)ctlWindow, (unsigned long)arm);
                        if (ctlWindow != NULL)
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
                }
                else
                {
                    HksDbg(" 控制块不可用，丢弃这条上报");
                }
            }
        }
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        HksDbg("hook 过程异常 0x%08lX，已丢弃本条消息并放行", (unsigned long)GetExceptionCode());
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
