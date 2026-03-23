namespace WatsonTcp
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

    internal sealed class ClientMetadataManager : IDisposable
    {
        #region Internal-Members

        #endregion

        #region Private-Members

        private readonly ReaderWriterLockSlim _Lock = new ReaderWriterLockSlim();
        private Dictionary<Guid, DateTime> _UnauthenticatedClients = new Dictionary<Guid, DateTime>();
        private Dictionary<Guid, ClientMetadata> _Clients = new Dictionary<Guid, ClientMetadata>();
        private Dictionary<Guid, DateTime> _ClientsLastSeen = new Dictionary<Guid, DateTime>();
        private Dictionary<Guid, DateTime> _ClientsKicked = new Dictionary<Guid, DateTime>();
        private Dictionary<Guid, DateTime> _ClientsTimedout = new Dictionary<Guid, DateTime>();

        #endregion

        #region Constructors-and-Factories

        internal ClientMetadataManager()
        {

        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        /// <param name="disposing">Indicate if resources should be disposed.</param>
        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                _Lock.EnterWriteLock();
                try
                {
                    _UnauthenticatedClients = null;
                    _Clients = null;
                    _ClientsLastSeen = null;
                    _ClientsKicked = null;
                    _ClientsTimedout = null;
                }
                finally
                {
                    _Lock.ExitWriteLock();
                }
            }
        }

        #endregion

        #region Internal-Methods

        internal static void Reset()
        {

        }

        internal void ReplaceGuid(Guid original, Guid replace)
        {
            _Lock.EnterWriteLock();
            try
            {
                // Unauthenticated clients
                DateTime dt;
                if (_UnauthenticatedClients.TryGetValue(original, out dt))
                {
                    _UnauthenticatedClients.Remove(original);
                    _UnauthenticatedClients[replace] = dt;
                }

                // Clients
                ClientMetadata md;
                if (_Clients.TryGetValue(original, out md))
                {
                    _Clients.Remove(original);
                    _Clients[replace] = md;
                }

                // Last seen
                if (_ClientsLastSeen.TryGetValue(original, out dt))
                {
                    _ClientsLastSeen.Remove(original);
                    _ClientsLastSeen[replace] = dt;
                }

                // Kicked
                if (_ClientsKicked.TryGetValue(original, out dt))
                {
                    _ClientsKicked.Remove(original);
                    _ClientsKicked[replace] = dt;
                }

                // Timed out
                if (_ClientsTimedout.TryGetValue(original, out dt))
                {
                    _ClientsTimedout.Remove(original);
                    _ClientsTimedout[replace] = dt;
                }
            }
            finally
            {
                _Lock.ExitWriteLock();
            }
        }

        internal void Remove(Guid guid)
        {
            _Lock.EnterWriteLock();
            try
            {
                _UnauthenticatedClients.Remove(guid);
                _Clients.Remove(guid);
                _ClientsLastSeen.Remove(guid);
                _ClientsKicked.Remove(guid);
                _ClientsTimedout.Remove(guid);
            }
            finally
            {
                _Lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Purge stale kicked and timed-out client records older than the specified age.
        /// </summary>
        /// <param name="maxAge">Maximum age of records to keep.</param>
        internal void PurgeStaleRecords(TimeSpan maxAge)
        {
            DateTime cutoff = DateTime.UtcNow - maxAge;
            List<Guid> toRemoveKicked = new List<Guid>();
            List<Guid> toRemoveTimedout = new List<Guid>();

            _Lock.EnterWriteLock();
            try
            {
                foreach (var kvp in _ClientsKicked)
                {
                    if (kvp.Value < cutoff && !_Clients.ContainsKey(kvp.Key))
                    {
                        toRemoveKicked.Add(kvp.Key);
                    }
                }

                foreach (var kvp in _ClientsTimedout)
                {
                    if (kvp.Value < cutoff && !_Clients.ContainsKey(kvp.Key))
                    {
                        toRemoveTimedout.Add(kvp.Key);
                    }
                }

                foreach (Guid guid in toRemoveKicked)
                {
                    _ClientsKicked.Remove(guid);
                }

                foreach (Guid guid in toRemoveTimedout)
                {
                    _ClientsTimedout.Remove(guid);
                }
            }
            finally
            {
                _Lock.ExitWriteLock();
            }
        }

        #region Unauthenticated-Clients

        internal void AddUnauthenticatedClient(Guid guid)
        {
            _Lock.EnterWriteLock();
            try
            {
                _UnauthenticatedClients[guid] = DateTime.UtcNow;
            }
            finally
            {
                _Lock.ExitWriteLock();
            }
        }

        internal void RemoveUnauthenticatedClient(Guid guid)
        {
            _Lock.EnterWriteLock();
            try
            {
                _UnauthenticatedClients.Remove(guid);
            }
            finally
            {
                _Lock.ExitWriteLock();
            }
        }

        internal bool ExistsUnauthenticatedClient(Guid guid)
        {
            _Lock.EnterReadLock();
            try
            {
                return _UnauthenticatedClients.ContainsKey(guid);
            }
            finally
            {
                _Lock.ExitReadLock();
            }
        }

        internal Dictionary<Guid, DateTime> AllUnauthenticatedClients()
        {
            _Lock.EnterReadLock();
            try
            {
                return new Dictionary<Guid, DateTime>(_UnauthenticatedClients);
            }
            finally
            {
                _Lock.ExitReadLock();
            }
        }

        #endregion

        #region Clients

        internal void AddClient(Guid guid, ClientMetadata client)
        {
            _Lock.EnterWriteLock();
            try
            {
                _Clients[guid] = client;
            }
            finally
            {
                _Lock.ExitWriteLock();
            }
        }

        internal ClientMetadata GetClient(Guid guid)
        {
            _Lock.EnterReadLock();
            try
            {
                ClientMetadata md;
                if (_Clients.TryGetValue(guid, out md)) return md;
                return null;
            }
            finally
            {
                _Lock.ExitReadLock();
            }
        }

        internal void RemoveClient(Guid guid)
        {
            _Lock.EnterWriteLock();
            try
            {
                _Clients.Remove(guid);
            }
            finally
            {
                _Lock.ExitWriteLock();
            }
        }

        internal bool ExistsClient(Guid guid)
        {
            _Lock.EnterReadLock();
            try
            {
                return _Clients.ContainsKey(guid);
            }
            finally
            {
                _Lock.ExitReadLock();
            }
        }

        internal Dictionary<Guid, ClientMetadata> AllClients()
        {
            _Lock.EnterReadLock();
            try
            {
                return new Dictionary<Guid, ClientMetadata>(_Clients);
            }
            finally
            {
                _Lock.ExitReadLock();
            }
        }

        internal int ClientCount()
        {
            _Lock.EnterReadLock();
            try
            {
                return _Clients.Count;
            }
            finally
            {
                _Lock.ExitReadLock();
            }
        }

        #endregion

        #region Clients-Last-Seen

        internal void AddClientLastSeen(Guid guid)
        {
            _Lock.EnterWriteLock();
            try
            {
                _ClientsLastSeen[guid] = DateTime.UtcNow;
            }
            finally
            {
                _Lock.ExitWriteLock();
            }
        }

        internal void RemoveClientLastSeen(Guid guid)
        {
            _Lock.EnterWriteLock();
            try
            {
                _ClientsLastSeen.Remove(guid);
            }
            finally
            {
                _Lock.ExitWriteLock();
            }
        }

        internal bool ExistsClientLastSeen(Guid guid)
        {
            _Lock.EnterReadLock();
            try
            {
                return _ClientsLastSeen.ContainsKey(guid);
            }
            finally
            {
                _Lock.ExitReadLock();
            }
        }

        internal void UpdateClientLastSeen(Guid guid, DateTime dt)
        {
            _Lock.EnterWriteLock();
            try
            {
                if (_ClientsLastSeen.ContainsKey(guid))
                {
                    _ClientsLastSeen[guid] = dt.ToUniversalTime();
                }
            }
            finally
            {
                _Lock.ExitWriteLock();
            }
        }

        internal Dictionary<Guid, DateTime> AllClientsLastSeen()
        {
            _Lock.EnterReadLock();
            try
            {
                return new Dictionary<Guid, DateTime>(_ClientsLastSeen);
            }
            finally
            {
                _Lock.ExitReadLock();
            }
        }

        #endregion

        #region Clients-Kicked

        internal void AddClientKicked(Guid guid)
        {
            _Lock.EnterWriteLock();
            try
            {
                if (!_ClientsKicked.ContainsKey(guid))
                    _ClientsKicked[guid] = DateTime.UtcNow;
            }
            finally
            {
                _Lock.ExitWriteLock();
            }
        }

        internal void RemoveClientKicked(Guid guid)
        {
            _Lock.EnterWriteLock();
            try
            {
                _ClientsKicked.Remove(guid);
            }
            finally
            {
                _Lock.ExitWriteLock();
            }
        }

        internal bool ExistsClientKicked(Guid guid)
        {
            _Lock.EnterReadLock();
            try
            {
                return _ClientsKicked.ContainsKey(guid);
            }
            finally
            {
                _Lock.ExitReadLock();
            }
        }

        internal Dictionary<Guid, DateTime> AllClientsKicked()
        {
            _Lock.EnterReadLock();
            try
            {
                return new Dictionary<Guid, DateTime>(_ClientsKicked);
            }
            finally
            {
                _Lock.ExitReadLock();
            }
        }

        #endregion

        #region Clients-Timedout

        internal void AddClientTimedout(Guid guid)
        {
            _Lock.EnterWriteLock();
            try
            {
                if (!_ClientsTimedout.ContainsKey(guid))
                    _ClientsTimedout[guid] = DateTime.UtcNow;
            }
            finally
            {
                _Lock.ExitWriteLock();
            }
        }

        internal void RemoveClientTimedout(Guid guid)
        {
            _Lock.EnterWriteLock();
            try
            {
                _ClientsTimedout.Remove(guid);
            }
            finally
            {
                _Lock.ExitWriteLock();
            }
        }

        internal bool ExistsClientTimedout(Guid guid)
        {
            _Lock.EnterReadLock();
            try
            {
                return _ClientsTimedout.ContainsKey(guid);
            }
            finally
            {
                _Lock.ExitReadLock();
            }
        }

        internal Dictionary<Guid, DateTime> AllClientsTimedout()
        {
            _Lock.EnterReadLock();
            try
            {
                return new Dictionary<Guid, DateTime>(_ClientsTimedout);
            }
            finally
            {
                _Lock.ExitReadLock();
            }
        }

        #endregion

        #endregion

        #region Private-Methods

        #endregion
    }
}
