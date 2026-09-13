from __future__ import annotations

import ctypes
import os
from ctypes import wintypes


TARGET_NAME = "SoundCloudReleaseTracker:SoundCloudClientSecret"
CRED_TYPE_GENERIC = 1
CRED_PERSIST_LOCAL_MACHINE = 2
ERROR_NOT_FOUND = 1168


class CredentialStoreError(RuntimeError):
    pass


class FILETIME(ctypes.Structure):
    _fields_ = [
        ("dwLowDateTime", wintypes.DWORD),
        ("dwHighDateTime", wintypes.DWORD),
    ]


class CREDENTIALW(ctypes.Structure):
    _fields_ = [
        ("Flags", wintypes.DWORD),
        ("Type", wintypes.DWORD),
        ("TargetName", wintypes.LPWSTR),
        ("Comment", wintypes.LPWSTR),
        ("LastWritten", FILETIME),
        ("CredentialBlobSize", wintypes.DWORD),
        ("CredentialBlob", ctypes.POINTER(ctypes.c_ubyte)),
        ("Persist", wintypes.DWORD),
        ("AttributeCount", wintypes.DWORD),
        ("Attributes", ctypes.c_void_p),
        ("TargetAlias", wintypes.LPWSTR),
        ("UserName", wintypes.LPWSTR),
    ]


def _api():
    if os.name != "nt":
        raise CredentialStoreError("Windows Credential Manager доступен только в Windows.")

    advapi32 = ctypes.WinDLL("Advapi32.dll", use_last_error=True)

    advapi32.CredWriteW.argtypes = [ctypes.POINTER(CREDENTIALW), wintypes.DWORD]
    advapi32.CredWriteW.restype = wintypes.BOOL

    advapi32.CredReadW.argtypes = [
        wintypes.LPCWSTR,
        wintypes.DWORD,
        wintypes.DWORD,
        ctypes.POINTER(ctypes.POINTER(CREDENTIALW)),
    ]
    advapi32.CredReadW.restype = wintypes.BOOL

    advapi32.CredDeleteW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD]
    advapi32.CredDeleteW.restype = wintypes.BOOL

    advapi32.CredFree.argtypes = [ctypes.c_void_p]
    advapi32.CredFree.restype = None
    return advapi32


def write_client_secret(secret: str) -> None:
    secret = str(secret or "")
    if not secret:
        raise CredentialStoreError("Client Secret пуст.")

    api = _api()
    blob = secret.encode("utf-16-le")
    blob_buffer = ctypes.create_string_buffer(blob)

    cred = CREDENTIALW()
    cred.Flags = 0
    cred.Type = CRED_TYPE_GENERIC
    cred.TargetName = TARGET_NAME
    cred.Comment = "SoundCloud Release Tracker API secret"
    cred.CredentialBlobSize = len(blob)
    cred.CredentialBlob = ctypes.cast(blob_buffer, ctypes.POINTER(ctypes.c_ubyte))
    cred.Persist = CRED_PERSIST_LOCAL_MACHINE
    cred.AttributeCount = 0
    cred.Attributes = None
    cred.TargetAlias = None
    cred.UserName = "SoundCloud Release Tracker"

    if not api.CredWriteW(ctypes.byref(cred), 0):
        err = ctypes.get_last_error()
        raise CredentialStoreError(
            f"Не удалось сохранить Client Secret в Windows Credential Manager (код {err})."
        )


def read_client_secret() -> str:
    api = _api()
    pcred = ctypes.POINTER(CREDENTIALW)()

    if not api.CredReadW(TARGET_NAME, CRED_TYPE_GENERIC, 0, ctypes.byref(pcred)):
        err = ctypes.get_last_error()
        if err == ERROR_NOT_FOUND:
            return ""
        raise CredentialStoreError(
            f"Не удалось прочитать Client Secret из Windows Credential Manager (код {err})."
        )

    try:
        cred = pcred.contents
        if not cred.CredentialBlob or not cred.CredentialBlobSize:
            return ""
        raw = ctypes.string_at(cred.CredentialBlob, cred.CredentialBlobSize)
        return raw.decode("utf-16-le")
    except Exception as exc:
        raise CredentialStoreError("Не удалось декодировать сохранённый Client Secret.") from exc
    finally:
        api.CredFree(pcred)


def delete_client_secret() -> None:
    api = _api()
    if api.CredDeleteW(TARGET_NAME, CRED_TYPE_GENERIC, 0):
        return
    err = ctypes.get_last_error()
    if err == ERROR_NOT_FOUND:
        return
    raise CredentialStoreError(
        f"Не удалось удалить Client Secret из Windows Credential Manager (код {err})."
    )
