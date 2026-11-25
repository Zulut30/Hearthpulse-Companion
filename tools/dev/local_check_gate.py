"""The named Windows mutex Hearthpulse.LocalCheck, shared with tools/dev/check.ps1 and tools/bgsim/sync.ps1.

Worktrees on one desktop queue their builds, tests and UI renders behind it, so only one runs at a time (and only
one render window is open). Used by the visual-check render.py and tools/visual/visual_check.py; loaded by file path.
A no-op off Windows.
"""
import contextlib
import ctypes
import sys

NAME = "Hearthpulse.LocalCheck"
WAIT_OBJECT_0 = 0x0
WAIT_ABANDONED = 0x80  # the previous owner exited without releasing it: the mutex is ours now
WAIT_TIMEOUT = 0x102
INFINITE = 0xFFFFFFFF


def kernel32():
    """kernel32 with the four calls typed, or None off Windows."""
    if sys.platform != "win32":
        return None
    from ctypes import wintypes
    api = ctypes.WinDLL("kernel32", use_last_error=True)
    api.CreateMutexW.argtypes = [wintypes.LPVOID, wintypes.BOOL, wintypes.LPCWSTR]
    api.CreateMutexW.restype = wintypes.HANDLE
    api.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
    api.WaitForSingleObject.restype = wintypes.DWORD
    api.ReleaseMutex.argtypes = [wintypes.HANDLE]
    api.ReleaseMutex.restype = wintypes.BOOL
    api.CloseHandle.argtypes = [wintypes.HANDLE]
    api.CloseHandle.restype = wintypes.BOOL
    return api


@contextlib.contextmanager
def held():
    """Owns Hearthpulse.LocalCheck for the block; waits (and says so once) while another check or render holds it."""
    api = kernel32()
    if api is None:
        yield
        return
    handle = api.CreateMutexW(None, False, NAME)
    if not handle:
        raise OSError(ctypes.get_last_error(), f"CreateMutexW({NAME}) failed")
    try:
        state = api.WaitForSingleObject(handle, 0)
        if state == WAIT_TIMEOUT:
            print("Waiting for the current Hearthpulse check or render to finish...", flush=True)
            state = api.WaitForSingleObject(handle, INFINITE)
        if state not in (WAIT_OBJECT_0, WAIT_ABANDONED):
            raise OSError(ctypes.get_last_error(), f"waiting for {NAME} failed ({state:#x})")
        try:
            yield
        finally:
            api.ReleaseMutex(handle)
    finally:
        api.CloseHandle(handle)
