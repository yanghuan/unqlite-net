using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace UnQLiteNet {
    /// <summary>
    ///  Base class for core interfaces.
    /// </summary>
    public sealed class UnQLite {
        private const int kSmallKeyBufferSize = 128;
        private const int kSmallDataBufferSize = 512;
        private static readonly Encoding Encoding = Encoding.UTF8;

        private IntPtr pDb_;
        internal bool isAutoCommit_ = true;

        private UnQLite(IntPtr pDb) {
            pDb_ = pDb;
        }

        /// <summary>
        ///  Initializes a new instance of the UnQLite class.
        /// </summary>
        /// <remarks>
        ///  Opening a new database connection.
        ///  If fileName is ":mem:", then a private, in-memory database is created for the connection.
        ///  The in-memory database will vanish when the database connection is closed.Future versions 
        ///  of UnQLite might make use of additional special filenames that begin with the ":" character. 
        ///  It is recommended that when a database filename actually does begin with a ":" character 
        ///  you should prefix the filename with a pathname such as "./" to avoid ambiguity.
        ///  Note: Transactions are not supported for in-memory databases.
        ///  Note: This routine does not open the target database file. 
        ///  It merely initialize and prepare the database object handle for later usage.
        /// </remarks>
        /// <param name="fileName">The Operation file.</param>
        /// <param name="model">Control the database access mode.</param>
        /// <exception cref="System.ArgumentNullException">
        ///  fileName is null.
        /// </exception>
        /// <exception cref="UnQLiteException">
        /// The UnQLiteException when open file.
        /// </exception>
        public UnQLite(string fileName, UnQLiteOpenModel model) {
            if(fileName == null) {
                throw new ArgumentNullException("fileName");
            }

            UnQLiteResultCode code = UnsafeNativeMethods.unqlite_open(out pDb_, fileName, model);
            if(code != UnQLiteResultCode.Ok) {
                throw new UnQLiteException(code, GetDataBaseErrorLog());
            }
        }

        /// <summary>
        /// Initializes a new instance of the UnQLite class without UnQLiteException.
        /// </summary>
        /// <exception cref="System.ArgumentNullException">
        ///  fileName is null.
        /// </exception>
        public static UnQLiteResultCode TryOpen(string fileName, UnQLiteOpenModel model, out UnQLite unqlite) {
            if(fileName == null) {
                throw new ArgumentNullException("fileName");
            }

            unqlite = null;
            IntPtr pDB;
            UnQLiteResultCode code = UnsafeNativeMethods.unqlite_open(out pDB, fileName, model);
            if(code == UnQLiteResultCode.Ok) {
                unqlite = new UnQLite(pDB);
            }
            return code;
        }

        /// <summary>
        /// The pointer of native unqlite object.
        /// </summary>
        public IntPtr DbPtr {
            get {
                return pDb_;
            }
        }

        /// <summary>
        /// Closing the database instance.destroyed and all associated resources are deallocated.
        /// </summary>
        /// <remarks>
        /// If Close is invoked while a transaction is open, the transaction is automatically committed.
        /// Each database must be closed in order to avoid memory leaks and malformed database image.
        /// </remarks>
        public void Close() {
            if(pDb_ != IntPtr.Zero) {
                UnsafeNativeMethods.unqlite_close(pDb_);
                pDb_ = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Determines whether thread-safe.
        /// </summary>
        /// <remarks>
        /// This is the default mode when UnQLite is compiled with threading support.  
        /// It will be true after the UnQLite library initialized. The first
        /// call to unqlite_open() will automatically initialize the library.
        /// Change thread-safe you can invoke UnQLite.LibInit method. 
        /// </remarks>
        public static bool IsThreadSafe {
            get {
                return UnsafeNativeMethods.unqlite_lib_is_threadsafe() == 1;
            }
        }
        /// <summary>
        ///  The current version of the UnQLite engine.
        /// </summary>
        public unsafe static string Version {
            get {
                return new string(UnsafeNativeMethods.unqlite_lib_version());
            }
        }

        /// <summary>
        ///  The library signature of the UnQLite engine.
        /// </summary>
        public unsafe static string Signature {
            get {
                return new string(UnsafeNativeMethods.unqlite_lib_signature());
            }
        }

        /// <summary>
        ///  The library identification of the UnQLite engine in the Symisc source tree.
        /// </summary>
        public unsafe static string Ident {
            get {
                return new string(UnsafeNativeMethods.unqlite_lib_ident());
            }
        }

        /// <summary>
        /// The copyright notice of the UnQLite engine.
        /// </summary>
        public unsafe static string Copyright {
            get {
                return new string(UnsafeNativeMethods.unqlite_lib_copyright());
            }
        }

        /// <summary>
        /// initializes the UnQLite library. It should be invoked before other methods called.  
        /// </summary>
        /// <exception cref="System.ArgumentNullException">
        /// setting is null
        /// </exception>
        /// <exception cref="UnQLiteException">
        /// The UnQLiteException when initializes the library.
        /// </exception>
        public static void LibInit(UnQLiteLibConfigSetting setting) {
            if(setting == null) {
                throw new ArgumentNullException("setting");
            }

            UnQLiteLibConfigCode config = setting.IsThreadSafe ? UnQLiteLibConfigCode.ThreadLevelMulti : UnQLiteLibConfigCode.ThreadLevelSingle;
            UnQLiteResultCode code = UnsafeNativeMethods.unqlite_lib_config(config, __arglist());
            if(code != UnQLiteResultCode.Ok) {
                throw new UnQLiteException(code, null);
            }

            if(setting.PageSize > 0) {
                code = UnsafeNativeMethods.unqlite_lib_config(UnQLiteLibConfigCode.PageSize, __arglist(setting.PageSize));
                if(code != UnQLiteResultCode.Ok) {
                    throw new UnQLiteException(code, null);
                }
            }

            code = UnsafeNativeMethods.unqlite_lib_init();
            if(code != UnQLiteResultCode.Ok) {
                throw new UnQLiteException(code, null);
            }
        }

        internal void TryCommit(ref UnQLiteResultCode code) {
            if(code == UnQLiteResultCode.Ok && isAutoCommit_) {
                code = UnsafeNativeMethods.unqlite_commit(pDb_);
                if(code != UnQLiteResultCode.Ok) {
                    if(code != UnQLiteResultCode.Busy && code != UnQLiteResultCode.NotImplemented) {
                        Rollback();
                    }
                }
            }
        }

        internal UnQLiteResultCode Rollback() {
            return UnsafeNativeMethods.unqlite_rollback(pDb_);
        }

        private unsafe UnQLiteResultCode InternalTryAppendRaw(byte* keyBuffer, int keyCount, ArraySegment<byte> data) {
            fixed (byte* pdata = data.Array) {
                UnQLiteResultCode code = UnsafeNativeMethods.unqlite_kv_append(pDb_, keyBuffer, keyCount, pdata + data.Offset, data.Count);
                TryCommit(ref code);
                return code;
            }
        }

        /// <summary>
        /// Append binary data to a database record without UnQLiteException.
        /// </summary>
        /// <param name="key">Record key.</param>
        /// <param name="data">Record data.</param>
        /// <returns>The result code.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// key is null
        /// </exception>
        public unsafe UnQLiteResultCode TryAppendRaw(string key, ArraySegment<byte> data) {
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

        /// <summary>
        /// Append binary data to a database record.
        /// </summary>
        /// <remarks>
        /// Write a new record into the database. If the record does not exists, it is created. 
        /// Otherwise, the new data chunk is appended to the end of the old chunk.
        /// </remarks>
        /// <param name="key">Record key.</param>
        /// <param name="data">Record data.</param>
        /// <exception cref="System.ArgumentNullException">
        /// key is null
        /// </exception>
        /// <exception cref="UnQLiteException">
        /// An UnQLiteException occurred.
        /// </exception>
        public void AppendRaw(string key, ArraySegment<byte> data) {
            UnQLiteResultCode code = TryAppendRaw(key, data);
            if(code != UnQLiteResultCode.Ok) {
                throw new UnQLiteException(code, GetDataBaseErrorLog());
            }
        }

        private unsafe UnQLiteResultCode InternalTryAppend(byte* keyBuffer, int keyCount, string data) {
            UnQLiteResultCode code;
            int dataCount = Encoding.GetByteCount(data);
            if(dataCount <= kSmallDataBufferSize) {
                byte* dataBuffer = stackalloc byte[dataCount];
                if(dataCount > 0) {
                    fixed (char* dataPtr = data) {
                        Encoding.GetBytes(dataPtr, data.Length, dataBuffer, dataCount);
                    }
                }
                code = UnsafeNativeMethods.unqlite_kv_append(pDb_, keyBuffer, keyCount, dataBuffer, dataCount);
            }
            else {
                byte[] dataBytes = Encoding.GetBytes(data);
                fixed (byte* dataBuffer = dataBytes) {
                    code = UnsafeNativeMethods.unqlite_kv_append(pDb_, keyBuffer, keyCount, dataBuffer, dataBytes.Length);
                }
            }
            TryCommit(ref code);
            return code;
        }

        /// <summary>
        /// Append string data to a database record without UnQLiteException.
        /// </summary>
        /// <param name="key">Record key.</param>
        /// <param name="data">Record data.</param>
        /// <returns>The result code.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// key or data is null
        /// </exception>
        public unsafe UnQLiteResultCode TryAppend(string key, string data) {
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

        /// <summary>
        /// Append string data to a database record.
        /// </summary>
        /// <remarks>
        /// Write a new record into the database. If the record does not exists, it is created. 
        /// Otherwise, the new data chunk is appended to the end of the old chunk.
        /// </remarks>
        /// <param name="key">Record key.</param>
        /// <param name="data">Record data.</param>
        /// <exception cref="System.ArgumentNullException">
        /// key or data is null
        /// </exception>
        /// <exception cref="UnQLiteException">
        /// An UnQLiteException occurred.
        /// </exception>
        public void Append(string key, string data) {
            UnQLiteResultCode code = TryAppend(key, data);
            if(code != UnQLiteResultCode.Ok) {
                throw new UnQLiteException(code, GetDataBaseErrorLog());
            }
        }

        private unsafe UnQLiteResultCode InteranlTryGetRaw(byte* keyBuffer, int keyCount, out byte[] data) {
            data = null;
            long dataLength;
            UnQLiteResultCode code = UnsafeNativeMethods.unqlite_kv_fetch(pDb_, keyBuffer, keyCount, null, out dataLength);
            if(code == 0) {
                byte[] valueBytes = new byte[dataLength];
                fixed (byte* valueBuffer = valueBytes) {
                    code = UnsafeNativeMethods.unqlite_kv_fetch(pDb_, keyBuffer, keyCount, valueBuffer, out dataLength);
                    if(code == 0) {
                        data = valueBytes;
                    }
                }
            }
            return code;
        }

        /// <summary>
        ///  Get a binary record from the database without UnQLiteException.
        /// </summary>
        /// <param name="key">Record key.</param>
        /// <param name="data">Record data.</param>
        /// <returns>The result code.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// key is null
        /// </exception>
        public unsafe UnQLiteResultCode TryGetRaw(string key, out byte[] data) {
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

        /// <summary>
        /// Get a binary record from the database.
        /// </summary>
        /// <param name="key">Record key.</param>
        /// <returns>Record data.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// key is null
        /// </exception>
        /// <exception cref="UnQLiteException">
        /// An UnQLiteException occurred.
        /// </exception>
        public byte[] GetRaw(string key) {
            byte[] data;
            UnQLiteResultCode code = TryGetRaw(key, out data);
            if(code == UnQLiteResultCode.Ok || code == UnQLiteResultCode.NotFound) {
                return data;
            }
            throw new UnQLiteException(code, GetDataBaseErrorLog());
        }

        private unsafe UnQLiteResultCode InternalTryGet(byte* keyBuffer, int keyCount, out string data) {
            data = null;
            long dataLength;
            UnQLiteResultCode code = UnsafeNativeMethods.unqlite_kv_fetch(pDb_, keyBuffer, keyCount, null, out dataLength);
            if(code == 0) {
                if(dataLength <= kSmallDataBufferSize) {
                    int len = (int)dataLength;
                    byte* dataBuffer = stackalloc byte[len];
                    code = UnsafeNativeMethods.unqlite_kv_fetch(pDb_, keyBuffer, keyCount, dataBuffer, out dataLength);
                    if(code == 0) {
                        data = new string((sbyte*)dataBuffer, 0, len, Encoding);
                    }
                }
                else {
                    byte[] dataBytes = new byte[dataLength];
                    fixed (byte* dataBuffer = dataBytes) {
                        code = UnsafeNativeMethods.unqlite_kv_fetch(pDb_, keyBuffer, keyCount, dataBuffer, out dataLength);
                        if(code == 0) {
                            data = Encoding.GetString(dataBytes);
                        }
                    }
                }
            }
            return code;
        }

        /// <summary>
        ///  Get a string record from the database without UnQLiteException.
        /// </summary>
        /// <param name="key">Record key.</param>
        /// <param name="data">Record data.</param>
        /// <returns>The result code.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// key is null
        /// </exception>
        public unsafe UnQLiteResultCode TryGet(string key, out string data) {
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

        /// <summary>
        /// Get a string record from the database.
        /// </summary>
        /// <param name="key">Record key.</param>
        /// <returns>Record data.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// key is null
        /// </exception>
        /// <exception cref="UnQLiteException">
        /// An UnQLiteException occurred.
        /// </exception>
        public string Get(string key) {
            string value;
            UnQLiteResultCode code = TryGet(key, out value);
            if(code == UnQLiteResultCode.Ok || code == UnQLiteResultCode.NotFound) {
                return value;
            }
            throw new UnQLiteException(code, GetDataBaseErrorLog());
        }

        private unsafe UnQLiteResultCode InternalTrySaveRaw(byte* keyBuffer, int keyCount, ArraySegment<byte> data) {
            fixed (byte* dataBuffer = data.Array) {
                UnQLiteResultCode code = UnsafeNativeMethods.unqlite_kv_store(pDb_, keyBuffer, keyCount, dataBuffer + data.Offset, data.Count);
                TryCommit(ref code);
                return code;
            }
        }

        /// <summary>
        /// Store binary record in the database without UnQLiteException.
        /// </summary>
        /// <param name="key">Record key.</param>
        /// <param name="data">Record data.</param>
        /// <exception cref="System.ArgumentNullException">
        /// key is null
        /// </exception>
        /// <returns>The result code.</returns>
        public unsafe UnQLiteResultCode TrySaveRaw(string key, ArraySegment<byte> data) {
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

        /// <summary>
        /// Store binary record in the database.
        /// </summary>
        /// <remarks>
        /// Write a new record into the database. If the record does not exists, it is created. 
        /// Otherwise, it is replaced. That is, the new data overwrite the old data.
        /// </remarks>
        /// <param name="key">Record key.</param>
        /// <param name="data">Record data.</param>
        /// <exception cref="System.ArgumentNullException">
        /// key is null
        /// </exception>
        /// <exception cref="UnQLiteException">
        /// An UnQLiteException occurred.
        /// </exception>
        public void SaveRaw(string key, ArraySegment<byte> data) {
            UnQLiteResultCode code = TrySaveRaw(key, data);
            if(code != UnQLiteResultCode.Ok) {
                throw new UnQLiteException(code, GetDataBaseErrorLog());
            }
        }

        private unsafe UnQLiteResultCode InternalTrySave(byte* keyBuffer, int keyCount, string data) {
            UnQLiteResultCode code;
            int dataCount = Encoding.GetByteCount(data);
            if(dataCount <= kSmallDataBufferSize) {
                byte* dataBuffer = stackalloc byte[dataCount];
                if(dataCount > 0) {
                    fixed (char* dataPtr = data) {
                        Encoding.GetBytes(dataPtr, data.Length, dataBuffer, dataCount);
                    }
                }
                code = UnsafeNativeMethods.unqlite_kv_store(pDb_, keyBuffer, keyCount, dataBuffer, dataCount);
            }
            else {
                byte[] dataBytes = Encoding.GetBytes(data);
                fixed (byte* dataBuffer = dataBytes) {
                    code = UnsafeNativeMethods.unqlite_kv_store(pDb_, keyBuffer, keyCount, dataBuffer, dataBytes.Length);
                }
            }
            TryCommit(ref code);
            return code;
        }

        /// <summary>
        /// Store string record in the database without UnQLiteException.
        /// </summary>
        /// <param name="key">Record key.</param>
        /// <param name="data">Record data.</param>
        /// <exception cref="System.ArgumentNullException">
        /// key is null
        /// </exception>
        /// <returns>The result code.</returns>
        public unsafe UnQLiteResultCode TrySave(string key, string data) {
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

        /// <summary>
        /// Store string record in the database.
        /// </summary>
        /// <remarks>
        /// Write a new record into the database. If the record does not exists, it is created. 
        /// Otherwise, it is replaced. That is, the new data overwrite the old data.
        /// </remarks>
        /// <param name="key">Record key.</param>
        /// <param name="data">Record data.</param>
        /// <exception cref="System.ArgumentNullException">
        /// key is null
        /// </exception>
        /// <exception cref="UnQLiteException">
        /// An UnQLiteException occurred.
        /// </exception>
        public void Save(string key, string data) {
            UnQLiteResultCode code = TrySave(key, data);
            if(code != UnQLiteResultCode.Ok) {
                throw new UnQLiteException(code, GetDataBaseErrorLog());
            }
        }

        private unsafe UnQLiteResultCode InternalTryRemove(byte* keyBuffer, int keyCount) {
            UnQLiteResultCode code = UnsafeNativeMethods.unqlite_kv_delete(pDb_, keyBuffer, keyCount);
            TryCommit(ref code);
            return code;
        }

        /// <summary>
        /// Remove the record from the database without UnQLiteException.
        /// </summary>
        /// <param name="key">Record key.</param>
        /// <exception cref="System.ArgumentNullException">
        /// key is null
        /// </exception>
        public unsafe UnQLiteResultCode TryRemove(string key) {
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

        /// <summary>
        /// Remove the record from the database.
        /// </summary>
        /// <remarks>
        /// To remove a particular record from the database, 
        /// you can use this high-level thread-safe routine to perform the deletion.
        /// </remarks>
        /// <param name="key">Record key.</param>
        /// <exception cref="System.ArgumentNullException">
        /// key is null
        /// </exception>
        /// <exception cref="UnQLiteException">
        /// An UnQLiteException occurred.
        /// </exception>
        public void Remove(string key) {
            UnQLiteResultCode code = TryRemove(key);
            if(code != UnQLiteResultCode.Ok) {
                throw new UnQLiteException(code, GetDataBaseErrorLog());
            }
        }

        /// <summary>
        ///  Get the error log of a database.
        /// </summary>
        /// <param name="pDb">The pointer of database object.</param>
        /// <returns></returns>
        public unsafe static string GetDataBaseErrorLog(IntPtr pDb) {
            sbyte* zbuf;
            int len;
            UnsafeNativeMethods.unqlite_config(pDb, UnQLiteConfigCode.ErrLog, __arglist(out zbuf, out len));
            if(len > 0) {
                return new string(zbuf, 0, len, Encoding.ASCII);
            }
            return null;
        }

        /// <summary>
        /// Get the error log of the database.
        /// </summary>
        /// <remarks>
        /// When something goes wrong during a commit, rollback, store, append operation, 
        /// a human-readable error message is generated to help clients diagnostic the problem.
        /// </remarks>
        public unsafe string GetDataBaseErrorLog() {
            return GetDataBaseErrorLog(pDb_);
        }

        /// <summary>
        /// Manually begin a write-transaction on the database.
        /// </summary>
        public UnQLiteTransaction BeginTransaction() {
            return new UnQLiteTransaction(this);
        }

        /// <summary>
        /// Core export Interfaces.Not recommended for direct use.
        /// </summary>
        /// <remarks>
        /// Referenced https://www.unqlite.org/api_intro.html
        /// </remarks>
        public static class UnsafeNativeMethods {
            private const string kDllName = "unqlite";

            #region  /* Database Engine Handle */
            /// <summary>
            /// The native original function
            /// </summary>
            [DllImport(kDllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
            public static extern UnQLiteResultCode unqlite_open(out IntPtr ppDB, string zFilename, UnQLiteOpenModel iMode);
            /// <summary>
            /// The native original function
            /// </summary>
            [DllImport(kDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern UnQLiteResultCode unqlite_config(IntPtr pDb, UnQLiteConfigCode nOp, __arglist);
            /// <summary>
            /// The native original function
            /// </summary>
            [DllImport(kDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern UnQLiteResultCode unqlite_close(IntPtr pDb);
            #endregion

            #region  /* Key/Value (KV) Store Interfaces */
            /// <summary>
            /// The native original function
            /// </summary>
            [DllImport(kDllName, CallingConvention = CallingConvention.Cdecl)]
            public static unsafe extern UnQLiteResultCode unqlite_kv_append(IntPtr pDb, void* pKey, int keyLen, void* pdata, Int64 nDataLen);
            /// <summary>
            /// The native original function
            /// </summary>
            [DllImport(kDllName, CallingConvention = CallingConvention.Cdecl)]
            public static unsafe extern UnQLiteResultCode unqlite_kv_store(IntPtr pDb, void* pKey, int keyLen, void* pdata, Int64 nDataLen);
            /// <summary>
            /// The native original function
            /// </summary>
            [DllImport(kDllName, CallingConvention = CallingConvention.Cdecl)]
            public static unsafe extern UnQLiteResultCode unqlite_kv_fetch(IntPtr pDb, void* pKey, int keyLen, void* pdata, out Int64 nDataLen);
            /// <summary>
            /// The native original function
            /// </summary>
            [DllImport(kDllName, CallingConvention = CallingConvention.Cdecl)]
            public static unsafe extern UnQLiteResultCode unqlite_kv_delete(IntPtr pDb, void* pKey, int keyLen);
            #endregion

            #region  /* Manual Transaction Manager */
            /// <summary>
            /// The native original function
            /// </summary>
            [DllImport(kDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern UnQLiteResultCode unqlite_begin(IntPtr pDb);
            /// <summary>
            /// The native original function
            /// </summary>
            [DllImport(kDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern UnQLiteResultCode unqlite_commit(IntPtr pDb);
            /// <summary>
            /// The native original function
            /// </summary>
            [DllImport(kDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern UnQLiteResultCode unqlite_rollback(IntPtr pDb);
            #endregion

            #region /* Global Library Management Interfaces */
            /// <summary>
            /// The native original function
            /// </summary>
            [DllImport(kDllName, CallingConvention = CallingConvention.Cdecl)]
            public static unsafe extern UnQLiteResultCode unqlite_lib_config(UnQLiteLibConfigCode nConfigOp, __arglist);
            /// <summary>
            /// The native original function
            /// </summary>
            [DllImport(kDllName, CallingConvention = CallingConvention.Cdecl)]
            public static unsafe extern UnQLiteResultCode unqlite_lib_init();
            /// <summary>
            /// The native original function
            /// </summary>
            [DllImport(kDllName, CallingConvention = CallingConvention.Cdecl)]
            public static unsafe extern UnQLiteResultCode unqlite_lib_shutdown();
            /// <summary>
            /// The native original function
            /// </summary>
            [DllImport(kDllName, CallingConvention = CallingConvention.Cdecl)]
            public static unsafe extern int unqlite_lib_is_threadsafe();
            /// <summary>
            /// The native original function
            /// </summary>
            [DllImport(kDllName, CallingConvention = CallingConvention.Cdecl)]
            public static unsafe extern sbyte* unqlite_lib_version();
            /// <summary>
            /// The native original function
            /// </summary>
            [DllImport(kDllName, CallingConvention = CallingConvention.Cdecl)]
            public static unsafe extern sbyte* unqlite_lib_signature();
            /// <summary>
            /// The native original function
            /// </summary>
            [DllImport(kDllName, CallingConvention = CallingConvention.Cdecl)]
            public static unsafe extern sbyte* unqlite_lib_ident();
            /// <summary>
            /// The native original function
            /// </summary>
            [DllImport(kDllName, CallingConvention = CallingConvention.Cdecl)]
            public static unsafe extern sbyte* unqlite_lib_copyright();
            #endregion
        }
    }

    /// <summary>
    /// The open mode codes.
    /// </summary>
    [Flags]
    public enum UnQLiteOpenModel {
        /// <summary>
        /// Read only mode. Ok for [unqlite_open] 
        /// </summary>
        ReadOnly = 0x00000001,
        /// <summary>
        /// Ok for [unqlite_open]
        /// </summary>
        ReadWrite = 0x00000002,
        /// <summary>
        /// Ok for [unqlite_open]
        /// </summary>
        Create = 0x00000004,
        /// <summary>
        /// VFS only
        /// </summary>
        Exclusive = 0x00000008,
        /// <summary>
        /// VFS only
        /// </summary>
        TempDb = 0x00000010,
        /// <summary>
        /// Ok for [unqlite_open]
        /// </summary>
        NoMutex = 0x00000020,
        /// <summary>
        /// Omit journaling for this database. Ok for [unqlite_open]
        /// </summary>
        OmitJournaling = 0x00000040,
        /// <summary>
        /// An in memory database. Ok for [unqlite_open]
        /// </summary>
        InMemory = 0x00000080,
        /// <summary>
        /// Obtain a memory view of the whole file. Ok for [unqlite_open]
        /// </summary>
        MemoryMapped = 0x00000100,
    }

    /// <summary>
    ///  Standard UnQLite result codes.
    /// </summary>
    public enum UnQLiteResultCode {
        /// <summary>
        /// Successful result
        /// </summary>
        Ok = 0,
        /// <summary>
        /// Out of memory
        /// </summary>
        NoMem = -1,
        /// <summary>
        /// Another thread have released this instance
        /// </summary>
        Abort = -10,
        /// <summary>
        /// IO error
        /// </summary>
        IOErr = -2,
        /// <summary>
        /// Corrupt pointer
        /// </summary>
        Corrupt = -24,
        /// <summary>
        /// Forbidden Operation
        /// </summary>
        Locked = -4,
        /// <summary>
        /// The database file is locked 
        /// </summary>
        Busy = -14,
        /// <summary>
        /// Operation done
        /// </summary>
        Done = -28,
        /// <summary>
        /// Permission error
        /// </summary>
        Perm = -19,
        /// <summary>
        /// Method not implemented by the underlying Key/Value storage engine
        /// </summary>
        NotImplemented = -17,
        /// <summary>
        /// No such record
        /// </summary>
        NotFound = -6,
        /// <summary>
        /// No such method
        /// </summary>
        Noop = -20,
        /// <summary>
        /// Invalid parameter
        /// </summary>
        Invalid = -9,
        /// <summary>
        /// End Of Input
        /// </summary>
        EOF = -18,
        /// <summary>
        /// Unknown configuration option
        /// </summary>
        Unknown = -13,
        /// <summary>
        /// Database limit reached
        /// </summary>
        Limit = -7,
        /// <summary>
        /// Record exists
        /// </summary>
        Exists = -11,
        /// <summary>
        /// Empty record
        /// </summary>
        Empty = -3,
        /// <summary>
        /// Compilation error
        /// </summary>
        CompileErr = -70,
        /// <summary>
        /// Virtual machine error
        /// </summary>
        VMErr = 71,
        /// <summary>
        /// Full database (unlikely)
        /// </summary>
        Full = -73,
        /// <summary>
        /// Unable to open the database file
        /// </summary>
        CantOpen = -74,
        /// <summary>
        /// Read only Key/Value storage engine
        /// </summary>
        ReadOnly = -75,
        /// <summary>
        /// Locking protocol error
        /// </summary>
        LockErr = -76,          
    }

    /// <summary>
    /// Database Handle Configuration Commands.
    /// </summary>
    public enum UnQLiteConfigCode {
        /// <summary>
        /// TWO ARGUMENTS: const char **pzBuf, int *pLen
        /// </summary>
        JX9ErrLog = 1,
        /// <summary>
        /// ONE ARGUMENT: int nMaxPage
        /// </summary>
        MaxPageCache = 2,
        /// <summary>
        /// TWO ARGUMENTS: const char **pzBuf, int *pLen
        /// </summary>
        ErrLog = 3,
        /// <summary>
        /// ONE ARGUMENT: const char *zKvName
        /// </summary>
        KVEngine = 4,
        /// <summary>
        /// NO ARGUMENTS
        /// </summary>
        DisableAutoCommit = 5,
        /// <summary>
        /// ONE ARGUMENT: const char **pzPtr
        /// </summary>
        GetKVName = 6,             
    }

    /// <summary>
    /// Global Library Configuration Commands.
    /// </summary>
    public enum UnQLiteLibConfigCode {
        /// <summary>
        /// ONE ARGUMENT: const SyMemMethods *pMemMethods
        /// </summary>
        UserMalloc = 1,
        /// <summary>
        /// TWO ARGUMENTS: int (*xMemError)(void *), void *pUserData
        /// </summary>
        MemErrCallback = 2,
        /// <summary>
        /// ONE ARGUMENT: const SyMutexMethods *pMutexMethods
        /// </summary>
        UserMutex = 3,
        /// <summary>
        /// NO ARGUMENTS
        /// </summary>
        ThreadLevelSingle = 4,
        /// <summary>
        /// NO ARGUMENTS
        /// </summary>
        ThreadLevelMulti = 5,
        /// <summary>
        /// ONE ARGUMENT: const unqlite_vfs *pVfs
        /// </summary>
        VFS = 6,
        /// <summary>
        /// ONE ARGUMENT: unqlite_kv_methods *pStorage
        /// </summary>
        StorageEngine = 7,
        /// <summary>
        /// ONE ARGUMENT: int iPageSize
        /// </summary>
        PageSize = 8,             
    }

    /// <summary>
    /// The UnQLite library setting class.
    /// </summary>
    public sealed class UnQLiteLibConfigSetting {
        /// <summary>
        /// This option sets the threading mode whether thread-safe.
        /// </summary>
        public bool IsThreadSafe;
        /// <summary>
        /// This option let you set a new database page size in bytes.
        /// </summary>
        /// <remarks>
        /// The default page size (4096 Bytes) is recommended for most applications, 
        /// but application can use this option to experiment with other page sizes. 
        /// A valid page size must be a power of two between 512 and 65535.
        /// </remarks>
        public int PageSize;
    }

    /// <summary>
    /// UnQLite exception class.
    /// </summary>
    public sealed class UnQLiteException : Exception {
        /// <summary>
        /// Public constructor for generating a UnQLite exception given the result code and message.
        /// </summary>
        /// <param name="code">The UnQLite return code to report.</param>
        /// <param name="message">Message text to go along with the return code message text.</param>
        public UnQLiteException(UnQLiteResultCode code, string message) : base(!string.IsNullOrEmpty(message) ? message : code.ToString()) {
        }
    }

    /// <summary>
    /// Manual Transaction Manager
    /// </summary>
    public sealed class UnQLiteTransaction : IDisposable {
        private UnQLite unQLite_;

        internal UnQLiteTransaction(UnQLite unqlite) {
            unQLite_ = unqlite;
            unQLite_.isAutoCommit_ = false;
        }

        private bool IsNeedCommit {
            get {
                return unQLite_.isAutoCommit_ == false;
            }
        }

        /// <summary>
        /// Commit all changes to the database without UnQLiteException.
        /// </summary>
        /// <returns>The result code.</returns>
        public UnQLiteResultCode TryCommit() {
            unQLite_.isAutoCommit_ = true;
            UnQLiteResultCode code = UnQLiteResultCode.Ok;
            unQLite_.TryCommit(ref code);
            return code;
        }

        /// <summary>
        /// Commit all changes to the database.
        /// </summary>
        /// <exception cref="UnQLiteException">
        /// An UnQLiteException occurred.
        /// </exception>
        public void Commit() {
            UnQLiteResultCode code = TryCommit();
            if(code != UnQLiteResultCode.Ok) {
                new UnQLiteException(code, unQLite_.GetDataBaseErrorLog());
            }
        }

        /// <summary>
        /// Rollback a write-transaction on the database without UnQLiteException.
        /// </summary>
        /// <returns>The result code.</returns>
        public UnQLiteResultCode TryRollback() {
            unQLite_.isAutoCommit_ = true;
            return unQLite_.Rollback();
        }

        /// <summary>
        /// Rollback a write-transaction on the database.
        /// </summary>
        /// <exception cref="UnQLiteException">
        /// An UnQLiteException occurred.
        /// </exception>
        public void Rollback() {
            UnQLiteResultCode code = TryRollback();
            if(code != UnQLiteResultCode.Ok) {
                new UnQLiteException(code, unQLite_.GetDataBaseErrorLog());
            }
        }

        /// <summary>
        /// Disposes the transaction, if applicable.
        /// </summary>
        public void Dispose() {
            if(IsNeedCommit) {
                Commit();
            }
        }
    }
}
