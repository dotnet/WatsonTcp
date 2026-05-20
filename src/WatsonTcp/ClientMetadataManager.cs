namespace WatsonTcp
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    internal sealed class ClientMetadataManager : IDisposable
    {
        #region Private-Members

        private ConcurrentDictionary<Guid, DateTime> _UnauthenticatedClients = new ConcurrentDictionary<Guid, DateTime>();
        private ConcurrentDictionary<Guid, ClientMetadata> _PendingClients = new ConcurrentDictionary<Guid, ClientMetadata>();
        private ConcurrentDictionary<Guid, ClientMetadata> _Clients = new ConcurrentDictionary<Guid, ClientMetadata>();
        private ConcurrentDictionary<Guid, DateTime> _ClientsKicked = new ConcurrentDictionary<Guid, DateTime>();
        private ConcurrentDictionary<Guid, DateTime> _ClientsTimedout = new ConcurrentDictionary<Guid, DateTime>();

        #endregion

        #region Public-Methods

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!disposing) return;

            _UnauthenticatedClients = null;
            _PendingClients = null;
            _Clients = null;
            _ClientsKicked = null;
            _ClientsTimedout = null;
        }

        #endregion

        #region Internal-Methods

        internal static void Reset()
        {
        }

        internal void ReplaceGuid(Guid original, Guid replace)
        {
            if (_UnauthenticatedClients.TryRemove(original, out DateTime unauthenticated))
            {
                _UnauthenticatedClients[replace] = unauthenticated;
            }

            if (_PendingClients.TryRemove(original, out ClientMetadata pending))
            {
                _PendingClients[replace] = pending;
            }

            if (_Clients.TryRemove(original, out ClientMetadata client))
            {
                _Clients[replace] = client;
            }

            if (_ClientsKicked.TryRemove(original, out DateTime kicked))
            {
                _ClientsKicked[replace] = kicked;
            }

            if (_ClientsTimedout.TryRemove(original, out DateTime timedout))
            {
                _ClientsTimedout[replace] = timedout;
            }
        }

        internal void Remove(Guid guid)
        {
            _UnauthenticatedClients.TryRemove(guid, out _);
            _PendingClients.TryRemove(guid, out _);
            _Clients.TryRemove(guid, out _);
            _ClientsKicked.TryRemove(guid, out _);
            _ClientsTimedout.TryRemove(guid, out _);
        }

        /// <summary>
        /// Purge stale kicked and timed-out client records older than the specified age.
        /// </summary>
        /// <param name="maxAge">Maximum age of records to keep.</param>
        internal void PurgeStaleRecords(TimeSpan maxAge)
        {
            DateTime cutoff = DateTime.UtcNow - maxAge;

            foreach (KeyValuePair<Guid, DateTime> kvp in _ClientsKicked)
            {
                if (kvp.Value < cutoff && !_Clients.ContainsKey(kvp.Key))
                {
                    _ClientsKicked.TryRemove(kvp.Key, out _);
                }
            }

            foreach (KeyValuePair<Guid, DateTime> kvp in _ClientsTimedout)
            {
                if (kvp.Value < cutoff && !_Clients.ContainsKey(kvp.Key))
                {
                    _ClientsTimedout.TryRemove(kvp.Key, out _);
                }
            }
        }

        #region Unauthenticated-Clients

        internal void AddUnauthenticatedClient(Guid guid)
        {
            _UnauthenticatedClients[guid] = DateTime.UtcNow;
        }

        internal void RemoveUnauthenticatedClient(Guid guid)
        {
            _UnauthenticatedClients.TryRemove(guid, out _);
        }

        internal bool ExistsUnauthenticatedClient(Guid guid)
        {
            return _UnauthenticatedClients.ContainsKey(guid);
        }

        internal Dictionary<Guid, DateTime> AllUnauthenticatedClients()
        {
            return new Dictionary<Guid, DateTime>(_UnauthenticatedClients);
        }

        #endregion

        #region Pending-Clients

        internal void AddPendingClient(Guid guid, ClientMetadata client)
        {
            _PendingClients[guid] = client;
        }

        internal ClientMetadata GetPendingClient(Guid guid)
        {
            _PendingClients.TryGetValue(guid, out ClientMetadata md);
            return md;
        }

        internal void RemovePendingClient(Guid guid)
        {
            _PendingClients.TryRemove(guid, out _);
        }

        internal bool ExistsPendingClient(Guid guid)
        {
            return _PendingClients.ContainsKey(guid);
        }

        internal Dictionary<Guid, ClientMetadata> AllPendingClients()
        {
            return new Dictionary<Guid, ClientMetadata>(_PendingClients);
        }

        internal IEnumerable<ClientMetadata> EnumeratePendingClients()
        {
            return _PendingClients.Values;
        }

        internal int PendingClientCount()
        {
            return _PendingClients.Count;
        }

        internal ClientMetadata GetTrackedClient(Guid guid)
        {
            if (_Clients.TryGetValue(guid, out ClientMetadata client)) return client;
            if (_PendingClients.TryGetValue(guid, out ClientMetadata pending)) return pending;
            return null;
        }

        #endregion

        #region Clients

        internal void AddClient(Guid guid, ClientMetadata client)
        {
            _Clients[guid] = client;
        }

        internal ClientMetadata GetClient(Guid guid)
        {
            _Clients.TryGetValue(guid, out ClientMetadata client);
            return client;
        }

        internal void RemoveClient(Guid guid)
        {
            _Clients.TryRemove(guid, out _);
        }

        internal bool ExistsClient(Guid guid)
        {
            return _Clients.ContainsKey(guid);
        }

        internal Dictionary<Guid, ClientMetadata> AllClients()
        {
            return new Dictionary<Guid, ClientMetadata>(_Clients);
        }

        internal IEnumerable<ClientMetadata> EnumerateClients()
        {
            return _Clients.Values;
        }

        internal int ClientCount()
        {
            return _Clients.Count;
        }

        #endregion

        #region Clients-Last-Seen

        internal void AddClientLastSeen(Guid guid)
        {
            UpdateClientLastSeen(guid, DateTime.UtcNow);
        }

        internal void RemoveClientLastSeen(Guid guid)
        {
            if (_Clients.TryGetValue(guid, out ClientMetadata client))
            {
                client.LastSeenUtcTicks = 0;
            }
        }

        internal bool ExistsClientLastSeen(Guid guid)
        {
            if (_Clients.TryGetValue(guid, out ClientMetadata client))
            {
                return client.LastSeenUtcTicks > 0;
            }

            return false;
        }

        internal void UpdateClientLastSeen(Guid guid, DateTime dt)
        {
            if (_Clients.TryGetValue(guid, out ClientMetadata client))
            {
                client.LastSeenUtcTicks = dt.ToUniversalTime().Ticks;
            }
        }

        #endregion

        #region Clients-Kicked

        internal void AddClientKicked(Guid guid)
        {
            _ClientsKicked.TryAdd(guid, DateTime.UtcNow);
        }

        internal void RemoveClientKicked(Guid guid)
        {
            _ClientsKicked.TryRemove(guid, out _);
        }

        internal bool ExistsClientKicked(Guid guid)
        {
            return _ClientsKicked.ContainsKey(guid);
        }

        internal Dictionary<Guid, DateTime> AllClientsKicked()
        {
            return new Dictionary<Guid, DateTime>(_ClientsKicked);
        }

        #endregion

        #region Clients-Timedout

        internal void AddClientTimedout(Guid guid)
        {
            _ClientsTimedout.TryAdd(guid, DateTime.UtcNow);
        }

        internal void RemoveClientTimedout(Guid guid)
        {
            _ClientsTimedout.TryRemove(guid, out _);
        }

        internal bool ExistsClientTimedout(Guid guid)
        {
            return _ClientsTimedout.ContainsKey(guid);
        }

        internal Dictionary<Guid, DateTime> AllClientsTimedout()
        {
            return new Dictionary<Guid, DateTime>(_ClientsTimedout);
        }

        #endregion

        #endregion
    }
}
