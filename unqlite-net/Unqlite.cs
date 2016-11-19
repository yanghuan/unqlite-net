using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace UnqliteNet {
    public sealed class Unqlite {
        private const string kDllName = "unqlite.dll";
        private const int kSmallKeyBufferSize = 128;
        private const int kSmallDataBufferSize = 512;
        private static readonly Encoding Encoding = Encoding.UTF8;

        private IntPtr pDb_;
        internal bool isAutoCommit_ = true;

        private Unqlite(IntPtr pDb) {
            pDb_ = pDb;
        }

        public Unqlite(string fileName, UnqliteOpenModel model) {
            UnqliteResultCode code = unqlite_open(out pDb_, fileName, model);
            if(code != UnqliteResultCode.Ok) {
                throw new UnqliteException(code, GetDataBaseErrorLog());
            }
        }

        public static UnqliteResultCode TryOpen(string fileName, UnqliteOpenModel model, out Unqlite unqlite) {
            unqlite = null;
            IntPtr pDB;
            UnqliteResultCode code = unqlite_open(out pDB, fileName, model);
            if(code == UnqliteResultCode.Ok) {
                unqlite = new Unqlite(pDB);
            }
            return code;
        }

        public void Close() {
            if(pDb_ != IntPtr.Zero) {
                unqlite_close(pDb_);
                pDb_ = IntPtr.Zero;
            }
        }

        public static bool IsThreadSafe {
            get {
                return unqlite_lib_is_threadsafe() == 1;
            }
        }

        public unsafe static string Version {
            get {
                return new string(unqlite_lib_version());
            }
        }

        public unsafe static string Signature {
            get {
                return new string(unqlite_lib_signature());
            }
        }

        public unsafe static string Ident {
            get {
                return new string(unqlite_lib_ident());
            }
        }

        public unsafe static string Copyright {
            get {
                return new string(unqlite_lib_copyright());
            }
        }

        public static void LibInit(UnqliteLibConfigSetting setting) {
            if(setting == null) {
                throw new ArgumentNullException("setting");
            }

            bool hasChange = false;
            if(setting.IsThreadSafe) {
                UnqliteResultCode code = unqlite_lib_config(UnqliteLibConfigCode.ThreadLevelMulti, __arglist());
                if(code != UnqliteResultCode.Ok) {
                    throw new UnqliteException(code, null);
                }
                hasChange = true;
            }

            if(setting.PageSize > 0) {
                UnqliteResultCode code = unqlite_lib_config(UnqliteLibConfigCode.PageSize, __arglist(setting.PageSize));
                if(code != UnqliteResultCode.Ok) {
                    throw new UnqliteException(code, null);
                }
                hasChange = true;
            }

            if(hasChange) {
                UnqliteResultCode code = unqlite_lib_init();
                if(code != UnqliteResultCode.Ok) {
                    throw new UnqliteException(code, null);
                }
            }
        }

        internal void TryCommit(ref UnqliteResultCode code) {
            if(code == UnqliteResultCode.Ok && isAutoCommit_) {
                code = unqlite_commit(pDb_);
                if(code != UnqliteResultCode.Ok) {
                    if(code != UnqliteResultCode.Busy && code != UnqliteResultCode.NotImplemented) {
                        Rollback();
                    }
                }
            }
        }

        internal UnqliteResultCode Rollback() {
            return unqlite_rollback(pDb_);
        }

        private unsafe UnqliteResultCode InternalTryAppendRaw(byte* keyBuffer, int keyCount, ArraySegment<byte> data) {
            fixed (byte* pdata = data.Array) {
                UnqliteResultCode code = unqlite_kv_append(pDb_, keyBuffer, keyCount, pdata + data.Offset, data.Count);
                TryCommit(ref code);
                return code;
            }
        }

        public unsafe UnqliteResultCode TryAppendRaw(string key, ArraySegment<byte> data) {
            if(key == null) {
                throw new ArgumentNullException("key");
            }

            int keyCount = Encoding.GetByteCount(key);
            if(keyCount <= kSmallKeyBufferSize) {
                byte* keyBuffer = stackalloc byte[keyCount];
                return InternalTryAppendRaw(keyBuffer, keyCount, data);
            }
            else {
                byte[] keyBytes = new byte[keyCount];
                fixed (byte* keyBuffer = keyBytes) {
                    return InternalTryAppendRaw(keyBuffer, keyCount, data);
                }
            }
        }

        public void AppendRaw(string key, ArraySegment<byte> data) {
            UnqliteResultCode code = TryAppendRaw(key, data);
            if(code != UnqliteResultCode.Ok) {
                throw new UnqliteException(code, GetDataBaseErrorLog());
            }
        }

        private unsafe UnqliteResultCode InternalTryAppend(byte* keyBuffer, int keyCount, string data) {
            UnqliteResultCode code;
            int dataCount = Encoding.GetByteCount(data);
            if(dataCount <= kSmallDataBufferSize) {
                byte* dataBuffer = stackalloc byte[dataCount];
                if(dataCount > 0) {
                    fixed (char* dataPtr = data) {
                        Encoding.GetBytes(dataPtr, data.Length, dataBuffer, dataCount);
                    }
                }
                code = unqlite_kv_append(pDb_, keyBuffer, keyCount, dataBuffer, dataCount);
            }
            else {
                byte[] dataBytes = Encoding.GetBytes(data);
                fixed (byte* dataBuffer = dataBytes) {
                    code = unqlite_kv_append(pDb_, keyBuffer, keyCount, dataBuffer, dataBytes.Length);
                }
            }
            TryCommit(ref code);
            return code;
        }

        public unsafe UnqliteResultCode TryAppend(string key, string data) {
            if(key == null) {
                throw new ArgumentNullException("key");
            }
            if(data == null) {
                throw new ArgumentNullException("data");
            }

            int keyCount = Encoding.GetByteCount(key);
            if(keyCount <= kSmallKeyBufferSize) {
                byte* keyBuffer = stackalloc byte[keyCount];
                return InternalTryAppend(keyBuffer, keyCount, data);
            }
            else {
                byte[] keyBytes = new byte[keyCount];
                fixed (byte* keyBuffer = keyBytes) {
                    return InternalTryAppend(keyBuffer, keyCount, data);
                }
            }
        }

        private unsafe UnqliteResultCode InteranlTryGetRaw(byte* keyBuffer, int keyCount, out byte[] data) {
            data = null;
            long dataLength;
            UnqliteResultCode code = unqlite_kv_fetch(pDb_, keyBuffer, keyCount, null, out dataLength);
            if(code == 0) {
                byte[] valueBytes = new byte[dataLength];
                fixed (byte* valueBuffer = valueBytes) {
                    code = unqlite_kv_fetch(pDb_, keyBuffer, keyCount, valueBuffer, out dataLength);
                    if(code == 0) {
                        data = valueBytes;
                    }
                }
            }
            return code;
        }

        public unsafe UnqliteResultCode TryGetRaw(string key, out byte[] data) {
            if(key == null) {
                throw new ArgumentNullException("key");
            }

            int keyCount = Encoding.GetByteCount(key);
            if(keyCount <= kSmallKeyBufferSize) {
                byte* keyBuffer = stackalloc byte[keyCount];
                return InteranlTryGetRaw(keyBuffer, keyCount, out data);
            }
            else {
                byte[] keyBytes = new byte[keyCount];
                fixed (byte* keyBuffer = keyBytes) {
                    return InteranlTryGetRaw(keyBuffer, keyCount, out data);
                }
            }
        }

        public byte[] GetRaw(string key) {
            byte[] data;
            UnqliteResultCode code = TryGetRaw(key, out data);
            if(code == UnqliteResultCode.Ok || code == UnqliteResultCode.NotFound) {
                return data;
            }
            throw new UnqliteException(code, GetDataBaseErrorLog());
        }

        private unsafe UnqliteResultCode InternalTryGet(byte* keyBuffer, int keyCount, out string data) {
            data = null;
            long dataLength;
            UnqliteResultCode code = unqlite_kv_fetch(pDb_, keyBuffer, keyCount, null, out dataLength);
            if(code == 0) {
                if(dataLength <= kSmallDataBufferSize) {
                    int len = (int)dataLength;
                    byte* dataBuffer = stackalloc byte[len];
                    code = unqlite_kv_fetch(pDb_, keyBuffer, keyCount, dataBuffer, out dataLength);
                    if(code == 0) {
                        data = new string((sbyte*)dataBuffer, 0, len, Encoding);
                    }
                }
                else {
                    byte[] dataBytes = new byte[dataLength];
                    fixed (byte* dataBuffer = dataBytes) {
                        code = unqlite_kv_fetch(pDb_, keyBuffer, keyCount, dataBuffer, out dataLength);
                        if(code == 0) {
                            data = Encoding.GetString(dataBytes);
                        }
                    }
                }
            }
            return code;
        }

        public unsafe UnqliteResultCode TryGet(string key, out string data) {
            if(key == null) {
                throw new ArgumentNullException("key");
            }

            int keyCount = Encoding.GetByteCount(key);
            if(keyCount <= kSmallKeyBufferSize) {
                byte* keyBuffer = stackalloc byte[keyCount];
                return InternalTryGet(keyBuffer, keyCount, out data);
            }
            else {
                byte[] keyBytes = new byte[keyCount];
                fixed (byte* keyBuffer = keyBytes) {
                    return InternalTryGet(keyBuffer, keyCount, out data);
                }
            }
        }

        public string Get(string key) {
            string value;
            UnqliteResultCode code = TryGet(key, out value);
            if(code == UnqliteResultCode.Ok || code == UnqliteResultCode.NotFound) {
                return value;
            }
            throw new UnqliteException(code, GetDataBaseErrorLog());
        }

        private unsafe UnqliteResultCode InternalTrySaveRaw(byte* keyBuffer, int keyCount, ArraySegment<byte> data) {
            fixed (byte* dataBuffer = data.Array) {
                UnqliteResultCode code = unqlite_kv_store(pDb_, keyBuffer, keyCount, dataBuffer + data.Offset, data.Count);
                TryCommit(ref code);
                return code;
            }
        }

        public unsafe UnqliteResultCode TrySaveRaw(string key, ArraySegment<byte> data) {
            if(key == null) {
                throw new ArgumentNullException("key");
            }

            int keyCount = Encoding.GetByteCount(key);
            if(keyCount <= kSmallKeyBufferSize) {
                byte* keyBuffer = stackalloc byte[keyCount];
                return InternalTrySaveRaw(keyBuffer, keyCount, data);
            }
            else {
                byte[] keyBytes = new byte[keyCount];
                fixed (byte* keyBuffer = keyBytes) {
                    return InternalTrySaveRaw(keyBuffer, keyCount, data);
                }
            }
        }

        public void SaveRaw(string key, ArraySegment<byte> data) {
            UnqliteResultCode code = TrySaveRaw(key, data);
            if(code != UnqliteResultCode.Ok) {
                throw new UnqliteException(code, GetDataBaseErrorLog());
            }
        }

        private unsafe UnqliteResultCode InternalTrySave(byte* keyBuffer, int keyCount, string data) {
            UnqliteResultCode code;
            int dataCount = Encoding.GetByteCount(data);
            if(dataCount <= kSmallDataBufferSize) {
                byte* dataBuffer = stackalloc byte[dataCount];
                if(dataCount > 0) {
                    fixed (char* dataPtr = data) {
                        Encoding.GetBytes(dataPtr, data.Length, dataBuffer, dataCount);
                    }
                }
                code = unqlite_kv_store(pDb_, keyBuffer, keyCount, dataBuffer, dataCount);
            }
            else {
                byte[] dataBytes = Encoding.GetBytes(data);
                fixed (byte* dataBuffer = dataBytes) {
                    code = unqlite_kv_store(pDb_, keyBuffer, keyCount, dataBuffer, dataBytes.Length);
                }
            }
            TryCommit(ref code);
            return code;
        }

        public unsafe UnqliteResultCode TrySave(string key, string data) {
            if(key == null) {
                throw new ArgumentNullException("key");
            }
            if(data == null) {
                throw new ArgumentNullException("data");
            }

            int keyCount = Encoding.GetByteCount(key);
            if(keyCount <= kSmallKeyBufferSize) {
                byte* keyBuffer = stackalloc byte[keyCount];
                return InternalTrySave(keyBuffer, keyCount, data);
            }
            else {
                byte[] keyBytes = new byte[keyCount];
                fixed (byte* keyBuffer = keyBytes) {
                    return InternalTrySave(keyBuffer, keyCount, data);
                }
            }
        }

        public void Save(string key, string data) {
            UnqliteResultCode code = TrySave(key, data);
            if(code != UnqliteResultCode.Ok) {
                throw new UnqliteException(code, GetDataBaseErrorLog());
            }
        }

        private unsafe UnqliteResultCode InternalTryRemove(byte* keyBuffer, int keyCount) {
            UnqliteResultCode code = unqlite_kv_delete(pDb_, keyBuffer, keyCount);
            TryCommit(ref code);
            return code;
        }

        public unsafe UnqliteResultCode TryRemove(string key) {
            if(key == null) {
                throw new ArgumentNullException("key");
            }

            int keyCount = Encoding.GetByteCount(key);
            if(keyCount <= kSmallKeyBufferSize) {
                byte* keyBuffer = stackalloc byte[keyCount];
                return InternalTryRemove(keyBuffer, keyCount);
            }
            else {
                byte[] keyBytes = new byte[keyCount];
                fixed (byte* keyBuffer = keyBytes) {
                    return InternalTryRemove(keyBuffer, keyCount);
                }
            }
        }

        public void Remove(string key) {
            UnqliteResultCode code = TryRemove(key);
            if(code != UnqliteResultCode.Ok) {
                throw new UnqliteException(code, GetDataBaseErrorLog());
            }
        }

        public unsafe static string GetDataBaseErrorLog(IntPtr pDb) {
            sbyte* zbuf;
            int len;
            unqlite_config(pDb, UnqliteConfigCode.ErrLog, __arglist(out zbuf, out len));
            if(len > 0) {
                return new string(zbuf, 0, len, Encoding.ASCII);
            }
            return null;
        }

        public unsafe string GetDataBaseErrorLog() {
            return GetDataBaseErrorLog(pDb_);
        }

        public UnqliteTransaction BeginTransaction() {
            return new UnqliteTransaction(this);
        }

        /* Database Engine Handle */
        [DllImport(kDllName, CharSet = CharSet.Ansi)]
        public static extern UnqliteResultCode unqlite_open(out IntPtr ppDB, string zFilename, UnqliteOpenModel iMode);
        [DllImport(kDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern UnqliteResultCode unqlite_config(IntPtr pDb, UnqliteConfigCode nOp, __arglist);
        [DllImport(kDllName)]
        public static extern UnqliteResultCode unqlite_close(IntPtr pDb);

        /* Key/Value (KV) Store Interfaces */
        [DllImport(kDllName)]
        public static unsafe extern UnqliteResultCode unqlite_kv_append(IntPtr pDb, void* pKey, int keyLen, void* pdata, Int64 nDataLen);
        [DllImport(kDllName)]
        public static unsafe extern UnqliteResultCode unqlite_kv_store(IntPtr pDb, void* pKey, int keyLen, void* pdata, Int64 nDataLen);
        [DllImport(kDllName)]
        public static unsafe extern UnqliteResultCode unqlite_kv_fetch(IntPtr pDb, void* pKey, int keyLen, void* pdata, out Int64 nDataLen);
        [DllImport(kDllName)]
        public static unsafe extern UnqliteResultCode unqlite_kv_delete(IntPtr pDb, void* pKey, int keyLen);

        /* Manual Transaction Manager */
        [DllImport(kDllName)]
        public static extern UnqliteResultCode unqlite_begin(IntPtr pDb);
        [DllImport(kDllName)]
        public static extern UnqliteResultCode unqlite_commit(IntPtr pDb);
        [DllImport(kDllName)]
        public static extern UnqliteResultCode unqlite_rollback(IntPtr pDb);

        /* Global Library Management Interfaces */
        [DllImport(kDllName)]
        public static unsafe extern UnqliteResultCode unqlite_lib_config(UnqliteLibConfigCode nConfigOp, __arglist);
        [DllImport(kDllName)]
        public static unsafe extern UnqliteResultCode unqlite_lib_init();
        [DllImport(kDllName)]
        public static unsafe extern UnqliteResultCode unqlite_lib_shutdown();
        [DllImport(kDllName)]
        public static unsafe extern int unqlite_lib_is_threadsafe();
        [DllImport(kDllName)]
        public static unsafe extern sbyte* unqlite_lib_version();
        [DllImport(kDllName)]
        public static unsafe extern sbyte* unqlite_lib_signature();
        [DllImport(kDllName)]
        public static unsafe extern sbyte* unqlite_lib_ident();
        [DllImport(kDllName)]
        public static unsafe extern sbyte* unqlite_lib_copyright();
    }

    [Flags]
    public enum UnqliteOpenModel {
        ReadOnly = 0x00000001,              /* Read only mode. Ok for [unqlite_open] */
        ReadWrite = 0x00000002,             /* Ok for [unqlite_open] */
        Create = 0x00000004,                /* Ok for [unqlite_open] */
        Exclusive = 0x00000008,             /* VFS only */
        TempDb = 0x00000010,                /* VFS only */
        NoMutex = 0x00000020,               /* Ok for [unqlite_open] */
        OmitJournaling = 0x00000040,        /* Omit journaling for this database. Ok for [unqlite_open] */
        InMemory = 0x00000080,              /* An in memory database. Ok for [unqlite_open]*/
        MemoryMapped = 0x00000100,          /* Obtain a memory view of the whole file. Ok for [unqlite_open] */
    }

    public enum UnqliteResultCode {
        Ok = 0,                             /* Successful result */
        NoMem = -1,                         /* Out of memory */
        Abort = -10,                        /* Another thread have released this instance */
        IOErr = -2,                         /* IO error */
        Corrupt = -24,                      /* Corrupt pointer */
        Locked = -4,                        /* Forbidden Operation */
        Busy = -14,                         /* The database file is locked */
        Done = -28,                         /* Operation done */
        Perm = -19,                         /* Permission error */
        NotImplemented = -17,               /* Method not implemented by the underlying Key/Value storage engine */
        NotFound = -6,                      /* No such record */
        Noop = -20,                         /* No such method */
        Invalid = -9,                       /* Invalid parameter */
        EOF = -18,                          /* End Of Input */
        Unknown = -13,                      /* Unknown configuration option */
        Limit = -7,                         /* Database limit reached */
        Exists = -11,                       /* Record exists */
        Empty = -3,                         /* Empty record */
        CompileErr = -70,                   /* Compilation error */
        VMErr = 71,                         /* Virtual machine error */
        Full = -73,                         /* Full database (unlikely) */
        CantOpen = -74,                     /* Unable to open the database file */
        ReadOnly = -75,                     /* Read only Key/Value storage engine */
        LockErr = -76,                      /* Locking protocol error */
    }

    public enum UnqliteConfigCode {
        JX9ErrLog = 1,                      /* TWO ARGUMENTS: const char **pzBuf, int *pLen */
        MaxPageCache = 2,                   /* ONE ARGUMENT: int nMaxPage */
        ErrLog = 3,                         /* TWO ARGUMENTS: const char **pzBuf, int *pLen */
        KVEngine = 4,                       /* ONE ARGUMENT: const char *zKvName */
        DisableAutoCommit = 5,              /* NO ARGUMENTS */
        GetKVName = 6,                      /* ONE ARGUMENT: const char **pzPtr */
    }

    public enum UnqliteLibConfigCode {
        UserMalloc = 1,                     /* ONE ARGUMENT: const SyMemMethods *pMemMethods */
        MemErrCallback = 2,                 /* TWO ARGUMENTS: int (*xMemError)(void *), void *pUserData */
        UserMutex = 3,                      /* ONE ARGUMENT: const SyMutexMethods *pMutexMethods */
        ThreadLevelSingle = 4,              /* NO ARGUMENTS */
        ThreadLevelMulti = 5,               /* NO ARGUMENTS */
        VFS = 6,                            /* ONE ARGUMENT: const unqlite_vfs *pVfs */
        StorageEngine = 7,                  /* ONE ARGUMENT: unqlite_kv_methods *pStorage */
        PageSize = 8,                       /* ONE ARGUMENT: int iPageSize */
    }

    public sealed class UnqliteLibConfigSetting {
        public bool IsThreadSafe;
        public int PageSize;
    }

    public sealed class UnqliteException : Exception {
        public UnqliteException(UnqliteResultCode code, string log) : base(!string.IsNullOrEmpty(log) ? log : code.ToString()) {
        }
    }

    public sealed class UnqliteTransaction : IDisposable {
        private Unqlite unqlite_;

        internal UnqliteTransaction(Unqlite unqlite) {
            unqlite_ = unqlite;
            unqlite_.isAutoCommit_ = false;
        }

        private bool IsNeedCommit {
            get {
                return unqlite_.isAutoCommit_ == false;
            }
        }

        public UnqliteResultCode TryCommit() {
            unqlite_.isAutoCommit_ = true;
            UnqliteResultCode code = UnqliteResultCode.Ok;
            unqlite_.TryCommit(ref code);
            return code;
        }

        public void Commit() {
            UnqliteResultCode code = TryCommit();
            if(code != UnqliteResultCode.Ok) {
                new UnqliteException(code, unqlite_.GetDataBaseErrorLog());
            }
        }

        public void Rollback() {
            unqlite_.isAutoCommit_ = true;
            UnqliteResultCode code = unqlite_.Rollback();
            if(code != UnqliteResultCode.Ok) {
                new UnqliteException(code, unqlite_.GetDataBaseErrorLog());
            }
        }

        public void Dispose() {
            if(IsNeedCommit) {
                Commit();
            }
        }
    }
}
