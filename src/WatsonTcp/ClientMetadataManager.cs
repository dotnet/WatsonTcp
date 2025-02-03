namespace WatsonTcp
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

    internal class ClientMetadataManager : IDisposable
    {
        #region Internal-Members

        #endregion

        #region Private-Members

        private readonly ReaderWriterLockSlim _UnauthenticatedClientsLock = new ReaderWriterLockSlim();
        private Dictionary<Guid, DateTime> _UnauthenticatedClients = new Dictionary<Guid, DateTime>();

        private readonly ReaderWriterLockSlim _ClientsLock = new ReaderWriterLockSlim();
        private Dictionary<Guid, ClientMetadata> _Clients = new Dictionary<Guid, ClientMetadata>();

        private readonly ReaderWriterLockSlim _ClientsLastSeenLock = new ReaderWriterLockSlim();
        private Dictionary<Guid, DateTime> _ClientsLastSeen = new Dictionary<Guid, DateTime>();

        private readonly ReaderWriterLockSlim _ClientsKickedLock = new ReaderWriterLockSlim();
        private Dictionary<Guid, DateTime> _ClientsKicked = new Dictionary<Guid, DateTime>();

        private readonly ReaderWriterLockSlim _ClientsTimedoutLock = new ReaderWriterLockSlim();
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
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _UnauthenticatedClients = null;
                _Clients = null;
                _ClientsLastSeen = null;
                _ClientsKicked = null;
                _ClientsTimedout = null;
            }
        }

        #endregion

        #region Internal-Methods

        internal void Reset()
        {

        }

        internal void ReplaceGuid(Guid original, Guid replace)
        {
            ReplaceUnauthenticatedClient(original, replace);
            ReplaceClient(original, replace);
            ReplaceClientLastSeen(original, replace);
            ReplaceClientKicked(original, replace);
            ReplaceClientTimedout(original, replace);
        }

        internal void Remove(Guid guid)
        {
            RemoveUnauthenticatedClient(guid);
            RemoveClient(guid);
            RemoveClientLastSeen(guid);
            RemoveClientKicked(guid);
            RemoveClientTimedout(guid);
        }

        /*

        private ConcurrentDictionary<Guid, DateTime> _UnauthenticatedClients = new ConcurrentDictionary<Guid, DateTime>();
        private ConcurrentDictionary<Guid, ClientMetadata> _Clients = new ConcurrentDictionary<Guid, ClientMetadata>();
        private ConcurrentDictionary<Guid, DateTime> _ClientsLastSeen = new ConcurrentDictionary<Guid, DateTime>();
        private ConcurrentDictionary<Guid, DateTime> _ClientsKicked = new ConcurrentDictionary<Guid, DateTime>();
        private ConcurrentDictionary<Guid, DateTime> _ClientsTimedout = new ConcurrentDictionary<Guid, DateTime>();

         */

        #region Unauthenticated-Clients

        #region Helpers
        private void _addUnauthenticatedClient(Guid guid, DateTime? dt = null)
        {
            _UnauthenticatedClientsLock.EnterWriteLock();

            try
            {
                if (dt == null)
                {
                    _UnauthenticatedClients.Add(guid, DateTime.UtcNow);
                }
                else
                {
                    _UnauthenticatedClients.Add(guid, dt.Value);
                }
            }
            finally
            {
                _UnauthenticatedClientsLock.ExitWriteLock();
            }

        }

        private void _removeUnauthenticatedClient(Guid guid)
        {
            if (_existsUnauthenticatedClient(guid))
            {
                _UnauthenticatedClientsLock.EnterWriteLock();

                try
                {
                    _UnauthenticatedClients.Remove(guid);
                }
                finally
                {
                    _UnauthenticatedClientsLock.ExitWriteLock();
                }


            }
        }

        private bool _existsUnauthenticatedClient(Guid guid)
        {
            _UnauthenticatedClientsLock.EnterReadLock();

            try
            {
                return _UnauthenticatedClients.ContainsKey(guid);
            }
            finally
            {
                _UnauthenticatedClientsLock.ExitReadLock();
            }
        }

        private DateTime _unauthenticatedClientsGetDateTime(Guid guid)
        {
            _UnauthenticatedClientsLock.EnterReadLock();

            try
            {
                return _UnauthenticatedClients[guid];
            }
            finally
            {
                _UnauthenticatedClientsLock.ExitReadLock();
            }

        }
        #endregion

        internal void AddUnauthenticatedClient(Guid guid) => _addUnauthenticatedClient(guid);

        internal void RemoveUnauthenticatedClient(Guid guid) => _removeUnauthenticatedClient(guid);


        internal bool ExistsUnauthenticatedClient(Guid guid) => _existsUnauthenticatedClient(guid);


        internal void ReplaceUnauthenticatedClient(Guid original, Guid update)
        {

            if (_existsUnauthenticatedClient(original))
            {
                DateTime dt = _unauthenticatedClientsGetDateTime(original);
                _removeUnauthenticatedClient(original);
                _addUnauthenticatedClient(update, dt);
            }

        }

        internal Dictionary<Guid, DateTime> AllUnauthenticatedClients()
        {

            _UnauthenticatedClientsLock.EnterReadLock();

            try
            {
                return new Dictionary<Guid, DateTime>(_UnauthenticatedClients);
            }
            finally
            {
                _UnauthenticatedClientsLock.ExitReadLock();
            }

        }

        #endregion



        #region Clients


        #region Helpers

        private void _addClient(Guid guid, ClientMetadata client)
        {
            _ClientsLock.EnterWriteLock();

            try
            {

                _Clients.Add(guid, client);

            }
            finally
            {
                _ClientsLock.ExitWriteLock();
            }

        }

        private void _removeClient(Guid guid)
        {
            if (_existsClient(guid))
            {
                _ClientsLock.EnterWriteLock();

                try
                {
                    _Clients.Remove(guid);
                }
                finally
                {
                    _ClientsLock.ExitWriteLock();
                }


            }
        }

        private bool _existsClient(Guid guid)
        {
            _ClientsLock.EnterReadLock();

            try
            {
                return _Clients.ContainsKey(guid);
            }
            finally
            {
                _ClientsLock.ExitReadLock();
            }
        }

        private ClientMetadata _getClientMetadata(Guid guid)
        {
            _ClientsLock.EnterReadLock();

            try
            {
                return _Clients[guid];
            }
            finally
            {
                _ClientsLock.ExitReadLock();
            }

        }


        #endregion

        internal void AddClient(Guid guid, ClientMetadata client) => _addClient(guid, client);

        internal ClientMetadata GetClient(Guid guid) => _existsClient(guid) ? _Clients[guid] : null;

        internal void RemoveClient(Guid guid) => _removeClient(guid);

        internal bool ExistsClient(Guid guid) => _existsClient(guid);

        internal void ReplaceClient(Guid original, Guid update)
        {

            if (_existsClient(original))
            {
                ClientMetadata md = _getClientMetadata(original);
                _removeClient(original);
                _addClient(update, md);
            }

        }

        internal Dictionary<Guid, ClientMetadata> AllClients()
        {

            _ClientsLock.EnterReadLock();

            try
            {
                return new Dictionary<Guid, ClientMetadata>(_Clients);
            }
            finally
            {
                _ClientsLock.ExitReadLock();
            }
        }

        #endregion



        #region Clients-Last-Seen


        #region Helpers
        private void _addClientLastSeen(Guid guid, DateTime? dt = null)
        {
            if (_existsClientLastSeen(guid)) return;

            _ClientsLastSeenLock.EnterWriteLock();

            try
            {
                if (dt == null)
                {
                    _ClientsLastSeen.Add(guid, DateTime.UtcNow);
                }
                else
                {
                    _ClientsLastSeen.Add(guid, dt.Value);
                }
            }
            finally
            {
                _ClientsLastSeenLock.ExitWriteLock();
            }

        }

        private void _removeClientLastSeen(Guid guid)
        {
            if (!_existsClientLastSeen(guid)) return;

            _ClientsLastSeenLock.EnterWriteLock();

            try
            {
                _ClientsLastSeen.Remove(guid);
            }
            finally
            {
                _ClientsLastSeenLock.ExitWriteLock();
            }



        }

        private bool _existsClientLastSeen(Guid guid)
        {
            _ClientsLastSeenLock.EnterReadLock();

            try
            {
                return _ClientsLastSeen.ContainsKey(guid);
            }
            finally
            {
                _ClientsLastSeenLock.ExitReadLock();
            }
        }

        private DateTime _clientLastSeenGetDateTime(Guid guid)
        {
            _ClientsLastSeenLock.EnterReadLock();

            try
            {
                return _ClientsLastSeen[guid];
            }
            finally
            {
                _ClientsLastSeenLock.ExitReadLock();
            }

        }
        #endregion

        internal void AddClientLastSeen(Guid guid) => _addClientLastSeen(guid);

        internal void RemoveClientLastSeen(Guid guid) => _removeClientLastSeen(guid);

        internal bool ExistsClientLastSeen(Guid guid) => _existsClientLastSeen(guid);

        internal void ReplaceClientLastSeen(Guid original, Guid update)
        {

            if (_existsClientLastSeen(original))
            {
                DateTime dt = _clientLastSeenGetDateTime(original);
                _removeClientLastSeen(original);
                _addClientLastSeen(update, dt);
            }

        }

        internal void UpdateClientLastSeen(Guid guid, DateTime dt)
        {

            if (_existsClientLastSeen(guid))
            {
                _removeClientLastSeen(guid);
                _addClientLastSeen(guid, dt.ToUniversalTime());
            }

        }

        internal Dictionary<Guid, DateTime> AllClientsLastSeen()
        {

            _ClientsLastSeenLock.EnterReadLock();

            try
            {
                return new Dictionary<Guid, DateTime>(_ClientsLastSeen);
            }
            finally
            {
                _ClientsLastSeenLock.ExitReadLock();
            }


        }

        #endregion




        #region Clients-Kicked


        #region Helpers
        private void _addClientKicked(Guid guid, DateTime? dt = null)
        {
            if (_existsClientKicked(guid)) return;

            _ClientsKickedLock.EnterWriteLock();

            try
            {
                if (dt == null)
                {
                    _ClientsKicked.Add(guid, DateTime.UtcNow);
                }
                else
                {
                    _ClientsKicked.Add(guid, dt.Value);
                }
            }
            finally
            {
                _ClientsKickedLock.ExitWriteLock();
            }

        }

        private void _removeClientKicked(Guid guid)
        {
            if (!_existsClientKicked(guid)) return;

            _ClientsKickedLock.EnterWriteLock();

            try
            {
                _ClientsKicked.Remove(guid);
            }
            finally
            {
                _ClientsKickedLock.ExitWriteLock();
            }



        }

        private bool _existsClientKicked(Guid guid)
        {
            _ClientsKickedLock.EnterReadLock();

            try
            {
                return _ClientsKicked.ContainsKey(guid);
            }
            finally
            {
                _ClientsKickedLock.ExitReadLock();
            }
        }

        private DateTime _clientKickedGetDateTime(Guid guid)
        {
            _ClientsKickedLock.EnterReadLock();

            try
            {
                return _ClientsKicked[guid];
            }
            finally
            {
                _ClientsKickedLock.ExitReadLock();
            }

        }
        #endregion



        internal void AddClientKicked(Guid guid) => _addClientKicked(guid);

        internal void RemoveClientKicked(Guid guid) => _removeClientKicked(guid);

        internal bool ExistsClientKicked(Guid guid) => _existsClientKicked(guid);

        internal void ReplaceClientKicked(Guid original, Guid update)
        {

            if (_existsClientKicked(original))
            {
                DateTime dt = _clientKickedGetDateTime(original);
                _removeClientKicked(original);
                _addClientKicked(update, dt);
            }

        }

        internal void UpdateClientKicked(Guid guid, DateTime dt)
        {

            if (_existsClientKicked(guid))
            {
                _removeClientKicked(guid);
                _addClientKicked(guid, dt.ToUniversalTime());
            }

        }

        internal Dictionary<Guid, DateTime> AllClientsKicked()
        {

            _ClientsKickedLock.EnterReadLock();

            try
            {
                return new Dictionary<Guid, DateTime>(_ClientsKicked);
            }
            finally
            {
                _ClientsKickedLock.ExitReadLock();
            }

        }

        #endregion



        #region Clients-Timedout


        #region Helpers
        private void _addClientTimedout(Guid guid, DateTime? dt = null)
        {
            if (_existsClientTimedout(guid)) return;

            _ClientsTimedoutLock.EnterWriteLock();

            try
            {
                if (dt == null)
                {
                    _ClientsTimedout.Add(guid, DateTime.UtcNow);
                }
                else
                {
                    _ClientsTimedout.Add(guid, dt.Value);
                }
            }
            finally
            {
                _ClientsTimedoutLock.ExitWriteLock();
            }

        }

        private void _removeClientTimedout(Guid guid)
        {
            if (!_existsClientTimedout(guid)) return;

            _ClientsTimedoutLock.EnterWriteLock();

            try
            {
                _ClientsTimedout.Remove(guid);
            }
            finally
            {
                _ClientsTimedoutLock.ExitWriteLock();
            }



        }

        private bool _existsClientTimedout(Guid guid)
        {
            _ClientsTimedoutLock.EnterReadLock();

            try
            {
                return _ClientsTimedout.ContainsKey(guid);
            }
            finally
            {
                _ClientsTimedoutLock.ExitReadLock();
            }
        }

        private DateTime _clientTimeoutGetDateTime(Guid guid)
        {
            _ClientsTimedoutLock.EnterReadLock();

            try
            {
                return _ClientsTimedout[guid];
            }
            finally
            {
                _ClientsTimedoutLock.ExitReadLock();
            }

        }
        #endregion

        internal void AddClientTimedout(Guid guid) => _addClientTimedout(guid);

        internal void RemoveClientTimedout(Guid guid) => _removeClientTimedout(guid);

        internal bool ExistsClientTimedout(Guid guid) => _existsClientTimedout(guid);

        internal void ReplaceClientTimedout(Guid original, Guid update)
        {

            if (_existsClientTimedout(original))
            {
                DateTime dt = _clientTimeoutGetDateTime(original);
                _removeClientTimedout(original);
                _addClientTimedout(update, dt);
            }

        }

        internal void UpdateClientTimeout(Guid guid, DateTime dt)
        {
            if (_existsClientTimedout(guid))
            {
                _removeClientTimedout(guid);
                _addClientTimedout(guid, dt.ToUniversalTime());
            }

        }

        internal Dictionary<Guid, DateTime> AllClientsTimedout()
        {
            _ClientsTimedoutLock.EnterReadLock();

            try
            {
                return new Dictionary<Guid, DateTime>(_ClientsTimedout);
            }
            finally
            {
                _ClientsTimedoutLock.ExitReadLock();
            }
        }

        #endregion


        #endregion

        #region Private-Methods

        #endregion
    }
}
