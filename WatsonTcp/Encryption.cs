using System;

namespace WatsonTcp
{
    public class Encryption
    {
        #region Public-Members
        /// <summary>
        /// Algorithm mechanism used to encrypt payload between clients and this server for encrypted communication.
        /// </summary>
        public EncryptionAlgorithm Algorithm = EncryptionAlgorithm.None;
        #endregion

        #region Internal-Members
        /// <summary>
        /// Passphrase that must be consistent between clients and this server for encrypted communication.
        /// </summary>
        internal string Passphrase = string.Empty;
        #endregion
    }
}