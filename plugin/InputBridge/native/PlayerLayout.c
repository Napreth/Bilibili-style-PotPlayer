#define UNICODE
#define _UNICODE
#include <windows.h>

/* Runs on the player's UI thread. No offsets or private player structures are used.
   Constrain playlist layout before Windows commits the new outer rectangle. */
#define MSG_LAYOUT (WM_APP + 0x317)
#define PROP(name) L"BilibiliLayout." name

static void FixVideo(HWND window)
{
    HWND video = (HWND)GetPropW(window, PROP(L"Video"));
    HWND bar = (HWND)GetPropW(window, PROP(L"Bar"));
    if (!IsWindow(video)) return;
    if (IsWindow(bar)) ShowWindow(bar, SW_HIDE);
    RECT client, bounds; GetClientRect(window, &client); GetWindowRect(video, &bounds);
    POINT origin = {bounds.left, bounds.top}; ScreenToClient(window, &origin);
    int small = GetPropW(window, PROP(L"Small")) != 0;
    int border = (int)(INT_PTR)GetPropW(window, PROP(L"Border"));
    int top = small ? border : (int)(INT_PTR)GetPropW(window, PROP(L"Top"));
    int right = small ? client.right - border : origin.x + bounds.right - bounds.left;
    if (right > client.right - border || right <= border) right = client.right - border;
    /* Video is always the direct child that owns the renderer; WM_SIZE on this
       child lets the renderer resize itself without touching its swap chain. */
    SetWindowPos(video, 0, border, top, max(1, right - border), max(1, client.bottom - border - top), SWP_NOZORDER | SWP_NOACTIVATE);
}

static void ConstrainAspect(HWND window, WPARAM edge, RECT *rect)
{
    int ratio = (int)(INT_PTR)GetPropW(window, PROP(L"Ratio"));
    if (!ratio || !GetPropW(window, PROP(L"Small"))) return;
    RECT client, outer; GetClientRect(window, &client); GetWindowRect(window, &outer);
    int border = (int)(INT_PTR)GetPropW(window, PROP(L"Border"));
    int dx = outer.right - outer.left - client.right + 2 * border;
    int dy = outer.bottom - outer.top - client.bottom + 2 * border;
    int width = max(1, rect->right - rect->left - dx), height = max(1, rect->bottom - rect->top - dy);
    if (edge == WMSZ_TOP || edge == WMSZ_BOTTOM) width = MulDiv(height, ratio, 100000);
    else height = MulDiv(width, 100000, ratio);
    if (edge == WMSZ_LEFT || edge == WMSZ_TOPLEFT || edge == WMSZ_BOTTOMLEFT) rect->left = rect->right - width - dx;
    else rect->right = rect->left + width + dx;
    if (edge == WMSZ_TOP || edge == WMSZ_TOPLEFT || edge == WMSZ_TOPRIGHT) rect->top = rect->bottom - height - dy;
    else rect->bottom = rect->top + height + dy;
}

static LRESULT CALLBACK LayoutProc(HWND window, UINT message, WPARAM wparam, LPARAM lparam)
{
    WNDPROC original = (WNDPROC)GetPropW(window, PROP(L"Original"));
    if (!original) return DefWindowProcW(window, message, wparam, lparam);
    if (message == MSG_LAYOUT && wparam == 2) {
        SetWindowLongPtrW(window, GWLP_WNDPROC, (LONG_PTR)original);
        RemovePropW(window, PROP(L"Original"));
        HWND bar = (HWND)GetPropW(window, PROP(L"Bar"));
        if (IsWindow(bar)) ShowWindow(bar, SW_SHOWNA);
        return 1;
    }
    if (message == MSG_LAYOUT) { FixVideo(window); return 1; }
    if (message == WM_SIZING && GetPropW(window, PROP(L"Ratio")) && GetPropW(window, PROP(L"Small"))) {
        /* Do not compose two aspect-ratio policies: the native skin includes its
           own header/footer in this calculation, while small mode is video only. */
        ConstrainAspect(window, wparam, (RECT *)lparam);
        SetPropW(window, PROP(L"AspectResizeCount"), (HANDLE)((INT_PTR)GetPropW(window, PROP(L"AspectResizeCount")) + 1));
        return TRUE;
    }
    if (message == WM_WINDOWPOSCHANGING && GetPropW(window, PROP(L"Freeze"))) {
        WINDOWPOS *position = (WINDOWPOS *)lparam;
        if (!(position->flags & SWP_NOSIZE) && (position->cx != (int)(INT_PTR)GetPropW(window, PROP(L"Width")) || position->cy != (int)(INT_PTR)GetPropW(window, PROP(L"Height"))))
            SetPropW(window, PROP(L"ClampedCount"), (HANDLE)((INT_PTR)GetPropW(window, PROP(L"ClampedCount")) + 1));
        position->cx = (int)(INT_PTR)GetPropW(window, PROP(L"Width"));
        position->cy = (int)(INT_PTR)GetPropW(window, PROP(L"Height"));
        position->flags |= SWP_NOMOVE;
    }
    int playlist = message == WM_COMMAND && LOWORD(wparam) == 10011;
    if (playlist) {
        int opening = GetPropW(window, PROP(L"Open")) == 0;
        int automatic = GetPropW(window, PROP(L"Automatic")) != 0;
        if (opening && !automatic && !GetPropW(window, PROP(L"AllowSidebar"))) return 0;
        SetPropW(window, PROP(L"Open"), (HANDLE)(INT_PTR)opening);
        if (!automatic) SetPropW(window, PROP(L"Intent"), (HANDLE)(INT_PTR)opening);
        RECT rect; GetWindowRect(window, &rect);
        SetPropW(window, PROP(L"Width"), (HANDLE)(INT_PTR)(rect.right - rect.left));
        SetPropW(window, PROP(L"Height"), (HANDLE)(INT_PTR)(rect.bottom - rect.top));
        SetPropW(window, PROP(L"Freeze"), (HANDLE)1);
    }
    LRESULT result = CallWindowProcW(original, window, message, wparam, lparam);
    if (playlist) RemovePropW(window, PROP(L"Freeze"));
    if (playlist || message == WM_SIZE) FixVideo(window);
    return result;
}

__declspec(dllexport) LRESULT CALLBACK LayoutHook(int code, WPARAM wparam, LPARAM lparam)
{
    if (code >= 0) {
        CWPSTRUCT *call = (CWPSTRUCT *)lparam;
        if (call->message == MSG_LAYOUT && GetPropW(call->hwnd, PROP(L"Attach")) && !GetPropW(call->hwnd, PROP(L"Original"))) {
            /* A subclass must remain callable even if the helper is terminated. */
            HMODULE module;
            GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN, (LPCWSTR)LayoutHook, &module);
            WNDPROC previous = (WNDPROC)SetWindowLongPtrW(call->hwnd, GWLP_WNDPROC, (LONG_PTR)LayoutProc);
            SetPropW(call->hwnd, PROP(L"Original"), (HANDLE)previous);
        }
    }
    return CallNextHookEx(0, code, wparam, lparam);
}
