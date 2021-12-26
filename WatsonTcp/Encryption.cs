using System;

namespace WatsonTcp
{
    public class Encryption
    {
        #region Public-Members
        /// <summary>
        /// Passphrase that must be consistent between clients and this server for encrypted communication.
        /// </summary>
        public string Passphrase = string.Empty;

        /// <summary>
        /// Algorithm mechanism used to encrypt payload between clients and this server for encrypted communication.
        /// </summary>
        public EncryptionType Algorithm = EncryptionType.None;
        #endregion
    }
}